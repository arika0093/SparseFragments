using System.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.CompilerServices;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Comparison;

/// <summary>
/// Fallback-path equality tests (issue #60): custom collection shapes that miss
/// the fast typed/common paths. The object path (<c>AreEqual</c>) is the source
/// of truth here: it honors any discovered non-generic comparer symmetrically,
/// while the typed <c>AreSetEqual{T}</c> tail fallback only honors comparers on
/// <c>ISet{T}</c>/<c>HashSet{T}</c>/<c>SortedSet{T}</c> shapes. That boundary is
/// intentional (issue #5 semantics stay untouched) and documented, not asserted.
/// </summary>
public sealed class EqualityFallbackTests
{
    private sealed class FallbackSet<T> : IReadOnlySet<T>
    {
        private readonly HashSet<T> _inner;

        public FallbackSet(IEnumerable<T> items, IEqualityComparer<T>? comparer = null) =>
            _inner = new HashSet<T>(items, comparer);

        public int Count => _inner.Count;

        public IEqualityComparer<T> Comparer => _inner.Comparer;

        public bool Contains(T item) => _inner.Contains(item);

        public bool IsProperSubsetOf(IEnumerable<T> other) => _inner.IsProperSubsetOf(other);

        public bool IsProperSupersetOf(IEnumerable<T> other) => _inner.IsProperSupersetOf(other);

        public bool IsSubsetOf(IEnumerable<T> other) => _inner.IsSubsetOf(other);

        public bool IsSupersetOf(IEnumerable<T> other) => _inner.IsSupersetOf(other);

        public bool Overlaps(IEnumerable<T> other) => _inner.Overlaps(other);

        public bool SetEquals(IEnumerable<T> other) => _inner.SetEquals(other);

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FallbackDictionary<TKey, TValue>(IDictionary<TKey, TValue> inner)
        : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _inner = new(
            inner,
            (inner as Dictionary<TKey, TValue>)?.Comparer ?? EqualityComparer<TKey>.Default
        );

        public TValue this[TKey key] => _inner[key];

        public IEnumerable<TKey> Keys => _inner.Keys;

        public IEnumerable<TValue> Values => _inner.Values;

        public int Count => _inner.Count;

