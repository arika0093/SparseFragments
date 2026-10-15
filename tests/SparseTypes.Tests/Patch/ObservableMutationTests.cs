using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SparseTypes.Tests;

public sealed class ObservableMutationTests
{
    private sealed class Child
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class View(Child target, Action changed)
    {
        public Child Target { get; } = target;

        public Action Changed { get; } = changed;
    }

    [Test]
    public void NotifyingListMutationsOnlyWrapDeliveredPayloads()
    {
        var wraps = 0;
        var notified = 0;
        var source = new ObservableCollection<Child> { new() { Name = "old" } };
        var view = new SparseObservableList<Child, View>(
            source,
            (item, changed) =>
            {
                wraps++;
                return new View(item, changed);
            },
            static item => item.Target,
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var added = new Child { Name = "added" };
        var replacement = new Child { Name = "replacement" };
        var indexed = new Child { Name = "indexed" };

        view.AddModel(added);
        wraps.ShouldBe(1);
        view.SetModel(0, replacement);
        wraps.ShouldBe(3);
        view[0] = new View(indexed, static () => { });
        wraps.ShouldBe(5);
        view.RemoveAt(1);
        wraps.ShouldBe(6);

        events
            .Select(args => args.Action)
            .ShouldBe([
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Replace,
                NotifyCollectionChangedAction.Replace,
                NotifyCollectionChangedAction.Remove,
            ]);
        events[0].NewStartingIndex.ShouldBe(1);
        ((View)events[0].NewItems![0]!).Target.ShouldBe(added);
        events[1].NewStartingIndex.ShouldBe(0);
        ((View)events[1].NewItems![0]!).Target.ShouldBe(replacement);
        ((View)events[1].OldItems![0]!).Target.Name.ShouldBe("old");
        ((View)events[2].NewItems![0]!).Target.ShouldBe(indexed);
        events[3].OldStartingIndex.ShouldBe(1);
        ((View)events[3].OldItems![0]!).Target.ShouldBe(added);
        notified.ShouldBe(4);
    }

    [Test]
    public void NotifyingListMoveConvertsOnlyItsNewItem()
    {
        var wraps = 0;
        var notified = 0;
        var first = new Child { Name = "first" };
        var source = new ObservableCollection<Child>
        {
            first,
            new() { Name = "second" },
        };
        var view = new SparseObservableList<Child, View>(
            source,
            (item, changed) =>
            {
                wraps++;
                return new View(item, changed);
            },
            static item => item.Target,
            () => notified++
        );
        NotifyCollectionChangedEventArgs? delivered = null;
        view.CollectionChanged += (_, args) => delivered = args;

        view.Move(0, 1);

        wraps.ShouldBe(1);
        delivered!.Action.ShouldBe(NotifyCollectionChangedAction.Move);
        delivered.OldStartingIndex.ShouldBe(0);
        delivered.NewStartingIndex.ShouldBe(1);
        ((View)delivered.NewItems![0]!).Target.ShouldBe(first);
        notified.ShouldBe(1);
    }

    [Test]
    public void SilentNotifyingListUsesDeferredFallbackNotification()
    {
        var wraps = 0;
        var notified = 0;
        var view = new SparseObservableList<int, string>(
            new SilentNotifyingList<int>(),
            (item, _) =>
            {
                wraps++;
                return item.ToString();
            },
            static item => int.Parse(item),
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);

        view.AddModel(42);

        wraps.ShouldBe(1);
        events.Count.ShouldBe(1);
        events[0].Action.ShouldBe(NotifyCollectionChangedAction.Add);
        events[0].NewStartingIndex.ShouldBe(0);
        events[0].NewItems![0].ShouldBe("42");
        notified.ShouldBe(1);
    }

    [Test]
    public void FailedListMutationDoesNotWrapOrNotify()
    {
        var wraps = 0;
        var notified = 0;
        var view = new SparseObservableList<int, string>(
            new List<int>().AsReadOnly(),
            (item, _) =>
            {
                wraps++;
                return item.ToString();
            },
            static item => int.Parse(item),
            () => notified++
        );
        var events = 0;
        view.CollectionChanged += (_, _) => events++;

        Should.Throw<NotSupportedException>(() => view.AddModel(42));

        wraps.ShouldBe(0);
        events.ShouldBe(0);
        notified.ShouldBe(0);
    }

    [Test]
    public void ReentrantNotifyingListMutationDeliversEachSourceEventOnce()
    {
        var wraps = 0;
        var notified = 0;
        var source = new ObservableCollection<Child>();
        var view = new SparseObservableList<Child, View>(
            source,
            (item, changed) =>
            {
                wraps++;
                return new View(item, changed);
            },
            static item => item.Target,
            () => notified++
        );
        var events = 0;
        view.CollectionChanged += (_, _) =>
        {
            events++;
            if (events == 1)
            {
                source.Add(new Child { Name = "nested" });
            }
        };

        view.AddModel(new Child { Name = "outer" });

        wraps.ShouldBe(2);
        events.ShouldBe(2);
        notified.ShouldBe(2);
        view.Count.ShouldBe(2);
    }

    [Test]
    public void ListRangeNotificationWrapsOnlyItemsInRange()
    {
        var wraps = 0;
        var notified = 0;
        var source = new RangeNotifyingList<Child>();
        var view = new SparseObservableList<Child, View>(
            source,
            (item, changed) =>
            {
                wraps++;
                return new View(item, changed);
            },
            static item => item.Target,
            () => notified++
        );
        NotifyCollectionChangedEventArgs? delivered = null;
        view.CollectionChanged += (_, args) => delivered = args;

        source.AddRange([new Child { Name = "a" }, new Child { Name = "b" }]);

        wraps.ShouldBe(2);
        delivered!.Action.ShouldBe(NotifyCollectionChangedAction.Add);
        delivered.NewStartingIndex.ShouldBe(0);
        delivered.NewItems!.Count.ShouldBe(2);
        ((View)delivered.NewItems[0]!).Target.Name.ShouldBe("a");
        ((View)delivered.NewItems[1]!).Target.Name.ShouldBe("b");
        notified.ShouldBe(1);
    }

    [Test]
    public void DictionaryMutationAndRangeNotificationsUseDirectSingleItemsAndRangeBuffers()
    {
        var wraps = 0;
        var notified = 0;
        var oldValue = new Child { Name = "old" };
        var source = new NotifyingDictionary<string, Child> { ["key"] = oldValue };
        var view = new SparseObservableDictionary<string, Child, View>(
            source,
            (item, changed) =>
            {
                wraps++;
                return new View(item, changed);
            },
            static item => item.Target,
            () => notified++
        );
        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, args) => events.Add(args);
        var added = new Child { Name = "added" };
        var replacement = new Child { Name = "replacement" };

        view.AddModel("extra", added);
        wraps.ShouldBe(1);
        view.SetModel("key", replacement);
        wraps.ShouldBe(3);
        view.Remove("extra").ShouldBeTrue();
        wraps.ShouldBe(4);
        source.AddRange([
            new KeyValuePair<string, Child>("a", new Child { Name = "a" }),
            new KeyValuePair<string, Child>("b", new Child { Name = "b" }),
        ]);

        wraps.ShouldBe(6);
        events
            .Select(args => args.Action)
            .ShouldBe([
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Replace,
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Add,
            ]);
        ((KeyValuePair<string, View>)events[0].NewItems![0]!).Value.Target.ShouldBe(added);
        ((KeyValuePair<string, View>)events[1].NewItems![0]!).Value.Target.ShouldBe(replacement);
        ((KeyValuePair<string, View>)events[1].OldItems![0]!).Value.Target.ShouldBe(oldValue);
        ((KeyValuePair<string, View>)events[2].OldItems![0]!).Value.Target.ShouldBe(added);
        events[3].NewItems!.Count.ShouldBe(2);
        ((KeyValuePair<string, View>)events[3].NewItems![0]!).Value.Target.Name.ShouldBe("a");
        ((KeyValuePair<string, View>)events[3].NewItems![1]!).Value.Target.Name.ShouldBe("b");
        notified.ShouldBe(4);
    }

