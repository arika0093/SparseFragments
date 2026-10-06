using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SparseFragments;

/// <summary>Allocation-friendly structural comparison shared by fragment equality.</summary>
/// <remarks>
/// Collection and fragment semantics live here so equality stays consistent.
/// The runtime package ships only a netstandard2.0 asset, so this path never
/// attempts runtime generic code generation (no MakeGenericMethod/CreateDelegate):
/// all collection comparisons below are statically reachable and trim/NativeAOT
/// clean. Shape classification only compares generic type definitions and reads
/// element types for assignability tests. Where a collection exposes its
/// comparer, the non-generic comparer interfaces are used so custom comparers
/// keep working without generic closures.
///
/// Complexity boundaries (issue #60): set/dictionary fallbacks index through
/// the discovered non-generic comparer in O(n) when one is available; without
/// a usable comparer, or when elements themselves require structural equality
/// with no compatible hash semantics, matching stays quadratic by design.
/// Count mismatches short-circuit in O(1) wherever a count is discoverable.
/// </remarks>
internal static class FragmentComparisonPrimitives
{
    private enum CollectionKind
    {
        Sequence,
        Set,
        Dictionary,
    }

    private sealed class CollectionShape
    {
        public CollectionKind Kind;

        public Type? ElementType;
    }

    private const string ReadOnlySetDefinitionName = "System.Collections.Generic.IReadOnlySet`1";

    private static readonly ConcurrentDictionary<Type, CollectionShape> ShapeCache = new();

