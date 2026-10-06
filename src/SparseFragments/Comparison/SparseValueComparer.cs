using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SparseFragments;

/// <summary>Default semantic equality used by generated sparse fragments.</summary>
internal static class SparseValueComparer
{
    /// <summary>Compares two values, treating ordinary sequences element-wise.</summary>
    public static bool AreEqual(object? left, object? right) =>
        FragmentComparisonPrimitives.AreValuesEqual(left, right);

    /// <summary>Compares two typed values using the default sparse semantics.</summary>
    public static bool AreEqual<T>(T? left, T? right)
    {
        return AreEqual((object?)left, (object?)right);
    }

    /// <summary>Compares set-shaped values without depending on enumeration order.</summary>
    /// <remarks>
    /// The element comparer is part of the set value: sets whose comparers differ are
    /// unequal even when their elements would match under one side's comparer, so the
    /// result never depends on operand order. When a comparer cannot be discovered from
    /// a custom set, lookups must succeed in both directions.
    /// </remarks>
    public static bool AreSetEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is ISet<T> leftSet && right is ISet<T> rightSet)
        {
            var leftComparer = TryGetSetComparer(left);
            var rightComparer = TryGetSetComparer(right);
            if (leftComparer is not null && rightComparer is not null)
            {
                if (!leftComparer.Equals(rightComparer))
                {
                    return false;
                }

                return leftSet.SetEquals(right);
            }

            return leftSet.SetEquals(right) && rightSet.SetEquals(left);
        }

        if (left is ISet<T> leftOnly)
        {
            return leftOnly.SetEquals(right);
        }

        if (right is ISet<T> rightOnly)
        {
            return rightOnly.SetEquals(left);
        }

        return new HashSet<T>(left).SetEquals(right);
    }

    /// <summary>Compares dictionary-shaped values by key/value semantics.</summary>
    /// <remarks>
    /// The key comparer is part of the dictionary value: dictionaries whose key comparers
    /// differ are unequal even when their entries would match under one side's lookup, so
    /// the result never depends on operand order. Comparison never depends on enumeration
    /// order either, including for custom read-only dictionaries that do not implement
    /// non-generic <see cref="ICollection"/>. When a key comparer cannot be discovered,
    /// lookups must succeed in both directions.
    /// </remarks>
    public static bool AreDictionaryEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>>? left,
        IEnumerable<KeyValuePair<TKey, TValue>>? right
    )
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        var leftDictionary = AsDictionary(left);
        var rightDictionary = AsDictionary(right);
        if (leftDictionary is { } leftView && rightDictionary is { } rightView)
        {
            if (leftView.Count != rightView.Count)
            {
                return false;
            }

            var leftComparer = TryGetDictionaryComparer(left);
            var rightComparer = TryGetDictionaryComparer(right);
            if (leftComparer is not null && rightComparer is not null)
            {
                if (!leftComparer.Equals(rightComparer))
                {
                    return false;
                }

                return leftView.ContainsAll(right);
            }

            return leftView.ContainsAll(right) && rightView.ContainsAll(left);
        }

        return PairSequenceEquals(left.ToArray(), right.ToArray());
    }

    private static DictionaryView<TKey, TValue>? AsDictionary<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> value
    )
    {
        if (value is IReadOnlyDictionary<TKey, TValue> readOnly)
        {
            return new DictionaryView<TKey, TValue>(readOnly);
        }

        if (value is IDictionary<TKey, TValue> dictionary)
        {
            return new DictionaryView<TKey, TValue>(dictionary);
        }

        return null;
    }

    private readonly struct DictionaryView<TKey, TValue>
    {
        private readonly IReadOnlyDictionary<TKey, TValue>? _readOnly;

        private readonly IDictionary<TKey, TValue>? _dictionary;

        public DictionaryView(IReadOnlyDictionary<TKey, TValue> readOnly) => _readOnly = readOnly;

        public DictionaryView(IDictionary<TKey, TValue> dictionary) => _dictionary = dictionary;

        public int Count => _readOnly?.Count ?? _dictionary!.Count;

        public bool ContainsAll(IEnumerable<KeyValuePair<TKey, TValue>> entries)
        {
            foreach (var pair in entries)
            {
                if (
                    !TryGetValue(pair.Key, out var value)
                    || !AreDictionaryValuesEqual(value, pair.Value)
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreDictionaryValuesEqual(TValue left, TValue right)
        {
            // Avoid boxing common value types for every dictionary entry. Enumerable
            // structs still use the structural comparison path to preserve semantics.
            return
                typeof(TValue).IsValueType && !typeof(IEnumerable).IsAssignableFrom(typeof(TValue))
                ? EqualityComparer<TValue>.Default.Equals(left, right)
                : AreEqual((object?)left, (object?)right);
        }

        private bool TryGetValue(TKey key, out TValue value)
        {
            if (_readOnly is not null)
            {
                return _readOnly.TryGetValue(key, out value!);
            }

            return _dictionary!.TryGetValue(key, out value!);
        }
    }

    private static object? TryGetSetComparer<T>(IEnumerable<T> value)
    {
        switch (value)
        {
            case HashSet<T> hashSet:
                return hashSet.Comparer;
            case SortedSet<T> sortedSet:
                return sortedSet.Comparer;
            default:
                return GetDeclaredComparer(value);
        }
    }

    private static object? TryGetDictionaryComparer<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> value
    )
    {
        switch (value)
        {
            case Dictionary<TKey, TValue> dictionary:
                return dictionary.Comparer;
            case SortedDictionary<TKey, TValue> sortedDictionary:
                return sortedDictionary.Comparer;
            case SortedList<TKey, TValue> sortedList:
                return sortedList.Comparer;
            default:
                return GetDeclaredComparer(value);
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Only reads an optional public Comparer property; a trimmed property is treated as an undiscoverable comparer with a symmetric bidirectional fallback."
    )]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Only reads an optional public Comparer property via object.GetType(); a trimmed property is treated as an undiscoverable comparer with a symmetric bidirectional fallback."
    )]
    private static object? GetDeclaredComparer(object value)
    {
        PropertyInfo? property;
        try
        {
            property = value
                .GetType()
                .GetProperty("Comparer", BindingFlags.Public | BindingFlags.Instance);
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }

        if (property is null || !property.CanRead || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        try
        {
            return property.GetValue(value, null);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static bool PairSequenceEquals<TKey, TValue>(
        KeyValuePair<TKey, TValue>[] left,
        KeyValuePair<TKey, TValue>[] right
    )
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var used = new bool[right.Length];
        var keyComparer = EqualityComparer<TKey>.Default;
        foreach (var leftPair in left)
        {
            var matched = false;
            for (var index = 0; index < right.Length; index++)
            {
                var rightPair = right[index];
                if (used[index] || !keyComparer.Equals(leftPair.Key, rightPair.Key))
                {
                    continue;
                }

                if (!AreEqual(leftPair.Value, rightPair.Value))
                {
                    continue;
                }

                used[index] = true;
                matched = true;
                break;
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }
}
