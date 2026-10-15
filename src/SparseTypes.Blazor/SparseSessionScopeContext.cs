using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Forms;

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
        Action<bool> SetAutoAccept,
        Action Dispose
    );

    private sealed record DetachedChildEntry(
        SparsePath Path,
        object Context,
        Func<bool> HasChanges,
        Action<bool> SetAutoAccept,
        Action Dispose
    );

    private readonly ISparseEditSession<TModel> _session;
    private readonly Dictionary<SparsePath, ChildEntry> _children = new();
    private readonly List<DetachedChildEntry> _detached = new();
    private INotifyPropertyChanged? _sessionNotifier;
    private bool _disposed;

    internal SparseSessionScopeContext(ISparseEditSession<TModel> session, bool autoAccept)
    {
        _session = session;
        SparseSessionScopeOwnership.Register(session, this);
        AutoAccept = autoAccept;
        if (session is INotifyPropertyChanged notifier)
        {
            _sessionNotifier = notifier;
            notifier.PropertyChanged += OnParentSessionChanged;
        }
    }

    /// <summary>Gets whether child sessions accept changes into their parent automatically.</summary>
    public bool AutoAccept { get; private set; }

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
        }

        if (targetModel is not TChild resolvedTarget)
        {
            context = null;
            return false;
        }

        var child = parentSession.OpenChildSession(path);
        var historyEnabled = false;
        try
        {
            SparseSessionInterop.EnableHistory(child.Session);
            historyEnabled = true;
            var childContext = new SparseChildEditContext<
                TChild,
                TChildPaths,
                TConflict,
                TChildSession
            >(child, () => DetachOrDisposeChild(untypedPath), AutoAccept, NotifyChanged);
            _children.Add(
                untypedPath,
                new ChildEntry(
                    childContext,
                    resolvedTarget,
                    () => ResolveChildModel(parentSession, untypedPath),
                    () => childContext.HasChanges,
                    childContext.SetAutoAccept,
                    childContext.DisposeOwned
                )
            );
            context = childContext;
            return true;
        }
        catch
        {
            if (historyEnabled)
            {
                SparseSessionInterop.DisableHistory(child.Session);
            }

            child.Dispose();
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

    internal void SetAutoAccept(bool autoAccept)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (AutoAccept == autoAccept)
        {
            return;
        }

        if (HasChanges)
        {
            throw new InvalidOperationException(
                "Cannot change automatic child acceptance while child edits are pending."
            );
        }

        AutoAccept = autoAccept;
        foreach (var entry in _children.Values.ToArray())
        {
            entry.SetAutoAccept(autoAccept);
        }

        foreach (var entry in _detached.ToArray())
        {
            entry.SetAutoAccept(autoAccept);
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
                    entry.SetAutoAccept,
                    entry.Dispose
                )
            );
        }
        else
        {
            entry.Dispose();
        }

        NotifyChanged();
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
        foreach (var (path, entry) in _children.ToArray())
        {
            if (!ReferenceEquals(entry.TargetModel, entry.ResolveTarget()))
            {
                DetachOrDisposeChild(path);
            }
        }
    }
}

