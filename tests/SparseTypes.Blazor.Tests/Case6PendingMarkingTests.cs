using Microsoft.AspNetCore.Components;
using SparseTypes.Generated;

namespace SparseTypes.Blazor.Tests;

// Case 6 keeps draft highlighting local to each child session. Root changes
// remain available for the catalog save preview after a section is accepted.
public sealed class Case6PendingMarkingTests
{
    private static readonly SparsePath SectionPath = Case6Catalog.SparsePath.Section.UntypedPath;

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

    // Captures one root snapshot to verify the delta remains available for Save.
    private static SparseChangeSetQueryView RootView(Case6Catalog.EditSession session)
    {
        var view = ((ISparseChangeSet)session.PreviewChangeSet()).CaptureQueryView();
        view.EnsureIndexed();
        return view;
    }

    private static bool SubtreeChanged(SparseChangeSetQueryView view, SparsePath section) =>
        view.FieldState(section).HasChanges;

    private static bool FieldChanged(SparseChangeSetQueryView view, SparsePath field)
    {
        var state = view.FieldState(field);
        return state.IsChanged || state.HasDescendantChange;
    }

    private static SparsePath TierRow(string code) =>
        SectionPath.Append(Case6Section.SparsePath.Tiers.Key(code).UntypedPath);

    private static SparsePath TiersPath =>
        SectionPath.Append(Case6Section.SparsePath.Tiers.UntypedPath);

    [Test]
    public void SectionAcceptanceClearsDraftStateAndRetainsRootChange()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        var sectionChild = (
            (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)session
        ).OpenChildSession(Case6Catalog.SparsePath.Section);
        var section = (Case6Section.EditSession)sectionChild.Session;

        section.Observable.Note = "Draft";
        sectionChild.HasChanges.ShouldBeTrue();
        section.FieldState(Case6Section.SparsePath.Note).IsChanged.ShouldBeTrue();
        // A pending draft is not parent-owned yet.
        RootView(session).FieldState(SectionPath).HasChanges.ShouldBeFalse();

        sectionChild.TryAcceptChanges(out _).ShouldBeTrue();
        sectionChild.HasChanges.ShouldBeFalse();
        section.FieldState(Case6Section.SparsePath.Note).IsChanged.ShouldBeFalse();

        // The accepted delta remains in the root change set while the child
        // session no longer reports a draft.
        var merged = RootView(session);
        SubtreeChanged(merged, SectionPath).ShouldBeTrue();
        FieldChanged(merged, SectionPath.Append(Case6Section.SparsePath.Note.UntypedPath))
            .ShouldBeTrue();
        session.HasOwnChanges.ShouldBeTrue();

        // Precision: untouched members stay clean.
        FieldChanged(
                merged,
                SectionPath.Append(Case6Section.SparsePath.Tiers.Key("standard").UntypedPath)
            )
            .ShouldBeFalse();
        FieldChanged(
                merged,
                SectionPath.Append(Case6Section.SparsePath.Tiers.Key("volume").UntypedPath)
            )
            .ShouldBeFalse();

        history.Undo();
        var undone = RootView(session);
        SubtreeChanged(undone, SectionPath).ShouldBeFalse();
        session.HasOwnChanges.ShouldBeFalse();
        live.Section.Note.ShouldBe("Baseline");

        history.Redo();
        var redone = RootView(session);
        SubtreeChanged(redone, SectionPath).ShouldBeTrue();
        FieldChanged(redone, SectionPath.Append(Case6Section.SparsePath.Note.UntypedPath))
            .ShouldBeTrue();

