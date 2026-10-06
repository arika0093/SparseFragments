using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SparseFragments;

/// <summary>Allocation-friendly structural comparison shared by fragment equality.</summary>
/// <remarks>
/// Collection and fragment semantics live here so equality stays consistent.
/// The typed native delegates below close generic helpers over runtime collection element
/// types, which needs dynamic code. They are created only when dynamic code is supported
/// (and cached per shape, preserving the non-allocating steady state); trimming and
/// NativeAOT callers use the structural comparisons instead, so the whole closure stays
/// warning-clean. Shape classification itself only compares generic type definitions and
/// reads element types for assignability tests.
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

        public Func<object, IEnumerable, bool?>? TrySetEquals;

        public Func<object, object, bool?>? TryDictionariesEqual;

        public Func<object, object, bool?>? TrySequencesEqual;
    }

    private const string ReadOnlySetDefinitionName = "System.Collections.Generic.IReadOnlySet`1";

    private static readonly ConcurrentDictionary<Type, CollectionShape> ShapeCache = new();

    private static readonly MethodInfo SetEqualsOpenMethod =
        typeof(FragmentComparisonPrimitives).GetMethod(nameof(SetEqualsTyped))!;

    private static readonly MethodInfo DictionariesEqualOpenMethod =
        typeof(FragmentComparisonPrimitives).GetMethod(nameof(DictionariesEqualTyped))!;

    private static readonly MethodInfo SequencesEqualOpenMethod =
        typeof(FragmentComparisonPrimitives).GetMethod(nameof(SequencesEqualTyped))!;

#if NETSTANDARD
    // RuntimeFeature.IsDynamicCodeSupported is unavailable on .NET Standard targets, which
    // never publish NativeAOT themselves. Assume JIT behavior there; NativeAOT hosts
    // consume the .NET 8+ asset where the check below is exact.
    private static bool IsDynamicCodeSupported => true;
#else
    private static bool IsDynamicCodeSupported =>
        System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
