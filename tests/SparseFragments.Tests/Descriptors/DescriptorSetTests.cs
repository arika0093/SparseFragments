using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Custom set implementing only IReadOnlySet (no ISet).</summary>
public sealed class CustomReadOnlySet<T> : IReadOnlySet<T>
{
    private readonly IReadOnlySet<T> _items;

    public CustomReadOnlySet(IEnumerable<T> items) => _items = items.ToHashSet();

    public int Count => _items.Count;

    public bool Contains(T item) => _items.Contains(item);

    public bool IsProperSubsetOf(IEnumerable<T> other) => _items.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<T> other) => _items.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<T> other) => _items.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<T> other) => _items.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<T> other) => _items.Overlaps(other);

    public bool SetEquals(IEnumerable<T> other) => _items.SetEquals(other);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

[SparseFragmentModel]
public partial class SetHolder
{
    public HashSet<string> Tags { get; set; } = ["a", "b"];

    public ISet<int> Scores { get; set; } = new HashSet<int> { 1 };

    public IReadOnlySet<string> ReadOnly { get; set; } = new HashSet<string> { "x" };

    public IReadOnlySet<string>? Maybe { get; set; }

    public HashSet<string> CaseBlind { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "abc" };

    public HashSet<ObservableChild> People { get; set; } = [];
}

public sealed class DescriptorSetTests
{
    [Test]
    public void HashSetExposesMembershipWithComparerAndMutations()
    {
        var model = new SetHolder();
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(SetHolder.Tags), out var descriptor).ShouldBeTrue();
        descriptor.Array.ShouldBeNull();
        descriptor.Dictionary.ShouldBeNull();
        var sets = descriptor.Set.ShouldNotBeNull();
        sets!.ItemType.ShouldBe(typeof(string));
        sets.IsItemNullable.ShouldBeFalse();
        sets.Count.ShouldBe(2);
        sets.Items.Cast<string>().OrderBy(item => item).ShouldBe(["a", "b"]);
        sets.Contains("a").ShouldBeTrue();
        sets.Contains("missing").ShouldBeFalse();
        // Default comparer is case-sensitive.
        sets.Contains("A").ShouldBeFalse();
        sets.CanAdd.ShouldBeTrue();
        sets.CanRemove.ShouldBeTrue();

        sets.TryAdd("c").ShouldBeTrue();
        model.Tags.ShouldContain("c");
        session.HasChanges.ShouldBeTrue();
        sets.TryAdd("c").ShouldBeFalse();
        sets.TryRemove("a").ShouldBeTrue();
        model.Tags.ShouldNotContain("a");
        sets.TryRemove("a").ShouldBeFalse();
        sets.Count.ShouldBe(2);
    }

    [Test]
    public void MembershipHonorsTheSourceComparer()
    {
        var session = new SetHolder().CreateEditSession();

        session.Descriptors.TryGet(nameof(SetHolder.CaseBlind), out var descriptor).ShouldBeTrue();
        var sets = descriptor.Set.ShouldNotBeNull();
        sets!.Contains("ABC").ShouldBeTrue();
        sets.Contains("abc").ShouldBeTrue();
        sets.TryAdd("ABC").ShouldBeFalse();
    }

    [Test]
    public void CustomPureReadOnlySetIsReadOnly()
    {
        var model = new SetHolder { ReadOnly = new CustomReadOnlySet<string>(["x", "y"]) };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(SetHolder.ReadOnly), out var descriptor).ShouldBeTrue();
        var sets = descriptor.Set.ShouldNotBeNull();
        sets!.Count.ShouldBe(2);
        sets.Contains("y").ShouldBeTrue();
        sets.Items.Cast<string>().OrderBy(item => item).ShouldBe(["x", "y"]);
        sets.CanAdd.ShouldBeFalse();
        sets.CanRemove.ShouldBeFalse();
        sets.TryAdd("z").ShouldBeFalse();
        sets.TryRemove("x").ShouldBeFalse();
        sets.Count.ShouldBe(2);
    }

    [Test]
    public void FragmentElementsSupportMembershipWithModelAndProxyValues()
    {
        var person = new ObservableChild { Name = "ann" };
        var model = new SetHolder { People = [person] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(SetHolder.People), out var descriptor).ShouldBeTrue();
        var sets = descriptor.Set.ShouldNotBeNull();
        sets!.ItemType.ShouldBe(typeof(ObservableChild));
        sets.Count.ShouldBe(1);
        sets.Contains(person).ShouldBeTrue();
        sets.Contains(new ObservableChild.SparseObservable(person)).ShouldBeTrue();
        sets.Contains(new ObservableChild { Name = "ann" }).ShouldBeFalse();

        var other = new ObservableChild { Name = "bob" };
        sets.TryAdd(other).ShouldBeTrue();
        model.People.ShouldContain(other);
        session.HasChanges.ShouldBeTrue();
        sets.TryRemove(person).ShouldBeTrue();
        model.People.ShouldNotContain(person);
    }

    [Test]
    public void NullSetHasNoLiveDescriptor()
    {
        var session = new SetHolder().CreateEditSession();

        session.Descriptors.TryGet(nameof(SetHolder.Maybe), out var descriptor).ShouldBeTrue();
        descriptor.Set.ShouldBeNull();
    }
}
