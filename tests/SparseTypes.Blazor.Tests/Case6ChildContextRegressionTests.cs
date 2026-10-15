using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Case 6 regression for #302: section edit → child accept → AutoAccept mode
// change → root Undo → root Redo with nested keyed grandchildren. The root
// history replay may replace nested instances, which must rebind descendant
// scopes before any render fragment reads a disposed session.
public sealed class Case6ChildContextRegressionTests
{
    private static Case6Catalog Catalog() =>
        new()
        {
            Title = "Catalog",
            Section = new Case6Section
            {
                Note = "Baseline",
                Tiers = new()
                {
                    new Case6Tier
                    {
                        Code = "standard",
                        Label = "Standard",
                        UnitPrice = 10m,
                        Bands = new()
                        {
                            new Case6Band { Code = "annual", Percent = 3m },
                        },
                    },
                    new Case6Tier
                    {
                        Code = "volume",
                        Label = "Volume",
                        UnitPrice = 9m,
                    },
                },
            },
        };

    private static RenderFragment ScopeContent(
        ISparseEditSession<Case6Catalog> session,
        RenderFragment child,
        bool? autoAccept = null
    ) =>
        builder =>
        {
            builder.OpenComponent<SparseSessionScope<Case6Catalog>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<Case6Catalog>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<Case6Catalog>.ChildContent), child);
            builder.AddAttribute(
                3,
                nameof(SparseSessionScope<Case6Catalog>.AutoAccept),
                autoAccept
            );
            builder.CloseComponent();
        };

    private static RenderFragment ChildWithContent<TParent, TChild, TPaths, TConflict, TSession>(
        ISparseEditSessionWithChildren<TParent, TConflict> parent,
        ISparseChildEditPath<TParent, TChild, TPaths, TSession> path,
        RenderFragment<SparseChildEditContext<TChild, TPaths, TConflict, TSession>> childContent
    )
        where TParent : class
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TSession : ISparseEditSessionWithPaths<TChild, TPaths> =>
        builder =>
        {
            builder.OpenComponent<SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>>(0);
            builder.AddAttribute(
                1,
                nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.Path),
                path
            );
            builder.AddAttribute(
                2,
                nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.ChildContent),
                childContent
            );
            builder.CloseComponent();
        };

    [Test]
    public async Task UndoAfterAutoAcceptModeChangeRebindsNestedKeyedGrandchildren()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var sectionContexts = new List<ISparseChildEditContext<Case6Section, SparseConflict>>();
        var tierContexts = new List<ISparseChildEditContext<Case6Tier, SparseConflict>>();
        var shownNote = string.Empty;
        var shownLabel = string.Empty;

        // Mirrors CommercialEditor/CatalogPriceTierEditor: the fragment caches
        // the child session like CaptureChildContext does, then reads the
        // cached draft session for display.
        Case6Section.EditSession? cachedSectionDraft = null;
        Case6Tier.EditSession? cachedTierDraft = null;

        RenderFragment SectionContent(ISparseEditSession<Case6Catalog> parent) =>
            ChildWithContent(
                (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)parent,
                Case6Catalog.SparsePath.Section,
                context =>
                    builder =>
                    {
                        if (
                            sectionContexts.Count == 0
                            || !ReferenceEquals(sectionContexts[^1], context)
                        )
                        {
                            sectionContexts.Add(context);
                            cachedSectionDraft = (Case6Section.EditSession)context.Session;
                        }

                        // Draft read through the cached session, as Case 6 markup does.
                        _ = cachedSectionDraft!.Observable.Note;
                        builder.AddContent(
                            0,
                            Headless.Field(context.Path.Note, field => shownNote = field.Value)
                        );
                        builder.AddContent(
                            1,
                            ChildWithContent(
                                (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                                    context.Session,
                                Case6Section.SparsePath.Tiers.Key("standard"),
                                tier =>
                                    tierBuilder =>
                                    {
                                        if (
                                            tierContexts.Count == 0
                                            || !ReferenceEquals(tierContexts[^1], tier)
                                        )
                                        {
                                            tierContexts.Add(tier);
                                            cachedTierDraft = (Case6Tier.EditSession)tier.Session;
                                        }

                                        _ = cachedTierDraft!.Observable.Label;
                                        tierBuilder.AddContent(
                                            0,
                                            Headless.Field(
                                                tier.Path.Label,
                                                field => shownLabel = field.Value
                                            )
                                        );
                                    }
                            )
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, SectionContent(session))
        );

        sectionContexts.ShouldHaveSingleItem();
        tierContexts.ShouldHaveSingleItem();
        shownNote.ShouldBe("Baseline");
        shownLabel.ShouldBe("Standard");

        // Section edit, including the nested keyed price-tier editor.
        ((Case6Section.EditSession)sectionContexts[0].Session)
            .Observable
            .Note = "Draft note";
        ((Case6Tier.EditSession)tierContexts[0].Session).Observable.Label = "Draft label";
        await tree.FlushAsync();
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");

        // Save section draft: the child merges into the root session.
        sectionContexts[0].TryAcceptChanges(out var acceptConflicts).ShouldBeTrue();
        acceptConflicts.ShouldBeNull();
        await tree.FlushAsync();
        live.Section.Note.ShouldBe("Draft note");
        live.Section.Tiers.Single(tier => tier.Code == "standard").Label.ShouldBe("Draft label");

        // Disable section drafts: the scope flips to AutoAccept.
        await tree.ShowAsync(ScopeContent(session, SectionContent(session), autoAccept: true));
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");

        // Root Undo must rebind descendants instead of rendering disposed sessions.
        history.Undo();
        await tree.FlushAsync();
        live.Section.Note.ShouldBe("Baseline");
        shownNote.ShouldBe("Baseline");
        shownLabel.ShouldBe("Standard");
        live.Section.Tiers.Single(tier => tier.Code == "standard").Label.ShouldBe("Standard");
        history.CanRedo.ShouldBeTrue();

        // Root Redo restores the merged state through rebound sessions.
        history.Redo();
        await tree.FlushAsync();
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");
        live.Section.Note.ShouldBe("Draft note");
        live.Section.Tiers.Single(tier => tier.Code == "standard").Label.ShouldBe("Draft label");

        // Repeated rerenders stay clean.
        await tree.FlushAsync();
        await tree.FlushAsync();
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");
        sectionContexts[^1].ShouldNotBeSameAs(sectionContexts[0]);
        tierContexts[^1].ShouldNotBeSameAs(tierContexts[0]);
    }

    [Test]
    public async Task StructuralUndoRedoRebindsNestedKeyedGrandchildren()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var sectionContexts = new List<ISparseChildEditContext<Case6Section, SparseConflict>>();
        var tierContexts = new List<ISparseChildEditContext<Case6Tier, SparseConflict>>();
        var shownNote = string.Empty;
        var shownLabel = string.Empty;
        Case6Section.EditSession? cachedSectionDraft = null;
        Case6Tier.EditSession? cachedTierDraft = null;

        RenderFragment SectionContent(ISparseEditSession<Case6Catalog> parent) =>
            ChildWithContent(
                (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)parent,
                Case6Catalog.SparsePath.Section,
                context =>
                    builder =>
                    {
                        if (
                            sectionContexts.Count == 0
                            || !ReferenceEquals(sectionContexts[^1], context)
                        )
                        {
                            sectionContexts.Add(context);
                            cachedSectionDraft = (Case6Section.EditSession)context.Session;
                        }

                        _ = cachedSectionDraft!.Observable.Note;
                        builder.AddContent(
                            0,
                            Headless.Field(context.Path.Note, field => shownNote = field.Value)
                        );
                        builder.AddContent(
                            1,
                            ChildWithContent(
                                (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                                    context.Session,
                                Case6Section.SparsePath.Tiers.Key("standard"),
                                tier =>
                                    tierBuilder =>
                                    {
                                        if (
                                            tierContexts.Count == 0
                                            || !ReferenceEquals(tierContexts[^1], tier)
                                        )
                                        {
                                            tierContexts.Add(tier);
                                            cachedTierDraft = (Case6Tier.EditSession)tier.Session;
                                        }

                                        _ = cachedTierDraft!.Observable.Label;
                                        tierBuilder.AddContent(
                                            0,
                                            Headless.Field(
                                                tier.Path.Label,
                                                field => shownLabel = field.Value
                                            )
                                        );
                                    }
                            )
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, SectionContent(session), autoAccept: true)
        );

        // Structural edit through the tier draft in AutoAccept mode: the merge
        // cascades into the root session, which may replace nested instances
        // and dispose the very contexts whose renders are already queued.
        ((Case6Tier.EditSession)tierContexts[0].Session).Observable.Bands.AddModel(
            new Case6Band { Code = "promo", Percent = 7m }
        );
        await tree.FlushAsync();
        live.Section.Tiers.Single(tier => tier.Code == "standard")
            .Bands.ShouldContain(band => band.Code == "promo");
    }

    [Test]
    public async Task ChildRenderQueuedBeforeDisposalNeverReadsDisposedSession()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var sectionContexts = new List<ISparseChildEditContext<Case6Section, SparseConflict>>();
        var tierContexts = new List<ISparseChildEditContext<Case6Tier, SparseConflict>>();
        var shownNote = string.Empty;
        var shownLabel = string.Empty;

        RenderFragment SectionContent(ISparseEditSession<Case6Catalog> parent) =>
            ChildWithContent(
                (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)parent,
                Case6Catalog.SparsePath.Section,
                context =>
                    builder =>
                    {
                        if (
                            sectionContexts.Count == 0
                            || !ReferenceEquals(sectionContexts[^1], context)
                        )
                        {
                            sectionContexts.Add(context);
                        }

                        _ = ((Case6Section.EditSession)context.Session).Observable.Note;
                        builder.AddContent(
                            0,
                            Headless.Field(context.Path.Note, field => shownNote = field.Value)
                        );
                        builder.AddContent(
                            1,
                            ChildWithContent(
                                (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                                    context.Session,
                                Case6Section.SparsePath.Tiers.Key("standard"),
                                tier =>
                                    tierBuilder =>
                                    {
                                        if (
                                            tierContexts.Count == 0
                                            || !ReferenceEquals(tierContexts[^1], tier)
                                        )
                                        {
                                            tierContexts.Add(tier);
                                        }

                                        _ = ((Case6Tier.EditSession)tier.Session).Observable.Label;
                                        tierBuilder.AddContent(
                                            0,
                                            Headless.Field(
                                                tier.Path.Label,
                                                field => shownLabel = field.Value
                                            )
                                        );
                                    }
                            )
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, SectionContent(session))
        );

        ((Case6Section.EditSession)sectionContexts[0].Session).Observable.Note = "Draft note";
        ((Case6Tier.EditSession)tierContexts[0].Session).Observable.Label = "Draft label";
        await tree.FlushAsync();
        sectionContexts[0].TryAcceptChanges(out _).ShouldBeTrue();
        await tree.FlushAsync();
        await tree.ShowAsync(ScopeContent(session, SectionContent(session), autoAccept: true));

        // Queue a tier-subtree render while its context is still live, then
        // dispose it underneath via root Undo before anything flushes.
        var tierSession = (Case6Tier.EditSession)tierContexts[^1].Session;
        tierSession.DisableHistory();
        tierSession.EnableHistory();
        history.Undo();
        await tree.FlushAsync();

        shownNote.ShouldBe("Baseline");
        shownLabel.ShouldBe("Standard");
    }

    [Test]
    public async Task InlineDispatchedUndoRebindsBeforeDescendantReads()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var sectionContexts = new List<ISparseChildEditContext<Case6Section, SparseConflict>>();
        var tierContexts = new List<ISparseChildEditContext<Case6Tier, SparseConflict>>();
        var shownNote = string.Empty;
        var shownLabel = string.Empty;

        RenderFragment SectionContent(ISparseEditSession<Case6Catalog> parent) =>
            ChildWithContent(
                (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)parent,
                Case6Catalog.SparsePath.Section,
                context =>
                    builder =>
                    {
                        if (
                            sectionContexts.Count == 0
                            || !ReferenceEquals(sectionContexts[^1], context)
                        )
                        {
                            sectionContexts.Add(context);
                        }

                        _ = ((Case6Section.EditSession)context.Session).Observable.Note;
                        builder.AddContent(
                            0,
                            Headless.Field(context.Path.Note, field => shownNote = field.Value)
                        );
                        builder.AddContent(
                            1,
                            ChildWithContent(
                                (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                                    context.Session,
                                Case6Section.SparsePath.Tiers.Key("standard"),
                                tier =>
                                    tierBuilder =>
                                    {
                                        if (
                                            tierContexts.Count == 0
                                            || !ReferenceEquals(tierContexts[^1], tier)
                                        )
                                        {
                                            tierContexts.Add(tier);
                                        }

                                        _ = ((Case6Tier.EditSession)tier.Session).Observable.Label;
                                        tierBuilder.AddContent(
                                            0,
                                            Headless.Field(
                                                tier.Path.Label,
                                                field => shownLabel = field.Value
                                            )
                                        );
                                    }
                            )
                        );
                    }
            );

        using var tree = await InlineTree.CreateAsync(
            ScopeContent(session, SectionContent(session))
        );

        ((Case6Section.EditSession)sectionContexts[0].Session).Observable.Note = "Draft note";
        ((Case6Tier.EditSession)tierContexts[0].Session).Observable.Label = "Draft label";
        sectionContexts[0].TryAcceptChanges(out _).ShouldBeTrue();
        await tree.ShowAsync(ScopeContent(session, SectionContent(session), autoAccept: true));
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");

        // Every notification renders inline: the disposal inside Undo runs the
        // descendant renders before the root rebind render. No fragment may
        // observe the disposed contexts.
        history.Undo();
        shownNote.ShouldBe("Baseline");
        shownLabel.ShouldBe("Standard");
        live.Section.Note.ShouldBe("Baseline");
        history.CanRedo.ShouldBeTrue();

        history.Redo();
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");
        live.Section.Note.ShouldBe("Draft note");

        // The rebound sessions are live: the old ones throw, the current ones
        // report the restored state and history.
        Should.Throw<ObjectDisposedException>(() => sectionContexts[0].Session);
        Should.Throw<ObjectDisposedException>(() => tierContexts[0].Session);
        sectionContexts[^1].HasChanges.ShouldBeFalse();
        tierContexts[^1].HasChanges.ShouldBeFalse();
        session.HasOwnChanges.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
        history.CanRedo.ShouldBeFalse();

        // Replacement, removal, and reorder replay through the same safe path.
        // Back to drafts so the structural edits merge as one history entry.
        await tree.ShowAsync(ScopeContent(session, SectionContent(session), autoAccept: false));
        sectionContexts[^1].HasChanges.ShouldBeFalse();
        var sectionDraft = (Case6Section.EditSession)sectionContexts[^1].Session;
        sectionDraft.Observable.Tiers.RemoveAt(1);
        sectionDraft.Observable.Tiers.AddModel(
            new Case6Tier
            {
                Code = "promo",
                Label = "Promo",
                UnitPrice = 5m,
            }
        );
        sectionDraft.Observable.Tiers.Move(0, 1);
        sectionContexts[^1].TryAcceptChanges(out _).ShouldBeTrue();
        live.Section.Tiers.Select(tier => tier.Code).ShouldBe(["promo", "standard"]);
        await tree.ShowAsync(ScopeContent(session, SectionContent(session), autoAccept: true));

        history.Undo();
        live.Section.Tiers.Select(tier => tier.Code).ShouldBe(["standard", "volume"]);
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");

        history.Redo();
        live.Section.Tiers.Select(tier => tier.Code).ShouldBe(["promo", "standard"]);
        shownNote.ShouldBe("Draft note");
        shownLabel.ShouldBe("Draft label");
    }

    [Test]
    public async Task FuzzSectionTierUndoRedoNeverReadsDisposedSessions()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var sectionContexts = new List<ISparseChildEditContext<Case6Section, SparseConflict>>();
        var tierContexts = new List<ISparseChildEditContext<Case6Tier, SparseConflict>>();
        var shownNote = string.Empty;
        var shownLabel = string.Empty;

        RenderFragment SectionContent(ISparseEditSession<Case6Catalog> parent) =>
            ChildWithContent(
                (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)parent,
                Case6Catalog.SparsePath.Section,
                context =>
                    builder =>
                    {
                        if (
                            sectionContexts.Count == 0
                            || !ReferenceEquals(sectionContexts[^1], context)
                        )
                        {
                            sectionContexts.Add(context);
                        }

                        // Any disposed-context read through the retained fragment
                        // surfaces here as ObjectDisposedException.
                        _ = ((Case6Section.EditSession)context.Session).Observable.Note;
                        builder.AddContent(
                            0,
                            Headless.Field(context.Path.Note, field => shownNote = field.Value)
                        );
                        builder.AddContent(
                            1,
                            ChildWithContent(
                                (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                                    context.Session,
                                Case6Section.SparsePath.Tiers.Key("standard"),
                                tier =>
                                    tierBuilder =>
                                    {
                                        if (
                                            tierContexts.Count == 0
                                            || !ReferenceEquals(tierContexts[^1], tier)
                                        )
                                        {
                                            tierContexts.Add(tier);
                                        }

                                        _ = ((Case6Tier.EditSession)tier.Session).Observable.Label;
                                        tierBuilder.AddContent(
                                            0,
                                            Headless.Field(
                                                tier.Path.Label,
                                                field => shownLabel = field.Value
                                            )
                                        );
                                    }
                            )
                        );
                    }
            );

        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, SectionContent(session))
        );
        var autoAccept = false;
        var random = new Random(302);
        for (var step = 0; step < 60; step++)
        {
            var action = random.Next(8);
            var sectionDraft = (Case6Section.EditSession)sectionContexts[^1].Session;
            var tierDraft = (Case6Tier.EditSession)tierContexts[^1].Session;
            switch (action)
            {
                case 0:
                    sectionDraft.Observable.Note = $"note-{step}";
                    break;
                case 1:
                    tierDraft.Observable.Label = $"label-{step}";
                    break;
                case 2:
                    tierDraft.Observable.Bands.AddModel(
                        new Case6Band { Code = $"band-{step}", Percent = step }
                    );
                    break;
                case 3:
                    if (tierDraft.Observable.Bands.Count > 0)
                    {
                        tierDraft.Observable.Bands.RemoveAt(0);
                    }

                    break;
                case 4:
                    if (sectionContexts[^1].TryAcceptChanges(out _)) { }

                    break;
                case 5:
                    if (history.CanUndo)
                    {
                        history.TryUndo(out _);
                    }

                    break;
                case 6:
                    if (history.CanRedo)
                    {
                        history.TryRedo(out _);
                    }

                    break;
                default:
                    // Mirrors the Case 6 checkbox: mode changes are blocked
                    // while child edits are pending.
                    if (!session.HasPendingChildSessions)
                    {
                        autoAccept = !autoAccept;
                        try
                        {
                            await tree.ShowAsync(
                                ScopeContent(session, SectionContent(session), autoAccept)
                            );
                        }
                        catch (InvalidOperationException) { }
                    }

                    break;
            }

            await tree.FlushAsync();

            // The rendered values always track the current draft sessions
            // through whatever rebinds happened; the drafts themselves may
            // legitimately differ from the root model while unaccepted.
            shownNote.ShouldBe(
                ((Case6Section.EditSession)sectionContexts[^1].Session).Observable.Note
            );
            var currentTiers = ((Case6Section.EditSession)sectionContexts[^1].Session)
                .Observable
                .Tiers;
            if (currentTiers.Any(tier => tier.Code == "standard"))
            {
                shownLabel.ShouldBe(
                    ((Case6Tier.EditSession)tierContexts[^1].Session).Observable.Label
                );
            }
        }
    }
}
