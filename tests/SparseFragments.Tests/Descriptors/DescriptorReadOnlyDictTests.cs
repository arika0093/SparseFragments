using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Custom dictionary implementing only IReadOnlyDictionary.</summary>
public sealed class CustomReadOnlyDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly IReadOnlyDictionary<TKey, TValue> _items;

    public CustomReadOnlyDictionary(IReadOnlyDictionary<TKey, TValue> items) => _items = items;

    public TValue this[TKey key] => _items[key];

    public IEnumerable<TKey> Keys => _items.Keys;

    public IEnumerable<TValue> Values => _items.Values;

    public int Count => _items.Count;

    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

[SparseFragmentModel]
public partial class ReadOnlyDictHolder
{
    public IReadOnlyDictionary<string, int> ReadOnlyScores { get; set; } =
        new Dictionary<string, int> { ["k"] = 3 };

    // Sorted backing stores stay ordered through the read-only live view.
    public IReadOnlyDictionary<string, int> SortedReadOnly { get; set; } =
        new SortedDictionary<string, int> { ["b"] = 2, ["a"] = 1 };

    // NOTE: SortedDictionary/SortedList as DECLARED member types still fail in the
    // fragment WriteTo emitter (it patterns matches Dictionary<K,V>), which is
    // outside descriptors. Sorted runtimes behind interface declarations work here.
    public IReadOnlyDictionary<string, int>? MaybeScores { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public IReadOnlyDictionary<string, ObservableChild> ReadOnlyChildren { get; set; } =
        new Dictionary<string, ObservableChild>();
}

public sealed class DescriptorReadOnlyDictTests
{
    [Test]
    public void SortedBackingStoreExposesOrderedKeysWithoutMutators()
    {
        var session = new ReadOnlyDictHolder().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlyDictHolder.SortedReadOnly), out var descriptor)
            .ShouldBeTrue();
        var dict = descriptor.Dictionary.ShouldNotBeNull();
        dict!.KeyType.ShouldBe(typeof(string));
        dict.ValueType.ShouldBe(typeof(int));
        dict.ValueViewType.ShouldBe(typeof(int));
        dict.Count.ShouldBe(2);
        dict.Keys.Cast<string>().ShouldBe(["a", "b"]);
        dict.TryGetValue("a", out var value).ShouldBeTrue();
        value.ShouldBe(1);
        dict.TryGetValue("missing", out _).ShouldBeFalse();
        dict.CanAdd.ShouldBeFalse();
        dict.CanRemove.ShouldBeFalse();
        dict.CanSet.ShouldBeFalse();
        dict.TryAdd("c", 3).ShouldBeFalse();
        dict.TrySetValue("a", 9).ShouldBeFalse();
        dict.TryRemove("a").ShouldBeFalse();
        dict.Count.ShouldBe(2);
    }

    [Test]
    public void SortedListBackingStoreIsDescribed()
    {
        var model = new ReadOnlyDictHolder
        {
            SortedReadOnly = new SortedList<string, int> { ["b"] = 2, ["a"] = 1 },
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlyDictHolder.SortedReadOnly), out var descriptor)
            .ShouldBeTrue();
        var dict = descriptor.Dictionary.ShouldNotBeNull();
        dict!.Count.ShouldBe(2);
        dict.Keys.Cast<string>().ShouldBe(["a", "b"]);
        dict.TryGetValue("b", out var value).ShouldBeTrue();
        value.ShouldBe(2);
        dict.CanSet.ShouldBeFalse();
    }

    [Test]
    public void CustomPureReadOnlyDictionaryIsSupported()
    {
        var model = new ReadOnlyDictHolder
        {
            ReadOnlyScores = new CustomReadOnlyDictionary<string, int>(
                new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 }
            ),
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlyDictHolder.ReadOnlyScores), out var descriptor)
            .ShouldBeTrue();
        var dict = descriptor.Dictionary.ShouldNotBeNull();
        dict!.Count.ShouldBe(2);
        dict.Keys.Cast<string>().OrderBy(key => key).ShouldBe(["x", "y"]);
        dict.TryGetValue("y", out var value).ShouldBeTrue();
        value.ShouldBe(2);
        dict.TryAdd("z", 3).ShouldBeFalse();
    }

    [Test]
    public void ReadOnlyFragmentValuesExposeNestedDescriptors()
    {
        var model = new ReadOnlyDictHolder
        {
            ReadOnlyChildren = new Dictionary<string, ObservableChild>
            {
                ["entry"] = new ObservableChild { Name = "first" },
            },
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlyDictHolder.ReadOnlyChildren), out var descriptor)
            .ShouldBeTrue();
        var dict = descriptor.Dictionary.ShouldNotBeNull();
        dict!.ValueType.ShouldBe(typeof(ObservableChild));
        dict.TryGetValue("entry", out var entry).ShouldBeTrue();
        entry.ShouldNotBeNull();

        var nested = dict.GetValueDescriptors("entry").ShouldNotBeNull();
        nested!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();
        name.TrySetValue("renamed").ShouldBeTrue();
        model.ReadOnlyChildren["entry"].Name.ShouldBe("renamed");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void NullReadOnlyDictionaryHasNoLiveDescriptor()
    {
        var session = new ReadOnlyDictHolder().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlyDictHolder.MaybeScores), out var descriptor)
            .ShouldBeTrue();
        descriptor.Dictionary.ShouldBeNull();
    }
}
