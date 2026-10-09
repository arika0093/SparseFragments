using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Custom read-only list that deliberately implements no IList&lt;T&gt;.</summary>
public sealed class CustomReadOnlyList<T> : IReadOnlyList<T>
{
    private readonly IList<T> _items;

    public CustomReadOnlyList(IList<T> items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Count;

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

/// <summary>Custom collection implementing only IReadOnlyCollection&lt;T&gt;.</summary>
public sealed class PureReadOnlyCollection<T> : IReadOnlyCollection<T>
{
    private readonly T[] _items;

    public PureReadOnlyCollection(IEnumerable<T> items) => _items = items.ToArray();

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

[SparseFragmentModel]
public partial class ReadOnlySequenceHolder
{
    public IReadOnlyList<string> Names { get; set; } = ["a", "b"];

    public IReadOnlyCollection<int> Scores { get; set; } = [1, 2, 3];

    public IEnumerable<string> Tags { get; set; } = ["x", "y"];

    public IReadOnlyList<string>? MaybeNames { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public IReadOnlyList<ObservableChild> Children { get; set; } = [];
}

public sealed class DescriptorReadOnlySequenceTests
{
    [Test]
    public void ReadOnlyListExposesLiveItemsWithoutMutations()
    {
        var session = new ReadOnlySequenceHolder().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlySequenceHolder.Names), out var descriptor)
            .ShouldBeTrue();
        var array = descriptor.Array.ShouldNotBeNull();
        array!.ItemType.ShouldBe(typeof(string));
        array.ItemViewType.ShouldBe(typeof(string));
        array.Count.ShouldBe(2);
        array.GetItem(0).ShouldBe("a");
        array.GetItemDescriptors(0).ShouldBeNull();
        array.CanAdd.ShouldBeFalse();
        array.CanSetItem.ShouldBeFalse();
        array.CanInsert.ShouldBeFalse();
        array.CanRemove.ShouldBeFalse();
        array.CanMove.ShouldBeFalse();
        array.TryAdd("c").ShouldBeFalse();
        array.TryInsert(0, "c").ShouldBeFalse();
        array.TrySetItem(0, "c").ShouldBeFalse();
        array.TryRemoveAt(0).ShouldBeFalse();
        array.TryMove(0, 1).ShouldBeFalse();
        array.Count.ShouldBe(2);
    }

    [Test]
    public void CustomReadOnlyListWithoutIListStaysLive()
    {
        var backing = new List<string> { "p", "q" };
        var model = new ReadOnlySequenceHolder { Names = new CustomReadOnlyList<string>(backing) };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlySequenceHolder.Names), out var descriptor)
            .ShouldBeTrue();
        var array = descriptor.Array.ShouldNotBeNull();
        array!.Count.ShouldBe(2);
        array.GetItem(1).ShouldBe("q");
        array.CanAdd.ShouldBeFalse();
        array.TryAdd("r").ShouldBeFalse();

        // No snapshot: the descriptor observes the live custom list.
        backing.Add("r");
        array.Count.ShouldBe(3);
        array.GetItem(2).ShouldBe("r");
    }

    [Test]
    public void PureEnumerableSnapshotsWithoutPerItemEnumeration()
    {
        var backing = new List<string> { "x", "y" };
        var model = new ReadOnlySequenceHolder
        {
            Tags = backing.Select(item => item),
            Scores = new PureReadOnlyCollection<int>([1, 2, 3]),
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlySequenceHolder.Tags), out var tags)
            .ShouldBeTrue();
        var tagArray = tags.Array.ShouldNotBeNull();
        tagArray!.Count.ShouldBe(2);
        tagArray.GetItem(0).ShouldBe("x");

        session
            .Descriptors.TryGet(nameof(ReadOnlySequenceHolder.Scores), out var scores)
            .ShouldBeTrue();
        var scoreArray = scores.Array.ShouldNotBeNull();
        scoreArray!.Count.ShouldBe(3);
        scoreArray.GetItem(2).ShouldBe(3);
        scoreArray.CanAdd.ShouldBeFalse();
        scoreArray.TryAdd(4).ShouldBeFalse();

        // Snapshot descriptors ignore later backing changes.
        backing.Add("z");
        tagArray.Count.ShouldBe(2);
        tagArray.GetItem(1).ShouldBe("y");
    }

    [Test]
    public void ReadOnlyFragmentElementsExposeNestedDescriptors()
    {
        var model = new ReadOnlySequenceHolder
        {
            Children = [new ObservableChild { Name = "first" }],
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlySequenceHolder.Children), out var descriptor)
            .ShouldBeTrue();
        var array = descriptor.Array.ShouldNotBeNull();
        array!.ItemType.ShouldBe(typeof(ObservableChild));
        array.GetItem(0).ShouldNotBeNull();

        var nested = array.GetItemDescriptors(0).ShouldNotBeNull();
        nested!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();
        name.Path.ShouldBe("Children[0].Name");
        name.TrySetValue("renamed").ShouldBeTrue();
        model.Children[0].Name.ShouldBe("renamed");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void NullReadOnlySequenceHasNoLiveDescriptor()
    {
        var session = new ReadOnlySequenceHolder().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ReadOnlySequenceHolder.MaybeNames), out var descriptor)
            .ShouldBeTrue();
        descriptor.Array.ShouldBeNull();
    }
}
