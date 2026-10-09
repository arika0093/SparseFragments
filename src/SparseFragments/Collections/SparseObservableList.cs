using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>A notifying view over a live mutable list.</summary>
/// <remarks>
/// Mutations are applied to the supplied list instance. Changes made directly to that list are not
/// observable unless it implements <see cref="INotifyCollectionChanged"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseObservableList<TModel, TView>
    : IList<TView>,
        IReadOnlyList<TView>,
        IList,
        INotifyCollectionChanged,
        INotifyPropertyChanged,
        IDisposable
{
    private readonly IList<TModel> _model;
    private readonly Func<TModel, Action, TView> _wrap;
    private readonly Func<TView, TModel> _unwrap;
    private readonly Action _onChanged;
    private readonly Action? _onRawModelAccess;
    private readonly bool _cacheReferences;
    private readonly Dictionary<object, ProxyEntry> _proxies = new(
        SparseReferenceEqualityComparer.Instance
    );
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs ItemChanged = new("Item[]");
    private int _suppressedEvents;
    private bool _disposed;

    /// <summary>Creates a notifying view over a live mutable list.</summary>
    /// <remarks>
    /// The optional raw-model access callback is invoked when <see cref="Model"/> is
    /// read so owners (such as an edit session) can invalidate caches that raw
    /// mutations would bypass. Generated sessions pass their raw-model callback here;
    /// trusted session internals read <see cref="UnsafeModel"/> instead.
    /// </remarks>
    public SparseObservableList(
        IList<TModel> model,
        Func<TModel, Action, TView> wrap,
        Func<TView, TModel> unwrap,
        Action onChanged,
        bool cacheReferences = false,
        Action? onRawModelAccess = null
    )
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _wrap = wrap ?? throw new ArgumentNullException(nameof(wrap));
        _unwrap = unwrap ?? throw new ArgumentNullException(nameof(unwrap));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _onRawModelAccess = onRawModelAccess;
        _cacheReferences = cacheReferences;
        if (_model is INotifyCollectionChanged notifying)
        {
            notifying.CollectionChanged += OnModelCollectionChanged;
        }
    }

    /// <summary>The live list instance wrapped by this view.</summary>
    /// <remarks>
    /// Reading this property reports raw-model access to the owner so session caches
    /// stay valid when the returned list is mutated directly. Generated session
    /// internals that already account for the access read <see cref="UnsafeModel"/>.
    /// </remarks>
    public IList<TModel> Model
    {
        get
        {
            _onRawModelAccess?.Invoke();
            return _model;
        }
    }

    /// <summary>The live list instance without reporting raw-model access.</summary>
    /// <remarks>
    /// For trusted generated code only: the caller must raise the change itself.
    /// External callers should read <see cref="Model"/> so session caches stay valid.
    /// </remarks>
    public IList<TModel> UnsafeModel => _model;

    /// <summary>Whether this view has been detached from its model collection.</summary>
    public bool IsDisposed => _disposed;

    /// <inheritdoc />
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public int Count => _model.Count;

    /// <inheritdoc />
    public bool IsReadOnly => _model.IsReadOnly;

    /// <inheritdoc />
    public TView this[int index]
    {
        get => Wrap(_model[index]);
        set
        {
            ThrowIfDisposed();
            var oldModel = _model[index];
            var newModel = _unwrap(value);
            Mutate(
                () => _model[index] = newModel,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    Wrap(newModel),
                    Wrap(oldModel),
                    index
                ),
                () => PruneRemoved(oldModel)
            );
        }
    }

    /// <summary>Replaces an element with a model value and raises one Replace notification.</summary>
    public void SetModel(int index, TModel item)
    {
        ThrowIfDisposed();
        var oldModel = _model[index];
        Mutate(
            () => _model[index] = item,
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace,
                Wrap(item),
                Wrap(oldModel),
                index
            ),
            () => PruneRemoved(oldModel)
        );
    }

    /// <summary>Moves an element and raises one Move notification.</summary>
    public void Move(int oldIndex, int newIndex)
    {
        ThrowIfDisposed();
        if ((uint)oldIndex >= (uint)_model.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(oldIndex));
        }

        if ((uint)newIndex >= (uint)_model.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(newIndex));
        }

        if (oldIndex == newIndex)
        {
            return;
        }

        if (_model is ObservableCollection<TModel> observable)
        {
            observable.Move(oldIndex, newIndex);
            return;
        }

        var item = _model[oldIndex];
        _suppressedEvents++;
        try
        {
            _model.RemoveAt(oldIndex);
            _model.Insert(newIndex, item);
        }
        finally
        {
            _suppressedEvents--;
        }

        RaiseCollectionChanged(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Move,
                Wrap(item),
                newIndex,
                oldIndex
            )
        );
    }

    /// <summary>Adds a model value, useful when elements are exposed as observable proxies.</summary>
    public void AddModel(TModel item)
    {
        // Pure addition cannot orphan cached proxies, so no prune is needed.
        Mutate(
            () => _model.Add(item),
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                Wrap(item),
                _model.Count
            ),
            prune: null
        );
    }

    /// <inheritdoc />
    public void Add(TView item) => AddModel(_unwrap(item));

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfDisposed();
        if (_model.Count == 0)
        {
            return;
        }

        Mutate(
            _model.Clear,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
            DeactivateAll
        );
    }

    /// <inheritdoc />
    public bool Contains(TView item) => IndexOf(item) >= 0;

    /// <inheritdoc />
    public void CopyTo(TView[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var index = 0; index < _model.Count; index++)
        {
            array[arrayIndex + index] = Wrap(_model[index]);
        }
    }

    /// <inheritdoc />
    public IEnumerator<TView> GetEnumerator()
    {
        for (var index = 0; index < _model.Count; index++)
        {
            yield return Wrap(_model[index]);
        }
    }

    /// <inheritdoc />
    public int IndexOf(TView item)
    {
        var model = _unwrap(item);
        for (var index = 0; index < _model.Count; index++)
        {
            if (EqualityComparer<TModel>.Default.Equals(_model[index], model))
            {
                return index;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public void Insert(int index, TView item) => InsertModel(index, _unwrap(item));

    /// <summary>Inserts a model value, useful when elements are exposed as observable proxies.</summary>
    public void InsertModel(int index, TModel item)
    {
        // Pure insertion cannot orphan cached proxies, so no prune is needed.
        Mutate(
            () => _model.Insert(index, item),
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                Wrap(item),
                index
            ),
            prune: null
        );
    }

    /// <inheritdoc />
    public bool Remove(TView item)
    {
        var index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        ThrowIfDisposed();
        var removedModel = _model[index];
        var removed = Wrap(removedModel);
        Mutate(
            () => _model.RemoveAt(index),
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                removed,
                index
            ),
            () => PruneRemoved(removedModel)
        );
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    bool IList.IsReadOnly => IsReadOnly;

    /// <inheritdoc />
    bool IList.IsFixedSize => IsReadOnly;

    /// <inheritdoc />
    int ICollection.Count => Count;

    /// <inheritdoc />
    bool ICollection.IsSynchronized => false;

    /// <inheritdoc />
    object ICollection.SyncRoot => this;

    /// <inheritdoc />
    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = CastView(value);
    }

    /// <inheritdoc />
    int IList.Add(object? value)
    {
        Add(CastView(value));
        return Count - 1;
    }

    /// <inheritdoc />
    bool IList.Contains(object? value) => value is TView item && Contains(item);

    /// <inheritdoc />
    int IList.IndexOf(object? value) => value is TView item ? IndexOf(item) : -1;

    /// <inheritdoc />
    void IList.Insert(int index, object? value) => Insert(index, CastView(value));

    /// <inheritdoc />
    void IList.Remove(object? value)
    {
        if (value is TView item)
        {
            Remove(item);
        }
    }

    /// <inheritdoc />
    void IList.RemoveAt(int index) => RemoveAt(index);

    /// <inheritdoc />
    void IList.Clear() => Clear();

    /// <inheritdoc />
    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (var itemIndex = 0; itemIndex < Count; itemIndex++)
        {
            array.SetValue(Wrap(_model[itemIndex]), index + itemIndex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_model is INotifyCollectionChanged notifying)
        {
            notifying.CollectionChanged -= OnModelCollectionChanged;
        }

        DeactivateAll();
    }

    // Only the evicted model can go stale; a duplicate reference still present
    // elsewhere keeps its proxy alive. One O(n) scan beats O(proxies * n).
    private void PruneRemoved(TModel removed)
    {
        if (!_cacheReferences || removed is null)
        {
            return;
        }

        var key = (object)removed;
        if (_proxies.TryGetValue(key, out var entry) && !IsPresent(removed))
        {
            entry.Active = false;
            _proxies.Remove(key);
        }
    }

    private void PruneItems(System.Collections.IList? items)
    {
        if (!_cacheReferences || items is null)
        {
            return;
        }

        foreach (TModel item in items)
        {
            PruneRemoved(item);
        }
    }

    // Reset may reload arbitrary content, so sweep once against a live set:
    // O(model + proxies) instead of O(proxies * model).
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit stale-key collection avoids the Where+ToArray allocation this optimization removes."
    )]
    private void PruneStale()
    {
        if (!_cacheReferences)
        {
            return;
        }

        if (_model.Count == 0)
        {
            DeactivateAll();
            return;
        }

        var live = new HashSet<object>(SparseReferenceEqualityComparer.Instance);
        for (var index = 0; index < _model.Count; index++)
        {
            if (_model[index] is { } item)
            {
                live.Add(item);
            }
        }

        var staleKeys = new List<object>();
        foreach (var key in _proxies.Keys)
        {
            if (!live.Contains(key))
            {
                staleKeys.Add(key);
            }
        }

        foreach (var key in staleKeys)
        {
            _proxies[key].Active = false;
            _proxies.Remove(key);
        }
    }

    private void DeactivateAll()
    {
        foreach (var entry in _proxies.Values)
        {
            entry.Active = false;
        }

        _proxies.Clear();
    }

    private void Mutate(Action mutation, NotifyCollectionChangedEventArgs args, Action? prune)
    {
        ThrowIfDisposed();
        var before = _notificationVersion;
        mutation();
        if (before == _notificationVersion)
        {
            prune?.Invoke();
            RaiseCollectionChanged(args);
        }
    }

    private int _notificationVersion;

    private void OnModelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_suppressedEvents != 0 || _disposed)
        {
            return;
        }

        var converted = ConvertArgs(args);
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                PruneItems(args.OldItems);
                break;
            case NotifyCollectionChangedAction.Reset:
                PruneStale();
                break;
            // Add/Move only introduce or permute models, so cached proxies stay live.
        }

        RaiseCollectionChanged(converted);
    }

    private NotifyCollectionChangedEventArgs ConvertArgs(NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            return new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
        }

        var newItems = ConvertItems(args.NewItems);
        var oldItems = ConvertItems(args.OldItems);
        return args.Action switch
        {
            NotifyCollectionChangedAction.Add when newItems is { Count: 1 } => new(
                NotifyCollectionChangedAction.Add,
                newItems[0],
                args.NewStartingIndex
            ),
            NotifyCollectionChangedAction.Add => new(
                NotifyCollectionChangedAction.Add,
                newItems!,
                args.NewStartingIndex
            ),
            NotifyCollectionChangedAction.Remove when oldItems is { Count: 1 } => new(
                NotifyCollectionChangedAction.Remove,
                oldItems[0],
                args.OldStartingIndex
            ),
            NotifyCollectionChangedAction.Remove => new(
                NotifyCollectionChangedAction.Remove,
                oldItems!,
                args.OldStartingIndex
            ),
            NotifyCollectionChangedAction.Replace
                when newItems is { Count: 1 } && oldItems is { Count: 1 } => new(
                NotifyCollectionChangedAction.Replace,
                newItems[0],
                oldItems[0],
                args.NewStartingIndex
            ),
            NotifyCollectionChangedAction.Replace => new(
                NotifyCollectionChangedAction.Replace,
                newItems!,
                oldItems!,
                args.NewStartingIndex
            ),
            NotifyCollectionChangedAction.Move when newItems is { Count: 1 } => new(
                NotifyCollectionChangedAction.Move,
                newItems[0],
                args.NewStartingIndex,
                args.OldStartingIndex
            ),
            _ => new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
        };
    }

    private List<TView>? ConvertItems(System.Collections.IList? items)
    {
        if (items is null)
        {
            return null;
        }

        var converted = new List<TView>(items.Count);
        foreach (TModel item in items)
        {
            converted.Add(Wrap(item));
        }

        return converted;
    }

    private void RaiseCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        _notificationVersion++;
        if (
            args.Action
            is NotifyCollectionChangedAction.Add
                or NotifyCollectionChangedAction.Remove
                or NotifyCollectionChangedAction.Reset
        )
        {
            PropertyChanged?.Invoke(this, CountChanged);
        }

        if (
            args.Action
            is NotifyCollectionChangedAction.Add
                or NotifyCollectionChangedAction.Remove
                or NotifyCollectionChangedAction.Replace
                or NotifyCollectionChangedAction.Move
                or NotifyCollectionChangedAction.Reset
        )
        {
            PropertyChanged?.Invoke(this, ItemChanged);
        }

        CollectionChanged?.Invoke(this, args);
        _onChanged();
    }

    private TView Wrap(TModel item)
    {
        if (!_cacheReferences || item is null)
        {
            return _wrap(item, _onChanged);
        }

        var key = (object)item;
        if (_proxies.TryGetValue(key, out var existing))
        {
            return existing.View;
        }

        var entry = new ProxyEntry();
        entry.View = _wrap(
            item,
            () =>
            {
                if (entry.Active && IsPresent(item))
                {
                    _onChanged();
                }
            }
        );
        _proxies.Add(key, entry);
        return entry.View;
    }

    private bool IsPresent(TModel item)
    {
        for (var index = 0; index < _model.Count; index++)
        {
            if (ReferenceEquals(_model[index], item))
            {
                return true;
            }
        }

        return false;
    }

    private static TView CastView(object? value)
    {
        if (value is null && default(TView) is null)
        {
            return default!;
        }

        if (value is TView view)
        {
            return view;
        }

        throw new ArgumentException("Value has an incompatible type.", nameof(value));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SparseObservableList<TModel, TView>));
        }
    }

    private sealed class ProxyEntry
    {
        public TView View = default!;
        public bool Active = true;
    }
}