        session.AcceptChanges();
        SubtreeChanged(RootView(session), SectionPath).ShouldBeFalse();
        session.HasOwnChanges.ShouldBeFalse();
    }

    [Test]
    public void NestedKeyedRowsMarkPrecisely()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var sectionChild = (
            (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)session
        ).OpenChildSession(Case6Catalog.SparsePath.Section);
        var section = (Case6Section.EditSession)sectionChild.Session;
        var tierChild = (
            (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)section
        ).OpenChildSession(Case6Section.SparsePath.Tiers.Key("standard"));
        var tier = (Case6Tier.EditSession)tierChild.Session;

        // A nested band edit appears at its row, tier, collection, and section
        // paths without affecting the sibling tier.
        tier.Observable.Bands.AddModel(new Case6Band { Code = "promo", Percent = 7m });
        tierChild.TryAcceptChanges(out _).ShouldBeTrue();
        sectionChild.TryAcceptChanges(out _).ShouldBeTrue();

        var merged = RootView(session);
        var bandRow = TierRow("standard")
            .Append(Case6Tier.SparsePath.Bands.Key("promo").UntypedPath);
        merged.FieldState(bandRow).IsChanged.ShouldBeTrue();
        merged.FieldState(bandRow).Kind.ShouldBe(SparseFieldChangeKind.Added);
        FieldChanged(merged, TierRow("standard")).ShouldBeTrue();
        FieldChanged(merged, TiersPath).ShouldBeTrue();
        SubtreeChanged(merged, SectionPath).ShouldBeTrue();
        FieldChanged(merged, TierRow("volume")).ShouldBeFalse();
        FieldChanged(merged, SectionPath.Append(Case6Section.SparsePath.Note.UntypedPath))
            .ShouldBeFalse();

        // A scalar tier edit reports exactly at its member path.
        tier.Observable.Label = "Draft label";
        tierChild.TryAcceptChanges(out _).ShouldBeTrue();
        sectionChild.TryAcceptChanges(out _).ShouldBeTrue();
        var edited = RootView(session);
        var labelPath = TierRow("standard").Append(Case6Tier.SparsePath.Label.UntypedPath);
        edited.FieldState(labelPath).IsChanged.ShouldBeTrue();
        FieldChanged(edited, TierRow("standard")).ShouldBeTrue();
        FieldChanged(edited, TierRow("volume")).ShouldBeFalse();
    }

    [Test]
    public void RemovedRowsStayVisibleThroughRecords()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var sectionChild = (
            (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)session
        ).OpenChildSession(Case6Catalog.SparsePath.Section);
        var section = (Case6Section.EditSession)sectionChild.Session;

        section.Observable.Tiers.RemoveAt(1);
        sectionChild.TryAcceptChanges(out _).ShouldBeTrue();
        live.Section.Tiers.Select(tier => tier.Code).ShouldBe(["standard"]);

        var merged = RootView(session);
        var removed = merged
            .Records.Where(record =>
                record.Kind == SparseChangeKind.Removed
                && record.Path.Parent?.Equals(TiersPath) == true
            )
            .ToArray();
        var volume = removed.ShouldHaveSingleItem();
        volume.Path.ShouldBe(TierRow("volume"));
        volume.Before.IsPresent.ShouldBeTrue();
        volume.Before.Value.ShouldBeOfType<Case6Tier>().Code.ShouldBe("volume");
        SubtreeChanged(merged, SectionPath).ShouldBeTrue();

        session.RevertChanges();
        RootView(session)
            .Records.Any(record => record.Kind == SparseChangeKind.Removed)
            .ShouldBeFalse();
        SubtreeChanged(RootView(session), SectionPath).ShouldBeFalse();
        live.Section.Tiers.Select(tier => tier.Code).ShouldBe(["standard", "volume"]);
    }

    [Test]
    public void ReorderReportsAtTheCollectionPath()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        session.Observable.Section!.Tiers.Move(0, 1);
        session.TryAcceptChildSessions(out _).ShouldBeTrue();

        var merged = RootView(session);
        var order = merged
            .Records.Where(record => record.Kind == SparseChangeKind.Order)
            .Select(record => record.Path)
            .ToArray();
        order.ShouldContain(TiersPath);
        // A pure reorder leaves only the collection-level Order. Broad field
        // ancestry remains available to existing callers; row highlighting
        // uses keyed item state from the active draft instead.
        var standard = merged.FieldState(TierRow("standard"));
        standard.IsChanged.ShouldBeFalse();
        standard.HasDescendantChange.ShouldBeFalse();
        standard.HasAncestorChange.ShouldBeTrue();
        SubtreeChanged(merged, SectionPath).ShouldBeTrue();

        session.RevertChanges();
        SubtreeChanged(RootView(session), SectionPath).ShouldBeFalse();
    }

    [Test]
    public void DraftRowStateCoversNestedBandEdits()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var sectionChild = (
            (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)session
        ).OpenChildSession(Case6Catalog.SparsePath.Section);
        var section = (Case6Section.EditSession)sectionChild.Session;
        var tierChild = (
            (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)section
        ).OpenChildSession(Case6Section.SparsePath.Tiers.Key("standard"));
        var tier = (Case6Tier.EditSession)tierChild.Session;

        // A pending band draft lives in the tier session: its own Bands
        // collection state marks the added row, and the tier draft itself is
        // dirty, so the tier row stays marked without depending on section
        // dirtiness. The section-level Tiers state only sees accepted edits.
        tier.Observable.Bands.AddModel(new Case6Band { Code = "promo", Percent = 7m });
        tierChild.HasChanges.ShouldBeTrue();
        var bands = tier.CollectionState(Case6Tier.SparsePath.Bands);
        bands.GetItemState("promo").IsAdded.ShouldBeTrue();
        var tiers = section.CollectionState(Case6Section.SparsePath.Tiers);
        tiers.GetItemState("standard").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public async Task PassThroughEditsReportParentChangesAndRemainInRootChangeSet()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        ISparseChildEditContext<Case6Section, SparseConflict>? sectionContext = null;
        var child = ChildScope.Section(
            (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)session,
            context => sectionContext = context
        );
        using var tree = await Headless.Tree.CreateAsync(
            ChildScope.Root(session, child, mode: SparseChildEditMode.PassThrough)
        );

        var section = (Case6Section.EditSession)sectionContext!.Session;
        section.Observable.Note = "Auto";
        await tree.FlushAsync();

        // Pass-through accepts into the parent, which owns the pending state.
        sectionContext.HasChanges.ShouldBeTrue();
        section.HasOwnChanges.ShouldBeFalse();
        session.HasOwnChanges.ShouldBeTrue();
        section.FieldState(Case6Section.SparsePath.Note).IsChanged.ShouldBeFalse();
        var merged = RootView(session);
        SubtreeChanged(merged, SectionPath).ShouldBeTrue();
        FieldChanged(merged, SectionPath.Append(Case6Section.SparsePath.Note.UntypedPath))
            .ShouldBeTrue();

        history.Undo();
        await tree.FlushAsync();
        SubtreeChanged(RootView(session), SectionPath).ShouldBeFalse();
        live.Section.Note.ShouldBe("Baseline");
    }

    [Test]
    public async Task PassThroughCollectionStateIncludesNestedKeyedRows()
    {
        var live = Catalog();
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        ISparseChildEditContext<Case6Tier, SparseConflict>? tierContext = null;
        SparseFieldContext<Case6Tier, List<Case6Band>>? bandsField = null;
        using var tree = await Headless.Tree.CreateAsync(
            ChildScope.Root(
                session,
                ChildScope.SectionWithTier(
                    (ISparseEditSessionWithChildren<Case6Catalog, SparseConflict>)session,
                    context => tierContext = context,
                    context => bandsField = context
                ),
                mode: SparseChildEditMode.PassThrough
            )
        );

        var tier = (Case6Tier.EditSession)tierContext!.Session;
        tier.Observable.Bands.AddModel(new Case6Band { Code = "promo", Percent = 7m });
        await tree.FlushAsync();

        tierContext.HasChanges.ShouldBeTrue();
        tier.HasOwnChanges.ShouldBeFalse();
        bandsField!.TryGetCollectionState(out var collection).ShouldBeTrue();
        collection.ShouldNotBeNull();
        collection.Value.GetItemState("promo").IsAdded.ShouldBeTrue();

        history.Undo();
        await tree.FlushAsync();
        bandsField.TryGetCollectionState(out collection).ShouldBeTrue();
        collection.ShouldNotBeNull();
        collection.Value.GetItemState("promo").IsEmpty.ShouldBeTrue();
        live.Section.Tiers[0].Bands.Select(static band => band.Code).ShouldBe(["annual"]);
    }

    [Test]
    public void UnkeyedScalarListsDiffWholeMember()
    {
        var live = new OrderDto
        {
            Number = "ORD-1",
            Tags = new() { "a", "b" },
        };
        var session = live.CreateEditSession();
        session.Observable.Tags.RemoveAt(0);
        session.Observable.Tags.Add("c");

        var view = ((ISparseChangeSet)session.PreviewChangeSet()).CaptureQueryView();
        var state = view.FieldState(OrderDto.SparsePath.Tags.UntypedPath);
        state.IsChanged.ShouldBeTrue();
        ((List<string>)state.Before.Value!).ShouldBe(["a", "b"]);
        ((List<string>)state.After.Value!).ShouldBe(["b", "c"]);
    }

    private static class ChildScope
    {
        public static RenderFragment Root(
            ISparseEditSession<Case6Catalog> session,
            RenderFragment child,
            SparseChildEditMode? mode = null
        ) =>
            builder =>
            {
                builder.OpenComponent<SparseSessionScope<Case6Catalog>>(0);
                builder.AddAttribute(1, nameof(SparseSessionScope<Case6Catalog>.Session), session);
                builder.AddAttribute(
                    2,
                    nameof(SparseSessionScope<Case6Catalog>.ChildContent),
                    child
                );
                builder.AddAttribute(3, nameof(SparseSessionScope<Case6Catalog>.Mode), mode);
                builder.CloseComponent();
            };

        public static RenderFragment Section(
            ISparseEditSessionWithChildren<Case6Catalog, SparseConflict> parent,
            Action<ISparseChildEditContext<Case6Section, SparseConflict>> capture
        ) =>
            Child(
                parent,
                Case6Catalog.SparsePath.Section,
                context =>
                    builder =>
                    {
                        capture(context);
                        builder.AddContent(0, "section");
                    }
            );

        public static RenderFragment SectionWithTier(
            ISparseEditSessionWithChildren<Case6Catalog, SparseConflict> parent,
            Action<ISparseChildEditContext<Case6Tier, SparseConflict>> captureTier,
            Action<SparseFieldContext<Case6Tier, List<Case6Band>>> captureBands
        ) =>
            Child(
                parent,
                Case6Catalog.SparsePath.Section,
                sectionContext =>
                    Child(
                        (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                            sectionContext.Session,
                        Case6Section.SparsePath.Tiers.Key("standard"),
                        tierContext =>
                            builder =>
                            {
                                captureTier(tierContext);
                                builder.OpenComponent<SparseField<Case6Tier, List<Case6Band>>>(0);
                                builder.AddAttribute(
                                    1,
                                    nameof(SparseField<Case6Tier, List<Case6Band>>.Path),
                                    Case6Tier.SparsePath.Bands
                                );
                                builder.AddAttribute(
                                    2,
                                    nameof(SparseField<Case6Tier, List<Case6Band>>.ChildContent),
                                    (RenderFragment<
                                        SparseFieldContext<Case6Tier, List<Case6Band>>
                                    >)(
                                        field =>
                                            fieldBuilder =>
                                            {
                                                captureBands(field);
                                            }
                                    )
                                );
                                builder.CloseComponent();
                            }
                    )
            );

        private static RenderFragment Child<TParent, TChild, TPaths, TConflict, TSession>(
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
                builder.OpenComponent<
                    SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>
                >(0);
                builder.AddAttribute(
                    1,
                    nameof(SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.Path),
                    path
                );
                builder.AddAttribute(
                    2,
                    nameof(
                        SparseChildEdit<TParent, TChild, TPaths, TConflict, TSession>.ChildContent
                    ),
                    childContent
                );
                builder.CloseComponent();
            };
    }
}
