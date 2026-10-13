using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>
/// Appearance-free edit root over a path-capable session with Session-only parameters.
/// </summary>
/// <remarks>
/// <para>
/// Declares only <see cref="Session"/> and <see cref="ChildContent"/>: <c>TModel</c> and the
/// concrete generated <c>TPaths</c> infer from <see cref="Session"/>, so no <c>Paths</c>
/// parameter, explicit <c>TValue</c>, or per-model Blazor context is needed for ordinary use.
/// The template context exposes the concrete <c>Path</c> builder, the original-model
/// <see cref="EditContext"/>, live <c>HasChanges</c>, and the session-owned history surface.
/// </para>
/// <para>
/// The component creates the <see cref="EditContext"/> from the session and cascades an
/// <see cref="ISparseEditScope{TModel}"/> for <see cref="SparseField{TModel, TValue}"/>.
/// It subscribes to session notifications (one <c>BatchEdit</c> raises one notification, so
/// one user action refreshes once), history availability, and history stack changes, always
/// marshalling through the Blazor dispatcher. Replacing <see cref="Session"/> unsubscribes
/// the previous session and history before subscribing the next one and builds a fresh
/// <see cref="EditContext"/>; key the <c>EditForm</c> on the session so inputs rebuild
/// against the new context. Disposal unsubscribes only: the session, its model, its edit
/// context attachments, and the session-owned history are never disposed here. History is
/// owned and configured by the session and is never enabled by this component.
/// </para>
/// </remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
/// <typeparam name="TPaths">The concrete generated root path builder type.</typeparam>
public sealed class SparseEdit<TModel, TPaths> : ComponentBase, IDisposable
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
{
    private ISparseEditSessionWithPaths<TModel, TPaths>? _session;
    private SparseEditContext<TModel, TPaths>? _context;
    private INotifyPropertyChanged? _notifier;
    private Action? _unsubscribeHistoryChanged;
    private ISparseEditHistory? _history;
    private bool _disposed;

    /// <summary>Gets or sets the active path-capable edit session.</summary>
    /// <remarks>
    /// Replacing the reference unsubscribes the previous session and history, builds a fresh
    /// original-model edit context, and subscribes the next session. A null value clears the
    /// cascade and renders nothing.
    /// </remarks>
    [Parameter]
    public ISparseEditSessionWithPaths<TModel, TPaths>? Session { get; set; }

    /// <summary>Gets or sets the content rendered inside the session cascade.</summary>
    [Parameter]
    public RenderFragment<SparseEditContext<TModel, TPaths>>? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!ReferenceEquals(_session, Session))
        {
            Unsubscribe();
            _session = Session;
            if (Session is not null)
            {
                // Created through trusted framework access, so the observable
                // change cache stays intact for later HasChanges reads.
                _context = new SparseEditContext<TModel, TPaths>(
                    Session,
                    Session.Paths,
                    Session.CreateEditContext()
                );
                Subscribe(Session);
            }
            else
            {
                _context = null;
            }
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

        // No markup of its own: only the scope cascade, so rendering stays
        // layout, CSS, and vendor neutral.
        builder.OpenComponent<CascadingValue<ISparseEditScope<TModel>>>(0);
        builder.AddAttribute(1, nameof(CascadingValue<ISparseEditScope<TModel>>.Value), context);
        builder.AddAttribute(
            2,
            nameof(CascadingValue<ISparseEditScope<TModel>>.ChildContent),
            (RenderFragment)(cascade => cascade.AddContent(0, content(context)))
        );
        builder.CloseComponent();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unsubscribe();
    }

    private void Subscribe(ISparseEditSessionWithPaths<TModel, TPaths> session)
    {
        if (session is INotifyPropertyChanged notifier)
        {
            _notifier = notifier;
            notifier.PropertyChanged += OnSessionChanged;
        }

        _unsubscribeHistoryChanged = SparseSessionInterop.SubscribeHistoryChanged(
            session,
            OnHistoryAvailabilityChanged
        );
        WireHistory(SparseSessionInterop.TryGetHistory(session));
    }

    private void Unsubscribe()
    {
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSessionChanged;
            _notifier = null;
        }

        _unsubscribeHistoryChanged?.Invoke();
        _unsubscribeHistoryChanged = null;
        if (_history is not null)
        {
            // Detach only; the history stays owned by its session and is
            // never disposed here, so remounts observe the retained stacks.
            _history.StateChanged -= OnHistoryStateChanged;
            _history = null;
        }
    }

    private void WireHistory(ISparseEditHistory? history)
    {
        if (!ReferenceEquals(_history, history))
        {
            if (_history is not null)
            {
                _history.StateChanged -= OnHistoryStateChanged;
            }

            _history = history;
            if (_history is not null)
            {
                _history.StateChanged += OnHistoryStateChanged;
            }
        }
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        // Session notifications arrive on the editing thread, so the render
        // is always marshalled through the dispatcher.
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnHistoryAvailabilityChanged()
    {
        if (_disposed)
        {
            return;
        }

        // EnableHistory/DisableHistory swaps the owned control; rewire the
        // stack subscription instead of keeping the released instance.
        if (_session is not null)
        {
            WireHistory(SparseSessionInterop.TryGetHistory(_session));
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private void OnHistoryStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(StateHasChanged);
    }
}