        public IEqualityComparer<TKey> Comparer => _inner.Comparer;

        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class EnumerableOnly<T>(IEnumerable<T> inner) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in inner)
            {
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DuplicateSet<T>(params T[] items) : IReadOnlySet<T>
    {
        private readonly List<T> _items = new(items);

        public int Count => _items.Count;

        public bool Contains(T item) => _items.Contains(item);

        public bool IsProperSubsetOf(IEnumerable<T> other) => throw new NotSupportedException();

        public bool IsProperSupersetOf(IEnumerable<T> other) => throw new NotSupportedException();

        public bool IsSubsetOf(IEnumerable<T> other) => throw new NotSupportedException();

        public bool IsSupersetOf(IEnumerable<T> other) => throw new NotSupportedException();

        public bool Overlaps(IEnumerable<T> other) => throw new NotSupportedException();

        public bool SetEquals(IEnumerable<T> other) => throw new NotSupportedException();

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static bool Equal(object? left, object? right) =>
        SparseFragmentRuntime.AreEqual(left, right);

    private static FallbackSet<string> Strings(params string[] items) =>
        new(items, StringComparer.OrdinalIgnoreCase);

    private static FallbackDictionary<string, int> Numbers(params (string Key, int Value)[] entries) =>
        new(
            entries.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value,
                StringComparer.OrdinalIgnoreCase
            )
        );

    [Test]
    public void CustomReadOnlySet_IsOrderIndependentAndSymmetric()
    {
        var left = Strings("alpha", "beta");
        var reordered = Strings("BETA", "ALPHA");

        Equal(left, reordered).ShouldBeTrue();
        Equal(reordered, left).ShouldBeTrue();
    }

    [Test]
    public void CustomReadOnlySet_DetectsEarlyLateAndCountMismatches()
    {
        var left = Strings("a", "b", "c");
        var early = Strings("CHANGED", "b", "c");
        var late = Strings("a", "b", "CHANGED");
        var shorter = Strings("a", "b");

        Equal(left, early).ShouldBeFalse();
        Equal(early, left).ShouldBeFalse();
        Equal(left, late).ShouldBeFalse();
        Equal(late, left).ShouldBeFalse();
        Equal(left, shorter).ShouldBeFalse();
        Equal(shorter, left).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlySet_DifferentComparersAreUnequalInBothDirections()
    {
        var ignoreCase = new FallbackSet<string>(["alpha"], StringComparer.OrdinalIgnoreCase);
        var ordinal = new FallbackSet<string>(["alpha"], StringComparer.Ordinal);

        Equal(ignoreCase, ordinal).ShouldBeFalse();
        Equal(ordinal, ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlySet_MatchingUnderOneComparerOnlyIsUnequal()
    {
        var ignoreCase = new FallbackSet<string>(["ALPHA"], StringComparer.OrdinalIgnoreCase);
        var ordinal = new FallbackSet<string>(["alpha"], StringComparer.Ordinal);

        Equal(ignoreCase, ordinal).ShouldBeFalse();
        Equal(ordinal, ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlySet_WithNullMembersComparesByMultiset()
    {
        var left = new FallbackSet<string?>(["a", null], StringComparer.OrdinalIgnoreCase);
        var reordered = new FallbackSet<string?>([null, "A"], StringComparer.OrdinalIgnoreCase);
        var changed = new FallbackSet<string?>(["a", "b"], StringComparer.OrdinalIgnoreCase);

        Equal(left, reordered).ShouldBeTrue();
        Equal(reordered, left).ShouldBeTrue();
        Equal(left, changed).ShouldBeFalse();
        Equal(changed, left).ShouldBeFalse();
    }

    [Test]
    public void DuplicateBearingSet_ComparesByMultiset()
    {
        var left = new DuplicateSet<string>("a", "a", "b");
        var reordered = new DuplicateSet<string>("b", "a", "a");
        var redistributed = new DuplicateSet<string>("a", "b", "b");

        Equal(left, reordered).ShouldBeTrue();
        Equal(reordered, left).ShouldBeTrue();
        Equal(left, redistributed).ShouldBeFalse();
        Equal(redistributed, left).ShouldBeFalse();
    }

    [Test]
    public void StructuralSet_ComparesDeeplyAndSymmetrically()
    {
        var left = new HashSet<List<string>> { new(["k0", "v0"]), new(["k1", "v1"]) };
        var same = new HashSet<List<string>> { new(["k1", "v1"]), new(["k0", "v0"]) };
        var changed = new HashSet<List<string>> { new(["k0", "v0"]), new(["k1", "CHANGED"]) };
        var shorter = new HashSet<List<string>> { new(["k0", "v0"]) };

        Equal(left, same).ShouldBeTrue();
        Equal(same, left).ShouldBeTrue();
        Equal(left, changed).ShouldBeFalse();
        Equal(changed, left).ShouldBeFalse();
        Equal(left, shorter).ShouldBeFalse();
        Equal(shorter, left).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlyDictionary_IsOrderIndependentAndSymmetric()
    {
        var left = Numbers(("first", 1), ("second", 2));
        var reordered = Numbers(("SECOND", 2), ("FIRST", 1));

        Equal(left, reordered).ShouldBeTrue();
        Equal(reordered, left).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(left, reordered).ShouldBeTrue();
        SparseFragmentRuntime.AreDictionaryEqual(reordered, left).ShouldBeTrue();
    }

    [Test]
    public void CustomReadOnlyDictionary_DetectsEarlyLateAndCountMismatches()
    {
        var left = Numbers(("a", 1), ("b", 2), ("c", 3));
        var early = Numbers(("a", -1), ("b", 2), ("c", 3));
        var late = Numbers(("a", 1), ("b", 2), ("c", -1));
        var shorter = Numbers(("a", 1), ("b", 2));

        foreach (var altered in new[] { early, late, shorter })
        {
            Equal(left, altered).ShouldBeFalse();
            Equal(altered, left).ShouldBeFalse();
        }
    }

    [Test]
    public void CustomReadOnlyDictionary_DifferentComparersAreUnequalInBothDirections()
    {
        var ignoreCase = new FallbackDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1 }
        );
        var ordinal = new FallbackDictionary<string, int>(
            new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 }
        );

        Equal(ignoreCase, ordinal).ShouldBeFalse();
        Equal(ordinal, ignoreCase).ShouldBeFalse();
    }

    [Test]
    public void CustomReadOnlyDictionary_MatchesStandardDictionaryRegardlessOfOrder()
    {
        var custom = Numbers(("first", 1), ("second", 2));
        var standard = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SECOND"] = 2,
            ["FIRST"] = 1,
        };

        Equal(custom, standard).ShouldBeTrue();
        Equal(standard, custom).ShouldBeTrue();
    }

    [Test]
    public void EnumerableOnlySequence_IsOrderSensitiveAndSymmetric()
    {
        var left = new EnumerableOnly<string>(["a", "b"]);
        var same = new EnumerableOnly<string>(["a", "b"]);
        var reordered = new EnumerableOnly<string>(["b", "a"]);
        var early = new EnumerableOnly<string>(["CHANGED", "b"]);
        var late = new EnumerableOnly<string>(["a", "CHANGED"]);
        var shorter = new EnumerableOnly<string>(["a"]);

        Equal(left, same).ShouldBeTrue();
        Equal(same, left).ShouldBeTrue();
        foreach (var altered in new IEnumerable<string>[] { reordered, early, late, shorter })
        {
            Equal(left, altered).ShouldBeFalse();
            Equal(altered, left).ShouldBeFalse();
        }
    }

    [Test]
    public void FallbackShapes_AgreeWithThemselvesAcrossRepeatedComparisons()
    {
        // Reflection/shape caches must not change results between cold and warm lookups.
        var left = Strings("a", "b", "c");
        var same = Strings("C", "B", "A");
        var dictLeft = Numbers(("a", 1), ("b", 2));
        var dictSame = Numbers(("B", 2), ("A", 1));

        for (var iteration = 0; iteration < 3; iteration++)
        {
            Equal(left, same).ShouldBeTrue();
            Equal(dictLeft, dictSame).ShouldBeTrue();
        }
    }

    [Test]
    public void GeneratedCode_UsesTypedEqualityForKnownSetAndDictionaryMembers()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Model.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class FallbackTypedModel
                {
                    public IReadOnlySet<string> Tags { get; set; } = new HashSet<string>();
                    public IReadOnlyDictionary<string, int> Scores { get; set; } = new Dictionary<string, int>();
                }
                """,
        };
        var compilation = CreateCompilation(files);
        var driver = CSharpGeneratorDriver
            .Create(new SparseFragmentsGenerator())
            .RunGenerators(compilation);
        var sources = driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Select(static source => source.SourceText.ToString())
            .ToList();

        sources.ShouldNotBeEmpty();
        var combined = string.Join("\n", sources);
        combined.ShouldContain("AreSetEqual<string>");
        combined.ShouldContain("AreDictionaryEqual<string, int>");
        combined.ShouldNotContain("MakeGenericMethod");
    }

    private static CSharpCompilation CreateCompilation(Dictionary<string, string> files)
    {
        var trees = files
            .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
            .ToArray();
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "SparseFallbackTypedProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(
                NullableContextOptions.Enable
            )
        );
    }
}
