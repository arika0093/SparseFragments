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
        Func<bool> HasChanges,
        Action<bool> SetAutoAccept,
        Action Dispose
    );

    private readonly ISparseEditSession<TModel> _session;
    private readonly Dictionary<SparsePath, ChildEntry> _children = new();
    private bool _disposed;

    internal SparseSessionScopeContext(ISparseEditSession<TModel> session, bool autoAccept)
    {
        _session = session;
        SparseSessionScopeOwnership.Register(session, this);
        AutoAccept = autoAccept;
    }

    /// <summary>Gets whether child sessions accept changes into their parent automatically.</summary>
    public bool AutoAccept { get; private set; }

    /// <summary>Occurs when a child session changes, is accepted, or is removed.</summary>
    public event Action? Changed;

    /// <summary>Opens or reuses a typed child session owned by the scope.</summary>
    /// <remarks>The child is retained by its typed path until the scope is replaced or disposed.</remarks>
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
        if (_children.TryGetValue(untypedPath, out var entry))
        {
            if (
                entry.Context
                is SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession> retained
            )
            {
                return retained;
            }

            throw new InvalidOperationException(
                "A child session with this path was opened using incompatible generated types."
            );
        }

        var child = parentSession.OpenChildSession(path);
        var historyEnabled = false;
        try
        {
            SparseSessionInterop.EnableHistory(child.Session);
            historyEnabled = true;
            var context = new SparseChildEditContext<TChild, TChildPaths, TConflict, TChildSession>(
                child,
                () => RemoveChild(untypedPath),
                AutoAccept,
                NotifyChanged
            );
            _children.Add(
                untypedPath,
                new ChildEntry(
                    context,
                    () => context.HasChanges,
                    context.SetAutoAccept,
                    context.DisposeOwned
                )
            );
            return context;
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

    /// <summary>Gets whether any opened child session has pending changes.</summary>
    public bool HasChanges => _children.Values.Any(static entry => entry.HasChanges());

    internal bool OwnsSession(ISparseEditSession<TModel> session) =>
        !_disposed && ReferenceEquals(_session, session);

    internal void SetAutoAccept(bool autoAccept)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (AutoAccept == autoAccept)
        {
            return;
        }

        AutoAccept = autoAccept;
        foreach (var entry in _children.Values.ToArray())
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
        }
        finally
        {
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

    private void RemoveChild(SparsePath path)
    {
        if (_children.Remove(path, out var entry))
        {
            entry.Dispose();
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
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
        EditContext = child.Session.CreateEditContext();
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
    public TSession Session => _child.Session;

    internal SparseSessionScopeContext<TModel> DescendantScopeContext { get; }

    ISparseEditSession<TModel> ISparseChildEditContext<TModel, TConflict>.Session => Session;

    /// <summary>Gets the generated paths rooted at the child model.</summary>
    public TPaths Path => Session.Paths;

    /// <summary>Gets the Blazor edit context bound to the child model.</summary>
    public EditContext EditContext { get; }

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
    public void Undo() => RequireHistory().Undo();

    /// <summary>Redoes the newest edit in this child session.</summary>
    public void Redo() => RequireHistory().Redo();

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
                SparseSessionInterop.DisableHistory(Session);
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
