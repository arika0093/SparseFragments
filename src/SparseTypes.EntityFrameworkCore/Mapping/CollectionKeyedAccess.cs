using System.Reflection;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// One changed keyed item projected without naming generated transition types.
/// </summary>
/// <typeparam name="TDtoChild">The DTO element type.</typeparam>
internal sealed class KeyedItemView<TDtoChild>
    where TDtoChild : class
{
    public KeyedItemView(
        object key,
        Guid? temporaryKey,
        Optional<TDtoChild?> before,
        Optional<TDtoChild?> after,
        bool isAdded,
        bool isRemoved,
        bool isEdited,
        bool isReordered,
        ISparseChangeSet? edit,
        int beforeIndex = -1,
        int afterIndex = -1
    )
    {
        Key = key;
        TemporaryKey = temporaryKey;
        Before = before;
        After = after;
        IsAdded = isAdded;
        IsRemoved = isRemoved;
        IsEdited = isEdited;
        IsReordered = isReordered;
        Edit = edit;
        BeforeIndex = beforeIndex;
        AfterIndex = afterIndex;
    }

    /// <summary>Gets the stable key (boxed).</summary>
    public object Key { get; }

    /// <summary>Gets the collection-scoped temporary identity for unassigned entries.</summary>
    public Guid? TemporaryKey { get; }

    /// <summary>Gets the before-state element.</summary>
    public Optional<TDtoChild?> Before { get; }

    /// <summary>Gets the after-state element.</summary>
    public Optional<TDtoChild?> After { get; }

    /// <summary>Gets whether the entry was added.</summary>
    public bool IsAdded { get; }

    /// <summary>Gets whether the entry was removed.</summary>
    public bool IsRemoved { get; }

    /// <summary>Gets whether the entry was edited.</summary>
    public bool IsEdited { get; }

    /// <summary>Gets whether the entry was reordered.</summary>
    public bool IsReordered { get; }

    /// <summary>Gets the nested element transition for edits.</summary>
    public ISparseChangeSet? Edit { get; }

    /// <summary>Gets the absolute index before the transition.</summary>
    public int BeforeIndex { get; }

    /// <summary>Gets the absolute index after the transition.</summary>
    public int AfterIndex { get; }
}

/// <summary>
/// A keyed transition projected without naming generated transition types.
/// </summary>
/// <typeparam name="TDtoChild">The DTO element type.</typeparam>
internal sealed class KeyedCollectionView<TDtoChild>
    where TDtoChild : class
{
    public KeyedCollectionView(
        bool isChanged,
        bool orderChanged,
        IReadOnlyList<object> beforeOrder,
        IReadOnlyList<object> afterOrder,
        IReadOnlyList<KeyedItemView<TDtoChild>> items
    )
    {
        IsChanged = isChanged;
        OrderChanged = orderChanged;
        BeforeOrder = beforeOrder;
        AfterOrder = afterOrder;
        Items = items;
    }

    /// <summary>Gets whether the transition contains any changes.</summary>
    public bool IsChanged { get; }

    /// <summary>Gets whether the key order changed.</summary>
    public bool OrderChanged { get; }

    /// <summary>Gets the assigned-key order before the transition.</summary>
    public IReadOnlyList<object> BeforeOrder { get; }

    /// <summary>Gets the assigned-key order after the transition.</summary>
    public IReadOnlyList<object> AfterOrder { get; }

    /// <summary>Gets the changed items.</summary>
    public IReadOnlyList<KeyedItemView<TDtoChild>> Items { get; }
}

