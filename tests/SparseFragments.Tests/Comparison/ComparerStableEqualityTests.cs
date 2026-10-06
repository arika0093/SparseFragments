using System.Collections;
using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

public sealed class ComparerStableEqualityTests
{
    private sealed class CustomReadOnlyDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _inner;

        public CustomReadOnlyDictionary(Dictionary<TKey, TValue> inner) => _inner = inner;

        public TValue this[TKey key] => _inner[key];

        public IEnumerable<TKey> Keys => _inner.Keys;

        public IEnumerable<TValue> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Test]
    public void SetsWithSameComparerAreEqualRegardlessOfOrder()
    {
        var left = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" };
        var reordered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BETA", "ALPHA" };

        SparseFragmentRuntime.AreSetEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreSetEqual(reordered, left).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)left, (object)reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)reordered, (object)left).ShouldBeTrue();
    }

    [Test]
    public void SetsWithDifferentComparersAreUnequalInBothDirections()
    {
        var ignoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha" };
        var ordinal = new HashSet<string>(StringComparer.Ordinal) { "alpha" };

        SparseFragmentRuntime.AreSetEqual(ignoreCase, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(ordinal, ignoreCase).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ignoreCase, (object)ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ordinal, (object)ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void SetsMatchingUnderOneComparerOnlyAreUnequalInBothDirections()
    {
        var ignoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ALPHA" };
        var ordinal = new HashSet<string>(StringComparer.Ordinal) { "alpha" };

        SparseFragmentRuntime.AreSetEqual(ignoreCase, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreSetEqual(ordinal, ignoreCase).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ignoreCase, (object)ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ordinal, (object)ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void DictionariesWithSameComparerAreEqualRegardlessOfOrder()
    {
        var left = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = 1,
            ["second"] = 2,
        };
        var reordered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["second"] = 2,
            ["first"] = 1,
        };

        SparseFragmentRuntime.AreDictionaryEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(reordered, left).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)left, (object)reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)reordered, (object)left).ShouldBeTrue();
    }

    [Test]
    public void DictionariesWithDifferentComparersAreUnequalInBothDirections()
    {
        var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = 1,
        };
        var ordinal = new Dictionary<string, int>(StringComparer.Ordinal) { ["first"] = 1 };

        SparseFragmentRuntime.AreDictionaryEqual(ignoreCase, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(ordinal, ignoreCase).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ignoreCase, (object)ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ordinal, (object)ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void DictionariesMatchingUnderOneComparerOnlyAreUnequalInBothDirections()
    {
        var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["FIRST"] = 1,
        };
        var ordinal = new Dictionary<string, int>(StringComparer.Ordinal) { ["first"] = 1 };

        SparseFragmentRuntime.AreDictionaryEqual(ignoreCase, ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(ordinal, ignoreCase).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ignoreCase, (object)ordinal).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)ordinal, (object)ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void FragmentSetsWithDifferentComparersAreUnequalInBothDirections()
    {
        var ignoreCase = new SetSettings.Fragment
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha" }
            ),
        };
        var ordinal = new SetSettings.Fragment
        {
            Values = Optional<ISet<string>>.Present(
                new HashSet<string>(StringComparer.Ordinal) { "alpha" }
            ),
        };

        SetAreEqual(ignoreCase, ordinal).ShouldBeFalse();
        SetAreEqual(ordinal, ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void FragmentDictionariesMatchingUnderOneComparerOnlyAreUnequalInBothDirections()
    {
        var ignoreCase = new DictionarySettings.Fragment
        {
            Values = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["FIRST"] = 1 }
            ),
        };
        var ordinal = new DictionarySettings.Fragment
        {
            Values = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.Ordinal) { ["first"] = 1 }
            ),
        };

        DictionaryAreEqual(ignoreCase, ordinal).ShouldBeFalse();
        DictionaryAreEqual(ordinal, ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlyDictionarySkipsNonGenericCollection()
    {
        var custom = new CustomReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1 }
        );

        ((object)custom is ICollection).ShouldBeFalse();
        ((object)custom is IDictionary).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlyDictionaryComparesOrderIndependently()
    {
        var left = new CustomReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["first"] = 1,
                ["second"] = 2,
            }
        );
        var reordered = new CustomReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["second"] = 2,
                ["first"] = 1,
            }
        );
        var changed = new CustomReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["first"] = 1,
                ["second"] = 3,
            }
        );

        SparseFragmentRuntime.AreDictionaryEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(reordered, left).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)left, (object)reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)reordered, (object)left).ShouldBeTrue();

        SparseFragmentRuntime.AreDictionaryEqual(left, changed).ShouldBeFalse();
        SparseFragmentRuntime.AreDictionaryEqual(changed, left).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)left, (object)changed).ShouldBeFalse();
        SparseFragmentRuntime.AreEqual((object)changed, (object)left).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlyDictionaryMatchesStandardDictionaryRegardlessOfOrder()
    {
        var custom = new CustomReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["first"] = 1,
                ["second"] = 2,
            }
        );
        var standard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["second"] = 2,
            ["first"] = 1,
        };

        SparseFragmentRuntime.AreDictionaryEqual(custom, standard).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(standard, custom).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)custom, (object)standard).ShouldBeTrue();
        SparseFragmentRuntime.AreEqual((object)standard, (object)custom).ShouldBeTrue();
    }

    private static bool DictionaryAreEqual(
        DictionarySettings.Fragment left,
        DictionarySettings.Fragment right
    ) =>
        DictionarySettings.Patch
            .Between(
                Optional<DictionarySettings.Fragment?>.Present(left),
                Optional<DictionarySettings.Fragment?>.Present(right)
            )
            .IsEmpty;

    private static bool SetAreEqual(SetSettings.Fragment left, SetSettings.Fragment right) =>
        SetSettings.Patch
            .Between(
                Optional<SetSettings.Fragment?>.Present(left),
                Optional<SetSettings.Fragment?>.Present(right)
            )
            .IsEmpty;
}
