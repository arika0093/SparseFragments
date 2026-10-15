using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>Opens a retained child session by typed path and scopes its content with <see cref="SparseEdit{TModel, TPaths}"/>.</summary>
/// <typeparam name="TParent">The model owned by the parent session scope.</typeparam>
/// <typeparam name="TChild">The child model addressed by <see cref="Path"/>.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The generated structured conflict type for the parent session.</typeparam>
/// <typeparam name="TChildSession">The generated child edit-session type.</typeparam>
public sealed class SparseChildEdit<TParent, TChild, TChildPaths, TConflict, TChildSession>
    : ComponentBase
    where TParent : class
    where TChild : class
    where TChildPaths : ISparseTypedPath<TChild, TChild>
    where TConflict : class
    where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
{
    private SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>? _context;
    private ISparseEditSession<TParent>? _boundParentSession;
    private SparseSessionScopeContext<TParent>? _boundScopeContext;
    private SparsePath _boundPath;
    private bool _hasBoundPath;

    /// <summary>Gets the parent session cascaded by the owning session scope.</summary>
    [CascadingParameter]
    public ISparseEditSession<TParent>? ParentSession { get; set; }

    /// <summary>Gets the child registry cascaded by the owning session scope.</summary>
    [CascadingParameter]
    public SparseSessionScopeContext<TParent>? ScopeContext { get; set; }

    /// <summary>Gets or sets the typed path to the child model.</summary>
    [Parameter]
    public ISparseChildEditPath<TParent, TChild, TChildPaths, TChildSession>? Path { get; set; }

    /// <summary>Gets or sets the content rendered in the child edit scope.</summary>
    [Parameter]
    public RenderFragment<
        SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>
    >? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var parent =
            ParentSession
            ?? throw new InvalidOperationException(
                "SparseChildEdit must be placed inside a SparseSessionScope for its parent model."
            );
        var scope =
            ScopeContext
            ?? throw new InvalidOperationException(
                "SparseChildEdit requires the ScopeContext from its owning SparseSessionScope."
            );
        var path =
            Path ?? throw new InvalidOperationException("SparseChildEdit requires a child Path.");
        if (!scope.OwnsSession(parent))
        {
            _context = null;
            return;
        }

        var untypedPath = path.UntypedPath;
        if (
            !ReferenceEquals(_boundParentSession, parent)
            || !ReferenceEquals(_boundScopeContext, scope)
            || !_hasBoundPath
            || !_boundPath.Equals(untypedPath)
        )
        {
            if (parent is not ISparseEditSessionWithChildren<TParent, TConflict> parentWithChildren)
            {
                throw new InvalidOperationException(
                    $"The parent session type '{parent.GetType()}' does not support child sessions."
                );
            }

            _context = scope.OpenChildSession(parentWithChildren, path);
            _boundParentSession = parent;
            _boundScopeContext = scope;
            _boundPath = untypedPath;
            _hasBoundPath = true;
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var context = _context;
        var content = ChildContent;
        if (context is null || content is null)
        {
            return;
        }

        builder.OpenComponent<CascadingValue<bool?>>(0);
        builder.AddAttribute(
            1,
            nameof(CascadingValue<bool?>.Value),
            context.DescendantScopeContext.AutoAccept
        );
        builder.AddAttribute(
            2,
            nameof(CascadingValue<bool?>.Name),
            SparseSessionScope<TChild>.AutoAcceptCascadeName
        );
        builder.AddAttribute(
            3,
            nameof(CascadingValue<bool?>.ChildContent),
            (RenderFragment)(
                autoAcceptBuilder =>
                {
                    autoAcceptBuilder.OpenComponent<CascadingValue<ISparseEditSession<TChild>>>(0);
                    autoAcceptBuilder.AddAttribute(
                        1,
                        nameof(CascadingValue<ISparseEditSession<TChild>>.Value),
                        context.Session
                    );
                    autoAcceptBuilder.AddAttribute(
                        2,
                        nameof(CascadingValue<ISparseEditSession<TChild>>.ChildContent),
                        (RenderFragment)(
                            sessionBuilder =>
                            {
                                sessionBuilder.OpenComponent<
                                    CascadingValue<SparseSessionScopeContext<TChild>?>
                                >(0);
                                sessionBuilder.AddAttribute(
                                    1,
                                    nameof(
                                        CascadingValue<SparseSessionScopeContext<TChild>?>.Value
                                    ),
                                    context.DescendantScopeContext
                                );
                                sessionBuilder.AddAttribute(
                                    2,
                                    nameof(
                                        CascadingValue<SparseSessionScopeContext<TChild>?>.ChildContent
                                    ),
                                    (RenderFragment)(
                                        scopeBuilder =>
                                        {
                                            scopeBuilder.OpenComponent<
                                                SparseEdit<TChild, TChildPaths>
                                            >(0);
                                            scopeBuilder.AddAttribute(
                                                1,
                                                nameof(SparseEdit<TChild, TChildPaths>.Session),
                                                context.Session
                                            );
                                            scopeBuilder.AddAttribute(
                                                2,
                                                nameof(
                                                    SparseEdit<TChild, TChildPaths>.ChildContent
                                                ),
                                                (RenderFragment<
                                                    SparseEditContext<TChild, TChildPaths>
                                                >)(
                                                    _ =>
                                                        childBuilder =>
                                                            childBuilder.AddContent(
                                                                0,
                                                                content(context)
                                                            )
                                                )
                                            );
                                            scopeBuilder.CloseComponent();
                                        }
                                    )
                                );
                                sessionBuilder.CloseComponent();
                            }
                        )
                    );
                    autoAcceptBuilder.CloseComponent();
                }
            )
        );
        builder.CloseComponent();
    }
}