/// <summary>
/// Closes typed keyed-transition dispatch once per plan build.
/// </summary>
/// <remarks>The ordinary <c>SparseTypes.Generator</c> emits the per-member keyed transition types consumed here.</remarks>
internal static class KeyedCollectionView
{
    private static readonly MethodInfo FetcherFactory =
        typeof(KeyedCollectionView).GetMethod(
            nameof(CreateFetcherCore),
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException("The keyed fetch factory is missing.");

    /// <summary>Finds the closed keyed-change interface on a generated transition type.</summary>
    public static Type? FindKeyedInterface(Type transitionType)
    {
        ArgumentNullException.ThrowIfNull(transitionType);
        foreach (var candidate in transitionType.GetInterfaces())
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (
                definition == typeof(ISparseKeyedChange<,,>)
                || definition == typeof(ISparseTemporaryKeyedChange<,,>)
            )
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Closes a typed fetch delegate for one DTO member transition.</summary>
    public static Func<
        ISparseChangeSet,
        string,
        KeyedCollectionView<TDtoChild>?
    > CreateFetcher<TDtoChild>(Type keyedInterface)
        where TDtoChild : class
    {
        ArgumentNullException.ThrowIfNull(keyedInterface);
        var arguments = keyedInterface.GetGenericArguments();
        var method = FetcherFactory.MakeGenericMethod(
            arguments[0],
            arguments[1],
            arguments[2],
            typeof(TDtoChild)
        );
        return (Func<ISparseChangeSet, string, KeyedCollectionView<TDtoChild>?>)
            method.Invoke(null, null)!;
    }

    /// <summary>Public factory entry so plan-time generic closing needs no accessibility bypass.</summary>
    public static Func<ISparseChangeSet, string, KeyedCollectionView<TDtoChild>?> CreateFetcherCore<
        TKey,
        TElement,
        TElementChange,
        TDtoChild
    >()
        where TDtoChild : class
    {
        return static (changes, memberName) =>
        {
            if (
                !changes.TryGetKeyedChange<TKey, TElement, TElementChange>(
                    memberName,
                    out var change
                ) || change is null
            )
            {
                return null;
            }

            var items = new List<KeyedItemView<TDtoChild>>();
            foreach (var item in change.EnumerateItems())
            {
                items.Add(ConvertItem<TKey, TElement, TElementChange, TDtoChild>(item));
            }

            return new KeyedCollectionView<TDtoChild>(
                change.IsChanged,
                change.OrderChanged,
                change.BeforeOrder.Select(static key => (object)key!).ToList(),
                change.AfterOrder.Select(static key => (object)key!).ToList(),
                items
            );
        };
    }

    private static KeyedItemView<TDtoChild> ConvertItem<TKey, TElement, TElementChange, TDtoChild>(
        ISparseKeyedItemChange<TKey, TElement, TElementChange> item
    )
        where TDtoChild : class
    {
        if (
            item.Before.IsPresent
            && item.Before.Value is not null
            && item.Before.Value is not TDtoChild
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The keyed element type '{typeof(TElement).Name}' does not match the mapped DTO element '{typeof(TDtoChild).Name}'."
            );
        }

        if (
            item.After.IsPresent
            && item.After.Value is not null
            && item.After.Value is not TDtoChild
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The keyed element type '{typeof(TElement).Name}' does not match the mapped DTO element '{typeof(TDtoChild).Name}'."
            );
        }

        ISparseChangeSet? edit = null;
        if (item.IsEdited)
        {
            edit =
                (item.Edit as ISparseChangeSet)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    null,
                    $"The element transition '{typeof(TElementChange).Name}' is not a change set, so nested child edits cannot recurse."
                );
        }

        return new KeyedItemView<TDtoChild>(
            (object)item.Key!,
            item.TemporaryKey,
            ConvertOptional(item.Before),
            ConvertOptional(item.After),
            item.IsAdded,
            item.IsRemoved,
            item.IsEdited,
            item.IsReordered,
            edit,
            item.BeforeIndex,
            item.AfterIndex
        );

        static Optional<TDtoChild?> ConvertOptional(Optional<TElement> optional) =>
            optional.IsPresent
                ? Optional<TDtoChild?>.Present((TDtoChild?)(object?)optional.Value)
                : Optional<TDtoChild?>.Missing;
    }
}
