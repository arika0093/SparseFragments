using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>Applies a change set to the live model for history replay.</summary>
/// <remarks>
/// Returns true on success with null conflicts. Returns false with conflicts
/// when the before-state no longer matches. Throws when setters partially
/// mutate the model or the model cannot be written in place.
/// </remarks>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
/// <param name="changes">The change set to apply.</param>
/// <param name="conflicts">Structured conflicts when the result is false.</param>
/// <returns>True when the model was updated; otherwise false.</returns>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public delegate bool EditHistoryTryApply<TChangeSet>(
    TChangeSet changes,
    out IReadOnlyList<SparseConflict>? conflicts
)
    where TChangeSet : class;

/// <summary>Typed operations binding a history to one generated change set.</summary>
/// <remarks>
/// The generated <c>CreateHistory</c> factory supplies these delegates from
/// the concrete change-set type, so no reflection is required.
/// </remarks>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditHistoryHooks<TChangeSet>
    where TChangeSet : class
{
    /// <summary>Gets or sets the empty check for the change-set type.</summary>
    public Func<TChangeSet, bool>? IsEmpty { get; set; }

    /// <summary>Gets or sets the inversion for the change-set type.</summary>
    public Func<TChangeSet, TChangeSet>? Invert { get; set; }

    /// <summary>Gets or sets sequential composition for the change-set type.</summary>
    public Func<TChangeSet, TChangeSet, TChangeSet>? Compose { get; set; }

    /// <summary>Gets or sets the in-place apply used by undo and redo.</summary>
    public EditHistoryTryApply<TChangeSet>? TryApply { get; set; }

    /// <summary>Gets or sets the transition subscription.</summary>
    public Action<Action<TChangeSet>>? Subscribe { get; set; }

    /// <summary>Gets or sets the transition unsubscription.</summary>
    public Action<Action<TChangeSet>>? Unsubscribe { get; set; }
}

