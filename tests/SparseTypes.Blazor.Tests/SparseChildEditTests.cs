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
        Action<SparseSessionScope<TModel>> capture
    )
        where TModel : class =>
        builder =>
        {
            builder.OpenComponent<SparseSessionScope<TModel>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<TModel>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<TModel>.ChildContent), child);
            builder.AddComponentReferenceCapture(
                3,
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
            Child = new ScopeGrandchildChild
            {
                Leaf = new ScopeGrandchildLeaf { Name = "Baseline" },
            },
        };
        var rootSession = root.CreateEditSession();
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
        leafName!.Value.ShouldBe("Draft");
        rootSession.GetModelForFrameworkAccess().Child.Leaf.Name.ShouldBe("Baseline");
        ((ScopeGrandchildChild.EditSession)childContext!.Session)
            .GetModelForFrameworkAccess()
            .Leaf.Name.ShouldBe("Baseline");
    }
}
