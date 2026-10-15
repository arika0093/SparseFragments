using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Path-first semantic state queries over retained change sets.
/// </summary>
/// <remarks>All queries derive from the change set they are called on.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseStateQueries
{
    /// <summary>
    /// Queries the semantic field state at a typed path.
    /// </summary>
    /// <param name="changes">The change set to inspect.</param>
    /// <param name="path">The typed path to inspect.</param>
    /// <typeparam name="TModel">The root model type.</typeparam>
    /// <typeparam name="TValue">The addressed value type.</typeparam>
    /// <returns>The point-in-time field state.</returns>
    /// <remarks>Unknown paths report a clean state (no change.</remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="changes"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The exact transition value cannot be represented as
    /// <typeparamref name="TValue"/>.
    /// </exception>
    public static SparseFieldState<TValue> FieldState<TModel, TValue>(
        this ISparseChangeSet changes,
        SparsePath<TModel, TValue> path
    )
    {
        ArgumentNullException.ThrowIfNull(changes);
        return FieldStateCore<TValue>(changes, path.Path);
    }

    /// <summary>
    /// Queries the semantic field state at a model-agnostic path.
    /// </summary>
    /// <param name="changes">The change set to inspect.</param>
    /// <param name="path">The path to inspect.</param>
    /// <returns>The point-in-time field state with boxed endpoints.</returns>
    /// <remarks>Covers paths without a compile-time typed form, such as nested-model builders and missing or deleted members.</remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="changes"/> is null.
    /// </exception>
    public static SparseFieldState<object?> FieldState(
        this ISparseChangeSet changes,
        SparsePath path
    )
    {
        ArgumentNullException.ThrowIfNull(changes);
        return FieldStateCore<object?>(changes, path);
    }

    /// <summary>
    /// Builds a collection state view over a keyed-sequence transition.
    /// </summary>
    /// <param name="change">The member's typed keyed transition.</param>
    /// <param name="path">The collection member path the view reports.</param>
    /// <typeparam name="TKey">The stable key type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <typeparam name="TElementChange">The element change-set type.</typeparam>
    /// <returns>A point-in-time view retaining
    /// <paramref name="change"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="change"/> is null.
    /// </exception>
    public static SparseCollectionState KeyedState<TKey, TElement, TElementChange>(
        this ISparseKeyedChange<TKey, TElement, TElementChange> change,
        SparsePath path
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        var temporary = change as ISparseTemporaryKeyedChange<TKey, TElement, TElementChange>;
        return new SparseCollectionState(
            path,
            change.IsEmpty,
            change.IsChanged,
            change.OrderChanged,
            key => GetKeyedItemState(change, key),
            temporaryKey => GetTemporaryItemState(temporary, temporaryKey)
        );
    }

    /// <summary>
    /// Builds a collection state view over a dictionary transition.
    /// </summary>
    /// <param name="change">The member's typed dictionary transition.</param>
    /// <param name="path">The collection member path the view reports.</param>
    /// <typeparam name="TKey">The dictionary key type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <typeparam name="TEdited">The edited-projection type.</typeparam>
    /// <returns>A point-in-time view retaining
    /// <paramref name="change"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="change"/> is null.
    /// </exception>
    public static SparseCollectionState DictionaryState<TKey, TValue, TEdited>(
        this ISparseDictionaryChange<TKey, TValue, TEdited> change,
        SparsePath path
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        return new SparseCollectionState(
            path,
            change.IsEmpty,
            change.IsChanged,
            false,
            key => GetDictionaryEntryState(change, key),
            _ => SparseCollectionItemState.Empty
        );
    }

    private static SparseFieldState<T> FieldStateCore<T>(ISparseChangeSet changes, SparsePath query)
    {
        var exact = false;
        var ancestor = false;
        var descendant = false;
        var kind = SparseFieldChangeKind.Unchanged;
        var before = Optional<object?>.Missing;
        var after = Optional<object?>.Missing;
        foreach (var record in changes.EnumerateChangeRecords())
        {
            var current = record.Path;
            if (current.Equals(query))
            {
                if (!exact)
                {
                    exact = true;
                    kind = MapKind(record.Kind);
                    before = record.Before;
                    after = record.After;
                }
            }
            else if (query.StartsWith(current))
            {
                ancestor = true;
            }
            else if (current.StartsWith(query))
            {
                descendant = true;
            }
        }

        return new SparseFieldState<T>
        {
            Path = query,
            IsChanged = exact,
            Kind = kind,
            HasAncestorChange = ancestor,
            HasDescendantChange = descendant,
            Before = exact ? Unbox<T>(before, query) : Optional<T>.Missing,
            After = exact ? Unbox<T>(after, query) : Optional<T>.Missing,
        };
    }

    private static SparseFieldChangeKind MapKind(SparseChangeKind kind) =>
        kind switch
        {
            SparseChangeKind.Added => SparseFieldChangeKind.Added,
            SparseChangeKind.Removed => SparseFieldChangeKind.Removed,
            SparseChangeKind.Order => SparseFieldChangeKind.Reordered,
            _ => SparseFieldChangeKind.Changed,
        };

    private static Optional<T> Unbox<T>(Optional<object?> boxed, SparsePath query)
    {
        if (!boxed.IsPresent)
        {
            return Optional<T>.Missing;
        }

        var value = boxed.Value;
        if (value is T typed)
        {
            return Optional<T>.Present(typed);
        }

        if (value is null && default(T) is null)
        {
            return Optional<T>.Present(default!);
        }

        throw new InvalidOperationException(
            "The change record at '"
                + query.ToString()
                + "' cannot be represented as '"
                + typeof(T)
                + "'."
        );
    }

    private static SparseCollectionItemState GetKeyedItemState<TKey, TElement, TElementChange>(
        ISparseKeyedChange<TKey, TElement, TElementChange> change,
        object? key
    )
    {
        if (key is null && default(TKey) is not null)
        {
            return SparseCollectionItemState.Empty;
        }

        return SparseCollectionItemState.FromKeyed(change.GetChange((TKey)key!));
    }

    private static SparseCollectionItemState GetTemporaryItemState<TKey, TElement, TElementChange>(
        ISparseTemporaryKeyedChange<TKey, TElement, TElementChange>? temporary,
        Guid temporaryKey
    )
    {
        if (temporary is null)
        {
            return SparseCollectionItemState.Empty;
        }

        return SparseCollectionItemState.FromKeyed(temporary.GetTemporaryChange(temporaryKey));
    }

    private static SparseCollectionItemState GetDictionaryEntryState<TKey, TValue, TEdited>(
        ISparseDictionaryChange<TKey, TValue, TEdited> change,
        object? key
    )
    {
        if (key is null && default(TKey) is not null)
        {
            return SparseCollectionItemState.Empty;
        }

        return SparseCollectionItemState.FromDictionary(change.GetChange((TKey)key!));
    }
}
