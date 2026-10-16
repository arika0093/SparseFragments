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
    internal const string ModeCascadeName = "SparseSessionScope.Mode";
    internal const string StateScopeCascadeName = "SparseSessionScope.StateScope";

    private INotifyPropertyChanged? _subscribedNotifier;
    private ISparseEditSession<TModel>? _subscribedSession;
    private SparsePassThroughStateScope? _activeParentStateScope;
    private SparseSessionScopeContext<TModel>? _scopeContext;
    private SparseChildEditMode _effectiveMode;
    private bool _disposed;

    [CascadingParameter(Name = ModeCascadeName)]
    private SparseChildEditMode? ParentMode { get; set; }

    [CascadingParameter(Name = StateScopeCascadeName)]
    private SparsePassThroughStateScope? ParentStateScope { get; set; }

    /// <summary>Gets or sets the active edit session cascaded to descendants.</summary>
    /// <remarks>Replacing the reference unsubscribes the previous session before subscribing the next one.</remarks>
    [Parameter]
    public ISparseEditSession<TModel>? Session { get; set; }

    /// <summary>Gets the active context cascaded to descendants, or null when no session is assigned.</summary>
    public SparseSessionScopeContext<TModel>? ScopeContext => _scopeContext;

    /// <summary>Gets or sets how child editors relate their state to this session.</summary>
    /// <remarks>The root defaults to <see cref="SparseChildEditMode.Draft"/>; nested scopes inherit their parent's mode.</remarks>
    [Parameter]
    public SparseChildEditMode? Mode { get; set; }

    /// <summary>Gets or sets the content rendered inside the session cascade.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var mode = ResolveMode(Mode, ParentMode);
        if (
            !ReferenceEquals(_subscribedSession, Session)
            || !ReferenceEquals(_activeParentStateScope, ParentStateScope)
        )
        {
            Unsubscribe();
            UnsubscribeScopeContext();
            _scopeContext?.DisposeChildren();
            _scopeContext = Session is null
                ? null
                : new SparseSessionScopeContext<TModel>(Session, mode, ParentStateScope);
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
        else if (_scopeContext?.Mode != mode)
        {
            _scopeContext?.SetMode(mode);
        }

        _activeParentStateScope = ParentStateScope;
        _effectiveMode = mode;
    }

    private static SparseChildEditMode ResolveMode(
        SparseChildEditMode? mode,
        SparseChildEditMode? parentMode
    )
    {
        var effectiveMode = mode ?? parentMode ?? SparseChildEditMode.Draft;
        if (!Enum.IsDefined(typeof(SparseChildEditMode), effectiveMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                effectiveMode,
                "Unknown child edit mode."
            );
        }

        return effectiveMode;
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<SparseChildEditMode?>>(0);
        builder.AddAttribute(1, nameof(CascadingValue<SparseChildEditMode?>.Value), _effectiveMode);
        builder.AddAttribute(2, nameof(CascadingValue<SparseChildEditMode?>.Name), ModeCascadeName);
        builder.AddAttribute(
            3,
            nameof(CascadingValue<SparseChildEditMode?>.ChildContent),
            (RenderFragment)(
                modeBuilder =>
                {
                    modeBuilder.OpenComponent<CascadingValue<SparsePassThroughStateScope?>>(0);
                    modeBuilder.AddAttribute(
                        1,
                        nameof(CascadingValue<SparsePassThroughStateScope?>.Value),
                        _scopeContext?.StateScope
                    );
                    modeBuilder.AddAttribute(
                        2,
                        nameof(CascadingValue<SparsePassThroughStateScope?>.Name),
                        StateScopeCascadeName
                    );
                    modeBuilder.AddAttribute(
                        3,
                        nameof(CascadingValue<SparsePassThroughStateScope?>.ChildContent),
                        (RenderFragment)(
                            stateScopeBuilder =>
                            {
                                stateScopeBuilder.OpenComponent<
                                    CascadingValue<ISparseEditSession<TModel>>
                                >(0);
                                stateScopeBuilder.AddAttribute(
                                    1,
                                    nameof(CascadingValue<ISparseEditSession<TModel>>.Value),
                                    Session
                                );
                                stateScopeBuilder.AddAttribute(
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
                                stateScopeBuilder.CloseComponent();
                            }
                        )
                    );
                    modeBuilder.CloseComponent();
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
