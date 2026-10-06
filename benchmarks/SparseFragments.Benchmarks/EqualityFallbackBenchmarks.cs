using System.Collections;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

/// <summary>
/// Fallback-path equality benchmarks (issue #60): custom collection shapes that
/// miss the fast typed/common paths and exercise <c>ComparerAwareSetEquals</c>,
/// <c>SlowSetEquals</c>, <c>DictionariesEqualUnordered</c>, enumerable-only
/// ordered sequences, and reflection-backed comparer/count discovery (cold vs
/// warm). Existing <see cref="RuntimeCollectionBenchmarks"/> covers
/// <c>List{T}</c>/<c>HashSet{T}</c>/<c>Dictionary{TKey,TValue}</c>; these
/// fixtures use <c>IReadOnlySet{T}</c>-only / <c>IReadOnlyDictionary{TKey,TValue}</c>-only
/// wrappers (no non-generic <c>ICollection</c>/<c>IDictionary</c>, so count and
/// comparer metadata are discovered via cached reflection), structural set
/// elements (deep comparison, inherently quadratic), and
/// <c>IEnumerable{T}</c>-only sequences. Cold benchmarks clear the internal
/// caches on every invocation; warm benchmarks reuse them.
/// </summary>
[MemoryDiagnoser]
public class EqualityFallbackBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    private FallbackReadOnlySet<string> _setBase = null!;
    private FallbackReadOnlySet<string> _setSame = null!;
    private FallbackReadOnlySet<string> _setEarlyMismatch = null!;
    private FallbackReadOnlySet<string> _setLateMismatch = null!;
    private FallbackReadOnlySet<string> _setCountMismatch = null!;

    private HashSet<List<string>> _structuralBase = null!;
    private HashSet<List<string>> _structuralSame = null!;
    private HashSet<List<string>> _structuralLateMismatch = null!;
    private HashSet<List<string>> _structuralCountMismatch = null!;

    private FallbackReadOnlyDictionary<string, int> _dictBase = null!;
    private FallbackReadOnlyDictionary<string, int> _dictSame = null!;
    private FallbackReadOnlyDictionary<string, int> _dictEarlyMismatch = null!;
    private FallbackReadOnlyDictionary<string, int> _dictLateMismatch = null!;
    private FallbackReadOnlyDictionary<string, int> _dictCountMismatch = null!;

    private EnumerableOnly<string> _seqBase = null!;
    private EnumerableOnly<string> _seqSame = null!;
    private EnumerableOnly<string> _seqEarlyMismatch = null!;
    private EnumerableOnly<string> _seqLateMismatch = null!;
    private EnumerableOnly<string> _seqCountMismatch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var items = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            items.Add("item-" + index);
        }

        _setBase = new FallbackReadOnlySet<string>(items, StringComparer.OrdinalIgnoreCase);
        _setSame = new FallbackReadOnlySet<string>(items, StringComparer.OrdinalIgnoreCase);
        _setEarlyMismatch = new FallbackReadOnlySet<string>(
            ReplaceAt(items, 0, "item-changed"),
            StringComparer.OrdinalIgnoreCase
        );
        _setLateMismatch = new FallbackReadOnlySet<string>(
            Size == 0 ? new[] { "item-changed" } : ReplaceAt(items, Size - 1, "item-changed"),
            StringComparer.OrdinalIgnoreCase
        );
        _setCountMismatch = new FallbackReadOnlySet<string>(
            items.Take(Math.Max(0, Size - 1)),
            StringComparer.OrdinalIgnoreCase
        );

        _structuralBase = BuildStructural(Size, static index => "value-" + index);
        _structuralSame = BuildStructural(Size, static index => "value-" + index);
        _structuralLateMismatch = BuildStructural(
            Size,
            index => index == Size - 1 ? "value-changed" : "value-" + index
        );
        _structuralCountMismatch = BuildStructural(
            Math.Max(0, Size - 1),
            static index => "value-" + index
        );

        _dictBase = new FallbackReadOnlyDictionary<string, int>(
            BuildDictionary(Size, static index => index)
        );
        _dictSame = new FallbackReadOnlyDictionary<string, int>(
            BuildDictionary(Size, static index => index)
        );
        _dictEarlyMismatch = new FallbackReadOnlyDictionary<string, int>(
            BuildDictionary(Size, static index => index == 0 ? -1 : index)
        );
        _dictLateMismatch = new FallbackReadOnlyDictionary<string, int>(
            BuildDictionary(Size, index => index == Size - 1 ? -1 : index)
        );
        _dictCountMismatch = new FallbackReadOnlyDictionary<string, int>(
            BuildDictionary(Math.Max(0, Size - 1), static index => index)
        );

        _seqBase = new EnumerableOnly<string>(items);
        _seqSame = new EnumerableOnly<string>(new List<string>(items));
        _seqEarlyMismatch = new EnumerableOnly<string>(ReplaceAt(items, 0, "item-changed"));
        _seqLateMismatch = new EnumerableOnly<string>(
            Size == 0 ? new[] { "item-changed" } : ReplaceAt(items, Size - 1, "item-changed")
        );
        _seqCountMismatch = new EnumerableOnly<string>(items.Take(Math.Max(0, Size - 1)));

        // Pre-warm the reflection/shape caches for the warm paths. Cold paths
        // clear these caches on every invocation (see ColdSetEqual).
        SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setSame);
        SparseFragmentRuntime.AreEqual((object)_structuralBase, (object)_structuralSame);
        SparseFragmentRuntime.AreEqual((object)_dictBase, (object)_dictSame);
        SparseFragmentRuntime.AreEqual((object)_seqBase, (object)_seqSame);
    }

    [Benchmark(Description = "Fallback set (IReadOnlySet-only, comparer): equal")]
    public bool FallbackSet_Equal() =>
        SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setSame);

    [Benchmark(Description = "Fallback set (IReadOnlySet-only, comparer): early mismatch")]
    public bool FallbackSet_EarlyMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setEarlyMismatch);

    [Benchmark(Description = "Fallback set (IReadOnlySet-only, comparer): late mismatch")]
    public bool FallbackSet_LateMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setLateMismatch);

    [Benchmark(Description = "Fallback set (IReadOnlySet-only, comparer): count mismatch")]
    public bool FallbackSet_CountMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setCountMismatch);

    [Benchmark(Description = "Structural set (deep elements): equal")]
    public bool StructuralSet_Equal() =>
        SparseFragmentRuntime.AreEqual((object)_structuralBase, (object)_structuralSame);

    [Benchmark(Description = "Structural set (deep elements): late mismatch")]
    public bool StructuralSet_LateMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_structuralBase, (object)_structuralLateMismatch);

    [Benchmark(Description = "Structural set (deep elements): count mismatch")]
    public bool StructuralSet_CountMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_structuralBase, (object)_structuralCountMismatch);

    [Benchmark(Description = "Fallback dictionary (read-only-only): equal")]
    public bool FallbackDictionary_Equal() =>
        SparseFragmentRuntime.AreEqual((object)_dictBase, (object)_dictSame);

    [Benchmark(Description = "Fallback dictionary (read-only-only): early mismatch")]
    public bool FallbackDictionary_EarlyMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_dictBase, (object)_dictEarlyMismatch);

    [Benchmark(Description = "Fallback dictionary (read-only-only): late mismatch")]
    public bool FallbackDictionary_LateMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_dictBase, (object)_dictLateMismatch);

    [Benchmark(Description = "Fallback dictionary (read-only-only): count mismatch")]
    public bool FallbackDictionary_CountMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_dictBase, (object)_dictCountMismatch);

    [Benchmark(Description = "Enumerable-only sequence: equal")]
    public bool EnumerableOnly_Equal() =>
        SparseFragmentRuntime.AreEqual((object)_seqBase, (object)_seqSame);

    [Benchmark(Description = "Enumerable-only sequence: early mismatch")]
    public bool EnumerableOnly_EarlyMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_seqBase, (object)_seqEarlyMismatch);

    [Benchmark(Description = "Enumerable-only sequence: late mismatch")]
    public bool EnumerableOnly_LateMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_seqBase, (object)_seqLateMismatch);

    [Benchmark(Description = "Enumerable-only sequence: count mismatch")]
    public bool EnumerableOnly_CountMismatch() =>
        SparseFragmentRuntime.AreEqual((object)_seqBase, (object)_seqCountMismatch);

    [Benchmark(Description = "Fallback set: warm cached reflection/shape lookup")]
    public bool FallbackSet_Warm() =>
        SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setSame);

    [Benchmark(Description = "Fallback set: cold reflection/shape discovery every call")]
    public bool FallbackSet_Cold()
    {
        EqualityFallbackCaches.Clear();
        return SparseFragmentRuntime.AreEqual((object)_setBase, (object)_setSame);
    }

    [Benchmark(Description = "Fallback dictionary: cold reflection/shape discovery every call")]
    public bool FallbackDictionary_Cold()
    {
        EqualityFallbackCaches.Clear();
        return SparseFragmentRuntime.AreEqual((object)_dictBase, (object)_dictSame);
    }

    private static List<string> ReplaceAt(List<string> items, int index, string value)
    {
        var copy = new List<string>(items);
        if (copy.Count == 0)
        {
            copy.Add(value);
            return copy;
        }

        copy[index] = value;
        return copy;
    }

    private static HashSet<List<string>> BuildStructural(int size, Func<int, string> value)
    {
        var set = new HashSet<List<string>>();
        for (var index = 0; index < size; index++)
        {
            set.Add(new List<string> { "key-" + index, value(index) });
        }

        return set;
    }

    private static Dictionary<string, int> BuildDictionary(int size, Func<int, int> value)
    {
        var dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < size; index++)
        {
            dictionary["key-" + index] = value(index);
        }

        return dictionary;
    }
}

