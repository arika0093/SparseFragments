using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>
/// Cascades the active edit session and refreshes the subtree when the session reports changes.
/// </summary>
/// <remarks>
/// The scope renders no markup of its own: it wraps <see cref="ChildContent"/> in a cascading
/// value holding <see cref="Session"/> so descendants can declare a
/// <c>CascadingParameter</c> for <c>ISparseEditSession&lt;TModel&gt;</c> instead of receiving
/// the session through every intermediate component. Refresh is session-level: the scope
/// subscribes to <see cref="INotifyPropertyChanged.PropertyChanged"/> on the session and
/// re-renders through <c>InvokeAsync</c>, so callbacks always run on the Blazor
/// synchronization context. It never reads the live model, which keeps the session's
/// observable-only <c>HasChanges</c> cache intact; descendants read change state during
/// their own render. One outer <c>BatchEdit</c> raises one notification, so one user action
/// refreshes once. The scope needs no dependency injection and imposes no layout.
/// </remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public sealed class SparseSessionScope<TModel> : ComponentBase, IDisposable
    where TModel : class
{
    private INotifyPropertyChanged? _subscribedNotifier;
    private ISparseEditSession<TModel>? _subscribedSession;
    private bool _disposed;

    /// <summary>Gets or sets the active edit session cascaded to descendants.</summary>
    /// <remarks>
    /// Replacing the reference unsubscribes the previous session before subscribing the next
    /// one, so stale sessions can no longer refresh this subtree. A null value clears the
    /// cascade and unsubscribes without rendering anything extra.
    /// </remarks>
    [Parameter]
    public ISparseEditSession<TModel>? Session { get; set; }

    /// <summary>Gets or sets the content rendered inside the session cascade.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!ReferenceEquals(_subscribedSession, Session))
        {
            Unsubscribe();
            _subscribedSession = Session;
            if (Session is INotifyPropertyChanged notifier)
            {
                _subscribedNotifier = notifier;
                notifier.PropertyChanged += OnSessionPropertyChanged;
            }
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<ISparseEditSession<TModel>>>(0);
        builder.AddAttribute(1, nameof(CascadingValue<ISparseEditSession<TModel>>.Value), Session);
        builder.AddAttribute(
            2,
            nameof(CascadingValue<ISparseEditSession<TModel>>.ChildContent),
            ChildContent
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

    private void Unsubscribe()
    {
        if (_subscribedNotifier is not null)
        {
            _subscribedNotifier.PropertyChanged -= OnSessionPropertyChanged;
            _subscribedNotifier = null;
        }

        _subscribedSession = null;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        // The session raises synchronously on the editing thread, which may not be the
        // Blazor thread, so the render is always marshalled through the dispatcher.
        _ = InvokeAsync(StateHasChanged);
    }
}
