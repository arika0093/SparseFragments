using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Two-phase mode transitions (#314): a PassThrough switch validates every
// live and detached context, including nested descendants, before mutating
// anything, so an incompatible child rejects without partial mutation.
public sealed class ScopeModeTransitionTests
{
    // A session decorator that hides INotifyPropertyChanged while delegating
    // everything else, modelling a custom Draft-compatible child session.
    private sealed class NonNotifyingSession<TChild, TPaths, TRealSession>(TRealSession inner)
        : ISparseEditSessionWithPaths<TChild, TPaths>,
            ISparseEditSessionWithHistory,
            ISparseEditSessionModelAccessor<TChild>
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TRealSession : ISparseEditSessionWithPaths<TChild, TPaths>
    {
        private readonly TRealSession _inner = inner;

        public object Inner => _inner;

        public TChild Model => _inner.Model;

        public bool HasChanges => _inner.HasChanges;

        public void AcceptChanges() => _inner.AcceptChanges();

        public TPaths Paths => _inner.Paths;

        public TChild GetModelForFrameworkAccess() =>
            _inner is ISparseEditSessionModelAccessor<TChild> accessor
                ? accessor.GetModelForFrameworkAccess()
                : _inner.Model;

        public ISparseEditHistory? History => ((ISparseEditSessionWithHistory)_inner).History;

        public event Action? HistoryChanged
        {
            add => ((ISparseEditSessionWithHistory)_inner).HistoryChanged += value;
            remove => ((ISparseEditSessionWithHistory)_inner).HistoryChanged -= value;
        }

        public ISparseEditHistory EnableHistory(EditHistoryOptions? options = null) =>
            ((ISparseEditSessionWithHistory)_inner).EnableHistory(options);

        public void DisableHistory() => ((ISparseEditSessionWithHistory)_inner).DisableHistory();
    }

    private sealed class NonNotifyingPath<TParent, TChild, TPaths, TRealSession>(
        ISparseChildEditPath<TParent, TChild, TPaths, TRealSession> inner
    )
        : ISparseChildEditPath<
            TParent,
            TChild,
            TPaths,
            NonNotifyingSession<TChild, TPaths, TRealSession>
        >
        where TParent : class
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TRealSession : ISparseEditSessionWithPaths<TChild, TPaths>
    {
        private readonly ISparseChildEditPath<TParent, TChild, TPaths, TRealSession> _inner = inner;

        public TPaths CreateRootPaths() => _inner.CreateRootPaths();

        public TChild Clone(TChild model) => _inner.Clone(model);

        public NonNotifyingSession<TChild, TPaths, TRealSession> CreateEditSession(
            TChild baseline,
            TChild current,
            Action? onChanged
        ) => new(_inner.CreateEditSession(baseline, current, onChanged));

        public SparsePath<TParent, TChild> TypedPath => _inner.TypedPath;

        public SparsePath UntypedPath => _inner.UntypedPath;

        public bool CanWriteModelValueInPlace => _inner.CanWriteModelValueInPlace;

        public void WriteModelValueInPlace(TChild current, TChild updated) =>
            _inner.WriteModelValueInPlace(current, updated);
    }

    private static ISparseChildEditPath<
        TParent,
        TChild,
        TPaths,
        NonNotifyingSession<TChild, TPaths, TRealSession>
    > WithoutNotifier<TParent, TChild, TPaths, TRealSession>(
        ISparseChildEditPath<TParent, TChild, TPaths, TRealSession> inner
    )
        where TParent : class
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TRealSession : ISparseEditSessionWithPaths<TChild, TPaths> =>
        new NonNotifyingPath<TParent, TChild, TPaths, TRealSession>(inner);

    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
                new OrderLine
                {
                    Sku = "b",
                    Quantity = 2,
                    Price = 20m,
                },
            },
        };

    [Test]
    public async Task PassThroughSwitchRejectsIncompatibleChildAfterCompatibleChild()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(parent);
        var scope = host.ScopeContext;
        var compatible = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var incompatible = scope.OpenChildSession(
            parent,
            WithoutNotifier(OrderDto.SparsePath.Lines.Key("b"))
        );

        var rejection = await host.TrySetModeAsync(SparseChildEditMode.PassThrough);

        rejection.ShouldNotBeNull();
        rejection.ShouldBeOfType<InvalidOperationException>();
        rejection!.Message.ShouldContain("reports property changes");
        scope.Mode.ShouldBe(SparseChildEditMode.Draft);
        compatible.Mode.ShouldBe(SparseChildEditMode.Draft);
        incompatible.Mode.ShouldBe(SparseChildEditMode.Draft);
        scope.HasChanges.ShouldBeFalse();

        // The rejected switch leaves Draft fully working: edits, conflicts,
        // disposal, rendering, and a later supported mode all still work.
        compatible.Session.Observable.Name = "Draft";
        compatible.HasChanges.ShouldBeTrue();
        scope.HasChanges.ShouldBeTrue();
        await host.FlushAsync();
        await host.SetModeAsync(SparseChildEditMode.Draft);
        scope.Mode.ShouldBe(SparseChildEditMode.Draft);
        compatible.DiscardChanges();
        scope.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task PassThroughSwitchRejectsIncompatibleChildBeforeCompatibleChild()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(parent);
        var scope = host.ScopeContext;
        var incompatible = scope.OpenChildSession(
            parent,
            WithoutNotifier(OrderDto.SparsePath.Lines.Key("a"))
        );
        var compatible = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);

        var rejection = await host.TrySetModeAsync(SparseChildEditMode.PassThrough);

        rejection.ShouldNotBeNull();
        rejection.ShouldBeOfType<InvalidOperationException>();
        scope.Mode.ShouldBe(SparseChildEditMode.Draft);
        incompatible.Mode.ShouldBe(SparseChildEditMode.Draft);
        compatible.Mode.ShouldBe(SparseChildEditMode.Draft);

        // Neither the state source nor the sessions moved: both children stay
        // usable and report no changes.
        ((OrderLine.EditSession)incompatible.Session.Inner).Observable.Quantity.ShouldBe(1);
        compatible.Session.Observable.Name.ShouldBe("Ada");
        scope.HasChanges.ShouldBeFalse();
        await host.FlushAsync();
    }

    [Test]
    public async Task PassThroughSwitchRejectsCleanDetachedIncompatibleChild()
    {
        var parent = Order().CreateEditSession();
        using var host = await ScopeTestHost.CreateAsync(parent);
        var scope = host.ScopeContext;
        var compatible = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var path = OrderDto.SparsePath.Lines.Key("a");
        var customPath = WithoutNotifier(path);
        var incompatible = scope.OpenChildSession(parent, customPath);
        ((OrderLine.EditSession)incompatible.Session.Inner).Observable.Quantity = 9;

        // Removing the target detaches the pending draft; discarding leaves a
        // clean detached draft that still blocks PassThrough.
        parent.Observable.Lines.RemoveAt(0);
        await host.FlushAsync();
        scope.GetDetachedChildPaths().ShouldContain(path.UntypedPath);
        incompatible.DiscardChanges();

        var rejection = await host.TrySetModeAsync(SparseChildEditMode.PassThrough);

        rejection.ShouldNotBeNull();
        rejection.ShouldBeOfType<InvalidOperationException>();
        scope.Mode.ShouldBe(SparseChildEditMode.Draft);
        compatible.Mode.ShouldBe(SparseChildEditMode.Draft);
        incompatible.Mode.ShouldBe(SparseChildEditMode.Draft);
        scope.GetDetachedChildPaths().ShouldContain(path.UntypedPath);
        scope.TryGetDetachedChild(parent, customPath, out var recovered).ShouldBeTrue();
        recovered.ShouldBeSameAs(incompatible);

        // The retained draft is still editable after the rejection.
        ((OrderLine.EditSession)incompatible.Session.Inner).Observable.Quantity.ShouldBe(1);
    }

    [Test]
    public async Task PassThroughSwitchRejectsNestedIncompatibleGrandchild()
    {
        var live = new Case6Catalog
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
                    },
                },
            },
        };
        var session = live.CreateEditSession();
        var sectionContexts = new List<ISparseChildEditContext<Case6Section, SparseConflict>>();
        var tierContexts = new List<ISparseChildEditContext<Case6Tier, SparseConflict>>();
        var shownNote = string.Empty;

        RenderFragment SectionContent(ISparseEditSession<Case6Catalog> parent) =>
            NestedChild(
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

                        builder.AddContent(
                            0,
                            Headless.Field(context.Path.Note, field => shownNote = field.Value)
                        );
                        builder.AddContent(
                            1,
                            NestedChild(
                                (ISparseEditSessionWithChildren<Case6Section, SparseConflict>)
                                    context.Session,
                                WithoutNotifier(Case6Section.SparsePath.Tiers.Key("standard")),
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

                                        tierBuilder.AddContent(0, "tier");
                                    }
                            )
                        );
                    }
            );

        RenderFragment Root(SparseChildEditMode? mode) =>
            builder =>
            {
                builder.OpenComponent<SparseSessionScope<Case6Catalog>>(0);
                builder.AddAttribute(1, nameof(SparseSessionScope<Case6Catalog>.Session), session);
                builder.AddAttribute(
                    2,
                    nameof(SparseSessionScope<Case6Catalog>.ChildContent),
                    SectionContent(session)
                );
                builder.AddAttribute(3, nameof(SparseSessionScope<Case6Catalog>.Mode), mode);
                builder.CloseComponent();
            };

        using var tree = await Headless.Tree.CreateAsync(Root(null));
        sectionContexts.ShouldHaveSingleItem();
        tierContexts.ShouldHaveSingleItem();
        shownNote.ShouldBe("Baseline");

        var rejection = await tree.TryShowAsync(Root(SparseChildEditMode.PassThrough));

        rejection.ShouldNotBeNull();
        rejection.ShouldBeOfType<InvalidOperationException>();
        sectionContexts[^1].Mode.ShouldBe(SparseChildEditMode.Draft);
        tierContexts[^1].Mode.ShouldBe(SparseChildEditMode.Draft);

        // Draft editing, conflict tracking, and rendering still work after
        // the rejected switch, and staying on Draft remains possible.
        ((Case6Section.EditSession)sectionContexts[^1].Session)
            .Observable
            .Note = "Draft";
        await tree.FlushAsync();
        shownNote.ShouldBe("Draft");
        sectionContexts[^1].HasChanges.ShouldBeTrue();
        var stayed = await tree.TryShowAsync(Root(SparseChildEditMode.Draft));
        stayed.ShouldBeNull();
        await tree.FlushAsync();
        shownNote.ShouldBe("Draft");
    }

    private static RenderFragment NestedChild<TParent, TChild, TPaths, TConflict, TSession>(
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
}
