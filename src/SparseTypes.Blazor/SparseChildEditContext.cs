using System.ComponentModel;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

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
    private readonly object _parentSession;
    private readonly Action _invalidate;
    private readonly Action _changed;
    private readonly EditContext _editContext;
    private INotifyPropertyChanged? _notifier;
    private bool _handlingSessionChange;
    private bool _isApplyingPassThrough;
    private bool _disposed;
    private IReadOnlyList<TConflict>? _conflicts;

    internal SparseChildEditContext(
        ISparseEditChildSession<TModel, TPaths, TConflict, TSession> child,
        object parentSession,
        SparseSessionScopeContext<TModel> descendantScopeContext,
        Action invalidate,
        Action changed
    )
    {
        _child = child;
        _parentSession = parentSession;
        DescendantScopeContext = descendantScopeContext;
        _invalidate = invalidate;
        _changed = changed;
        _editContext = child.Session.CreateEditContext();
        if (child.Session is INotifyPropertyChanged notifier)
        {
            _notifier = notifier;
            notifier.PropertyChanged += OnSessionPropertyChanged;
        }
        else if (descendantScopeContext.Mode == SparseChildEditMode.PassThrough)
        {
            throw new InvalidOperationException(
                "Pass-through child edits require a session that reports property changes."
            );
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

    internal bool HasPendingChanges => _child.HasChanges;

    internal bool IsApplyingPassThrough => _isApplyingPassThrough;

    // Counts successful draft acceptances (including automatic pass-through
    // merges). The owning scope uses it to tell a freshly opened clean child
    // from one whose draft was resynchronized after parent drift.
    internal int AcceptVersion { get; private set; }

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

    /// <summary>Gets the ownership mode used by this child context.</summary>
    public SparseChildEditMode Mode => DescendantScopeContext.Mode;

    /// <summary>Gets whether this child has changes under the active mode.</summary>
    public bool HasChanges =>
        Mode == SparseChildEditMode.PassThrough
            ? DescendantScopeContext.HasChangesAtRoot
            : _child.HasChanges;

    /// <summary>Gets structured conflicts from the most recent merge attempt.</summary>
    public IReadOnlyList<TConflict>? Conflicts => _conflicts;

    /// <summary>Occurs after this child draft is accepted or passed through to its parent.</summary>
    public event Action? Accepted;

    /// <summary>Gets whether the active edit history can undo an edit.</summary>
    public bool CanUndo => SparseSessionInterop.TryGetHistory(HistorySession)?.CanUndo == true;

    /// <summary>Gets whether the active edit history can redo an edit.</summary>
    public bool CanRedo => SparseSessionInterop.TryGetHistory(HistorySession)?.CanRedo == true;

    /// <summary>Attempts to merge this child draft into its parent.</summary>
    /// <param name="conflicts">Structured conflicts when the merge fails.</param>
    /// <returns>True when the draft was accepted or no draft remains; otherwise false.</returns>
    public bool TryAcceptChanges(out IReadOnlyList<TConflict>? conflicts)
    {
        ThrowIfDisposed();
        if (!_child.HasChanges)
        {
            // A no-op acceptance still reports success, so stale conflicts
            // from an earlier failed merge must not survive it. Accepted
            // stays silent here: nothing was merged.
            if (_conflicts is not null)
            {
                _conflicts = null;
                _changed();
            }

            conflicts = null;
            return true;
        }

        var accepted = _child.TryAcceptChanges(out conflicts);
        _conflicts = accepted ? null : conflicts;
        if (accepted)
        {
            AcceptVersion++;
            Accepted?.Invoke();
        }
        else if (conflicts is null)
        {
            _invalidate();
        }

        return accepted;
    }

    /// <summary>Merges the child draft into its parent, throwing when the merge conflicts.</summary>
    public void AcceptChanges()
    {
        if (!TryAcceptChanges(out var conflicts))
        {
            throw new InvalidOperationException(
                conflicts is null
                    ? "The child draft could not be accepted."
                    : "The child draft conflicts with its parent."
            );
        }
    }

    /// <summary>Discards pending child edits while keeping the child session connected.</summary>
    public void DiscardChanges()
    {
        ThrowIfDisposed();
        _conflicts = null;
        _child.DiscardChanges();
        _changed();
    }

    /// <summary>Undoes the newest edit in the active history.</summary>
    public void Undo()
    {
        ThrowIfDisposed();
        RequireHistory().Undo();
    }

    /// <summary>Redoes the newest undone edit in the active history.</summary>
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

    internal void SetMode(SparseChildEditMode mode)
    {
        ThrowIfDisposed();
        if (mode == SparseChildEditMode.PassThrough && _notifier is null)
        {
            throw new InvalidOperationException(
                "Pass-through child edits require a session that reports property changes."
            );
        }

        DescendantScopeContext.SetMode(mode);
        _conflicts = null;
    }

    // Validates a mode transition without mutating anything, so the owning
    // scope can reject before any parent, child, or nested context moves.
    internal string? ValidateModeTransition(SparseChildEditMode mode)
    {
        ThrowIfDisposed();
        if (_child.HasChanges || DescendantScopeContext.HasChanges)
        {
            return "Cannot change child edit mode while child edits are pending.";
        }

        if (mode == SparseChildEditMode.PassThrough && _notifier is null)
        {
            return "Pass-through child edits require a session that reports property changes.";
        }

        return DescendantScopeContext.ValidateModeTransition(mode);
    }

    private object HistorySession =>
        Mode == SparseChildEditMode.PassThrough ? _parentSession : Session;

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || _handlingSessionChange)
        {
            return;
        }

        _handlingSessionChange = true;
        try
        {
            if (Mode == SparseChildEditMode.PassThrough && _child.HasChanges)
            {
                _isApplyingPassThrough = true;
                try
                {
                    TryAcceptChanges(out _);
                }
                finally
                {
                    _isApplyingPassThrough = false;
                }
            }
        }
        finally
        {
            _handlingSessionChange = false;
            _changed();
        }
    }

    private ISparseEditHistory RequireHistory() =>
        SparseSessionInterop.TryGetHistory(HistorySession)
        ?? throw new InvalidOperationException("The active session has no enabled edit history.");

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
