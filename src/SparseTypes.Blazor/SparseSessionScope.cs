using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor;

/// <summary>Cascades the active edit session and refreshes the subtree when the session reports changes.</summary>
/// <remarks>The scope renders no markup of its own.</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
public sealed class SparseSessionScope<TModel> : ComponentBase, IDisposable
    where TModel : class
{
    internal const string AutoAcceptCascadeName = "SparseSessionScope.AutoAccept";

    private INotifyPropertyChanged? _subscribedNotifier;
    private ISparseEditSession<TModel>? _subscribedSession;
    private SparseSessionScopeContext<TModel>? _scopeContext;
    private bool _effectiveAutoAccept;
    private bool _disposed;

    [CascadingParameter(Name = AutoAcceptCascadeName)]
    private bool? ParentAutoAccept { get; set; }

    /// <summary>Gets or sets the active edit session cascaded to descendants.</summary>
    /// <remarks>Replacing the reference unsubscribes the previous session before subscribing the next one.</remarks>
    [Parameter]
    public ISparseEditSession<TModel>? Session { get; set; }

    /// <summary>Gets or sets whether child session changes are accepted automatically.</summary>
    /// <remarks>Nested scopes inherit this setting. Failed acceptance keeps the child draft available.</remarks>
    [Parameter]
    public bool? AutoAccept { get; set; }

    /// <summary>Gets or sets the content rendered inside the session cascade.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var autoAccept = AutoAccept ?? ParentAutoAccept ?? false;
        if (!ReferenceEquals(_subscribedSession, Session))
        {
            Unsubscribe();
            UnsubscribeScopeContext();
            _scopeContext?.DisposeChildren();
            _scopeContext = Session is null
                ? null
                : new SparseSessionScopeContext<TModel>(Session, autoAccept);
            if (_scopeContext is not null)
            {
                _scopeContext.Changed += OnChildSessionChanged;
            }

            _subscribedSession = Session;
            if (Session is INotifyPropertyChanged notifier)
            {
                _subscribedNotifier = notifier;
                notifier.PropertyChanged += OnSessionPropertyChanged;
            }
        }
        else if (_effectiveAutoAccept != autoAccept)
        {
            _scopeContext?.SetAutoAccept(autoAccept);
        }

        _effectiveAutoAccept = autoAccept;
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<bool?>>(0);
        builder.AddAttribute(1, nameof(CascadingValue<bool?>.Value), _effectiveAutoAccept);
        builder.AddAttribute(2, nameof(CascadingValue<bool?>.Name), AutoAcceptCascadeName);
        builder.AddAttribute(
            3,
            nameof(CascadingValue<bool?>.ChildContent),
            (RenderFragment)(
                autoAcceptBuilder =>
                {
                    autoAcceptBuilder.OpenComponent<CascadingValue<ISparseEditSession<TModel>>>(0);
                    autoAcceptBuilder.AddAttribute(
                        1,
                        nameof(CascadingValue<ISparseEditSession<TModel>>.Value),
                        Session
                    );
                    autoAcceptBuilder.AddAttribute(
                        2,
                        nameof(CascadingValue<ISparseEditSession<TModel>>.ChildContent),
                        (RenderFragment)(
                            sessionBuilder =>
                            {
                                sessionBuilder.OpenComponent<
                                    CascadingValue<SparseSessionScopeContext<TModel>?>
                                >(0);
                                sessionBuilder.AddAttribute(
                                    1,
                                    nameof(
                                        CascadingValue<SparseSessionScopeContext<TModel>?>.Value
                                    ),
                                    _scopeContext
                                );
                                sessionBuilder.AddAttribute(
                                    2,
                                    nameof(
                                        CascadingValue<SparseSessionScopeContext<TModel>?>.ChildContent
                                    ),
                                    ChildContent
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unsubscribe();
        UnsubscribeScopeContext();
        _scopeContext?.DisposeChildren();
        _scopeContext = null;
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

    private void UnsubscribeScopeContext()
    {
        if (_scopeContext is not null)
        {
            _scopeContext.Changed -= OnChildSessionChanged;
        }
    }

    private void OnChildSessionChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        // The session raises synchronously on the editing thread.
        _ = InvokeAsync(StateHasChanged);
    }
}
