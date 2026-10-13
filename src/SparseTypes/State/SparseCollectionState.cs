using System.ComponentModel;

namespace SparseTypes;

/// <summary>
/// Point-in-time snapshot of one keyed or dictionary entry within a collection state.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots map one-to-one from the typed keyed/dictionary item transitions:
/// <see cref="IsAdded"/>, <see cref="IsRemoved"/>, <see cref="IsEdited"/>, and
/// (keyed only) <see cref="IsReordered"/> mirror the transition flags without
/// reimplementing them. Unknown keys and identities without temporary support
/// produce the empty state (<see cref="IsEmpty"/>), matching the typed
/// <c>GetChange</c> contract. A member-level whole transition (for example a
/// null-to-populated nullable collection) does not suppress row states: rows
/// still resolve through the typed transition's granular items.
/// </para>
/// <para>
/// Lifetime: the snapshot is detached from the change set. Querying it never
/// touches the live model.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct SparseCollectionItemState
{
    /// <summary>Gets the entry key, boxed for collection-agnostic lookup.</summary>
    /// <remarks>Misses report the default key, matching the typed empty item.</remarks>
    public object? Key { get; private init; }

    /// <summary>Gets the collection-scoped temporary identity for unassigned entries.</summary>
    public Guid? TemporaryKey { get; private init; }

    /// <summary>Gets the presence-aware value before the transition.</summary>
    public Optional<object?> Before { get; private init; }

    /// <summary>Gets the presence-aware value after the transition.</summary>
    public Optional<object?> After { get; private init; }

    /// <summary>Gets the absolute collection index before the transition, or -1 when absent.</summary>
    public int BeforeIndex { get; private init; }

    /// <summary>Gets the absolute collection index after the transition, or -1 when absent.</summary>
    public int AfterIndex { get; private init; }

    /// <summary>Gets whether the entry was added.</summary>
    public bool IsAdded { get; private init; }

    /// <summary>Gets whether the entry was removed.</summary>
    public bool IsRemoved { get; private init; }

    /// <summary>Gets whether the entry was edited.</summary>
    public bool IsEdited { get; private init; }

    /// <summary>
    /// Gets whether the entry was reordered.
    /// </summary>
    /// <remarks>
    /// Keyed only; dictionary entries always report false. Reorder observes
    /// surviving-key relative rank, so membership-only shifts leave it false.
    /// </remarks>
    public bool IsReordered { get; private init; }

    /// <summary>Gets whether this state carries no semantic change for the requested key.</summary>
    public bool IsEmpty { get; private init; }

    /// <summary>Gets whether this state contains any changes.</summary>
    public bool IsChanged => !IsEmpty;

    internal static SparseCollectionItemState Empty =>
        new()
        {
            BeforeIndex = -1,
            AfterIndex = -1,
            IsEmpty = true,
        };

    internal static SparseCollectionItemState FromKeyed<TKey, TElement, TElementChange>(
        ISparseKeyedItemChange<TKey, TElement, TElementChange> item
    ) =>
        new()
        {
            Key = item.Key,
            TemporaryKey = item.TemporaryKey,
            Before = Box(item.Before),
            After = Box(item.After),
            BeforeIndex = item.BeforeIndex,
            AfterIndex = item.AfterIndex,
            IsAdded = item.IsAdded,
            IsRemoved = item.IsRemoved,
            IsEdited = item.IsEdited,
            IsReordered = item.IsReordered,
            IsEmpty = item.IsEmpty,
        };

    internal static SparseCollectionItemState FromDictionary<TKey, TValue>(
        ISparseDictionaryItemChange<TKey, TValue> entry
    ) =>
        new()
        {
            Key = entry.Key,
            Before = Box(entry.Before),
            After = Box(entry.After),
            BeforeIndex = -1,
            AfterIndex = -1,
            IsAdded = entry.IsAdded,
            IsRemoved = entry.IsRemoved,
            IsEdited = entry.IsEdited,
            IsReordered = false,
            IsEmpty = entry.IsEmpty,
        };

    private static Optional<object?> Box<T>(Optional<T> value) =>
        value.IsPresent ? Optional<object?>.Present(value.Value) : Optional<object?>.Missing;
}

/// <summary>
/// Point-in-time state view over one keyed or dictionary collection member.
/// </summary>
/// <remarks>
/// <para>
/// The view is built from the member's typed transition, so row states reuse
/// the canonical added/removed/edited/reordered semantics (including
/// collection-scoped temporary identities) instead of rederiving them. It
/// addresses a single collection member at a root-level member path; deeper
/// paths and non-collection members fail fast with
/// <see cref="ArgumentException"/>. Keyed sub-collections below a key segment
/// are out of scope for this view; navigate the change set to the nested
/// transition and query its item states instead.
/// </para>
/// <para>
/// Lifetime: the view snapshots the change set captured when it was created.
/// Row lookups against the view are cheap dictionary probes; creating the view
/// from a session diffs the live model once. The view retains its change set,
/// so dropping the view releases it.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct SparseCollectionState
{
    internal SparseCollectionState(
        SparsePath path,
        bool isEmpty,
        bool isChanged,
        bool orderChanged,
        Func<object?, SparseCollectionItemState> getItem,
        Func<Guid, SparseCollectionItemState> getTemporary
    )
    {
        Path = path;
        IsEmpty = isEmpty;
        IsChanged = isChanged;
        OrderChanged = orderChanged;
        _getItem = getItem;
        _getTemporary = getTemporary;
    }

    /// <summary>Gets the queried collection member path.</summary>
    public SparsePath Path { get; }

    /// <summary>Gets whether the collection contains no semantic changes.</summary>
    public bool IsEmpty { get; }

    /// <summary>Gets whether the collection contains any semantic changes.</summary>
    public bool IsChanged { get; }

    /// <summary>
    /// Gets whether the key order changed.
    /// </summary>
    /// <remarks>Keyed collections only; dictionary members always report false.</remarks>
    public bool OrderChanged { get; }

    /// <summary>
    /// Looks up the state for a stable key; never returns an uninitialized state.
    /// </summary>
    /// <param name="key">The stable key to look up.</param>
    /// <returns>The item state, or the empty state for unknown keys.</returns>
    /// <remarks>
    /// The key must be assignable to the collection key type. Null keys for
    /// value-type keys report the empty state; other invalid keys follow the
    /// underlying typed transition behavior.
    /// </remarks>
    public SparseCollectionItemState GetItemState(object? key) => _getItem(key);

    /// <summary>
    /// Looks up the state for a temporary identity; never returns an uninitialized state.
    /// </summary>
    /// <param name="temporaryKey">The collection-scoped temporary identity.</param>
    /// <returns>The item state, or the empty state for unknown identities.</returns>
    /// <remarks>
    /// Members whose element type opts out of temporary identity always
    /// report the empty state here.
    /// </remarks>
    public SparseCollectionItemState GetTemporaryState(Guid temporaryKey) =>
        _getTemporary(temporaryKey);

    private readonly Func<object?, SparseCollectionItemState> _getItem;
    private readonly Func<Guid, SparseCollectionItemState> _getTemporary;
}
