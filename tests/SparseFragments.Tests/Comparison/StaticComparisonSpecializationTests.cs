using System.Collections;
using SparseFragments;
using SparseFragments.CompilerServices;

namespace SparseFragments.Tests.Comparison;

[SparseFragmentModel]
public partial class StaticSpecCollections
{
    public List<int> Numbers { get; set; } = [];

    public int[] Scores { get; set; } = [];

    public HashSet<string> Unique { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> Lookup { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Static specialization tests (issue #187): concrete collection overloads match the
/// dynamic fallback semantics while binding comparer access at compile time.
/// </summary>
public sealed class StaticComparisonSpecializationTests
{
    private sealed class ReversedIntList : List<int>, IList
    {
        public ReversedIntList(IEnumerable<int> values)
            : base(values) { }

        object? IList.this[int index]
        {
            get => this[Count - 1 - index];
            set => this[Count - 1 - index] = (int)value!;
        }
    }

    private static bool BetweenIsEmpty(StaticSpecCollections left, StaticSpecCollections right) =>
        StaticSpecCollections
            .Patch.Between(
                Optional<StaticSpecCollections.Fragment?>.Present(
                    StaticSpecCollections.Fragment.From(left)
                ),
                Optional<StaticSpecCollections.Fragment?>.Present(
                    StaticSpecCollections.Fragment.From(right)
                )
            )
            .IsEmpty;

    [Test]
    public void ConcreteArraysMatchObjectPath()
    {
        int[] left = [1, 2, 3];
        int[] same = [1, 2, 3];
        int[] reordered = [3, 2, 1];
        int[] changed = [1, 2, 4];
        int[] shorter = [1, 2];

        SparseFragmentRuntime.AreSequenceEqual(left, same).ShouldBeTrue();
        SparseFragmentRuntime.AreSequenceEqual(left, reordered).ShouldBeFalse();
        SparseFragmentRuntime.AreSequenceEqual(left, changed).ShouldBeFalse();
        SparseFragmentRuntime.AreSequenceEqual(left, shorter).ShouldBeFalse();
        SparseFragmentRuntime
            .AreSequenceEqual(left, shorter)
            .ShouldBe(SparseFragmentRuntime.AreEqual((object)left, (object)shorter));
        SparseFragmentRuntime.AreSequenceEqual((int[]?)null, (int[]?)null).ShouldBeTrue();
        SparseFragmentRuntime.AreSequenceEqual(left, null).ShouldBeFalse();
        SparseFragmentRuntime.AreSequenceEqual(null, left).ShouldBeFalse();
    }

    [Test]
    public void ConcreteListsMatchObjectPath()
    {
        var left = new List<int> { 1, 2, 3 };
        var same = new List<int> { 1, 2, 3 };
        var reordered = new List<int> { 3, 2, 1 };
        var changed = new List<int> { 1, 2, 4 };

        SparseFragmentRuntime.AreSequenceEqual(left, same).ShouldBeTrue();
        SparseFragmentRuntime.AreSequenceEqual(left, reordered).ShouldBeFalse();
        SparseFragmentRuntime.AreSequenceEqual(left, changed).ShouldBeFalse();
        SparseFragmentRuntime
            .AreSequenceEqual(left, reordered)
            .ShouldBe(SparseFragmentRuntime.AreEqual((object)left, (object)reordered));
    }

    [Test]
    public void MixedArrayListFallbackMatchesObjectPath()
    {
        int[] array = [1, 2, 3];
        var list = new List<int> { 1, 2, 3 };
        var changed = new List<int> { 1, 2, 4 };

        SparseFragmentRuntime
            .AreSequenceEqual((IEnumerable<int>)array, (IEnumerable<int>)list)
            .ShouldBeTrue();
        SparseFragmentRuntime
            .AreSequenceEqual((IEnumerable<int>)list, (IEnumerable<int>)array)
            .ShouldBeTrue();
        SparseFragmentRuntime
            .AreSequenceEqual((IEnumerable<int>)array, (IEnumerable<int>)changed)
            .ShouldBeFalse();
        SparseFragmentRuntime
            .AreSequenceEqual((IEnumerable<int>)changed, (IEnumerable<int>)array)
            .ShouldBeFalse();
    }

    [Test]
    public void DerivedListsKeepNonGenericComparisonView()
    {
        var stored = new ReversedIntList([1, 2, 3]);
        var reversed = new List<int> { 3, 2, 1 };
        var storageOrder = new List<int> { 1, 2, 3 };

        // The non-generic view reads storage reversed, so the derived list equals
        // the reversed plain list and differs from storage order on both paths.
        SparseFragmentRuntime.AreSequenceEqual(stored, reversed).ShouldBeTrue();
        SparseFragmentRuntime.AreSequenceEqual(reversed, stored).ShouldBeTrue();
        SparseFragmentRuntime.AreSequenceEqual(stored, storageOrder).ShouldBeFalse();
        SparseFragmentRuntime
            .AreSequenceEqual(stored, reversed)
            .ShouldBe(SparseFragmentRuntime.AreEqual((object)stored, (object)reversed));
    }

    [Test]
    public void ConcreteSetsPreserveComparerVeto()
    {
        var left = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" };
        var reordered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BETA", "ALPHA" };
        var ordinal = new HashSet<string>(StringComparer.Ordinal) { "alpha", "beta" };
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "gamma" };

        SparseFragmentRuntime.AreSetEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreSetEqual(reordered, left).ShouldBeTrue();
        SparseFragmentRuntime.AreSetEqual(left, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(ordinal, left).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(left, changed).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(left, left).ShouldBeTrue();
        SparseFragmentRuntime
            .AreSetEqual((HashSet<string>?)null, (HashSet<string>?)null)
            .ShouldBeTrue();
        SparseFragmentRuntime.AreSetEqual(left, null).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(null, left).ShouldBeFalse();
    }

    [Test]
    public void ConcreteSortedSetsMatchInterfacePath()
    {
        var left = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" };
        var reordered = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "BETA", "ALPHA" };
        var ordinal = new SortedSet<string>(StringComparer.Ordinal) { "alpha", "beta" };

        SparseFragmentRuntime.AreSetEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreSetEqual(left, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(ordinal, left).ShouldBeFalse();
        SparseFragmentRuntime
            .AreSetEqual(left, reordered)
            .ShouldBe(
                SparseFragmentRuntime.AreSetEqual(
                    (IEnumerable<string>)left,
                    (IEnumerable<string>)reordered
                )
            );
    }

    [Test]
    public void ConcreteDictionariesPreserveComparerVeto()
    {
        var left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = 1,
            ["second"] = 2,
        };
        var reordered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SECOND"] = 2,
            ["FIRST"] = 1,
        };
        var ordinal = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["first"] = 1,
            ["second"] = 2,
        };
        var changed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = 1,
            ["second"] = 3,
        };

        SparseFragmentRuntime.AreDictionaryEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(reordered, left).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(left, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(ordinal, left).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(left, changed).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(left, left).ShouldBeTrue();
        SparseFragmentRuntime
            .AreDictionaryEqual((Dictionary<string, int>?)null, (Dictionary<string, int>?)null)
            .ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(left, null).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(null, left).ShouldBeFalse();
    }

    [Test]
    public void ConcreteDictionaryValueComparerIsHonored()
    {
        var left = new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = "ALPHA" };
        var same = new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = "alpha" };
        var missing = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["other"] = "alpha",
        };

        SparseFragmentRuntime.AreDictionaryEqual(left, same).ShouldBeFalse();
        SparseFragmentRuntime
            .AreDictionaryEqual(left, same, StringComparer.OrdinalIgnoreCase.Equals)
            .ShouldBeTrue();
        SparseFragmentRuntime
            .AreDictionaryEqual(left, missing, StringComparer.OrdinalIgnoreCase.Equals)
            .ShouldBeFalse();
    }

    [Test]
    public void ConcreteSequenceItemComparerIsHonored()
    {
        var left = new List<string> { "ALPHA" };
        var right = new List<string> { "alpha" };
        var array = new[] { "alpha" };

        SparseFragmentRuntime.AreSequenceEqual(left, right).ShouldBeFalse();
        SparseFragmentRuntime
            .AreSequenceEqual(left, right, StringComparer.OrdinalIgnoreCase.Equals)
            .ShouldBeTrue();
        SparseFragmentRuntime
            .AreSequenceEqual(left.ToArray(), array, StringComparer.OrdinalIgnoreCase.Equals)
            .ShouldBeTrue();
    }

    [Test]
    public void InterfaceDeclaredConcreteValuesMatchStaticOverloads()
    {
        ISet<string> leftSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };
        ISet<string> rightSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "B", "A" };
        ISet<string> ordinalSet = new HashSet<string>(StringComparer.Ordinal) { "a", "b" };

        SparseFragmentRuntime.AreSetEqual(leftSet, rightSet).ShouldBeTrue();
        SparseFragmentRuntime.AreSetEqual(leftSet, ordinalSet).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(ordinalSet, leftSet).ShouldBeFalse();

        IDictionary<string, int> leftDict = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["k"] = 1,
        };
        IDictionary<string, int> rightDict = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["K"] = 1,
        };
        IDictionary<string, int> ordinalDict = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["k"] = 1,
        };

        SparseFragmentRuntime.AreDictionaryEqual(leftDict, rightDict).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(leftDict, ordinalDict).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(ordinalDict, leftDict).ShouldBeFalse();
    }

    [Test]
    public void GeneratedFragmentsCompareConcreteCollections()
    {
        var left = new StaticSpecCollections
        {
            Numbers = [1, 2, 3],
            Scores = [7, 8],
            Unique = new HashSet<string>(StringComparer.Ordinal) { "a", "b" },
            Lookup = new Dictionary<string, int>(StringComparer.Ordinal) { ["k"] = 1 },
        };
        var same = new StaticSpecCollections
        {
            Numbers = [1, 2, 3],
            Scores = [7, 8],
            Unique = new HashSet<string>(StringComparer.Ordinal) { "b", "a" },
            Lookup = new Dictionary<string, int>(StringComparer.Ordinal) { ["k"] = 1 },
        };
        var reorderedSequence = new StaticSpecCollections
        {
            Numbers = [3, 2, 1],
            Scores = [7, 8],
            Unique = new HashSet<string>(StringComparer.Ordinal) { "a", "b" },
            Lookup = new Dictionary<string, int>(StringComparer.Ordinal) { ["k"] = 1 },
        };
        var changedValue = new StaticSpecCollections
        {
            Numbers = [1, 2, 3],
            Scores = [7, 8],
            Unique = new HashSet<string>(StringComparer.Ordinal) { "a", "b" },
            Lookup = new Dictionary<string, int>(StringComparer.Ordinal) { ["k"] = 2 },
        };
        var changedComparer = new StaticSpecCollections
        {
            Numbers = [1, 2, 3],
            Scores = [7, 8],
            Unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" },
            Lookup = new Dictionary<string, int>(StringComparer.Ordinal) { ["k"] = 1 },
        };

        BetweenIsEmpty(left, same).ShouldBeTrue();
        BetweenIsEmpty(left, reorderedSequence).ShouldBeFalse();
        BetweenIsEmpty(left, changedValue).ShouldBeFalse();
        BetweenIsEmpty(left, changedComparer).ShouldBeFalse();
    }
}
