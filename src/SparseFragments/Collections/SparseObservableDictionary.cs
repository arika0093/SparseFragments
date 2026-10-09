using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>A notifying view over a live mutable dictionary.</summary>
/// <remarks>
/// Mutations are applied to the supplied dictionary instance. Changes made directly to that
/// dictionary are not observable unless it implements <see cref="INotifyCollectionChanged"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseObservableDictionary<TKey, TModel, TView>
    : IDictionary<TKey, TView>,
        IReadOnlyDictionary<TKey, TView>,
        INotifyCollectionChanged,
        INotifyPropertyChanged,
        IDisposable
    where TKey : notnull
{
    private readonly IDictionary<TKey, TModel> _model;
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
    private int _notificationVersion;
    private bool _disposed;

    /// <summary>Creates a notifying view over a live mutable dictionary.</summary>
    /// <remarks>
    /// The optional raw-model access callback is invoked when <see cref="Model"/> is
    /// read so owners (such as an edit session) can invalidate caches that raw
    /// mutations would bypass. Generated sessions pass their raw-model callback here;
    /// trusted session internals read <see cref="UnsafeModel"/> instead.
    /// </remarks>
    public SparseObservableDictionary(
        IDictionary<TKey, TModel> model,
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

    /// <summary>The live dictionary instance wrapped by this view.</summary>
    /// <remarks>
    /// Reading this property reports raw-model access to the owner so session caches
    /// stay valid when the returned dictionary is mutated directly. Generated session
    /// internals that already account for the access read <see cref="UnsafeModel"/>.
    /// </remarks>
    public IDictionary<TKey, TModel> Model
    {
        get
        {
            _onRawModelAccess?.Invoke();
            return _model;
        }
    }

    /// <summary>The live dictionary instance without reporting raw-model access.</summary>
    /// <remarks>
    /// For trusted generated code only: the caller must raise the change itself.
    /// External callers should read <see cref="Model"/> so session caches stay valid.
    /// </remarks>
    public IDictionary<TKey, TModel> UnsafeModel => _model;

    /// <summary>Whether this view has been detached from its model dictionary.</summary>
    public bool IsDisposed => _disposed;

    /// <inheritdoc />
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc />
    public TView this[TKey key]
    {
        get => Wrap(_model[key]);
        set => Set(key, _unwrap(value));
    }

    /// <inheritdoc />
    public ICollection<TKey> Keys => _model.Keys;

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TView>.Keys => _model.Keys;

    /// <inheritdoc />
    public ICollection<TView> Values => new ValueCollection(this);

    IEnumerable<TView> IReadOnlyDictionary<TKey, TView>.Values => Values;

    /// <inheritdoc />
    public int Count => _model.Count;

    /// <inheritdoc />
    public bool IsReadOnly => _model.IsReadOnly;

    /// <inheritdoc />
    public void Add(TKey key, TView value) => AddModel(key, _unwrap(value));

    /// <summary>Adds a model value, useful when values are exposed as observable proxies.</summary>
    public void AddModel(TKey key, TModel value)
    {
        ThrowIfDisposed();
        var before = _notificationVersion;
        _model.Add(key, value);
        if (before == _notificationVersion)
        {
            // Pure addition cannot orphan cached proxies, so no prune is needed.
            RaiseCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    new KeyValuePair<TKey, TView>(key, Wrap(value))
                )
            );
        }
    }

    /// <summary>Sets a model value, useful when values are exposed as observable proxies.</summary>
    public void SetModel(TKey key, TModel value) => Set(key, value);

    /// <inheritdoc />
    public void Add(KeyValuePair<TKey, TView> item) => Add(item.Key, item.Value);

    /// <inheritdoc />
    public void Clear()
    {
        ThrowIfDisposed();
        if (_model.Count == 0)
        {
            return;
        }

        var before = _notificationVersion;
        _model.Clear();
        if (before == _notificationVersion)
        {
            DeactivateAll();
            RaiseCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            );
        }
    }

    /// <inheritdoc />
    public bool Contains(KeyValuePair<TKey, TView> item) =>
        TryGetValue(item.Key, out var value)
        && EqualityComparer<TView>.Default.Equals(value, item.Value);

    /// <inheritdoc />
    public bool ContainsKey(TKey key) => _model.ContainsKey(key);

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<TKey, TView>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var pair in this)
        {
            array[arrayIndex++] = pair;
        }
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<TKey, TView>> GetEnumerator()
    {
        foreach (var pair in _model)
        {
            yield return new KeyValuePair<TKey, TView>(pair.Key, Wrap(pair.Value));
        }
    }

    /// <inheritdoc />
    public bool Remove(TKey key)
    {
        ThrowIfDisposed();
        if (!_model.TryGetValue(key, out var oldValue))
        {
            return false;
        }

        var oldView = Wrap(oldValue);
        var before = _notificationVersion;
        var removed = _model.Remove(key);
        if (!removed)
        {
            return false;
        }

        if (before == _notificationVersion)
        {
            // Only the evicted value can go stale; a duplicate reference still
            // present under another key keeps its proxy alive.
            PruneRemoved(oldValue);
            RaiseCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove,
                    new KeyValuePair<TKey, TView>(key, oldView)
                )
            );
        }

        return true;
    }

    /// <inheritdoc />
    public bool Remove(KeyValuePair<TKey, TView> item) => Contains(item) && Remove(item.Key);

    /// <inheritdoc />
    public bool TryGetValue(TKey key, out TView value)
    {
        if (_model.TryGetValue(key, out var modelValue))
        {
            value = Wrap(modelValue);
            return true;
        }

        value = default!;
        return false;
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Raises a single Reset notification for bulk changes made directly to the backing
    /// dictionary.
    /// </summary>
    /// <param name="reportChange">
    /// Whether to report the change to the owner. Pass <see langword="false"/> when the
    /// owner already accounts for the mutation (such as a session refreshing after an
    /// in-place write) to avoid duplicate notifications.
    /// </param>
    /// <remarks>
    /// Session internals call this with <see langword="false"/> after an in-place write
    /// reuses the same backing instance; call it after your own bulk raw-model
    /// mutations. Notifications for backing dictionaries that implement
    /// <see cref="INotifyCollectionChanged"/> are forwarded automatically and must not
    /// be duplicated with this method.
    /// </remarks>
    public void NotifyReset(bool reportChange = true)
    {
        if (_disposed)
        {
            return;
        }

        PruneStale();
        if (reportChange)
        {
            RaiseCollectionChanged(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            );
            return;
        }

        _notificationVersion++;
        PropertyChanged?.Invoke(this, CountChanged);
        PropertyChanged?.Invoke(this, ItemChanged);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
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

    private void Set(TKey key, TModel value)
    {
        ThrowIfDisposed();
        var existed = _model.TryGetValue(key, out var oldValue);
        if (
            existed
            && (
                _cacheReferences
                    ? ReferenceEquals(oldValue, value)
                    : EqualityComparer<TModel>.Default.Equals(oldValue!, value)
            )
        )
        {
            return;
        }

        var oldView = existed ? Wrap(oldValue!) : default;
        var before = _notificationVersion;
        _model[key] = value;
        if (before == _notificationVersion)
        {
            if (existed)
            {
                PruneRemoved(oldValue!);
            }

            RaiseCollectionChanged(
                existed
                    ? new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace,
                        new KeyValuePair<TKey, TView>(key, Wrap(value)),
                        new KeyValuePair<TKey, TView>(key, oldView!)
                    )
                    : new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Add,
                        new KeyValuePair<TKey, TView>(key, Wrap(value))
                    )
            );
        }
    }

    private void OnModelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        var converted = ConvertArgs(args);
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                PrunePairs(args.OldItems);
                break;
            case NotifyCollectionChangedAction.Reset:
                PruneStale();
                break;
            // Add only introduces models, so cached proxies stay live.
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
                newItems[0]
            ),
            NotifyCollectionChangedAction.Add => new(NotifyCollectionChangedAction.Add, newItems!),
            NotifyCollectionChangedAction.Remove when oldItems is { Count: 1 } => new(
                NotifyCollectionChangedAction.Remove,
                oldItems[0]
            ),
            NotifyCollectionChangedAction.Remove => new(
                NotifyCollectionChangedAction.Remove,
                oldItems!
            ),
            NotifyCollectionChangedAction.Replace
                when newItems is { Count: 1 } && oldItems is { Count: 1 } => new(
                NotifyCollectionChangedAction.Replace,
                newItems[0],
                oldItems[0]
            ),
            NotifyCollectionChangedAction.Replace => new(
                NotifyCollectionChangedAction.Replace,
                newItems!,
                oldItems!
            ),
            _ => new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
        };
    }

    private List<KeyValuePair<TKey, TView>>? ConvertItems(System.Collections.IList? items)
    {
        if (items is null)
        {
            return null;
        }

        var converted = new List<KeyValuePair<TKey, TView>>(items.Count);
        foreach (KeyValuePair<TKey, TModel> pair in items)
        {
            converted.Add(new KeyValuePair<TKey, TView>(pair.Key, Wrap(pair.Value)));
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

        PropertyChanged?.Invoke(this, ItemChanged);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit scan avoids the LINQ Any delegate allocation on this hot path."
    )]
    private bool IsPresent(TModel item)
    {
        foreach (var value in _model.Values)
        {
            if (ReferenceEquals(value, item))
            {
                return true;
            }
        }

        return false;
    }

    // Only the evicted value can go stale; a duplicate reference still present
    // under another key keeps its proxy alive. One O(n) scan beats O(proxies * n).
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

    private void PrunePairs(System.Collections.IList? items)
    {
        if (!_cacheReferences || items is null)
        {
            return;
        }

        foreach (KeyValuePair<TKey, TModel> pair in items)
        {
            PruneRemoved(pair.Value);
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
        foreach (var value in _model.Values)
        {
            if (value is { } item)
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(SparseObservableDictionary<TKey, TModel, TView>)
            );
        }
    }

    private sealed class ProxyEntry
    {
        public TView View = default!;
        public bool Active = true;
    }

    private sealed class ValueCollection(SparseObservableDictionary<TKey, TModel, TView> owner)
        : ICollection<TView>
    {
        public int Count => owner.Count;

        public bool IsReadOnly => true;

        public bool Contains(TView item) =>
            this.Any(value => EqualityComparer<TView>.Default.Equals(value, item));

        public void CopyTo(TView[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            foreach (var value in this)
            {
                array[arrayIndex++] = value;
            }
        }

        public IEnumerator<TView> GetEnumerator()
        {
            foreach (var value in owner._model.Values)
            {
                yield return owner.Wrap(value);
            }
        }

        public void Add(TView item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        public bool Remove(TView item) => throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