/// <summary>A typed child-session context owned by a <see cref="SparseSessionScopeContext{TModel}"/>.</summary>
/// <typeparam name="TModel">The child model type.</typeparam>
/// <typeparam name="TPaths">The generated path builder rooted at the child model.</typeparam>
/// <typeparam name="TConflict">The generated structured conflict type.</typeparam>
/// <typeparam name="TSession">The generated child-model edit-session type.</typeparam>
public sealed class SparseChildEditContext<TModel, TPaths, TConflict, TSession>
    : ISparseChildEditContext<TModel, TConflict>
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
    where TConflict : class
    where TSession : ISparseEditSessionWithPaths<TModel, TPaths>
{
    private readonly ISparseEditChildSession<TModel, TPaths, TConflict, TSession> _child;
    private readonly Action _invalidate;
    private readonly Action _changed;
    private readonly EditContext _editContext;
    private INotifyPropertyChanged? _notifier;
    private bool _autoAccept;
    private bool _handlingSessionChange;
    private bool _disposed;
    private IReadOnlyList<TConflict>? _autoAcceptConflicts;

    internal SparseChildEditContext(
        ISparseEditChildSession<TModel, TPaths, TConflict, TSession> child,
        Action invalidate,
        bool autoAccept,
        Action changed
    )
    {
        _child = child;
        _invalidate = invalidate;
        _changed = changed;
        _editContext = child.Session.CreateEditContext();
        if (autoAccept && child.Session is not INotifyPropertyChanged)
        {
            throw new InvalidOperationException(
                "Automatic child acceptance requires a session that reports property changes."
            );
        }

        _autoAccept = autoAccept;
        DescendantScopeContext = new SparseSessionScopeContext<TModel>(child.Session, autoAccept);
        if (child.Session is INotifyPropertyChanged notifier)
        {
            _notifier = notifier;
            notifier.PropertyChanged += OnSessionPropertyChanged;
        }
    }

    /// <summary>Gets the generated edit session for the child model.</summary>
    public TSession Session
    {
        get
        {
            ThrowIfDisposed();
            return _child.Session;
        }
    }

    internal SparseSessionScopeContext<TModel> DescendantScopeContext { get; }

    // Lets render fragments re-resolve liveness without touching Session.
    internal bool IsDisposed => _disposed;

    ISparseEditSession<TModel> ISparseChildEditContext<TModel, TConflict>.Session => Session;

    /// <summary>Gets the generated paths rooted at the child model.</summary>
    public TPaths Path => Session.Paths;

    /// <summary>Gets the Blazor edit context bound to the child model.</summary>
    public EditContext EditContext
    {
        get
        {
            ThrowIfDisposed();
            return _editContext;
        }
    }

    /// <summary>Gets whether this child session or its descendants have pending changes.</summary>
    public bool HasChanges => _child.HasChanges;

    /// <summary>Gets conflicts from the most recent automatic acceptance attempt.</summary>
    public IReadOnlyList<TConflict>? AutoAcceptConflicts => _autoAcceptConflicts;

    /// <summary>Occurs after the parent accepts this child session.</summary>
    public event Action? AutoAccepted;

    /// <summary>Gets whether the child history can undo an edit.</summary>
    public bool CanUndo => SparseSessionInterop.TryGetHistory(Session)?.CanUndo == true;

    /// <summary>Gets whether the child history can redo an edit.</summary>
    public bool CanRedo => SparseSessionInterop.TryGetHistory(Session)?.CanRedo == true;

    /// <summary>Attempts to merge this child draft into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the draft was accepted; otherwise false.</returns>
    public bool TryAcceptChanges(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisposed();
        var accepted = _child.TryAcceptChanges(out conflicts);
        if (accepted)
        {
            _autoAcceptConflicts = null;
        }

        if (!accepted && conflicts is null)
        {
            _invalidate();
        }

        return accepted;
    }

    /// <summary>Merges this child draft into its parent, throwing when the merge conflicts.</summary>
    public void AcceptChanges() => _child.AcceptChanges();

    /// <summary>Discards pending child edits while keeping the child session connected.</summary>
    public void DiscardChanges()
    {
        _autoAcceptConflicts = null;
        _child.DiscardChanges();
        _changed();
    }

    /// <summary>Undoes the newest edit in this child session.</summary>
    public void Undo()
    {
        ThrowIfDisposed();
        RequireHistory().Undo();
    }

    /// <summary>Redoes the newest edit in this child session.</summary>
    public void Redo()
    {
        ThrowIfDisposed();
        RequireHistory().Redo();
    }

    internal void DisposeOwned()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notifier is not null)
        {
            _notifier.PropertyChanged -= OnSessionPropertyChanged;
            _notifier = null;
        }

        try
        {
            DescendantScopeContext.DisposeChildren();
        }
        finally
        {
            try
            {
                SparseSessionInterop.DisableHistory(_child.Session);
            }
            finally
            {
                _child.Dispose();
            }
        }
    }

    internal void SetAutoAccept(bool autoAccept)
    {
        ThrowIfDisposed();
        if (autoAccept && _notifier is null)
        {
            throw new InvalidOperationException(
                "Automatic child acceptance requires a session that reports property changes."
            );
        }

        _autoAccept = autoAccept;
        DescendantScopeContext.SetAutoAccept(autoAccept);
        if (autoAccept)
        {
            TryAutoAccept();
        }
        else
        {
            _autoAcceptConflicts = null;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || _handlingSessionChange)
        {
            return;
        }

        _handlingSessionChange = true;
        try
        {
            TryAutoAccept();
        }
        finally
        {
            _handlingSessionChange = false;
            _changed();
        }
    }

    private void TryAutoAccept()
    {
        if (!_autoAccept || !HasChanges)
        {
            return;
        }

        var accepted = TryAcceptChanges(out var conflicts);
        _autoAcceptConflicts = accepted ? null : conflicts;
        if (accepted)
        {
            AutoAccepted?.Invoke();
        }
    }

    private ISparseEditHistory RequireHistory() =>
        SparseSessionInterop.TryGetHistory(Session)
        ?? throw new InvalidOperationException("The child session has no enabled edit history.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
