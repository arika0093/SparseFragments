using System.ComponentModel;

namespace SparseFragments.CompilerServices;

/// <summary>Minimal generated-code runtime facade.</summary>
/// <remarks>Generated-code plumbing: referenced by emitted code, not hand-written callers.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SparseFragmentRuntime
{
    /// <summary>Compares two values using the default sparse semantics.</summary>
    public static bool AreEqual(object? left, object? right) =>
        SparseValueComparer.AreEqual(left, right);

    /// <summary>Compares two typed values using the default sparse semantics.</summary>
    public static bool AreEqual<T>(T? left, T? right) => SparseValueComparer.AreEqual(left, right);

    /// <summary>Compares sequence-shaped values in their existing order.</summary>
    public static bool AreSequenceEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right) =>
        SparseValueComparer.AreSequenceEqual(left, right);

    /// <summary>Compares a sequence using a generated semantic item comparer.</summary>
    public static bool AreSequenceEqual<T>(
        IEnumerable<T>? left,
        IEnumerable<T>? right,
        Func<T, T, bool> itemComparer
    ) => SparseValueComparer.AreSequenceEqual(left, right, itemComparer);

    /// <summary>Compares arrays in order without runtime shape probing.</summary>
    /// <remarks>
    /// Static specialization for declared <c>T[]</c> members (issue #187): binds at compile
    /// time and skips shape classification plus comparer/count reflection. Falls back to the
    /// <c>IEnumerable{T}</c> overload for interface-declared or unknown shapes.
    /// </remarks>
    public static bool AreSequenceEqual<T>(T[]? left, T[]? right) =>
        SparseConcreteComparisons.AreSequenceEqual(left, right);

    /// <summary>Compares lists in order without runtime shape probing.</summary>
    /// <remarks>
    /// Derived lists keep their non-generic <c>IList</c> comparison view via the object
    /// fallback; exact <c>List{T}</c> instances compare by indexer.
    /// </remarks>
    public static bool AreSequenceEqual<T>(List<T>? left, List<T>? right) =>
        SparseConcreteComparisons.AreSequenceEqual(left, right);

    /// <summary>Compares arrays with a generated semantic item comparer.</summary>
    public static bool AreSequenceEqual<T>(T[]? left, T[]? right, Func<T, T, bool> itemComparer) =>
        SparseConcreteComparisons.AreSequenceEqual(left, right, itemComparer);

    /// <summary>Compares lists with a generated semantic item comparer.</summary>
    public static bool AreSequenceEqual<T>(
        List<T>? left,
        List<T>? right,
        Func<T, T, bool> itemComparer
    ) => SparseConcreteComparisons.AreSequenceEqual(left, right, itemComparer);

    /// <summary>Compares set-shaped values without depending on enumeration order.</summary>
    public static bool AreSetEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right) =>
        SparseValueComparer.AreSetEqual(left, right);

    /// <summary>Compares hash sets without depending on enumeration order.</summary>
    /// <remarks>
    /// The set comparer is read statically; differing comparers veto equality in both
    /// directions. Interface-declared or custom sets keep the <c>IEnumerable{T}</c> fallback.
    /// </remarks>
    public static bool AreSetEqual<T>(HashSet<T>? left, HashSet<T>? right) =>
        SparseConcreteComparisons.AreSetEqual(left, right);

    /// <summary>Compares sorted sets without depending on enumeration order.</summary>
    /// <remarks>
    /// The set comparer is read statically; differing comparers veto equality in both
    /// directions. Interface-declared or custom sets keep the <c>IEnumerable{T}</c> fallback.
    /// </remarks>
    public static bool AreSetEqual<T>(SortedSet<T>? left, SortedSet<T>? right) =>
        SparseConcreteComparisons.AreSetEqual(left, right);

    /// <summary>Compares dictionary-shaped values by key/value semantics.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? left,
        IEnumerable<KeyValuePair<TKey, TValue>>? right
    ) => SparseValueComparer.AreDictionaryEqual(left, right);

    /// <summary>Compares a dictionary using a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? left,
        IEnumerable<KeyValuePair<TKey, TValue>>? right,
        Func<TValue, TValue, bool> valueComparer
    ) => SparseValueComparer.AreDictionaryEqual(left, right, valueComparer);

    /// <summary>Compares dictionaries by key/value semantics with static comparer access.</summary>
    /// <remarks>
    /// The key comparer is read statically; differing comparers veto equality in both
    /// directions. Interface-declared or custom dictionaries keep the
    /// <c>IEnumerable{KeyValuePair{TKey,TValue}}</c> fallback.
    /// </remarks>
    public static bool AreDictionaryEqual<TKey, TValue>(
        Dictionary<TKey, TValue>? left,
        Dictionary<TKey, TValue>? right
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right);

    /// <summary>Compares sorted dictionaries by key/value semantics with static comparer access.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedDictionary<TKey, TValue>? left,
        SortedDictionary<TKey, TValue>? right
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right);

    /// <summary>Compares sorted lists by key/value semantics with static comparer access.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedList<TKey, TValue>? left,
        SortedList<TKey, TValue>? right
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right);

    /// <summary>Compares a dictionary using a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        Dictionary<TKey, TValue>? left,
        Dictionary<TKey, TValue>? right,
        Func<TValue, TValue, bool> valueComparer
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right, valueComparer);

    /// <summary>Compares a sorted dictionary using a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedDictionary<TKey, TValue>? left,
        SortedDictionary<TKey, TValue>? right,
        Func<TValue, TValue, bool> valueComparer
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right, valueComparer);

    /// <summary>Compares a sorted list using a generated semantic value comparer.</summary>
    public static bool AreDictionaryEqual<TKey, TValue>(
        SortedList<TKey, TValue>? left,
        SortedList<TKey, TValue>? right,
        Func<TValue, TValue, bool> valueComparer
    ) => SparseConcreteComparisons.AreDictionaryEqual(left, right, valueComparer);

    /// <summary>Appends ordered contributions, preserving duplicates.</summary>
    public static List<T> MergeAppendList<T>(IEnumerable<T> lower, IEnumerable<T> higher) =>
        SparseCollectionMerger.MergeAppendList(lower, higher);

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
    public static bool TryRebaseSetUnion<T>(
        IEnumerable<T> before,
        IEnumerable<T> desired,
        IEnumerable<T> current,
        out HashSet<T> rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSetUnion(before, desired, current, out rebased, out reason);

    /// <summary>Reapplies an append edit onto a newer sequence without boxing or quadratic scans.</summary>
    public static bool TryRebaseSequenceAppend<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out List<T> rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSequenceAppend(
            before,
            desired,
            current,
            comparer,
            out rebased,
            out reason
        );

    /// <summary>Reapplies an append edit directly into an array.</summary>
    public static bool TryRebaseSequenceAppendArray<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out T[] rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSequenceAppendArray(
            before,
            desired,
            current,
            comparer,
            out rebased,
            out reason
        );

    /// <summary>Reapplies a sequence set-union edit onto a newer sequence without boxing or quadratic scans.</summary>
    public static bool TryRebaseSequenceSetUnion<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out List<T> rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSequenceSetUnion(
            before,
            desired,
            current,
            comparer,
            out rebased,
            out reason
        );

    /// <summary>Reapplies a sequence set-union edit directly into an array.</summary>
    public static bool TryRebaseSequenceSetUnionArray<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out T[] rebased,
        out string? reason
    ) =>
        SparseCollectionRebase.TryRebaseSequenceSetUnionArray(
            before,
            desired,
            current,
            comparer,
            out rebased,
            out reason
        );

    /// <summary>Creates a reference-identity clone context for generated deep-clone helpers.</summary>
    public static Dictionary<object, object> CreateCloneContext() =>
        new(SparseReferenceEqualityComparer.Instance);

    /// <summary>Creates a reference-identity cycle scope for generated <c>Fragment.From</c> helpers.</summary>
    public static HashSet<object> CreateFromCycleContext() =>
        new(SparseReferenceEqualityComparer.Instance);

    /// <summary>Creates a pair-identity cycle scope for generated <c>Fragment.Diff</c> helpers.</summary>
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
    public static bool TryExplainSetProvenance<T>(
        IReadOnlyList<Optional<IEnumerable<T>?>> contributions,
        Optional<IEnumerable<T>?> effective,
        out int[] origins,
        out string? reason
    ) =>
        SparseCollectionProvenance.TryExplainSet(contributions, effective, out origins, out reason);
}
