using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SparseTypes.Blazor;

internal static class SparseSessionScopeOwnership
{
    private static readonly ConditionalWeakTable<object, object> Owners = new();
    private static readonly object Gate = new();

    internal static void Register(object session, object owner)
    {
        lock (Gate)
        {
            if (Owners.TryGetValue(session, out _))
            {
                throw new InvalidOperationException(
                    "An edit session can have only one SparseSessionScope owner. "
                        + "Reuse its cascaded ScopeContext for all child sessions."
                );
            }

            Owners.Add(session, owner);
        }
    }

    internal static void Release(object session, object owner)
    {
        lock (Gate)
        {
            if (Owners.TryGetValue(session, out var current) && ReferenceEquals(current, owner))
            {
                Owners.Remove(session);
            }
        }
    }
}

/// <summary>Creates and owns typed child sessions for one <see cref="SparseSessionScope{TModel}"/>.</summary>
/// <typeparam name="TModel">The parent session model type.</typeparam>
public sealed class SparseSessionScopeContext<TModel>
    where TModel : class
{
    private sealed record ChildEntry(
        object Context,
        object TargetModel,
        Func<object?> ResolveTarget,
        Func<bool> HasChanges,
        Func<bool> IsApplyingPassThrough,
        Action<SparseChildEditMode> SetMode,
        Action Dispose
    );

    private sealed record DetachedChildEntry(
        SparsePath Path,
        object Context,
        Func<bool> HasChanges,
        Action<SparseChildEditMode> SetMode,
        Action Dispose
    );

    private readonly ISparseEditSession<TModel> _session;
    private readonly SparsePassThroughStateScope _localStateScope;
    private readonly SparsePassThroughStateScope _passThroughStateScope;
    private readonly bool _ownsLocalStateScope;
    private readonly Dictionary<SparsePath, ChildEntry> _children = new();
    private readonly List<DetachedChildEntry> _detached = new();
    private INotifyPropertyChanged? _sessionNotifier;
    private SparsePassThroughStateScope _stateScope;
    private bool _disposed;

    internal SparseSessionScopeContext(
        ISparseEditSession<TModel> session,
        SparseChildEditMode mode,
        SparsePassThroughStateScope? parentStateScope = null
    )
        : this(
            session,
            mode,
            SparsePassThroughStateScope.CreateRoot(session),
            parentStateScope,
            ownsLocalStateScope: true
        ) { }

    internal SparseSessionScopeContext(
        ISparseEditSession<TModel> session,
        SparseChildEditMode mode,
        SparsePassThroughStateScope localStateScope,
        SparsePassThroughStateScope? passThroughStateScope,
        bool ownsLocalStateScope
    )
    {
        _session = session;
        _localStateScope = localStateScope;
        _passThroughStateScope = passThroughStateScope ?? localStateScope;
        _stateScope =
            mode == SparseChildEditMode.PassThrough ? _passThroughStateScope : _localStateScope;
        _ownsLocalStateScope = ownsLocalStateScope;
        try
        {
            SparseSessionScopeOwnership.Register(session, this);
            Mode = mode;
            if (session is INotifyPropertyChanged notifier)
            {
                _sessionNotifier = notifier;
                notifier.PropertyChanged += OnParentSessionChanged;
            }
        }
        catch
        {
            SparseSessionScopeOwnership.Release(session, this);
            if (_ownsLocalStateScope)
            {
                _localStateScope.Dispose();
            }

            throw;
        }
    }

    /// <summary>Gets the active child-edit ownership mode.</summary>
    public SparseChildEditMode Mode { get; private set; }

    /// <summary>Occurs when a child session changes, is accepted, or is removed.</summary>
    public event Action? Changed;

    /// <summary>Opens or reuses a typed child session owned by the scope.</summary>
    /// <remarks>The child is retained by its typed path until the scope is replaced or disposed. A changed child whose target was replaced or deleted moves to the detached holding instead of being disposed; see <see cref="GetDetachedChildPaths"/>.</remarks>
    /// <param name="parentSession">The exact parent session assigned to the owning scope.</param>
    /// <param name="path">A generated typed path to the child model.</param>
    /// <typeparam name="TChild">The child model type.</typeparam>
    /// <typeparam name="TChildPaths">The child model's generated path builder type.</typeparam>
    /// <typeparam name="TConflict">The generated structured conflict type.</typeparam>
    /// <typeparam name="TChildSession">The child model's generated edit-session type.</typeparam>
    /// <returns>The retained child context for this path.</returns>
    /// <exception cref="ArgumentException">The parent session differs from the owning scope session.</exception>
    /// <exception cref="ObjectDisposedException">The scope context has been disposed.</exception>
    public SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession> OpenChildSession<
        TChild,
        TChildPaths,
        TConflict,
        TChildSession
    >(
        ISparseEditSessionWithChildren<TModel, TConflict> parentSession,
        ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path
    )
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
    {
        if (TryOpenChildSession(parentSession, path, out var context))
        {
            return context!;
        }

        throw new ArgumentException(
            "The path does not resolve to a model value of the requested type.",
            nameof(path)
        );
    }

    internal bool TryOpenChildSession<TChild, TChildPaths, TConflict, TChildSession>(
        ISparseEditSessionWithChildren<TModel, TConflict> parentSession,
        ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path,
        out SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>? context
    )
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parentSession);
        ArgumentNullException.ThrowIfNull(path);
        if (!ReferenceEquals(parentSession, _session))
        {
            throw new ArgumentException(
                "The parent session must be the session assigned to this scope.",
                nameof(parentSession)
            );
        }

        var untypedPath = path.UntypedPath;
        var targetModel = ResolveChildModel(parentSession, untypedPath);
        if (_children.TryGetValue(untypedPath, out var entry))
        {
            if (
                targetModel is TChild target
                && ReferenceEquals(entry.TargetModel, target)
                && entry.Context
                    is SparseChildEditContext<
                        TChild,
                        TChildPaths,
                        TConflict,
                        TChildSession
                    > retained
            )
            {
                context = retained;
                return true;
            }

            DetachOrDisposeChild(untypedPath);
            NotifyChanged();
        }

        if (targetModel is not TChild resolvedTarget)
        {
            context = null;
            return false;
        }

        var child = parentSession.OpenChildSession(path);
        var historyEnabled = false;
        SparseSessionScopeContext<TChild>? childScopeContext = null;
        SparsePassThroughStateScope? childLocalStateScope = null;
        try
        {
            SparseSessionInterop.EnableHistory(child.Session);
            historyEnabled = true;
            childLocalStateScope = SparsePassThroughStateScope.CreateRoot(child.Session);
            var childPassThroughStateScope = _stateScope.Push(untypedPath);
            childScopeContext = new SparseSessionScopeContext<TChild>(
                child.Session,
                Mode,
                childLocalStateScope,
                childPassThroughStateScope,
                ownsLocalStateScope: true
            );
            childLocalStateScope = null;
            var childContext = new SparseChildEditContext<
                TChild,
                TChildPaths,
                TConflict,
                TChildSession
            >(
                child,
                parentSession,
                childScopeContext,
                () => DetachOrDisposeChild(untypedPath),
                NotifyChanged
            );
            _children.Add(
                untypedPath,
                new ChildEntry(
                    childContext,
                    resolvedTarget,
                    () => ResolveChildModel(parentSession, untypedPath),
                    () => childContext.HasPendingChanges,
                    () => childContext.IsApplyingPassThrough,
                    childContext.SetMode,
                    childContext.DisposeOwned
                )
            );
            context = childContext;
            return true;
        }
        catch
        {
            try
            {
                childScopeContext?.DisposeChildren();
            }
            finally
            {
                childLocalStateScope?.Dispose();
                if (historyEnabled)
                {
                    SparseSessionInterop.DisableHistory(child.Session);
                }

                child.Dispose();
            }

            throw;
        }
    }

    /// <summary>Gets whether the child at a typed path has pending changes.</summary>
    /// <param name="path">The generated path to the child model.</param>
    /// <typeparam name="TChild">The addressed child model type.</typeparam>
    /// <typeparam name="TChildPaths">The path builder rooted at the child model.</typeparam>
    /// <typeparam name="TChildSession">The generated child edit-session type.</typeparam>
    /// <returns>True when the child session or one of its descendants has pending changes.</returns>
    public bool HasChangesFor<TChild, TChildPaths, TChildSession>(
        ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path
    )
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Mode == SparseChildEditMode.PassThrough)
        {
            return _stateScope.HasChanges(path.UntypedPath);
        }

        return _children.TryGetValue(path.UntypedPath, out var entry) && entry.HasChanges();
    }

    /// <summary>Gets whether any opened or detached child session has pending changes.</summary>
    /// <remarks>Detached drafts keep their edits until the owner recovers or disposes them.</remarks>
    public bool HasChanges =>
        _children.Values.Any(static entry => entry.HasChanges())
        || _detached.Any(static entry => entry.HasChanges());

    /// <summary>Gets the paths of detached child drafts retained by the scope.</summary>
    /// <remarks>A changed child whose target was replaced or deleted moves to the detached holding instead of being disposed; see <see cref="GetDetachedChildPaths"/>.</remarks>
    /// <returns>The paths with a retained detached draft.</returns>
    public IReadOnlyList<SparsePath> GetDetachedChildPaths() =>
        _detached.Select(static entry => entry.Path).ToArray();

    /// <summary>Gets a detached child draft retained for a typed path.</summary>
    /// <param name="parentSession">The exact parent session assigned to the owning scope.</param>
    /// <param name="path">The generated path the draft was opened with.</param>
    /// <param name="context">The retained child context on success; otherwise null.</param>
    /// <typeparam name="TChild">The child model type.</typeparam>
    /// <typeparam name="TChildPaths">The child model's generated path builder type.</typeparam>
    /// <typeparam name="TConflict">The generated structured conflict type.</typeparam>
    /// <typeparam name="TChildSession">The child model's generated edit-session type.</typeparam>
    /// <returns>True when a detached draft is retained for the path; otherwise false.</returns>
    /// <exception cref="ArgumentException">The parent session differs from the owning scope session.</exception>
    /// <exception cref="ObjectDisposedException">The scope context has been disposed.</exception>
    public bool TryGetDetachedChild<TChild, TChildPaths, TConflict, TChildSession>(
        ISparseEditSessionWithChildren<TModel, TConflict> parentSession,
        ISparseChildEditPath<TModel, TChild, TChildPaths, TChildSession> path,
        out SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>? context
    )
        where TChild : class
        where TChildPaths : ISparseTypedPath<TChild, TChild>
        where TConflict : class
        where TChildSession : ISparseEditSessionWithPaths<TChild, TChildPaths>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parentSession);
        ArgumentNullException.ThrowIfNull(path);
        if (!ReferenceEquals(parentSession, _session))
        {
            throw new ArgumentException(
                "The parent session must be the session assigned to this scope.",
                nameof(parentSession)
            );
        }

        foreach (var entry in _detached)
        {
            if (
                entry.Path.Equals(path.UntypedPath)
                && entry.Context
                    is SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession> match
            )
            {
                context = match;
                return true;
            }
        }

        context = null;
        return false;
    }

    /// <summary>Disposes every detached child draft retained by the scope.</summary>
    /// <remarks>Use this to abandon drafts that can no longer be recovered, for example after copying their edits elsewhere.</remarks>
    /// <exception cref="ObjectDisposedException">The scope context has been disposed.</exception>
    public void DisposeDetachedChildren()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_detached.Count == 0)
        {
            return;
        }

        List<Exception>? failures = null;
        foreach (var entry in _detached)
        {
            try
            {
                entry.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        _detached.Clear();
        NotifyChanged();
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more detached child sessions failed to dispose.",
                failures
            );
        }
    }

    internal bool OwnsSession(ISparseEditSession<TModel> session) =>
        !_disposed && ReferenceEquals(_session, session);

    internal bool IsChildSessionCurrent(SparsePath path, object context) =>
        !_disposed
        && _children.TryGetValue(path, out var entry)
        && ReferenceEquals(entry.Context, context)
        && ReferenceEquals(entry.TargetModel, entry.ResolveTarget());

    internal SparsePassThroughStateScope StateScope => _stateScope;

    internal bool HasChangesAtRoot => _stateScope.HasChanges(SparsePath.Root<TModel>());

    internal bool TryFieldState<TValue>(
        SparsePath path,
        out SparseFieldState<TValue> state,
        out string? error
    ) => _stateScope.TryFieldState(path, out state, out error);

    internal bool TryCollectionState(SparsePath path, out SparseCollectionState? state) =>
        _stateScope.TryCollectionState(path, out state);

    internal void SetMode(SparseChildEditMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Mode == mode)
        {
            return;
        }

        if (HasChanges || _detached.Any(static entry => entry.HasChanges()))
        {
            throw new InvalidOperationException(
                "Cannot change child edit mode while child edits are pending."
            );
        }

        Mode = mode;
        _stateScope =
            mode == SparseChildEditMode.PassThrough ? _passThroughStateScope : _localStateScope;
        foreach (var entry in _children.Values.ToArray())
        {
            entry.SetMode(mode);
        }

        foreach (var entry in _detached.ToArray())
        {
            entry.SetMode(mode);
        }

        Changed?.Invoke();
    }

    internal void DisposeChildren()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;
        try
        {
            foreach (var entry in _children.Values)
            {
                try
                {
                    entry.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            _children.Clear();
            foreach (var entry in _detached)
            {
                try
                {
                    entry.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            _detached.Clear();
        }
        finally
        {
            if (_sessionNotifier is not null)
            {
                _sessionNotifier.PropertyChanged -= OnParentSessionChanged;
                _sessionNotifier = null;
            }

            SparseSessionScopeOwnership.Release(_session, this);
            if (_ownsLocalStateScope)
            {
                _localStateScope.Dispose();
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more owned child sessions failed to dispose.",
                failures
            );
        }
    }

    private void DetachOrDisposeChild(SparsePath path)
    {
        if (!_children.Remove(path, out var entry))
        {
            return;
        }

        if (entry.HasChanges())
        {
            _detached.Add(
                new DetachedChildEntry(
                    path,
                    entry.Context,
                    entry.HasChanges,
                    entry.SetMode,
                    entry.Dispose
                )
            );
        }
        else
        {
            entry.Dispose();
        }
    }

    private void NotifyChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    private static object? ResolveChildModel<TConflict>(
        ISparseEditSessionWithChildren<TModel, TConflict> parentSession,
        SparsePath path
    )
        where TConflict : class
    {
        if (parentSession is not ISparseEditSessionWithChildStatus<TModel, TConflict> childStatus)
        {
            throw new InvalidOperationException(
                "The parent session does not support resolving child model paths."
            );
        }

        return childStatus.ResolveChildModel(path);
    }

    private void OnParentSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        var changed = false;
        foreach (var (path, entry) in _children.ToArray())
        {
            if (entry.IsApplyingPassThrough())
            {
                continue;
            }

            if (!ReferenceEquals(entry.TargetModel, entry.ResolveTarget()))
            {
                DetachOrDisposeChild(path);
                changed = true;
                continue;
            }

            if (Mode == SparseChildEditMode.PassThrough && !entry.HasChanges())
            {
                _children.Remove(path);
                entry.Dispose();
                changed = true;
            }
        }

        if (changed)
        {
            NotifyChanged();
        }
    }
}
