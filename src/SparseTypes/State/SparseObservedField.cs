using System.ComponentModel;

namespace SparseTypes;

/// <summary>Typed operations binding an observed field to one edit session.</summary>
/// <remarks>The generated <c>ObserveField</c> factory supplies these delegates from the concrete session type, so no reflection is required.</remarks>
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
/// <remarks>Point-in-time <c>FieldState</c> reads stay separate: this object is the explicitly requested live subscription.</remarks>
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
    /// <exception cref="ArgumentNullException"><paramref name="hooks"/> is
    /// null.</exception>
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
    /// <remarks>Session invalidations call this automatically.</remarks>
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
            // states (duplicate keys mid-swap.
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
