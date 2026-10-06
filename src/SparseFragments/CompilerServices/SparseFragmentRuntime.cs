using System.ComponentModel;

namespace SparseFragments.CompilerServices;

/// <summary>
/// Minimal generated-code runtime facade. Generated fragments and patches call
/// these helpers across the assembly boundary; the individual implementation
/// types behind them remain internal to the runtime assembly.
/// </summary>
/// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparseFragmentRuntime
{
    /// <summary>Compares two values using the default sparse semantics.</summary>
    public static bool AreEqual(object? left, object? right) =>
        SparseValueComparer.AreEqual(left, right);

    /// <summary>Compares two typed values using the default sparse semantics.</summary>
    public static bool AreEqual<T>(T? left, T? right) => SparseValueComparer.AreEqual(left, right);

    /// <summary>Compares set-shaped values without depending on enumeration order.</summary>
    public static bool AreSetEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right) =>
        SparseValueComparer.AreSetEqual(left, right);

    /// <summary>Compares dictionary-shaped values by key/value semantics.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? left,
        IEnumerable<KeyValuePair<TKey, TValue>>? right
    ) => SparseValueComparer.AreDictionaryEqual(left, right);

    /// <summary>Merges ordered contributions, retaining the first occurrence of each value.</summary>
    public static T[] MergeDistinctArray<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeDistinctArray(lower, higher);

    /// <summary>Merges ordered contributions, retaining the first occurrence of each value.</summary>
    public static List<T> MergeDistinctList<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeDistinctList(lower, higher);

    /// <summary>Merges set-shaped contributions while preserving a concrete HashSet comparer when available.</summary>
    public static HashSet<T> MergeSet<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeSet(lower, higher);

    /// <summary>Reapplies an append edit onto a newer collection.</summary>
    public static bool TryRebaseAppend(
        IReadOnlyList<object?> before,
        IReadOnlyList<object?> desired,
        IReadOnlyList<object?> current,
        Func<object?, object?, bool> equal,
        out IReadOnlyList<object?> rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseAppend(
            before,
            desired,
            current,
            equal,
            out rebased,
            out reason
        );

    /// <summary>Reapplies a set-union edit onto a newer collection.</summary>
    public static bool TryRebaseSetUnion(
        IReadOnlyList<object?> before,
        IReadOnlyList<object?> desired,
        IReadOnlyList<object?> current,
        Func<object?, object?, bool> equal,
        out IReadOnlyList<object?> rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSetUnion(
            before,
            desired,
            current,
            equal,
            out rebased,
            out reason
        );

    /// <summary>Reapplies a set-union edit onto a newer set without boxing or quadratic scans.</summary>
    /// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
    public static bool TryRebaseSetUnion<T>(
        IEnumerable<T> before,
        IEnumerable<T> desired,
        IEnumerable<T> current,
        out HashSet<T> rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSetUnion(before, desired, current, out rebased, out reason);

    /// <summary>Creates a reference-identity clone context for generated deep-clone helpers.</summary>
    public static Dictionary<object, object> CreateCloneContext() =>
        new(SparseReferenceEqualityComparer.Instance);

    /// <summary>Creates a reference-identity cycle scope for generated <c>Fragment.From</c> helpers.</summary>
    /// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
    public static HashSet<object> CreateFromCycleContext() =>
        new(SparseReferenceEqualityComparer.Instance);

    /// <summary>Creates a pair-identity cycle scope for generated <c>Fragment.Diff</c> helpers.</summary>
    /// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
    public static HashSet<KeyValuePair<object, object>> CreateDiffCycleContext() =>
        new(SparseDiffPairEqualityComparer.Instance);

    /// <summary>Ensures keyed-collection keys are unique, throwing on duplicates.</summary>
    public static void EnsureUniqueKeys<TKey>(
        IEnumerable<TKey> keys,
        IEqualityComparer<TKey>? comparer = null
    ) => SparseKeyedCollection.EnsureUniqueKeys(keys, comparer);

    /// <summary>Compares two final key sequences for equality.</summary>
    public static bool KeyOrderEquals<TKey>(
        IReadOnlyList<TKey> left,
        IReadOnlyList<TKey> right,
        IEqualityComparer<TKey>? comparer = null
    ) => SparseKeyedCollection.OrderEquals(left, right, comparer);

    /// <summary>
    /// Explains which low-to-high contribution supplied each effective sequence element for a built-in merge mode.
    /// </summary>
    /// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
    public static bool TryExplainCollectionProvenance<T>(
        MergeMode mode,
        IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions,
        Optional<IReadOnlyList<T>?> effective,
        IEqualityComparer<T>? comparer,
        out int[] origins,
        out string? reason
    ) =>
        SparseCollectionProvenance.TryExplain(
            mode,
            contributions,
            effective,
            comparer,
            out origins,
            out reason
        );

    /// <summary>Explains which low-to-high contribution supplied each effective set element with comparer-correct equality.</summary>
    /// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
    public static bool TryExplainSetProvenance<T>(
        IReadOnlyList<Optional<IEnumerable<T>?>> contributions,
        Optional<IEnumerable<T>?> effective,
        out int[] origins,
        out string? reason
    ) =>
        SparseCollectionProvenance.TryExplainSet(contributions, effective, out origins, out reason);
}
