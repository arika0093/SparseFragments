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
    private readonly bool _cacheReferences;
    private readonly Dictionary<object, ProxyEntry> _proxies = new(
        SparseReferenceEqualityComparer.Instance
    );
    private bool _disposed;

    /// <summary>Creates a notifying view over a live mutable dictionary.</summary>
    public SparseObservableDictionary(
        IDictionary<TKey, TModel> model,
        Func<TModel, Action, TView> wrap,
        Func<TView, TModel> unwrap,
        Action onChanged,
        bool cacheReferences = false
    )
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _wrap = wrap ?? throw new ArgumentNullException(nameof(wrap));
        _unwrap = unwrap ?? throw new ArgumentNullException(nameof(unwrap));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _cacheReferences = cacheReferences;
        if (_model is INotifyCollectionChanged notifying)
        {
            notifying.CollectionChanged += OnModelCollectionChanged;
        }
    }

    /// <summary>The live dictionary instance wrapped by this view.</summary>
    public IDictionary<TKey, TModel> Model => _model;

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
        _model.Add(key, value);
        if (_model is not INotifyCollectionChanged)
        {
            RaiseCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    new KeyValuePair<TKey, TView>(key, Wrap(value))
                )
            );
        }
    }

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

        _model.Clear();
        PruneProxies();
        if (_model is not INotifyCollectionChanged)
        {
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
        var removed = _model.Remove(key);
        if (!removed)
        {
            return false;
        }

        PruneProxies();
        if (_model is not INotifyCollectionChanged)
        {
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

        foreach (var entry in _proxies.Values)
        {
            entry.Active = false;
        }

        _proxies.Clear();
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
        _model[key] = value;
        PruneProxies();
        if (_model is not INotifyCollectionChanged)
        {
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
        PruneProxies();
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

    private List<KeyValuePair<TKey, TView>>? ConvertItems(IList? items) =>
        items
            ?.Cast<KeyValuePair<TKey, TModel>>()
            .Select(pair => new KeyValuePair<TKey, TView>(pair.Key, Wrap(pair.Value)))
            .ToList();

    private void RaiseCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        if (
            args.Action
            is NotifyCollectionChangedAction.Add
                or NotifyCollectionChangedAction.Remove
                or NotifyCollectionChangedAction.Reset
        )
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
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

    private bool IsPresent(TModel item) => _model.Values.Any(value => ReferenceEquals(value, item));

    private void PruneProxies()
    {
        if (!_cacheReferences)
        {
            return;
        }

        var stale = _proxies.Keys.Where(key => !IsPresent((TModel)key)).ToArray();
        foreach (var key in stale)
        {
            _proxies[key].Active = false;
            _proxies.Remove(key);
        }
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
