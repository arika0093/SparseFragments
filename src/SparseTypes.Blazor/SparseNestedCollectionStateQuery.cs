using System.Collections.Concurrent;
using System.Reflection;

namespace SparseTypes.Blazor;

internal static class SparseNestedCollectionStateQuery
{
    private readonly record struct TransitionTypes(Type Key, Type Value, Type Change);

    private static readonly MethodInfo TryGetKeyedChangeDefinition = typeof(ISparseChangeSet)
        .GetMethods()
        .Single(static method => method.Name == "TryGetKeyedChange");
    private static readonly MethodInfo TryGetDictionaryChangeDefinition = typeof(ISparseChangeSet)
        .GetMethods()
        .Single(static method => method.Name == "TryGetDictionaryChange");
    private static readonly MethodInfo KeyedStateDefinition = typeof(SparseStateQueries)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(static method => method.Name == nameof(SparseStateQueries.KeyedState));
    private static readonly MethodInfo DictionaryStateDefinition = typeof(SparseStateQueries)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(static method => method.Name == nameof(SparseStateQueries.DictionaryState));

    private static readonly ConcurrentDictionary<TransitionTypes, MethodInfo> KeyedChangeMethods =
        new();
    private static readonly ConcurrentDictionary<
        TransitionTypes,
        MethodInfo
    > DictionaryChangeMethods = new();
    private static readonly ConcurrentDictionary<TransitionTypes, MethodInfo> KeyedStateMethods =
        new();
    private static readonly ConcurrentDictionary<
        TransitionTypes,
        MethodInfo
    > DictionaryStateMethods = new();

    internal static bool TryGetState<TModel>(
        ISparseChangeSet changes,
        Generated.IDescriptorSet descriptors,
        SparsePath path,
        out SparseCollectionState? state
    )
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(descriptors);
        state = null;
        if (path.RootType != typeof(TModel) || path.IsRoot || path.Segments.Count == 0)
        {
            return false;
        }

