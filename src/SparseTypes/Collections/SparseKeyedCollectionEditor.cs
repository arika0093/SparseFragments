using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>
/// Path-addressed typed editing facade for one keyed session collection member.
/// </summary>
/// <remarks>Adds stable-identity operations over <see cref="SparseCollectionEditor{TElement}"/>.</remarks>
/// <typeparam name="TElement">The collection element model type.</typeparam>
/// <typeparam name="TKey">The assigned key type declared by <c>[SparseKey]</c>.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseKeyedCollectionEditor<TElement, TKey> : SparseCollectionEditor<TElement>
{
    /// <summary>Creates a keyed editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="path">A single-member collection path (for example <c>Order.SparsePath.Items</c>).</param>
    /// <param name="batch">The session batch runner (for example <c>session.BatchEdit</c>).</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address one member.</exception>
    public SparseKeyedCollectionEditor(
        IDescriptorSet descriptors,
        SparsePath path,
        Action<Action> batch
    )
        : base(descriptors, path, batch) { }

    /// <summary>Finds the current position of an assigned key.</summary>
    /// <param name="key">The assigned key to locate.</param>
    /// <returns>The position, or -1 for unassigned, unknown, or unkeyed collections.</returns>
    /// <remarks>Unassigned sentinels never resolve; check <see cref="IsUnassignedKey"/> first when needed.</remarks>
    public int IndexOfKey(TKey? key)
    {
        if (!TryResolveArray(out var array, out _) || !IsUsableKeyedArray(array))
        {
            return -1;
        }

        if (key is null || array.IsUnassignedKey(key))
        {
            return -1;
        }

        return array.IndexOfKey(key);
    }

    /// <summary>Finds the current position of a temporary identity.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <returns>The position, or -1 when absent or unsupported.</returns>
    public int IndexOfTemporaryKey(Guid temporaryKey)
    {
        if (!TryResolveArray(out var array, out _) || temporaryKey == Guid.Empty)
        {
            return -1;
        }

        return array.IndexOfTemporaryKey(temporaryKey);
    }

    /// <summary>Determines whether a key is the configured unassigned marker.</summary>
    /// <param name="key">The key to test.</param>
    /// <returns>True when the key marks rows without durable identity.</returns>
    public bool IsUnassignedKey(TKey? key)
    {
        if (!TryResolveArray(out var array, out _))
        {
            return false;
        }

        return array.IsUnassignedKey(key);
    }

    /// <summary>Determines whether an assigned key currently has a row.</summary>
    /// <param name="key">The assigned key to test.</param>
    /// <returns>True when exactly the durable identity resolves; unassigned keys never count.</returns>
    public bool ContainsKey(TKey? key) => IndexOfKey(key) >= 0;

    /// <summary>Reads the assigned key of the row at a position, for UI row keys.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <param name="key">The row key, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the key was read.</returns>
    public bool TryGetKeyAt(int index, out TKey? key, out SparseCollectionEditError error)
    {
        key = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.IndexOutOfRange;
            return false;
        }

        var raw = array.GetItemKey(index);
        if (raw is null)
        {
            // A null row key carries no durable identity; callers interpret it
            // through IsUnassignedKey rather than treating it as a lookup key.
            error = SparseCollectionEditError.None;
            return true;
        }

        if (raw is TKey typed)
        {
            key = typed;
            error = SparseCollectionEditError.None;
            return true;
        }

        error = SparseCollectionEditError.InvalidItem;
        return false;
    }

    /// <summary>Reads a row by its assigned key through the current order.</summary>
    /// <param name="key">The assigned key to locate.</param>
    /// <param name="value">The element model, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was read.</returns>
    public bool TryGetByKey(TKey? key, out TElement? value, out SparseCollectionEditError error)
    {
        value = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        return TryGetAt(index, out value, out error);
    }

    /// <summary>Reads a pending row by its temporary identity.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <param name="value">The element model, or default on failure.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was read.</returns>
    public bool TryGetByTemporaryKey(
        Guid temporaryKey,
        out TElement? value,
        out SparseCollectionEditError error
    )
    {
        value = default;
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (temporaryKey == Guid.Empty)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        var index = array.IndexOfTemporaryKey(temporaryKey);
        if (index < 0 || index >= array.Count)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        return TryGetAt(index, out value, out error);
    }

    /// <summary>Removes the row with an assigned key as one batched user action.</summary>
    /// <param name="key">The assigned key to remove.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was removed.</returns>
    public bool TryRemoveByKey(TKey? key, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
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
            ? SparseCollectionEditError.UnknownKey
            : SparseCollectionEditError.NotMutable;
        return false;
    }

    /// <summary>Removes a pending row by its temporary identity as one batched user action.</summary>
    /// <param name="temporaryKey">The stable temporary identity.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was removed.</returns>
    public bool TryRemoveByTemporaryKey(Guid temporaryKey, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (temporaryKey == Guid.Empty)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        var index = array.IndexOfTemporaryKey(temporaryKey);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownTemporaryKey;
            return false;
        }

        return TryRemoveAt(index, out error);
    }

    /// <summary>Replaces the row with an assigned key as one batched user action.</summary>
    /// <param name="key">The assigned key to replace.</param>
    /// <param name="item">The replacement element model.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was replaced.</returns>
    /// <remarks>Changing the key is remove plus add.</remarks>
    public bool TryReplaceByKey(TKey? key, TElement? item, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        return TryReplace(index, item, out error);
    }

    /// <summary>Moves the row with an assigned key as one batched user action.</summary>
    /// <param name="key">The assigned key to move.</param>
    /// <param name="newIndex">The target zero-based position.</param>
    /// <param name="error">The failure reason, or <c>None</c> on success.</param>
    /// <returns>True when the row was moved.</returns>
    public bool TryMoveByKey(TKey? key, int newIndex, out SparseCollectionEditError error)
    {
        if (!TryResolveArray(out var array, out error))
        {
            return false;
        }

        RequireUsableKey(array);
        if (key is null || array.IsUnassignedKey(key))
        {
            error = SparseCollectionEditError.UnassignedKey;
            return false;
        }

        var index = array.IndexOfKey(key);
        if (index < 0)
        {
            error = SparseCollectionEditError.UnknownKey;
            return false;
        }

        // Reuse the positional move so index validation stays in one place;
        // the lookup above already pinned identity to the current order.
        return TryMove(index, newIndex, out error);
    }

    private static bool IsUsableKeyedArray(IArrayDescriptor array) =>
        array.IsKeyed && array.KeyType == typeof(TKey);

    private static void RequireUsableKey(IArrayDescriptor array)
    {
        if (!array.IsKeyed)
        {
            throw new InvalidOperationException(
                "The addressed collection carries no stable keyed identity."
            );
        }

        if (array.KeyType != typeof(TKey))
        {
            throw new InvalidOperationException(
                "The keyed editor key type '"
                    + typeof(TKey)
                    + "' does not match the collection key type '"
                    + (array.KeyType?.ToString() ?? "<unknown>")
                    + "'."
            );
        }
    }
}
