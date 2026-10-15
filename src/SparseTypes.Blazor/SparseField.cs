using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>
/// Appearance-free field over one typed path with Path-only generic inference.
/// </summary>
/// <remarks><c>TModel</c> and <c>TValue</c> infer from <see cref="Path"/>, so no explicit <c>TValue</c> or per-model Blazor context is needed.</remarks>
/// <typeparam name="TModel">The root session model type.</typeparam>
/// <typeparam name="TValue">The addressed value type.</typeparam>
public sealed class SparseField<TModel, TValue> : ComponentBase, IDisposable
    where TModel : class
{
    private ISparseEditScope<TModel>? _boundScope;
    private ISparseEditSession<TModel>? _boundSession;
    private EditContext? _boundEditContext;
    private SparsePath _bindingPath;
    private bool _hasBinding;
    private SparseFieldBinding<TModel, TValue>? _binding;
    private SparseFieldContext<TModel, TValue>? _field;
    private EditContext? _subscribedContext;
    private bool _disposed;

    /// <summary>Gets or sets the typed path this field binds.</summary>
    [Parameter]
    public ISparseTypedPath<TModel, TValue>? Path { get; set; }

    /// <summary>Gets or sets the content rendered with the live field context.</summary>
    [Parameter]
    public RenderFragment<SparseFieldContext<TModel, TValue>>? ChildContent { get; set; }

    /// <summary>Gets or sets the cascaded edit scope provided by the owning edit root.</summary>
    [CascadingParameter]
    public ISparseEditScope<TModel>? Scope { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var scope =
            Scope
            ?? throw new InvalidOperationException(
                "SparseField must be placed inside a SparseEdit for the same model type; "
                    + "no edit scope was cascaded."
            );
        var path =
            Path ?? throw new InvalidOperationException("SparseField requires a Path parameter.");
        var session = scope.Session;
        var editContext = scope.EditContext;
        var untyped = path.UntypedPath;
        if (
            !_hasBinding
            || _binding is null
            || !ReferenceEquals(_boundScope, scope)
            || !ReferenceEquals(_boundSession, session)
            || !ReferenceEquals(_boundEditContext, editContext)
            || !_bindingPath.Equals(untyped)
        )
        {
            Rebind(scope, session, editContext, path, untyped);
        }

        // Read one session snapshot per field render; BatchEdit coalesces
        // updates.
        if (
            SparseSessionInterop.TryFieldState(
                session,
                _binding!.TypedPath,
                out var state,
                out var error
            )
        )
        {
            _field!.State = state;
            _field.StateQueryError = null;
        }
        else
        {
            _field!.State = default;
            _field.StateQueryError = error;
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_field is not null && ChildContent is not null)
        {
            builder.AddContent(0, ChildContent(_field));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribedContext is not null)
        {
            _subscribedContext.OnValidationStateChanged -= OnValidationStateChanged;
            _subscribedContext = null;
        }
    }

    private void Rebind(
        ISparseEditScope<TModel> scope,
        ISparseEditSession<TModel> session,
        EditContext editContext,
        ISparseTypedPath<TModel, TValue> path,
        SparsePath untyped
    )
    {
        if (_subscribedContext is not null)
        {
            _subscribedContext.OnValidationStateChanged -= OnValidationStateChanged;
            _subscribedContext = null;
        }

        // The binding holds no subscriptions.
        var binding = session.Bind(path.TypedPath, editContext);
        _binding = binding;
        _field = new SparseFieldContext<TModel, TValue>(this, session, binding, path);
        _boundScope = scope;
        _boundSession = session;
        _boundEditContext = editContext;
        _bindingPath = untyped;
        _hasBinding = true;
        editContext.OnValidationStateChanged += OnValidationStateChanged;
        _subscribedContext = editContext;
    }

    private void OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(StateHasChanged);
    }
}
