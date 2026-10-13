using System.ComponentModel;
using SparseFragments.Generated;

namespace SparseFragments;

/// <summary>
/// Path-addressed typed editing facade for one session collection member.
/// </summary>
/// <remarks>
/// A thin typed front over the member's <see cref="IArrayDescriptor"/>: every
/// operation re-resolves the live descriptor from the session set, so a
/// replaced or nulled collection never acts on stale state, and every
/// mutation runs inside one <c>BatchEdit</c> so one user action raises one
/// transition notification. The facade keeps no collection snapshot and uses
/// no reflection. Reads return underlying models (never observable proxies);
/// bind controls to the session <c>Observable</c> view instead.
/// </remarks>
/// <typeparam name="TElement">The collection element model type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public class SparseCollectionEditor<TElement>
{
    private readonly IDescriptorSet _descriptors;
    private readonly Action<Action> _batch;
    private readonly string _memberName;

    /// <summary>Creates an editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="path">A single-member collection path (for example <c>Order.SparsePath.Items</c>).</param>
    /// <param name="batch">The session batch runner (for example <c>session.BatchEdit</c>).</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address one member.</exception>
    public SparseCollectionEditor(IDescriptorSet descriptors, SparsePath path, Action<Action> batch)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        if (path.Segments.Count != 1 || path.Segments[0].Kind != SparsePathSegmentKind.Member)
        {
            throw new ArgumentException(
                "The collection path must address exactly one member.",
                nameof(path)
            );
        }

        _descriptors = descriptors;
        Path = path;
        _batch = batch;
        _memberName = path.Segments[0].Name;
    }

    /// <summary>Gets the addressed collection path.</summary>
    public SparsePath Path { get; }

    /// <summary>Gets the live item count, or zero when the collection is unavailable.</summary>
    public int Count => TryResolveArray(out var array, out _) ? array.Count : 0;

    /// <summary>Gets whether items can currently be appended.</summary>
    public bool CanAdd => TryResolveArray(out var array, out _) && array.CanAdd;

    /// <summary>Gets whether items can currently be inserted.</summary>
    public bool CanInsert => TryResolveArray(out var array, out _) && array.CanInsert;

    /// <summary>Gets whether existing items can currently be replaced.</summary>
    public bool CanReplace => TryResolveArray(out var array, out _) && array.CanSetItem;

    /// <summary>Gets whether items can currently be removed.</summary>
    public bool CanRemove => TryResolveArray(out var array, out _) && array.CanRemove;

    /// <summary>Gets whether items can currently be moved.</summary>
    public bool CanMove => TryResolveArray(out var array, out _) && array.CanMove;

    /// <summary>Gets whether the collection carries stable keyed identity.</summary>
    public bool IsKeyed => TryResolveArray(out var array, out _) && array.IsKeyed;

    /// <summary>Gets the key type, or null for unkeyed or unavailable collections.</summary>
    public Type? KeyType => TryResolveArray(out var array, out _) ? array.KeyType : null;

    /// <summary>Gets the single key property name, or empty for unkeyed collections.</summary>
    public IReadOnlyList<string> KeyPropertyNames =>
        TryResolveArray(out var array, out _) ? array.KeyPropertyNames : Array.Empty<string>();

    /// <summary>Reports whether the collection is currently readable.</summary>
    /// <param name="error">The resolution failure, or <c>None</c> when readable.</param>
    /// <returns>True when the member resolves to a live sequence.</returns>
    /// <remarks>Immutable collections stay readable; only edits report <c>NotMutable</c>.</remarks>
    public bool IsAvailable(out SparseCollectionEditError error) =>
        TryResolveArray(out _, out error);

    /// <summary>Reads the underlying model at a position.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="value">The element model, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the element was read.</returns>
    public bool TryGetAt(int index, out TElement? value, out SparseCollectionEditError error)
    {
        value = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        // Models, not observable proxies: the descriptor view returns proxies
        // for fragment elements, while the model resolver unwraps them.
        var model = array.GetItemModel(index);
        if (model is null)
        {
            return true;
        }

        if (model is TElement typed)
        {
            value = typed;
            error = SparseCollectionEditError.None;
            return true;
        }

        error = SparseCollectionEditError.InvalidItem;
        return false;
    }

    /// <summary>Appends an item as one batched user action.</summary>
    /// <param name="item">The element model to append.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was appended.</returns>
    public bool TryAdd(TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (!array.CanAdd)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        Batched(() => accepted = array.TryAdd(item));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = DiagnoseMutationFailure(array.CanAdd, array, item);
        return false;
    }

    /// <summary>Inserts an item at a position as one batched user action.</summary>
    /// <param name="index">The insertion position; equal to <see cref="Count"/> appends.</param>
    /// <param name="item">The element model to insert.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was inserted.</returns>
    public bool TryInsert(int index, TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index > array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (!array.CanInsert)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedIndex = index;
        Batched(() => accepted = array.TryInsert(capturedIndex, item));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = DiagnoseMutationFailure(array.CanInsert, array, item);
        return false;
    }

    /// <summary>Replaces the item at a position as one batched user action.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="item">The replacement element model.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was replaced.</returns>
    public bool TryReplace(int index, TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (!array.CanSetItem)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedIndex = index;
        Batched(() => accepted = array.TrySetItem(capturedIndex, item));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = DiagnoseMutationFailure(array.CanSetItem, array, item);
        return false;
    }

    /// <summary>Removes the item at a position as one batched user action.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was removed.</returns>
    public bool TryRemoveAt(int index, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (!array.CanRemove)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedIndex = index;
        Batched(() => accepted = array.TryRemoveAt(capturedIndex));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = array.CanRemove
            ? SparseCollectionEditError.IndexOutOfRange
            : SparseCollectionEditError.NotMutable;
        return false;
    }

    /// <summary>Moves an item as one batched user action, replacing handwritten remove-then-insert code.</summary>
    /// <param name="oldIndex">The current zero-based position.</param>
    /// <param name="newIndex">The target zero-based position.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the item was moved.</returns>
    public bool TryMove(int oldIndex, int newIndex, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        if (oldIndex < 0 || oldIndex >= array.Count || newIndex < 0 || newIndex >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        if (!array.CanMove)
        {
            error = SparseCollectionEditError.NotMutable;
            return false;
        }

        var accepted = false;
        var capturedOld = oldIndex;
        var capturedNew = newIndex;
        Batched(() => accepted = array.TryMove(capturedOld, capturedNew));
        if (accepted)
        {
            error = SparseCollectionEditError.None;
            return true;
        }

        error = array.CanMove
            ? SparseCollectionEditError.IndexOutOfRange
            : SparseCollectionEditError.NotMutable;
        return false;
    }

    /// <summary>Runs one mutation through the session batch runner.</summary>
    /// <param name="mutation">The observable mutation to group.</param>
    protected void Batched(Action mutation) => _batch(mutation);

    /// <summary>Resolves the live sequence descriptor for one operation.</summary>
    /// <param name="array">The live descriptor, or null when unavailable.</param>
    /// <param name="error">The resolution failure, or <c>None</c> on success.</param>
    /// <returns>True when the member resolves to a live sequence.</returns>
    protected bool TryResolveArray(out IArrayDescriptor array, out SparseCollectionEditError error)
    {
        array = null!;
        if (!_descriptors.TryGet(_memberName, out var member) || member is null)
        {
            error = SparseCollectionEditError.UnknownCollection;
            return false;
        }

        var sequence = member.Array;
        if (sequence is null)
        {
            // A supported shape without a live accessor means the member is
            // null; an unsupported shape means the member is no sequence.
            error = member.Shape.HasArray
                ? SparseCollectionEditError.NullCollection
                : SparseCollectionEditError.NotACollection;
            return false;
        }

        array = sequence;
        error = SparseCollectionEditError.None;
        return true;
    }

    private static SparseCollectionEditError DiagnoseMutationFailure(
        bool canMutate,
        IArrayDescriptor array,
        TElement? item
    )
    {
        // The descriptor already validated indexes and rejected duplicates
        // atomically without notifying; only the cause is left to report.
        if (!canMutate)
        {
            return SparseCollectionEditError.NotMutable;
        }

        if (item is null)
        {
            return array.IsItemNullable
                ? SparseCollectionEditError.DuplicateKey
                : SparseCollectionEditError.InvalidItem;
        }

        if (!array.ItemType.IsInstanceOfType(item) && !array.ItemViewType.IsInstanceOfType(item))
        {
            return SparseCollectionEditError.InvalidItem;
        }

        return SparseCollectionEditError.DuplicateKey;
    }
}
