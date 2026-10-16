using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

public sealed class SparseChildEditTests
{
    private static RenderFragment Child<TParent, TChild, TPaths, TConflict, TSession>(
        ISparseEditSessionWithChildren<TParent, TConflict> parent,
        ISparseChildEditPath<TParent, TChild, TPaths, TSession> path,
        ISparseTypedPath<TChild, string> fieldPath,
        Action<ISparseChildEditContext<TChild, TConflict>> capture,
        Action<SparseFieldContext<TChild, string>> captureName
    )
        where TParent : class
        where TChild : class
        where TPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TSession : ISparseEditSessionWithPaths<TChild, TPaths> =>
        ChildWithContent(
            parent,
            path,
            context =>
                builder =>
                {
                    capture(context);
                    builder.AddContent(0, Headless.Field(fieldPath, captureName));
                }
        );

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

    private static RenderFragment ScopeContent<TModel>(
        ISparseEditSession<TModel> session,
        RenderFragment child,
        Action<SparseSessionScope<TModel>> capture,
        SparseChildEditMode? mode = null
    )
        where TModel : class =>
        builder =>
        {
            builder.OpenComponent<SparseSessionScope<TModel>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<TModel>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<TModel>.ChildContent), child);
            builder.AddAttribute(3, nameof(SparseSessionScope<TModel>.Mode), mode);
            builder.AddComponentReferenceCapture(
                4,
                component => capture((SparseSessionScope<TModel>)component)
            );
            builder.CloseComponent();
        };

    private sealed class PassThroughProbe : ComponentBase
    {
        [CascadingParameter(Name = "scope-pass-through")]
        public string? Value { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    [Test]
    public async Task ChildFragmentRebindsToNewParentSessionAndRetainsSharedPath()
    {
        var first = new OrderDto
        {
            Customer = new OrderCustomer { Name = "Ada" },
        }.CreateEditSession();
        var second = new OrderDto
        {
            Customer = new OrderCustomer { Name = "Grace" },
        }.CreateEditSession();
        var contexts = new List<ISparseChildEditContext<OrderCustomer, SparseConflict>>();
        var fields = new List<SparseFieldContext<OrderCustomer, string>>();
        SparseSessionScope<OrderDto>? owner = null;
        void CaptureContext(ISparseChildEditContext<OrderCustomer, SparseConflict> context)
        {
            if (contexts.Count == 0 || !ReferenceEquals(contexts[^1], context))
            {
                contexts.Add(context);
            }
        }

        RenderFragment ChildFor(ISparseEditSession<OrderDto> parent) =>
            Child(
                (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)parent,
                OrderDto.SparsePath.Customer,
                OrderCustomer.SparsePath.Name,
                CaptureContext,
                fields.Add
            );
        RenderFragment ChildPair(ISparseEditSession<OrderDto> parent) =>
            builder =>
            {
                builder.AddContent(0, ChildFor(parent));
                builder.AddContent(1, ChildFor(parent));
            };

        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(first, ChildPair(first), captured => owner = captured)
        );

        contexts.Count.ShouldBe(1);
        fields[^1].Value.ShouldBe("Ada");
        var firstContext = contexts[0];
        ((OrderCustomer.EditSession)firstContext.Session).Observable.Name = "Draft";
        firstContext.HasChanges.ShouldBeTrue();
        first.GetModelForFrameworkAccess().Customer.Name.ShouldBe("Ada");

        await tree.ShowAsync(ScopeContent(second, ChildPair(second), captured => owner = captured));

        contexts.Count.ShouldBe(2);
        ReferenceEquals(contexts[1], firstContext).ShouldBeFalse();
        fields[^1].Value.ShouldBe("Grace");
        ((OrderCustomer.EditSession)contexts[1].Session).Observable.Name.ShouldBe("Grace");
        owner?.Dispose();
    }

    [Test]
    public async Task ChildFragmentKeepsTheGeneratedSessionType()
    {
        var root = new OrderDto { Customer = new OrderCustomer { Name = "Ada" } };
        var session = root.CreateEditSession();
        OrderCustomer.EditSession? typedSession = null;
        var child = ChildWithContent(
            (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)session,
            OrderDto.SparsePath.Customer,
            context =>
                builder =>
                {
                    typedSession = context.Session;
                    builder.AddContent(0, context.Session.Observable.Name);
                }
        );

        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, child, static _ => { })
        );

        typedSession.ShouldNotBeNull();
        typedSession.Observable.Name.ShouldBe("Ada");
        typedSession.Observable.Name = "Typed draft";
        await tree.FlushAsync();
        typedSession.FieldState(OrderCustomer.SparsePath.Name).IsChanged.ShouldBeTrue();
        session.GetModelForFrameworkAccess().Customer.Name.ShouldBe("Ada");
    }

    [Test]
    public async Task PassThroughFieldsHighlightParentChangesUntilTheParentAccepts()
    {
        var model = new OrderDto { Customer = new OrderCustomer { Name = "Ada" } };
        var session = model.CreateEditSession();
        var contexts = new List<ISparseChildEditContext<OrderCustomer, SparseConflict>>();
        var fields = new List<SparseFieldContext<OrderCustomer, string>>();
        var child = Child(
            (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)session,
            OrderDto.SparsePath.Customer,
            OrderCustomer.SparsePath.Name,
            contexts.Add,
            fields.Add
        );
        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, child, static _ => { }, SparseChildEditMode.PassThrough)
        );

        var context = contexts.ShouldHaveSingleItem();
        ((OrderCustomer.EditSession)context.Session).Observable.Name = "Grace";
        await tree.FlushAsync();

        model.Customer.Name.ShouldBe("Grace");
        session.HasOwnChanges.ShouldBeTrue();
        context.HasChanges.ShouldBeTrue();
        fields[^1].IsChanged.ShouldBeTrue();

        session.AcceptChanges();
        await tree.FlushAsync();

        session.HasOwnChanges.ShouldBeFalse();
        context.HasChanges.ShouldBeFalse();
        fields[^1].IsChanged.ShouldBeFalse();
    }

    [Test]
    public async Task DefaultRazorChildEditRendersNestedPathsWithoutGeneratedTypeNames()
    {
        var session = new ScopeGrandchildRoot
        {
            Child = new ScopeGrandchildChild
            {
                Leaf = new ScopeGrandchildLeaf { Name = "Baseline" },
            },
        }.CreateEditSession();
        RenderFragment probe = builder =>
        {
            builder.OpenComponent<ChildEditInferenceProbe>(0);
            builder.AddAttribute(1, nameof(ChildEditInferenceProbe.Session), session);
            builder.CloseComponent();
        };

        using var tree = await Headless.Tree.CreateAsync(probe);

        await tree.FlushAsync();
        session.HasOwnChanges.ShouldBeFalse();
    }

    [Test]
    public async Task DuplicateScopeOwnerForOneSessionFailsClearly()
    {
        var session = new OrderDto().CreateEditSession();
        SparseSessionScope<OrderDto>? owner = null;
        RenderFragment duplicate = builder =>
        {
            builder.OpenComponent<SparseSessionScope<OrderDto>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<OrderDto>.Session), session);
            builder.AddAttribute(
                2,
                nameof(SparseSessionScope<OrderDto>.ChildContent),
                (RenderFragment)(
                    nested =>
                    {
                        nested.OpenComponent<SparseSessionScope<OrderDto>>(0);
                        nested.AddAttribute(
                            1,
                            nameof(SparseSessionScope<OrderDto>.Session),
                            session
                        );
                        nested.CloseComponent();
                    }
                )
            );
            builder.AddComponentReferenceCapture(
                3,
                component => owner = (SparseSessionScope<OrderDto>)component
            );
            builder.CloseComponent();
        };
        using var tree = await Headless.Tree.CreateAsync(builder => { });

        var failure = await tree.TryShowAsync(duplicate);

        failure.ShouldNotBeNull();
        failure.ToString().ShouldContain("only one SparseSessionScope owner");
        owner?.Dispose();
    }

    [Test]
    public async Task NestedChildScopesOpenGrandchildSessionsAndPassThroughCascades()
    {
        var root = new ScopeGrandchildRoot
        {
            Title = "Root",
            Child = new ScopeGrandchildChild
            {
                Leaf = new ScopeGrandchildLeaf { Name = "Baseline" },
            },
        };
        var rootSession = root.CreateEditSession();
        var rootHistory = rootSession.EnableHistory();
        ISparseChildEditContext<ScopeGrandchildChild, SparseConflict>? childContext = null;
        ISparseChildEditContext<ScopeGrandchildLeaf, SparseConflict>? leafContext = null;
        SparseFieldContext<ScopeGrandchildLeaf, string>? leafName = null;
        PassThroughProbe? passThrough = null;

        var childContent = ChildWithContent(
            (ISparseEditSessionWithChildren<ScopeGrandchildRoot, SparseConflict>)rootSession,
            ScopeGrandchildRoot.SparsePath.Child,
            context =>
                builder =>
                {
                    childContext = context;
                    var childSession = (ScopeGrandchildChild.EditSession)context.Session;
                    builder.AddContent(
                        0,
                        ChildWithContent(
                            (ISparseEditSessionWithChildren<ScopeGrandchildChild, SparseConflict>)
                                childSession,
                            ScopeGrandchildChild.SparsePath.Leaf,
                            leaf =>
                                leafBuilder =>
                                {
                                    leafContext = leaf;
                                    leafBuilder.AddContent(
                                        0,
                                        Headless.Field(
                                            ScopeGrandchildLeaf.SparsePath.Name,
                                            field => leafName = field
                                        )
                                    );
                                    leafBuilder.OpenComponent<PassThroughProbe>(1);
                                    leafBuilder.AddComponentReferenceCapture(
                                        2,
                                        component => passThrough = (PassThroughProbe)component
                                    );
                                    leafBuilder.CloseComponent();
                                }
                        )
                    );
                }
        );

        RenderFragment cascadingContent = builder =>
        {
            builder.OpenComponent<CascadingValue<string>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<string>.Name), "scope-pass-through");
            builder.AddAttribute(2, nameof(CascadingValue<string>.Value), "preserved");
            builder.AddAttribute(
                3,
                nameof(CascadingValue<string>.ChildContent),
                ScopeContent(rootSession, childContent, _ => { })
            );
            builder.CloseComponent();
        };

        using var tree = await Headless.Tree.CreateAsync(cascadingContent);

        childContext.ShouldNotBeNull();
        leafContext.ShouldNotBeNull();
        leafName.ShouldNotBeNull();
        leafName!.Value.ShouldBe("Baseline");
        passThrough.ShouldNotBeNull();
        passThrough!.Value.ShouldBe("preserved");

        ((ScopeGrandchildLeaf.EditSession)leafContext!.Session).Observable.Name = "Draft";
        await tree.FlushAsync();

        leafContext.HasChanges.ShouldBeTrue();
        childContext!.HasChanges.ShouldBeTrue();
        leafContext.CanUndo.ShouldBeTrue();
        rootHistory.CanUndo.ShouldBeFalse();
        leafName!.Value.ShouldBe("Draft");
        rootSession.GetModelForFrameworkAccess().Child.Leaf.Name.ShouldBe("Baseline");
        ((ScopeGrandchildChild.EditSession)childContext!.Session)
            .GetModelForFrameworkAccess()
            .Leaf.Name.ShouldBe("Baseline");

        rootSession.Observable.Title = "Direct";
        await tree.FlushAsync();
        rootHistory.CanUndo.ShouldBeTrue();
        leafContext.CanUndo.ShouldBeTrue();
    }

    [Test]
    public async Task ChildFragmentReopensWhenModelChangesAtTheSameParentPath()
    {
        var root = new OrderDto { Customer = new OrderCustomer { Name = "Ada" } };
        var session = root.CreateEditSession();
        var contexts = new List<ISparseChildEditContext<OrderCustomer, SparseConflict>>();

        var child = ChildWithContent(
            (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)session,
            OrderDto.SparsePath.Customer,
            context =>
                builder =>
                {
                    if (contexts.Count == 0 || !ReferenceEquals(contexts[^1], context))
                    {
                        contexts.Add(context);
                    }
                    builder.AddContent(0, "child");
                }
        );
        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, child, static _ => { })
        );

        var original = contexts.ShouldHaveSingleItem();
        session.Descriptors.TryGet(nameof(OrderDto.Customer), out var customer).ShouldBeTrue();
        customer.TrySetValue(new OrderCustomer { Name = "Grace" }).ShouldBeTrue();
        await tree.FlushAsync();

        contexts.Count.ShouldBe(2);
        ReferenceEquals(contexts[1], original).ShouldBeFalse();
        ((OrderCustomer.EditSession)contexts[1].Session).Observable.Name.ShouldBe("Grace");
        Should.Throw<ObjectDisposedException>(() => original.Session);
        Should.Throw<ObjectDisposedException>(() => original.Undo());
    }

    [Test]
    public async Task DeletedKeyedChildContextRetainsDraftInDetachedHolding()
    {
        var root = new OrderDto { Lines = [new OrderLine { Sku = "line", Quantity = 1 }] };
        var session = root.CreateEditSession();
        ISparseChildEditContext<OrderLine, SparseConflict>? childContext = null;
        var child = ChildWithContent(
            (ISparseEditSessionWithChildren<OrderDto, SparseConflict>)session,
            OrderDto.SparsePath.Lines.Key("line"),
            context =>
                builder =>
                {
                    childContext = context;
                    builder.AddContent(0, "line");
                }
        );
        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, child, static _ => { })
        );

        var detachedContext = childContext.ShouldNotBeNull();
        ((OrderLine.EditSession)detachedContext.Session).Observable.Quantity = 4;
        session.Observable.Lines.Clear();
        await tree.FlushAsync();

        // The draft survives deletion: it stays editable and inspectable.
        detachedContext.HasChanges.ShouldBeTrue();
        ((OrderLine.EditSession)detachedContext.Session).Observable.Quantity.ShouldBe(4);
        detachedContext.Undo();
        ((OrderLine.EditSession)detachedContext.Session).Observable.Quantity.ShouldBe(1);
        detachedContext.Redo();
        ((OrderLine.EditSession)detachedContext.Session).Observable.Quantity.ShouldBe(4);

        // Acceptance reports the missing target without abandoning the draft.
        detachedContext.TryAcceptChanges(out var conflicts).ShouldBeFalse();
        conflicts.ShouldBeNull();
        detachedContext.HasChanges.ShouldBeTrue();
        session.Observable.Lines.ShouldBeEmpty();

        detachedContext.DiscardChanges();
        detachedContext.HasChanges.ShouldBeFalse();
        ((OrderLine.EditSession)detachedContext.Session).Observable.Quantity.ShouldBe(1);
    }

    [Test]
    public async Task PassThroughModeCannotChangeWhileChildEditsArePending()
    {
        var root = new ScopeGrandchildRoot
        {
            Child = new ScopeGrandchildChild
            {
                Leaf = new ScopeGrandchildLeaf { Name = "Baseline" },
            },
        };
        var session = root.CreateEditSession();
        ISparseChildEditContext<ScopeGrandchildChild, SparseConflict>? childContext = null;
        var child = ChildWithContent(
            (ISparseEditSessionWithChildren<ScopeGrandchildRoot, SparseConflict>)session,
            ScopeGrandchildRoot.SparsePath.Child,
            context =>
                builder =>
                {
                    childContext = context;
                    builder.AddContent(0, "child");
                }
        );
        using var tree = await Headless.Tree.CreateAsync(
            ScopeContent(session, child, static _ => { })
        );

        var context = childContext.ShouldNotBeNull();
        ((ScopeGrandchildChild.EditSession)context.Session).Observable.Leaf!.Name = "Draft";
        await tree.FlushAsync();
        context.HasChanges.ShouldBeTrue();

        var failure = await tree.TryShowAsync(
            ScopeContent(session, child, static _ => { }, mode: SparseChildEditMode.PassThrough)
        );

        failure.ShouldNotBeNull();
        failure!.ToString().ShouldContain("while child edits are pending");
        context.HasChanges.ShouldBeTrue();
        session.GetModelForFrameworkAccess().Child.Leaf.Name.ShouldBe("Baseline");
    }
}
