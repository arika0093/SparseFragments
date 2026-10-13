using System.ComponentModel;

namespace SparseTypes;

/// <summary>Typed operations binding an observed field to one edit session.</summary>
/// <remarks>
/// The generated <c>ObserveField</c> factory supplies these delegates from the
/// concrete session type, so no reflection is required. <c>Query</c> re-reads
/// the live field state on every invalidation; <c>Subscribe</c> and
/// <c>Unsubscribe</c> attach the session's <c>PropertyChanged</c> notification.
/// </remarks>
/// <typeparam name="T">The addressed value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseObservedFieldHooks<T>
{
    /// <summary>Gets or sets the live field-state query.</summary>
    public Func<SparseFieldState<T>>? Query { get; set; }

    /// <summary>Gets or sets the session invalidation subscription.</summary>
    public Action<PropertyChangedEventHandler>? Subscribe { get; set; }

    /// <summary>Gets or sets the session invalidation unsubscription.</summary>
    public Action<PropertyChangedEventHandler>? Unsubscribe { get; set; }
}

/// <summary>Opt-in live field-state subscription for one path.</summary>
/// <remarks>
/// <para>
/// Point-in-time <c>FieldState</c> reads stay separate: this object is the
/// explicitly requested live subscription. It holds the last queried
/// <see cref="SparseFieldState{T}"/> and refreshes it whenever the session
/// raises <c>PropertyChanged</c>. The session raises that notification on
/// every state-affecting path (observable edits, <c>BatchEdit</c>, in-place
/// apply, undo/redo replay, <c>RevertChanges</c>, both <c>AcceptChanges</c>
/// overloads, <c>Reload</c>, and <c>TryReconcile</c>), which also covers the
/// baseline-only advances that never publish a <c>TransitionObserved</c>
/// event. Queries re-resolve the stored path against the live model, so object
/// replacement needs no re-registration; key segments keep identity across
/// moves while index segments stay positional.
/// </para>
/// <para>
/// Notifications carry only effective changes: when the refreshed state equals
/// the retained one, nothing is raised. Changed properties raise in the order
/// <c>IsChanged</c>, <c>Kind</c>, <c>HasAncestorChange</c>,
/// <c>HasDescendantChange</c>, <c>HasChanges</c>, <c>Before</c>, <c>After</c>,
/// <c>Current</c>. Events fire synchronously on the mutating thread; no UI
/// thread marshaling is performed and no UI framework is referenced.
/// <c>IsChanged</c> reports the semantic baseline diff, never a UI
/// visited/modified flag.
/// </para>
/// <para>
/// Without this object the session retains nothing per field. The watcher
/// itself keeps only its last state; disposing unsubscribes and releases the
/// session reference held by the event. Only session-observed transitions
/// refresh the watcher: direct <c>Model</c> writes bypass notifications, the
/// same caveat that applies to the session change cache. This type is not
/// thread-safe. Use it on the same thread as the session.
/// </para>
/// </remarks>
/// <typeparam name="T">The addressed value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseObservedField<T> : INotifyPropertyChanged, IDisposable
{
    private readonly SparsePath _path;
    private readonly Func<SparseFieldState<T>> _query;
    private readonly Action<PropertyChangedEventHandler> _unsubscribe;
    private readonly PropertyChangedEventHandler _handler;
    private SparseFieldState<T> _current;
    private bool _disposed;

    /// <summary>Initializes a watcher and subscribes to session invalidations.</summary>
    /// <param name="path">The model-agnostic path to observe.</param>
    /// <param name="hooks">The live query and event wiring.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hooks"/> is null.</exception>
    /// <exception cref="ArgumentException">A required hook delegate is missing.</exception>
    public SparseObservedField(SparsePath path, SparseObservedFieldHooks<T> hooks)
    {
        if (hooks is null)
        {
            throw new ArgumentNullException(nameof(hooks));
        }

        if (hooks.Query is null)
        {
            throw new ArgumentException("A query delegate is required.", nameof(hooks));
        }

        if (hooks.Subscribe is null)
        {
            throw new ArgumentException("A subscribe delegate is required.", nameof(hooks));
        }

        if (hooks.Unsubscribe is null)
        {
            throw new ArgumentException("An unsubscribe delegate is required.", nameof(hooks));
        }

        _path = path;
        _query = hooks.Query;
        _unsubscribe = hooks.Unsubscribe;
        _current = _query();
        _handler = OnSessionInvalidated;
        hooks.Subscribe(_handler);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the observed path.</summary>
    public SparsePath Path => _path;

    /// <summary>Gets the last refreshed field state.</summary>
    public SparseFieldState<T> Current
    {
        get
        {
            ThrowIfDisposed();
            return _current;
        }
    }

    /// <summary>Gets whether the path itself changed between the endpoints.</summary>
    public bool IsChanged
    {
        get
        {
            ThrowIfDisposed();
            return _current.IsChanged;
        }
    }

    /// <summary>Gets the kind of the exact transition, or Unchanged when absent.</summary>
    public SparseFieldChangeKind Kind
    {
        get
        {
            ThrowIfDisposed();
            return _current.Kind;
        }
    }

    /// <summary>Gets whether a strict ancestor of the path changed.</summary>
    public bool HasAncestorChange
    {
        get
        {
            ThrowIfDisposed();
            return _current.HasAncestorChange;
        }
    }

    /// <summary>Gets whether a strict descendant of the path changed.</summary>
    public bool HasDescendantChange
    {
        get
        {
            ThrowIfDisposed();
            return _current.HasDescendantChange;
        }
    }

    /// <summary>Gets whether the path, an ancestor, or a descendant changed.</summary>
    public bool HasChanges
    {
        get
        {
            ThrowIfDisposed();
            return _current.HasChanges;
        }
    }

    /// <summary>Gets the presence-aware value before the exact transition.</summary>
    public Optional<T> Before
    {
        get
        {
            ThrowIfDisposed();
            return _current.Before;
        }
    }

    /// <summary>Gets the presence-aware value after the exact transition.</summary>
    public Optional<T> After
    {
        get
        {
            ThrowIfDisposed();
            return _current.After;
        }
    }

    /// <summary>Re-reads the live field state and notifies on effective changes.</summary>
    /// <remarks>
    /// Session invalidations call this automatically. Explicit calls help tests
    /// assert the current state without mutating the session. No notification
    /// fires when the refreshed state equals the retained one.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The watcher is disposed.</exception>
    public void Refresh()
    {
        ThrowIfDisposed();
        var next = _query();
        if (_current.Equals(next))
        {
            return;
        }

        var previous = _current;
        _current = next;
        RaiseForChanges(previous, next);
    }

    /// <summary>Unsubscribes from session invalidations.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _unsubscribe(_handler);
    }

    private void OnSessionInvalidated(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Refresh();
        }
        catch (InvalidOperationException)
        {
            // Multi-step mutations pass through transiently invalid keyed
            // states (duplicate keys mid-swap, unassigned sentinels) that the
            // session itself tolerates; keep the last valid state until the
            // next notification resyncs. Explicit Refresh still surfaces it.
        }
    }

    // One property per changed member, in the documented order, so bindings
    // observe granular state before the aggregate snapshot.
    private void RaiseForChanges(SparseFieldState<T> previous, SparseFieldState<T> next)
    {
        if (previous.IsChanged != next.IsChanged)
        {
            OnPropertyChanged(nameof(IsChanged));
        }

        if (previous.Kind != next.Kind)
        {
            OnPropertyChanged(nameof(Kind));
        }

        if (previous.HasAncestorChange != next.HasAncestorChange)
        {
            OnPropertyChanged(nameof(HasAncestorChange));
        }

        if (previous.HasDescendantChange != next.HasDescendantChange)
        {
            OnPropertyChanged(nameof(HasDescendantChange));
        }

        if (previous.HasChanges != next.HasChanges)
        {
            OnPropertyChanged(nameof(HasChanges));
        }

        if (!previous.Before.Equals(next.Before))
        {
            OnPropertyChanged(nameof(Before));
        }

        if (!previous.After.Equals(next.After))
        {
            OnPropertyChanged(nameof(After));
        }

        OnPropertyChanged(nameof(Current));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