/// <summary>
/// Set-shaped fixture exposing only <c>IReadOnlySet{T}</c>: no
/// <c>ISet{T}</c>, <c>HashSet{T}</c>, or non-generic <c>ICollection</c>, so
/// count/comparer metadata is discovered through the cached reflection path.
/// </summary>
public sealed class FallbackReadOnlySet<T> : IReadOnlySet<T>
{
    private readonly HashSet<T> _inner;

    public FallbackReadOnlySet(IEnumerable<T> items, IEqualityComparer<T>? comparer = null) =>
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

/// <summary>
/// Dictionary-shaped fixture exposing only
/// <c>IReadOnlyDictionary{TKey,TValue}</c>: no generic/non-generic
/// <c>IDictionary</c> and no non-generic <c>ICollection</c>, so the runtime
/// compares entries order-independently via the unordered fallback with cached
/// reflection for count, comparer, and entry key/value.
/// </summary>
public sealed class FallbackReadOnlyDictionary<TKey, TValue>(
    IDictionary<TKey, TValue> inner
) : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _inner =
        new(inner, (inner as Dictionary<TKey, TValue>)?.Comparer ?? EqualityComparer<TKey>.Default);

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

/// <summary>
/// Sequence fixture exposing only <c>IEnumerable{T}</c>: no <c>IList</c> or
/// <c>ICollection</c>, so ordered comparison streams both enumerators without
/// indexed or count fast paths.
/// </summary>
public sealed class EnumerableOnly<T>(IEnumerable<T> inner) : IEnumerable<T>
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

/// <summary>
/// Benchmark-only helper that clears the cached shape/comparer/count/entry
/// metadata inside <c>FragmentComparisonPrimitives</c> so cold-discovery cost
/// is measured on every invocation. Reflection is confined to benchmarks;
/// the runtime itself uses no runtime code generation.
/// </summary>
internal static class EqualityFallbackCaches
{
    private static readonly FieldInfo[] CacheFields;

    static EqualityFallbackCaches()
    {
        var runtime = typeof(SparseFragmentRuntime).Assembly.GetType(
            "SparseFragments.FragmentComparisonPrimitives",
            throwOnError: true
        )!;
        CacheFields = runtime
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(static field =>
                field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition()
                    == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>)
            )
            .ToArray();
    }

    public static void Clear()
    {
        foreach (var field in CacheFields)
        {
            ((IDictionary)field.GetValue(null)!).Clear();
        }
    }
}