        var currentChanges = changes;
        var currentDescriptors = descriptors;
        var index = 0;
        while (index < path.Segments.Count)
        {
            var segment = path.Segments[index];
            if (
                segment.Kind != SparsePathSegmentKind.Member
                || !currentDescriptors.TryGet(segment.Name, out var member)
            )
            {
                return false;
            }

            if (index == path.Segments.Count - 1)
            {
                return TryGetMemberCollectionState(currentChanges, member, path, out state);
            }

            var next = path.Segments[index + 1];
            if (next.Kind == SparsePathSegmentKind.Member)
            {
                if (
                    !currentChanges.TryGetNestedChange(segment.Name, out var nestedChanges)
                    || member.Child is not { } childDescriptors
                )
                {
                    return false;
                }

                currentChanges = nestedChanges;
                currentDescriptors = childDescriptors;
                index++;
                continue;
            }

            if (member.Array is { IsKeyed: true } array)
            {
                if (
                    !TryGetKeyedChange(
                        currentChanges,
                        segment.Name,
                        array,
                        out var keyedChange,
                        out var elementChangeType
                    )
                    || !TryGetKeyedItemChange(
                        keyedChange!,
                        array,
                        elementChangeType!,
                        next,
                        out var itemChanges,
                        out var itemDescriptors
                    )
                )
                {
                    return false;
                }

                currentChanges = itemChanges!;
                currentDescriptors = itemDescriptors!;
                index += 2;
                continue;
            }

            if (member.Dictionary is { } dictionary && next.Kind == SparsePathSegmentKind.Key)
            {
                if (
                    !TryGetDictionaryChange(
                        currentChanges,
                        segment.Name,
                        dictionary,
                        member.Shape.HasChild,
                        out var dictionaryChange,
                        out var editedType
                    )
                    || !TryGetDictionaryItemChange(
                        dictionaryChange!,
                        dictionary,
                        editedType!,
                        next.Key,
                        out var itemChanges,
                        out var itemDescriptors
                    )
                )
                {
                    return false;
                }

                currentChanges = itemChanges!;
                currentDescriptors = itemDescriptors!;
                index += 2;
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool TryGetMemberCollectionState(
        ISparseChangeSet changes,
        Generated.IDescriptor member,
        SparsePath path,
        out SparseCollectionState? state
    )
    {
        if (member.Array is { IsKeyed: true } array)
        {
            if (
                !TryGetKeyedChange(
                    changes,
                    member.Name,
                    array,
                    out var keyedChange,
                    out var elementChangeType
                )
            )
            {
                state = null;
                return false;
            }

            var types = new TransitionTypes(array.KeyType!, array.ItemType, elementChangeType!);
            state = (SparseCollectionState)
                KeyedStateMethods
                    .GetOrAdd(
                        types,
                        static key =>
                            KeyedStateDefinition.MakeGenericMethod(key.Key, key.Value, key.Change)
                    )
                    .Invoke(null, [keyedChange, path])!;
            return true;
        }

        if (member.Dictionary is { } dictionary)
        {
            if (
                !TryGetDictionaryChange(
                    changes,
                    member.Name,
                    dictionary,
                    member.Shape.HasChild,
                    out var dictionaryChange,
                    out var editedType
                )
            )
            {
                state = null;
                return false;
            }

            var types = new TransitionTypes(dictionary.KeyType, dictionary.ValueType, editedType!);
            state = (SparseCollectionState)
                DictionaryStateMethods
                    .GetOrAdd(
                        types,
                        static key =>
                            DictionaryStateDefinition.MakeGenericMethod(
                                key.Key,
                                key.Value,
                                key.Change
                            )
                    )
                    .Invoke(null, [dictionaryChange, path])!;
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryGetKeyedChange(
        ISparseChangeSet changes,
        string memberName,
        Generated.IArrayDescriptor array,
        out object? change,
        out Type? elementChangeType
    )
    {
        elementChangeType = GetChangeSetType(array.ItemType);
        if (array.KeyType is not { } keyType || elementChangeType is null)
        {
            change = null;
            return false;
        }

        var types = new TransitionTypes(keyType, array.ItemType, elementChangeType);
        var method = KeyedChangeMethods.GetOrAdd(
            types,
            static key =>
                TryGetKeyedChangeDefinition.MakeGenericMethod(key.Key, key.Value, key.Change)
        );
        var arguments = new object?[] { memberName, null };
        if (method.Invoke(changes, arguments) is not true || arguments[1] is not { } result)
        {
            change = null;
            return false;
        }

        change = result;
        return true;
    }

    private static bool TryGetKeyedItemChange(
        object change,
        Generated.IArrayDescriptor array,
        Type elementChangeType,
        SparsePathSegment segment,
        out ISparseChangeSet? itemChanges,
        out Generated.IDescriptorSet? itemDescriptors
    )
    {
        itemChanges = null;
        itemDescriptors = null;
        if (segment.Kind is not (SparsePathSegmentKind.Key or SparsePathSegmentKind.TemporaryKey))
        {
            return false;
        }

        var keyType = array.KeyType;
        if (keyType is null)
        {
            return false;
        }

        var changeInterface = typeof(ISparseKeyedChange<,,>).MakeGenericType(
            keyType,
            array.ItemType,
            elementChangeType
        );
        object? item;
        if (segment.Kind == SparsePathSegmentKind.TemporaryKey)
        {
            var temporaryInterface = typeof(ISparseTemporaryKeyedChange<,,>).MakeGenericType(
                keyType,
                array.ItemType,
                elementChangeType
            );
            if (!temporaryInterface.IsInstanceOfType(change))
            {
                return false;
            }

            item = temporaryInterface
                .GetMethod("GetTemporaryChange")!
                .Invoke(change, [segment.TemporaryKey!.Value]);
        }
        else
        {
            if (!changeInterface.IsInstanceOfType(change))
            {
                return false;
            }

            item = changeInterface.GetMethod("GetChange")!.Invoke(change, [segment.Key]);
        }

        itemChanges = item?.GetType().GetProperty("Edit")?.GetValue(item) as ISparseChangeSet;
        var itemIndex =
            segment.Kind == SparsePathSegmentKind.TemporaryKey
                ? array.IndexOfTemporaryKey(segment.TemporaryKey!.Value)
                : array.IndexOfKey(segment.Key);
        itemDescriptors = itemIndex < 0 ? null : array.GetItemDescriptors(itemIndex);
        return itemChanges is not null && itemDescriptors is not null;
    }

    private static bool TryGetDictionaryChange(
        ISparseChangeSet changes,
        string memberName,
        Generated.IDictDescriptor dictionary,
        bool hasChild,
        out object? change,
        out Type? editedType
    )
    {
        editedType = hasChild ? GetChangeSetType(dictionary.ValueType) : dictionary.ValueType;
        if (editedType is null)
        {
            change = null;
            return false;
        }

        var types = new TransitionTypes(dictionary.KeyType, dictionary.ValueType, editedType);
        var method = DictionaryChangeMethods.GetOrAdd(
            types,
            static key =>
                TryGetDictionaryChangeDefinition.MakeGenericMethod(key.Key, key.Value, key.Change)
        );
        var arguments = new object?[] { memberName, null };
        if (method.Invoke(changes, arguments) is not true || arguments[1] is not { } result)
        {
            change = null;
            return false;
        }

        change = result;
        return true;
    }

    private static bool TryGetDictionaryItemChange(
        object change,
        Generated.IDictDescriptor dictionary,
        Type editedType,
        object? key,
        out ISparseChangeSet? itemChanges,
        out Generated.IDescriptorSet? itemDescriptors
    )
    {
        itemChanges = null;
        itemDescriptors = null;
        var dictionaryInterface = typeof(ISparseDictionaryChange<,,>).MakeGenericType(
            dictionary.KeyType,
            dictionary.ValueType,
            editedType
        );
        if (!dictionaryInterface.IsInstanceOfType(change))
        {
            return false;
        }

        var item = dictionaryInterface.GetMethod("GetChange")!.Invoke(change, [key]);
        var itemInterface = typeof(ISparseStructuralDictionaryItemChange<,,>).MakeGenericType(
            dictionary.KeyType,
            dictionary.ValueType,
            editedType
        );
        if (item is null || !itemInterface.IsInstanceOfType(item))
        {
            return false;
        }

        itemChanges = itemInterface.GetProperty("Edit")!.GetValue(item) as ISparseChangeSet;
        itemDescriptors = dictionary.GetValueDescriptors(key);
        return itemChanges is not null && itemDescriptors is not null;
    }

    private static Type? GetChangeSetType(Type modelType)
    {
        var changeSetContract = typeof(ISparseChangeSet<>).MakeGenericType(modelType);
        return modelType
            .GetNestedTypes(BindingFlags.Public)
            .FirstOrDefault(changeSetContract.IsAssignableFrom);
    }
}