    private sealed class SilentNotifyingList<T> : List<T>, INotifyCollectionChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class RangeNotifyingList<T> : List<T>, INotifyCollectionChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public void AddRange(IList<T> items)
        {
            var startIndex = Count;
            base.AddRange(items);
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    (System.Collections.IList)items,
                    startIndex
                )
            );
        }
    }

    private sealed class NotifyingDictionary<TKey, TValue>
        : IDictionary<TKey, TValue>,
            INotifyCollectionChanged
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _items = new();

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public TValue this[TKey key]
        {
            get => _items[key];
            set
            {
                var old = _items.TryGetValue(key, out var previous);
                _items[key] = value;
                var newPair = new KeyValuePair<TKey, TValue>(key, value);
                var args = old
                    ? new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace,
                        newPair,
                        new KeyValuePair<TKey, TValue>(key, previous!)
                    )
                    : new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Add,
                        newPair
                    );
                CollectionChanged?.Invoke(this, args);
            }
        }

        public ICollection<TKey> Keys => _items.Keys;

        public ICollection<TValue> Values => _items.Values;

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(TKey key, TValue value)
        {
            _items.Add(key, value);
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    new KeyValuePair<TKey, TValue>(key, value)
                )
            );
        }

        public bool Remove(TKey key)
        {
            if (!_items.Remove(key, out var value))
            {
                return false;
            }

            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove,
                    new KeyValuePair<TKey, TValue>(key, value)
                )
            );
            return true;
        }

        public bool ContainsKey(TKey key) => _items.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value!);

        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        public void AddRange(IList<KeyValuePair<TKey, TValue>> items)
        {
            foreach (var pair in items)
            {
                _items.Add(pair.Key, pair.Value);
            }

            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    (System.Collections.IList)items
                )
            );
        }

        public void Clear()
        {
            _items.Clear();
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            );
        }

        public bool Contains(KeyValuePair<TKey, TValue> item) =>
            ((ICollection<KeyValuePair<TKey, TValue>>)_items).Contains(item);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);

        public bool Remove(KeyValuePair<TKey, TValue> item) => Contains(item) && Remove(item.Key);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