/// <summary>Optional bounded change-set history with undo and redo.</summary>
/// <remarks>
/// Subscribe-free until constructed: the constructor subscribes to the
/// session transition event, and <see cref="Dispose"/> unsubscribes and
/// releases retention. Without this object the session retains no history.
/// The underlying session snapshot and diff work runs regardless of history;
/// this object only adds retention of observed transitions.
/// This type is not thread-safe. Use it on the same thread as the session.
/// </remarks>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
public sealed class SparseEditHistory<TChangeSet> : IDisposable
    where TChangeSet : class
{
    private readonly Func<TChangeSet, bool> _isEmpty;
    private readonly Func<TChangeSet, TChangeSet> _invert;
    private readonly Func<TChangeSet, TChangeSet, TChangeSet> _compose;
    private readonly EditHistoryTryApply<TChangeSet> _tryApply;
    private readonly Action<Action<TChangeSet>> _unsubscribe;
    private readonly Action<TChangeSet> _handler;
    private readonly int _maxEntries;
    private readonly LinkedList<TChangeSet> _undo = new LinkedList<TChangeSet>();
    private readonly Stack<TChangeSet> _redo = new Stack<TChangeSet>();
    private List<TChangeSet>? _groupBuffer;
    private int _groupDepth;
    private bool _replaying;
    private bool _disposed;

    /// <summary>Initializes a history and subscribes to session transitions.</summary>
    /// <param name="options">Retention options, or null for defaults.</param>
    /// <param name="hooks">Typed change-set operations and event wiring.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hooks"/> is null.</exception>
    /// <exception cref="ArgumentException">A required hook delegate is missing.</exception>
    public SparseEditHistory(EditHistoryOptions? options, SparseEditHistoryHooks<TChangeSet> hooks)
    {
        if (hooks == null)
        {
            throw new ArgumentNullException(nameof(hooks));
        }

        if (hooks.IsEmpty == null)
        {
            throw new ArgumentException("An empty-check delegate is required.", nameof(hooks));
        }

        if (hooks.Invert == null)
        {
            throw new ArgumentException("An invert delegate is required.", nameof(hooks));
        }

        if (hooks.Compose == null)
        {
            throw new ArgumentException("A compose delegate is required.", nameof(hooks));
        }

        if (hooks.TryApply == null)
        {
            throw new ArgumentException("An apply delegate is required.", nameof(hooks));
        }

        if (hooks.Subscribe == null)
        {
            throw new ArgumentException("A subscribe delegate is required.", nameof(hooks));
        }

        if (hooks.Unsubscribe == null)
        {
            throw new ArgumentException("An unsubscribe delegate is required.", nameof(hooks));
        }

        _isEmpty = hooks.IsEmpty;
        _invert = hooks.Invert;
        _compose = hooks.Compose;
        _tryApply = hooks.TryApply;
        _unsubscribe = hooks.Unsubscribe;
        _maxEntries = options == null ? 50 : options.MaxEntries;
        _handler = OnTransition;
        hooks.Subscribe(_handler);
    }

    /// <summary>Raised after the history is disposed.</summary>
    /// <remarks>Lets the owning session drop its registration.</remarks>
    public event Action? Disposed;

    /// <summary>Gets whether an undo entry is available.</summary>
    public bool CanUndo
    {
        get
        {
            ThrowIfDisposed();
            return _undo.Count != 0;
        }
    }

    /// <summary>Gets whether a redo entry is available.</summary>
    public bool CanRedo
    {
        get
        {
            ThrowIfDisposed();
            return _redo.Count != 0;
        }
    }

    /// <summary>Gets the retained undo entry count.</summary>
    public int UndoCount
    {
        get
        {
            ThrowIfDisposed();
            return _undo.Count;
        }
    }

    /// <summary>Gets the retained redo entry count.</summary>
    public int RedoCount
    {
        get
        {
            ThrowIfDisposed();
            return _redo.Count;
        }
    }

    /// <summary>Gets the maximum retained undo entries.</summary>
    public int MaxEntries => _maxEntries;

    /// <summary>Records one observed transition.</summary>
    /// <remarks>
    /// The generated factory wires this to the session transition event.
    /// Empty change sets are ignored. Calls made while an undo or redo replay
    /// applies are ignored so replays are never re-recorded. One call stores
    /// one entry; <c>BatchEdit</c> already publishes one transition and so
    /// stores one entry. Separate edits are never silently coalesced.
    /// </remarks>
    /// <param name="changes">The observed transition.</param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The history is disposed.</exception>
    public void OnTransition(TChangeSet changes)
    {
        ThrowIfDisposed();
        if (changes == null)
        {
            throw new ArgumentNullException(nameof(changes));
        }

        if (_replaying)
        {
            return;
        }

        if (_isEmpty(changes))
        {
            return;
        }

        if (_groupDepth > 0)
        {
            List<TChangeSet> buffer = _groupBuffer!;
            buffer.Add(changes);
            return;
        }

        PushUndo(changes);
        _redo.Clear();
    }

    /// <summary>Begins an explicit group across multiple events.</summary>
    /// <remarks>
    /// Buffered transitions compose into one undo entry when the outermost
    /// scope is disposed. Use this for typing or dragging. Groups nest; only
    /// the outermost dispose commits. An empty group stores nothing.
    /// </remarks>
    /// <returns>A scope that commits the group on dispose.</returns>
    /// <exception cref="ObjectDisposedException">The history is disposed.</exception>
    public IDisposable BeginGroup()
    {
        ThrowIfDisposed();
        _groupDepth++;
        if (_groupBuffer == null)
        {
            _groupBuffer = new List<TChangeSet>();
        }

        return new GroupScope(this);
    }

    /// <summary>Runs an action inside one explicit history group.</summary>
    /// <param name="action">The edits to group.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public void Group(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        using (BeginGroup())
        {
            action();
        }
    }

    /// <summary>Undoes the newest entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">No entry exists or the before-state conflicts.</exception>
    public void Undo()
    {
        if (!TryUndo(out IReadOnlyList<SparseConflict>? conflicts))
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (conflicts != null && conflicts.Count != 0)
            {
                throw new InvalidOperationException(
                    "The undo entry conflicts with the current model."
                );
            }

            throw new InvalidOperationException("No undo entries are available.");
        }
    }

    /// <summary>Attempts to undo the newest entry.</summary>
    /// <remarks>
    /// A false result leaves both stacks untouched. An apply that throws
    /// (partial setter failure or unwritable model) also leaves the stacks
    /// untouched; the session already resynchronized to the live state.
    /// </remarks>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryUndo(out IReadOnlyList<SparseConflict>? conflicts)
    {
        ThrowIfDisposed();
        conflicts = null;
        if (_groupDepth != 0)
        {
            throw new InvalidOperationException(
                "Undo cannot run inside an explicit history group."
            );
        }

        if (_undo.Count == 0)
        {
            return false;
        }

        LinkedListNode<TChangeSet> node = _undo.Last!;
        TChangeSet entry = node.Value;
        TChangeSet inverted = _invert(entry);
        bool applied;
        _replaying = true;
        try
        {
            applied = _tryApply(inverted, out conflicts);
        }
        finally
        {
            _replaying = false;
        }

        if (!applied)
        {
            return false;
        }

        _undo.RemoveLast();
        _redo.Push(entry);
        return true;
    }

    /// <summary>Redoes the newest undone entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">No entry exists or the before-state conflicts.</exception>
    public void Redo()
    {
        if (!TryRedo(out IReadOnlyList<SparseConflict>? conflicts))
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (conflicts != null && conflicts.Count != 0)
            {
                throw new InvalidOperationException(
                    "The redo entry conflicts with the current model."
                );
            }

            throw new InvalidOperationException("No redo entries are available.");
        }
    }

    /// <summary>Attempts to redo the newest undone entry.</summary>
    /// <remarks>
    /// A false result leaves both stacks untouched. A throwing apply also
    /// leaves the stacks untouched.
    /// </remarks>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryRedo(out IReadOnlyList<SparseConflict>? conflicts)
    {
        ThrowIfDisposed();
        conflicts = null;
        if (_groupDepth != 0)
        {
            throw new InvalidOperationException(
                "Redo cannot run inside an explicit history group."
            );
        }

        if (_redo.Count == 0)
        {
            return false;
        }

        TChangeSet entry = _redo.Peek();
        bool applied;
        _replaying = true;
        try
        {
            applied = _tryApply(entry, out conflicts);
        }
        finally
        {
            _replaying = false;
        }

        if (!applied)
        {
            return false;
        }

        _redo.Pop();
        PushUndo(entry);
        return true;
    }

    /// <summary>Clears undo entries, redo entries, and any buffered group.</summary>
    public void Clear()
    {
        ThrowIfDisposed();
        _undo.Clear();
        _redo.Clear();
        if (_groupBuffer != null)
        {
            _groupBuffer.Clear();
        }
    }

    /// <summary>Unsubscribes from transitions and releases retention.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unsubscribe(_handler);
        _undo.Clear();
        _redo.Clear();
        if (_groupBuffer != null)
        {
            _groupBuffer.Clear();
        }

        _groupDepth = 0;
        Action? raised = Disposed;
        if (raised != null)
        {
            raised();
        }
    }

    private void PushUndo(TChangeSet changes)
    {
        if (_undo.Count >= _maxEntries)
        {
            LinkedListNode<TChangeSet>? oldest = _undo.First;
            if (oldest != null)
            {
                _undo.RemoveFirst();
            }
        }

        _undo.AddLast(changes);
    }

    private void EndGroup()
    {
        _groupDepth--;
        if (_groupDepth != 0)
        {
            return;
        }

        List<TChangeSet> buffer = _groupBuffer!;
        if (buffer.Count == 0)
        {
            return;
        }

        List<TChangeSet> entries = new List<TChangeSet>(buffer);
        buffer.Clear();
        TChangeSet composed = entries[0];
        bool splintered = false;
        for (int index = 1; index < entries.Count; index++)
        {
            try
            {
                composed = _compose(composed, entries[index]);
            }
            catch (InvalidOperationException)
            {
                // Contiguous observed transitions compose. If a custom
                // transition breaks contiguity, keep every entry instead of
                // dropping the group.
                PushUndo(composed);
                composed = entries[index];
                splintered = true;
            }
        }

        if (splintered)
        {
            PushUndo(composed);
            _redo.Clear();
            return;
        }

        if (_isEmpty(composed))
        {
            _redo.Clear();
            return;
        }

        PushUndo(composed);
        _redo.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    private sealed class GroupScope : IDisposable
    {
        private SparseEditHistory<TChangeSet>? _owner;

        public GroupScope(SparseEditHistory<TChangeSet> owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            SparseEditHistory<TChangeSet>? owner = _owner;
            _owner = null;
            if (owner != null && !owner._disposed)
            {
                owner.EndGroup();
            }
        }
    }
}
