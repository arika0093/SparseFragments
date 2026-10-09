namespace SparseFragments.Tests;

/// <summary>List allowing replacement but rejecting size changes.</summary>
public sealed class FixedSizeList<T> : IList<T>, System.Collections.IList
{
    private readonly List<T> _items;

    public FixedSizeList(IEnumerable<T> items) => _items = items.ToList();

    public T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public void Add(T item) => throw new NotSupportedException("Fixed size.");

    public void Clear() => throw new NotSupportedException("Fixed size.");

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(T item) => _items.IndexOf(item);

    public void Insert(int index, T item) => throw new NotSupportedException("Fixed size.");

    public bool Remove(T item) => throw new NotSupportedException("Fixed size.");

    public void RemoveAt(int index) => throw new NotSupportedException("Fixed size.");

    int System.Collections.IList.Add(object? value) => throw new NotSupportedException();

    bool System.Collections.IList.Contains(object? value) => value is T item && Contains(item);

    int System.Collections.IList.IndexOf(object? value) => value is T item ? IndexOf(item) : -1;

    void System.Collections.IList.Insert(int index, object? value) =>
        throw new NotSupportedException();

    void System.Collections.IList.Remove(object? value) => throw new NotSupportedException();

    void System.Collections.ICollection.CopyTo(Array array, int index) =>
        ((System.Collections.ICollection)_items).CopyTo(array, index);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    object? System.Collections.IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T)value!;
    }
}

/// <summary>List throwing NotSupportedException without advertising fixed size.</summary>
public sealed class ThrowingList<T> : IList<T>
{
    private readonly List<T> _items;

    public ThrowingList(IEnumerable<T> items) => _items = items.ToList();

    public T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public void Add(T item) => throw new NotSupportedException("No adds.");

    public void Clear() => throw new NotSupportedException("No adds.");

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(T item) => _items.IndexOf(item);

    public void Insert(int index, T item) => throw new NotSupportedException("No inserts.");

    public bool Remove(T item) => throw new NotSupportedException("No removes.");

    public void RemoveAt(int index) => throw new NotSupportedException("No removes.");

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

[SparseFragmentModel]
public partial class MutableListHolder
{
    public List<string> Tags { get; set; } = new();

    public IList<string> CustomTags { get; set; } = new List<string>();
}

public sealed class DescriptorTryContractTests
{
    [Test]
    public void FixedSizeListAdvertisesReplaceOnly()
    {
        var model = new MutableListHolder { CustomTags = new FixedSizeList<string>(["a", "b"]) };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(MutableListHolder.CustomTags), out var tags)
            .ShouldBeTrue();
        var array = tags.Array.ShouldNotBeNull();
        array!.CanSetItem.ShouldBeTrue();
        array.CanAdd.ShouldBeFalse();
        array.CanInsert.ShouldBeFalse();
        array.CanRemove.ShouldBeFalse();
        array.CanMove.ShouldBeFalse();

        array.TrySetItem(0, "updated").ShouldBeTrue();
        model.CustomTags[0].ShouldBe("updated");
        array.TryAdd("c").ShouldBeFalse();
        array.TryInsert(0, "c").ShouldBeFalse();
        array.TryRemoveAt(0).ShouldBeFalse();
        array.TryMove(0, 1).ShouldBeFalse();
        model.CustomTags.ShouldBe(["updated", "b"]);
    }

    [Test]
    public void ThrowingListFailsPredictablyWithoutPartialMutation()
    {
        var model = new MutableListHolder { CustomTags = new ThrowingList<string>(["a", "b"]) };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(MutableListHolder.CustomTags), out var tags)
            .ShouldBeTrue();
        var array = tags.Array.ShouldNotBeNull();

        array!.TryAdd("c").ShouldBeFalse();
        array.TryInsert(0, "c").ShouldBeFalse();
        array.TryRemoveAt(0).ShouldBeFalse();
        array.TryMove(0, 1).ShouldBeFalse();
        model.CustomTags.ShouldBe(["a", "b"]);

        // Replacement still works on the throwing list.
        array.TrySetItem(0, "updated").ShouldBeTrue();
        model.CustomTags[0].ShouldBe("updated");
    }

    [Test]
    public void ReadOnlyListRejectsAllMutations()
    {
        var model = new MutableListHolder { CustomTags = new List<string> { "a" }.AsReadOnly() };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(MutableListHolder.CustomTags), out var tags)
            .ShouldBeTrue();
        var array = tags.Array.ShouldNotBeNull();
        array!.CanAdd.ShouldBeFalse();
        array.CanSetItem.ShouldBeFalse();
        array.CanInsert.ShouldBeFalse();
        array.CanRemove.ShouldBeFalse();
        array.CanMove.ShouldBeFalse();
        array.TryAdd("b").ShouldBeFalse();
        array.TrySetItem(0, "b").ShouldBeFalse();
        array.TryInsert(0, "b").ShouldBeFalse();
        array.TryRemoveAt(0).ShouldBeFalse();
        array.TryMove(0, 0).ShouldBeFalse();
        model.CustomTags.ShouldBe(["a"]);
    }

    [Test]
    public void RegularListsKeepWorkingPaths()
    {
        var model = new MutableListHolder { Tags = ["a"] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(MutableListHolder.Tags), out var tags).ShouldBeTrue();
        var array = tags.Array.ShouldNotBeNull();
        array!.CanAdd.ShouldBeTrue();
        array.CanMove.ShouldBeTrue();
        array.TryAdd("b").ShouldBeTrue();
        array.TryMove(0, 1).ShouldBeTrue();
        model.Tags.ShouldBe(["b", "a"]);
    }
}
