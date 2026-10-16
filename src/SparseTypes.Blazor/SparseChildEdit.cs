using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>Opens a retained child session by typed path and scopes its content with <see cref="SparseEdit{TModel, TPaths}"/>.</summary>
/// <typeparam name="TParent">The model owned by the parent session scope.</typeparam>
/// <typeparam name="TChild">The child model addressed by <see cref="Path"/>.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The generated structured conflict type for the parent session.</typeparam>
/// <typeparam name="TChildSession">The generated child edit-session type.</typeparam>
public class SparseChildEdit<TParent, TChild, TChildPaths, TConflict, TChildSession> : ComponentBase
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
            || _context is null
            || !scope.IsChildSessionCurrent(untypedPath, _context)
        )
        {
            if (parent is not ISparseEditSessionWithChildren<TParent, TConflict> parentWithChildren)
            {
                throw new InvalidOperationException(
                    $"The parent session type '{parent.GetType()}' does not support child sessions."
                );
            }

            if (!scope.TryOpenChildSession(parentWithChildren, path, out _context))
            {
                _context = null;
                _boundParentSession = parent;
                _boundScopeContext = scope;
                _boundPath = untypedPath;
                _hasBoundPath = true;
                return;
            }

            _boundParentSession = parent;
            _boundScopeContext = scope;
            _boundPath = untypedPath;
            _hasBoundPath = true;
        }
    }

    /// <summary>Resolves the live child context without ever reading a disposed one.</summary>
    /// <remarks>Shares the scope check with <see cref="OnParametersSet"/> but reports transient states (teardown, replaced target, mid-disposal scope) as null instead of throwing; the rebind render that follows fills the subtree back in. Opening here is side-effect free for rendering: it only registers the retained child, never notifies.</remarks>
    /// <returns>The current context, or null when the subtree must stay blank for this batch.</returns>
    private SparseChildEditContext<
        TChild,
        TChildPaths,
        TConflict,
        TChildSession
    >? TryResolveCurrentContext()
    {
        var scope = ScopeContext;
        var parent = ParentSession;
        var path = Path;
        if (scope is null || parent is null || path is null || !scope.OwnsSession(parent))
        {
            return null;
        }

        if (parent is not ISparseEditSessionWithChildren<TParent, TConflict> parentWithChildren)
        {
            return null;
        }

        try
        {
            if (
                !scope.TryOpenChildSession(parentWithChildren, path, out var resolved)
                || resolved is null
                || resolved.IsDisposed
            )
            {
                return null;
            }

            _context = resolved;
            _boundParentSession = parent;
            _boundScopeContext = scope;
            _boundPath = path.UntypedPath;
            _hasBoundPath = true;
            return resolved;
        }
        catch (ObjectDisposedException)
        {
            // The scope is mid-disposal (its entries are being released while
            // its own disposed flag is already set). The teardown or rebind
            // render that follows owns the subtree from here.
            return null;
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var content = ChildContent;
        if (content is null)
        {
            return;
        }

        // Re-resolve instead of trusting the retained context: a descendant
        // render (for example a child SparseEdit answering its own session
        // notification) can run after the scope disposed this context but
        // before the owning scope re-renders and rebinds. Rendering nothing
        // keeps that transient batch safe; the rebind render follows it.
        var context = TryResolveCurrentContext();
        if (context is null)
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
                                                        {
                                                            // Retained fragments execute on child
                                                            // renders too, so resolve again here:
                                                            // the bound context may have been
                                                            // disposed since this tree was built.
                                                            var current =
                                                                TryResolveCurrentContext();
                                                            if (current is null)
                                                            {
                                                                return;
                                                            }

                                                            childBuilder.AddContent(
                                                                0,
                                                                content(current)
                                                            );
                                                        }
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

/// <summary>Opens a child session using the standard <see cref="SparseConflict"/> conflict type.</summary>
/// <typeparam name="TParent">The model owned by the parent session scope.</typeparam>
/// <typeparam name="TChild">The child model addressed by <see cref="Path"/>.</typeparam>
/// <typeparam name="TChildPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TChildSession">The generated child edit-session type.</typeparam>
public sealed class SparseChildEditDefault<TParent, TChild, TChildPaths, TChildSession>
    : SparseChildEdit<TParent, TChild, TChildPaths, SparseConflict, TChildSession>
    where TParent : class
    where TChild : class
    where TChildPaths : ISparseTypedPath<TChild, TChild>
    where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths> { }