#endif

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2060",
        Justification = "Native delegates are created only when dynamic code is supported. Trimming and NativeAOT callers use the structural comparisons instead."
    )]
    [UnconditionalSuppressMessage(
        "Aot",
        "IL3050",
        Justification = "Native delegates are created only when dynamic code is supported. NativeAOT callers use the structural comparisons instead."
    )]
    private static TDelegate CreateNativeDelegate<TDelegate>(
        MethodInfo openMethod,
        Type[] typeArguments
    )
        where TDelegate : Delegate =>
        (TDelegate)openMethod.MakeGenericMethod(typeArguments).CreateDelegate(typeof(TDelegate));

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

            return AreDictionariesEqual(left, right, leftShape, rightShape);
        }

        if (leftKind == CollectionKind.Set || rightKind == CollectionKind.Set)
        {
            if (leftKind != CollectionKind.Set || rightKind != CollectionKind.Set)
            {
                return false;
            }

            return AreSetsEqual(left, right, leftShape, rightShape);
        }

        var sequencesFast =
            leftShape?.TrySequencesEqual?.Invoke(left, right)
            ?? rightShape?.TrySequencesEqual?.Invoke(right, left);
        if (sequencesFast.HasValue)
        {
            return sequencesFast.Value;
        }

        return SequencesEqualOrdered(leftItems, rightItems);
    }

    // Public so the open method resolves through public-only reflection (no
    // accessibility bypass); the containing type is internal.
    public static bool? SequencesEqualTyped<T>(object left, object right)
    {
        if (left is IList<T> leftList && right is IList<T> rightList)
        {
            if (leftList.Count != rightList.Count)
            {
                return false;
            }

            if (MayNeedDeepComparison(typeof(T)))
            {
                for (var index = 0; index < leftList.Count; index++)
                {
                    if (!AreValuesEqual(leftList[index], rightList[index]))
                    {
                        return false;
                    }
                }
            }
            else
            {
                var comparer = EqualityComparer<T>.Default;
                for (var index = 0; index < leftList.Count; index++)
                {
                    if (!comparer.Equals(leftList[index], rightList[index]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        if (left is IReadOnlyList<T> leftReadOnly && right is IReadOnlyList<T> rightReadOnly)
        {
            if (leftReadOnly.Count != rightReadOnly.Count)
            {
                return false;
            }

            if (MayNeedDeepComparison(typeof(T)))
            {
                for (var index = 0; index < leftReadOnly.Count; index++)
                {
                    if (!AreValuesEqual(leftReadOnly[index], rightReadOnly[index]))
                    {
                        return false;
                    }
                }
            }
            else
            {
                var comparer = EqualityComparer<T>.Default;
                for (var index = 0; index < leftReadOnly.Count; index++)
                {
                    if (!comparer.Equals(leftReadOnly[index], rightReadOnly[index]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        return null;
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

    private static bool AreDictionariesEqual(
        object left,
        object right,
        CollectionShape? leftShape,
        CollectionShape? rightShape
    )
    {
        var leftCount = TryDictionaryCount(left);
        var rightCount = TryDictionaryCount(right);
        if (!leftCount.HasValue || !rightCount.HasValue)
        {
            // Dictionaries never fall back to enumeration-order equality; exotic
            // shapes without a readable count still compare order-independently.
            return DictionariesEqualUnordered((IEnumerable)left, (IEnumerable)right);
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

        // The typed path avoids per-entry boxing for scalar values.
        var forward = leftShape?.TryDictionariesEqual?.Invoke(left, right);
        var backward = rightShape?.TryDictionariesEqual?.Invoke(right, left);
        if (forward.HasValue && backward.HasValue)
        {
            return forward.Value && backward.Value;
        }

        if (forward.HasValue || backward.HasValue)
        {
            return (forward ?? backward)!.Value;
        }

        if (left is IDictionary leftDictionary && right is IDictionary rightDictionary)
        {
            return DictionaryContainsAll(leftDictionary, rightDictionary)
                && DictionaryContainsAll(rightDictionary, leftDictionary);
        }

        return DictionariesEqualUnordered((IEnumerable)left, (IEnumerable)right);
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

    // Public so the open methods resolve through public-only reflection (no
    // accessibility bypass); the containing type is internal.
    public static bool? DictionariesEqualTyped<TKey, TValue>(object pairs, object lookup)
    {
        if (pairs is not IEnumerable<KeyValuePair<TKey, TValue>> entries)
        {
            return null;
        }

        if (lookup is IDictionary<TKey, TValue> dictionary)
        {
            return DictionaryEntriesEqual(entries, dictionary);
        }

        if (lookup is IReadOnlyDictionary<TKey, TValue> readOnly)
        {
            return DictionaryEntriesEqual(entries, readOnly);
        }

        return null;
    }

    private static bool DictionaryEntriesEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> entries,
        IDictionary<TKey, TValue> lookup
    )
    {
        if (MayNeedDeepComparison(typeof(TValue)))
        {
            foreach (var pair in entries)
            {
                if (
                    !lookup.TryGetValue(pair.Key, out var value)
                    || !AreValuesEqual(pair.Value, value)
                )
                {
                    return false;
                }
            }

            return true;
        }

        var comparer = EqualityComparer<TValue>.Default;
        foreach (var pair in entries)
        {
            if (!lookup.TryGetValue(pair.Key, out var value) || !comparer.Equals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionaryEntriesEqual<TKey, TValue>(
        IEnumerable<KeyValuePair<TKey, TValue>> entries,
        IReadOnlyDictionary<TKey, TValue> lookup
    )
    {
        if (MayNeedDeepComparison(typeof(TValue)))
        {
            foreach (var pair in entries)
            {
                if (
                    !lookup.TryGetValue(pair.Key, out var value)
                    || !AreValuesEqual(pair.Value, value)
                )
                {
                    return false;
                }
            }

            return true;
        }

        var comparer = EqualityComparer<TValue>.Default;
        foreach (var pair in entries)
        {
            if (!lookup.TryGetValue(pair.Key, out var value) || !comparer.Equals(pair.Value, value))
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

        var forward = leftShape?.TrySetEquals?.Invoke(left, (IEnumerable)right);
        var backward = rightShape?.TrySetEquals?.Invoke(right, (IEnumerable)left);
        if (forward.HasValue && backward.HasValue)
        {
            return forward.Value && backward.Value;
        }

        if (forward.HasValue || backward.HasValue)
        {
            return (forward ?? backward)!.Value;
        }

        return SlowSetEquals(left, right);
    }

    // Public so the open method resolves through public-only reflection (no
    // accessibility bypass); the containing type is internal.
    public static bool? SetEqualsTyped<T>(object candidate, IEnumerable other)
    {
        if (candidate is ISet<T> set && other is IEnumerable<T> items)
        {
            return set.SetEquals(items);
        }

        return null;
    }

    private static bool SlowSetEquals(object left, object right)
    {
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

        // Custom IReadOnlyDictionary implementations may not implement non-generic
        // ICollection; read Count structurally so they still compare
        // order-independently instead of falling back to enumeration order.
        return GetIntProperty(GetCachedProperty(CountProperties, value.GetType(), "Count"), value);
    }

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

    private static bool DictionariesEqualUnordered(IEnumerable left, IEnumerable right)
    {
        var leftEntries = MaterializeEntries(left);
        var rightEntries = MaterializeEntries(right);
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

                if (
                    !AreValuesEqual(key, rightEntries[index].Key)
                    || !AreValuesEqual(value, rightEntries[index].Value)
                )
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

    private static List<(object? Key, object? Value)> MaterializeEntries(IEnumerable entries)
    {
        var materialized = new List<(object? Key, object? Value)>();
        foreach (var item in entries)
        {
            if (item is DictionaryEntry entry)
            {
                materialized.Add((entry.Key, entry.Value));
                continue;
            }

            var itemType = item?.GetType();
            var key = GetPropertyValue(
                itemType is null ? null : GetCachedProperty(EntryKeyProperties, itemType, "Key"),
                item!
            );
            var value = GetPropertyValue(
                itemType is null
                    ? null
                    : GetCachedProperty(EntryValueProperties, itemType, "Value"),
                item!
            );
            materialized.Add((key, value));
        }

        return materialized;
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
            var dictionaryShape = new CollectionShape { Kind = CollectionKind.Dictionary };
            if (dictionaryArguments is not null && IsDynamicCodeSupported)
            {
                dictionaryShape.TryDictionariesEqual = CreateNativeDelegate<
                    Func<object, object, bool?>
                >(DictionariesEqualOpenMethod, [dictionaryArguments[0], dictionaryArguments[1]]);
            }

            return dictionaryShape;
        }

        var setElement = FindSetElementType(type, out var hasNativeSet);
        if (setElement is not null)
        {
            var setShape = new CollectionShape
            {
                Kind = CollectionKind.Set,
                ElementType = setElement,
            };
            if (hasNativeSet && IsDynamicCodeSupported)
            {
                setShape.TrySetEquals = CreateNativeDelegate<Func<object, IEnumerable, bool?>>(
                    SetEqualsOpenMethod,
                    [setElement]
                );
            }

            return setShape;
        }

        return CreateSequenceShape(type);
    }

    private static CollectionShape CreateSequenceShape(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type
    )
    {
        var sequenceShape = new CollectionShape { Kind = CollectionKind.Sequence };
        var elementType = FindSequenceElementType(type);
        sequenceShape.ElementType = elementType;
        if (elementType is not null && IsDynamicCodeSupported)
        {
            sequenceShape.TrySequencesEqual = CreateNativeDelegate<Func<object, object, bool?>>(
                SequencesEqualOpenMethod,
                [elementType]
            );
        }

        return sequenceShape;
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
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type,
        out bool hasNativeSet
    )
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(ISet<>))
            {
                hasNativeSet = true;
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
                hasNativeSet = true;
                return implemented.GetGenericArguments()[0];
            }

            if (readOnlyElement is null && definition.FullName == ReadOnlySetDefinitionName)
            {
                readOnlyElement = implemented.GetGenericArguments()[0];
            }
        }

        hasNativeSet = false;
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