    internal static bool AreValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is string || right is string)
        {
            return Equals(left, right);
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            return AreCollectionsEqual(left, right, leftItems, rightItems);
        }

        return Equals(left, right);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Shape inputs originate from object.GetType(), whose exact runtime type is known to the trimmer. Classification only compares generic type definitions and reads element types for assignability tests; no members are invoked on discovered types."
    )]
    private static bool AreCollectionsEqual(
        object left,
        object right,
        IEnumerable leftItems,
        IEnumerable rightItems
    )
    {
        var leftShape = GetShape(left.GetType());
        var rightShape = GetShape(right.GetType());
        var leftKind = leftShape?.Kind;
        var rightKind = rightShape?.Kind;

        if (leftKind == CollectionKind.Dictionary || rightKind == CollectionKind.Dictionary)
        {
            if (leftKind != CollectionKind.Dictionary || rightKind != CollectionKind.Dictionary)
            {
                return false;
            }

            return AreDictionariesEqual(left, right);
        }

        if (leftKind == CollectionKind.Set || rightKind == CollectionKind.Set)
        {
            if (leftKind != CollectionKind.Set || rightKind != CollectionKind.Set)
            {
                return false;
            }

            return AreSetsEqual(left, right, leftShape, rightShape);
        }

        return SequencesEqualOrdered(leftItems, rightItems);
    }

    private static bool SequencesEqualOrdered(IEnumerable left, IEnumerable right)
    {
        if (left is IList leftList && right is IList rightList)
        {
            if (leftList.Count != rightList.Count)
            {
                return false;
            }

            for (var index = 0; index < leftList.Count; index++)
            {
                if (!AreValuesEqual(leftList[index], rightList[index]))
                {
                    return false;
                }
            }

            return true;
        }

        if (
            left is ICollection leftCollection
            && right is ICollection rightCollection
            && leftCollection.Count != rightCollection.Count
        )
        {
            return false;
        }

        var leftEnumerator = left.GetEnumerator();
        IEnumerator? rightEnumerator = null;
        try
        {
            rightEnumerator = right.GetEnumerator();
            while (true)
            {
                var leftMoved = leftEnumerator.MoveNext();
                var rightMoved = rightEnumerator.MoveNext();
                if (leftMoved != rightMoved)
                {
                    return false;
                }

                if (!leftMoved)
                {
                    return true;
                }

                if (!AreValuesEqual(leftEnumerator.Current, rightEnumerator.Current))
                {
                    return false;
                }
            }
        }
        finally
        {
            (rightEnumerator as IDisposable)?.Dispose();
            (leftEnumerator as IDisposable)?.Dispose();
        }
    }

    private static bool AreDictionariesEqual(object left, object right)
    {
        var leftCount = TryDictionaryCount(left);
        var rightCount = TryDictionaryCount(right);
        if (!leftCount.HasValue || !rightCount.HasValue)
        {
            // Dictionaries never fall back to enumeration-order equality; exotic
            // shapes without a readable count still compare order-independently.
            return DictionariesEqualUnordered(
                (IEnumerable)left,
                (IEnumerable)right,
                TryCollectionComparer(left) ?? TryCollectionComparer(right)
            );
        }

        if (leftCount.Value != rightCount.Value)
        {
            return false;
        }

        // The key comparer is part of the dictionary value: differing comparers are
        // unequal regardless of operand order, keeping equality symmetric. When a
        // comparer cannot be discovered, both lookup directions must agree.
        var leftComparer = TryCollectionComparer(left);
        var rightComparer = TryCollectionComparer(right);
        if (
            leftComparer is not null
            && rightComparer is not null
            && !Equals(leftComparer, rightComparer)
        )
        {
            return false;
        }

        if (left is IDictionary leftDictionary && right is IDictionary rightDictionary)
        {
            // Non-generic IDictionary lookups honor each dictionary's own key
            // comparer, so no generic closure is needed for the common shapes
            // (including Dictionary<string, int> and its interface variants).
            return DictionaryContainsAll(leftDictionary, rightDictionary)
                && DictionaryContainsAll(rightDictionary, leftDictionary);
        }

        return DictionariesEqualUnordered(
            (IEnumerable)left,
            (IEnumerable)right,
            leftComparer ?? rightComparer
        );
    }

    private static bool DictionaryContainsAll(IDictionary pairs, IDictionary lookup)
    {
        foreach (DictionaryEntry entry in pairs)
        {
            if (!lookup.Contains(entry.Key) || !AreValuesEqual(entry.Value, lookup[entry.Key]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSetsEqual(
        object left,
        object right,
        CollectionShape? leftShape,
        CollectionShape? rightShape
    )
    {
        var elementType = leftShape?.ElementType ?? rightShape?.ElementType;
        if (elementType is not null && MayNeedDeepComparison(elementType))
        {
            return SlowSetEquals(left, right);
        }

        // The element comparer is part of the set value: differing comparers are
        // unequal regardless of operand order, keeping equality symmetric. When a
        // comparer cannot be discovered, both directions must agree.
        var leftComparer = TryCollectionComparer(left);
        var rightComparer = TryCollectionComparer(right);
        if (
            leftComparer is not null
            && rightComparer is not null
            && !Equals(leftComparer, rightComparer)
        )
        {
            return false;
        }

        // No generic closure: match members with the discovered non-generic
        // comparer when one is available (e.g. HashSet<string> with
        // OrdinalIgnoreCase), otherwise fall back to structural equality.
        // This keeps HashSet<int> and its ISet<int>/IReadOnlySet<int> variants
        // correct under NativeAOT without MakeGenericMethod/CreateDelegate.
        return ComparerAwareSetEquals(left, right, leftComparer ?? rightComparer);
    }

    private static bool ComparerAwareSetEquals(object left, object right, object? comparer)
    {
        var leftCount = TryDictionaryCount(left);
        var rightCount = TryDictionaryCount(right);
        if (leftCount.HasValue && rightCount.HasValue && leftCount.Value != rightCount.Value)
        {
            return false;
        }

        var keyEquality = AsNonGenericEquality(comparer);
        if (keyEquality is not null)
        {
            // Bounded fast path (issue #60): both Equals and GetHashCode flow
            // through the same discovered comparer, so hash lookup preserves its
            // exact semantics while avoiding the quadratic nested scan. Counts
            // track multiplicities so exotic duplicate-bearing enumerables still
            // compare as multisets. No generic closure is created: the adapter
            // only forwards to the non-generic comparer interface. A null result
            // means null members defeat exact hashing and falls back to the scan.
            var hashed = HashSetEquals(
                (IEnumerable)left,
                (IEnumerable)right,
                leftCount,
                keyEquality
            );
            if (hashed.HasValue)
            {
                return hashed.Value;
            }
        }

        // No usable comparer: elements compare structurally, which admits no
        // compatible hash semantics, so this path stays quadratic by design.
        // Matches are removed in place (order-preserving) so order-aligned
        // inputs keep matching at the head instead of degrading the search.
        var remaining = new List<object?>();
        foreach (var item in (IEnumerable)left)
        {
            remaining.Add(item);
        }

        foreach (var item in (IEnumerable)right)
        {
            var match = -1;
            for (var index = 0; index < remaining.Count; index++)
            {
                if (AreValuesEqual(remaining[index], item))
                {
                    match = index;
                    break;
                }
            }

            if (match < 0)
            {
                return false;
            }

            remaining.RemoveAt(match);
        }

        return remaining.Count == 0;
    }

    private static IEqualityComparer? AsNonGenericEquality(object? comparer) =>
        comparer as IEqualityComparer;

    /// <summary>
    /// Adapts a discovered non-generic element/key comparer for generic hash
    /// containers without runtime generic code generation.
    /// </summary>
    /// <remarks>
    /// Explicit interface implementation keeps this distinct from
    /// <see cref="object.Equals(object?, object?)"/> and stays trim/AOT clean.
    /// Null keys never reach the inner comparer; callers track them separately.
    /// </remarks>
    private sealed class NonGenericEqualityAdapter(IEqualityComparer comparer)
        : IEqualityComparer<object>
    {
        bool IEqualityComparer<object>.Equals(object x, object y) => comparer.Equals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) => comparer.GetHashCode(obj);
    }

    /// <summary>
    /// Hash-joins set members through the discovered element comparer.
    /// Returns <c>null</c> when null members defeat exact hashing so the caller
    /// falls back to the comparer scan.
    /// </summary>
    private static bool? HashSetEquals(
        IEnumerable left,
        IEnumerable right,
        int? leftCapacity,
        IEqualityComparer keyEquality
    )
    {
        var adapter = new NonGenericEqualityAdapter(keyEquality);
        Dictionary<object, int>? leftCounts = null;
        var nullCount = 0;
        var leftTotal = 0;
        var anyLeft = false;
        foreach (var item in left)
        {
            anyLeft = true;
            if (item is null)
            {
                nullCount++;
            }
            else
            {
                leftCounts ??= new Dictionary<object, int>(leftCapacity ?? 0, adapter);
                leftCounts.TryGetValue(item, out var seen);
                leftCounts[item] = seen + 1;
            }

            leftTotal++;
        }

        if (!anyLeft)
        {
            return !Any(right);
        }

        if (nullCount != 0 && !keyEquality.Equals(null, null))
        {
            return null;
        }

        var nullRemaining = nullCount;
        var matched = 0;
        foreach (var item in right)
        {
            if (item is null)
            {
                if (nullRemaining == 0)
                {
                    return false;
                }

                nullRemaining--;
            }
            else if (leftCounts is null || !leftCounts.TryGetValue(item, out var seen) || seen == 0)
            {
                return false;
            }
            else
            {
                leftCounts[item] = seen - 1;
            }

            matched++;
        }

        return matched == leftTotal && nullRemaining == 0;
    }

    private static bool Any(IEnumerable items)
    {
        var enumerator = items.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

    private static bool SlowSetEquals(object left, object right)
    {
        // Structural elements admit no compatible hash semantics, so matching
        // stays quadratic by design; the count pre-check keeps count mismatches
        // O(1). Matches are removed in place (order-preserving) so
        // order-aligned inputs keep matching at the head.
        var leftCount = TryDictionaryCount(left);
        var rightCount = TryDictionaryCount(right);
        if (leftCount.HasValue && rightCount.HasValue && leftCount.Value != rightCount.Value)
        {
            return false;
        }

        var remaining = new List<object?>();
        foreach (var item in (IEnumerable)left)
        {
            remaining.Add(item);
        }

        foreach (var item in (IEnumerable)right)
        {
            var match = -1;
            for (var index = 0; index < remaining.Count; index++)
            {
                if (AreValuesEqual(remaining[index], item))
                {
                    match = index;
                    break;
                }
            }

            if (match < 0)
            {
                return false;
            }

            remaining.RemoveAt(match);
        }

        return remaining.Count == 0;
    }

    private static bool MayNeedDeepComparison(Type elementType)
    {
        if (elementType == typeof(object))
        {
            return true;
        }

        if (elementType == typeof(string))
        {
            return false;
        }

        return typeof(IEnumerable).IsAssignableFrom(elementType);
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo> ComparerProperties = new();

    private static readonly ConcurrentDictionary<Type, PropertyInfo> CountProperties = new();

    private static int? TryDictionaryCount(object value)
    {
        if (value is ICollection collection)
        {
            return collection.Count;
        }

        // Custom read-only collections may not implement non-generic
        // ICollection; read Count structurally so they still compare
        // order-independently instead of falling back to enumeration order.
        return GetIntProperty(GetCachedProperty(CountProperties, value.GetType(), "Count"), value);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Only reads optional public Comparer/Count/Key/Value properties; a trimmed property is treated as undiscoverable with a symmetric structural fallback."
    )]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Only reads optional public Comparer/Count properties; a trimmed property is treated as undiscoverable with a symmetric order-independent fallback."
    )]
    private static PropertyInfo? GetCachedProperty(
        ConcurrentDictionary<Type, PropertyInfo> cache,
        Type type,
        string name
    )
    {
        if (cache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        PropertyInfo? property;
        try
        {
            property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }

        if (
            property is null
            || !property.CanRead
            || property.GetIndexParameters().Length != 0
            || (name == "Count" && property.PropertyType != typeof(int))
        )
        {
            return null;
        }

        cache.TryAdd(type, property);
        return property;
    }

    private static object? TryCollectionComparer(object value) =>
        GetPropertyValue(GetCachedProperty(ComparerProperties, value.GetType(), "Comparer"), value);

    private static object? GetPropertyValue(PropertyInfo? property, object value)
    {
        if (property is null)
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

    private static int? GetIntProperty(PropertyInfo? property, object value) =>
        GetPropertyValue(property, value) is int count ? count : null;

    private static bool DictionariesEqualUnordered(
        IEnumerable left,
        IEnumerable right,
        object? keyComparer
    )
    {
        var rightEntries = MaterializeEntries(right);

        var keyEquality = AsNonGenericEquality(keyComparer);
        if (keyEquality is not null)
        {
            // Bounded fast path (issue #60): index the right entries by key
            // through the same discovered key comparer, turning the nested
            // scan into hash lookups while streaming the left side instead of
            // materializing both. Duplicate keys defeat indexing and fall back
            // to the multiset scan below; keys without a usable comparer stay
            // on the structural scan by design (no safe hash exists).
            var hashed = HashDictionariesStreamed(left, rightEntries, keyEquality);
            if (hashed.HasValue)
            {
                return hashed.Value;
            }
        }

        var leftEntries = MaterializeEntries(left);
        if (leftEntries.Count != rightEntries.Count)
        {
            return false;
        }

        var used = new bool[rightEntries.Count];
        foreach (var (key, value) in leftEntries)
        {
            var matched = false;
            for (var index = 0; index < rightEntries.Count; index++)
            {
                if (used[index])
                {
                    continue;
                }

                var keysEqual = keyEquality is not null
                    ? keyEquality.Equals(key, rightEntries[index].Key)
                    : AreValuesEqual(key, rightEntries[index].Key);
                if (!keysEqual || !AreValuesEqual(value, rightEntries[index].Value))
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

    /// <summary>
    /// Hash-joins right entries (indexed by key) against a streamed left side
    /// through the discovered key comparer. Returns <c>null</c> when duplicate
    /// or null keys defeat exact indexing so the caller falls back to the
    /// multiset scan.
    /// </summary>
    private static bool? HashDictionariesStreamed(
        IEnumerable left,
        List<(object? Key, object? Value)> rightEntries,
        IEqualityComparer keyEquality
    )
    {
        var rightMap = new Dictionary<object, object?>(
            rightEntries.Count,
            new NonGenericEqualityAdapter(keyEquality)
        );
        var hasNullKey = false;
        object? nullValue = null;
        foreach (var (key, value) in rightEntries)
        {
            if (key is null)
            {
                if (hasNullKey)
                {
                    return null;
                }

                hasNullKey = true;
                nullValue = value;
                continue;
            }

            if (!rightMap.TryAdd(key, value))
            {
                return null;
            }
        }

        if (hasNullKey && !keyEquality.Equals(null, null))
        {
            return null;
        }

        var streamed = 0;
        var nullMatched = false;
        foreach (var item in left)
        {
            ExtractEntry(item, out var key, out var value);
            if (key is null)
            {
                if (!hasNullKey || nullMatched || !AreValuesEqual(value, nullValue))
                {
                    return false;
                }

                nullMatched = true;
            }
            else if (
                !rightMap.TryGetValue(key, out var rightValue) || !AreValuesEqual(value, rightValue)
            )
            {
                return false;
            }

            streamed++;
            if (streamed > rightEntries.Count)
            {
                return false;
            }
        }

        return streamed == rightEntries.Count;
    }

    private static List<(object? Key, object? Value)> MaterializeEntries(IEnumerable entries)
    {
        var materialized = new List<(object? Key, object? Value)>();
        foreach (var item in entries)
        {
            ExtractEntry(item, out var key, out var value);
            materialized.Add((key, value));
        }

        return materialized;
    }

    private static void ExtractEntry(object? item, out object? key, out object? value)
    {
        if (item is DictionaryEntry entry)
        {
            key = entry.Key;
            value = entry.Value;
            return;
        }

        var itemType = item?.GetType();
        key = GetPropertyValue(
            itemType is null ? null : GetCachedProperty(EntryKeyProperties, itemType, "Key"),
            item!
        );
        value = GetPropertyValue(
            itemType is null ? null : GetCachedProperty(EntryValueProperties, itemType, "Value"),
            item!
        );
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo> EntryKeyProperties = new();

    private static readonly ConcurrentDictionary<Type, PropertyInfo> EntryValueProperties = new();

    private static CollectionShape? GetShape(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        if (ShapeCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var created = CreateShape(type);
        if (created is not null)
        {
            ShapeCache.TryAdd(type, created);
        }

        return created;
    }

    private static CollectionShape? CreateShape(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        var dictionaryArguments = FindDictionaryArguments(type);
        if (dictionaryArguments is not null || typeof(IDictionary).IsAssignableFrom(type))
        {
            return new CollectionShape { Kind = CollectionKind.Dictionary };
        }

        var setElement = FindSetElementType(type);
        if (setElement is not null)
        {
            return new CollectionShape { Kind = CollectionKind.Set, ElementType = setElement };
        }

        return CreateSequenceShape(type);
    }

    private static CollectionShape CreateSequenceShape(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        return new CollectionShape
        {
            Kind = CollectionKind.Sequence,
            ElementType = FindSequenceElementType(type),
        };
    }

    private static Type[]? FindDictionaryArguments(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (
                definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>)
            )
            {
                return type.GetGenericArguments();
            }
        }

        Type[]? readOnlyArguments = null;
        foreach (var implemented in type.GetInterfaces())
        {
            if (!implemented.IsGenericType)
            {
                continue;
            }

            var definition = implemented.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>))
            {
                return implemented.GetGenericArguments();
            }

            readOnlyArguments ??=
                definition == typeof(IReadOnlyDictionary<,>)
                    ? implemented.GetGenericArguments()
                    : null;
        }

        return readOnlyArguments;
    }

    private static Type? FindSetElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(ISet<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        Type? readOnlyElement = null;
        foreach (var implemented in type.GetInterfaces())
        {
            if (!implemented.IsGenericType)
            {
                continue;
            }

            var definition = implemented.GetGenericTypeDefinition();
            if (definition == typeof(ISet<>))
            {
                return implemented.GetGenericArguments()[0];
            }

            if (readOnlyElement is null && definition.FullName == ReadOnlySetDefinitionName)
            {
                readOnlyElement = implemented.GetGenericArguments()[0];
            }
        }

        return readOnlyElement;
    }

    private static Type? FindSequenceElementType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (
                definition == typeof(IEnumerable<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(IList<>)
                || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IReadOnlyList<>)
            )
            {
                return type.GetGenericArguments()[0];
            }
        }

        foreach (var implemented in type.GetInterfaces())
        {
            if (
                implemented.IsGenericType
                && implemented.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            {
                return implemented.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
