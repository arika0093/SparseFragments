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
        if (typeof(T).IsValueType && ScalarEquality<T>.UseTypedComparer)
        {
            return EqualityComparer<T>.Default.Equals(left!, right!);
        }

        return AreEqual((object?)left, (object?)right);
    }

    private static class ScalarEquality<T>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S2743",
            Justification = "Scalar classification is intentionally cached separately for each closed generic type."
        )]
        public static readonly bool UseTypedComparer = IsKnownScalar();

        private static bool IsKnownScalar()
        {
            // Custom structs retain object equality and enumerable structs retain sequence equality.
            var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }
    }

    /// <summary>Compares sequence-shaped values in their existing order.</summary>
    public static bool AreSequenceEqual<T>(IEnumerable<T>? left, IEnumerable<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        // Keep native sequence access concrete so the JIT can optimize indexing.
        // Exact List types preserve custom/derived non-generic comparison views.
        if (left is T[] leftArray && right is T[] rightArray)
        {
            if (leftArray.Length != rightArray.Length)
            {
                return false;
            }

            for (var index = 0; index < leftArray.Length; index++)
            {
                if (!AreEqual(leftArray[index], rightArray[index]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left.GetType() == typeof(List<T>) && right.GetType() == typeof(List<T>))
        {
            var leftNativeList = (List<T>)left;
            var rightNativeList = (List<T>)right;
            if (leftNativeList.Count != rightNativeList.Count)
            {
                return false;
            }

            for (var index = 0; index < leftNativeList.Count; index++)
            {
                if (!AreEqual(leftNativeList[index], rightNativeList[index]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is T[] mixedLeftArray && right.GetType() == typeof(List<T>))
        {
            var mixedRightList = (List<T>)right;
            if (mixedLeftArray.Length != mixedRightList.Count)
            {
                return false;
            }

            for (var index = 0; index < mixedLeftArray.Length; index++)
            {
                if (!AreEqual(mixedLeftArray[index], mixedRightList[index]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left.GetType() == typeof(List<T>) && right is T[] mixedRightArray)
        {
            var mixedLeftList = (List<T>)left;
            if (mixedLeftList.Count != mixedRightArray.Length)
            {
                return false;
            }

            for (var index = 0; index < mixedLeftList.Count; index++)
            {
                if (!AreEqual(mixedLeftList[index], mixedRightArray[index]))
                {
                    return false;
                }
            }

            return true;
        }

        // Derived/custom collections keep their existing non-generic comparison views.
        return AreEqual((object?)left, (object?)right);
    }

    /// <summary>Compares set-shaped values without depending on enumeration order.</summary>
    /// <remarks>Comparers are part of the value: differing comparers are unequal regardless of operand order.</remarks>
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
    /// <remarks>Key comparers are part of the value: differing comparers are unequal regardless of operand order.</remarks>
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
#pragma warning disable S3267 // Concrete dictionary enumeration avoids boxing its value-type enumerator.
            if (entries is Dictionary<TKey, TValue> dictionary)
            {
                foreach (var pair in dictionary)
                {
                    if (!Contains(pair))
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (var pair in entries)
            {
                if (!Contains(pair))
                {
                    return false;
                }
            }
#pragma warning restore S3267

            return true;
        }

        private bool Contains(KeyValuePair<TKey, TValue> pair) =>
            TryGetValue(pair.Key, out var value) && AreDictionaryValuesEqual(value, pair.Value);

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
