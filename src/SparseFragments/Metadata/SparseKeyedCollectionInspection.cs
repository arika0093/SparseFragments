namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes how a sparse patch contribution changes a keyed collection property.
/// Instances are produced by generated patch inspection code; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseKeyedCollectionInspection
{
    /// <summary>Initializes a new instance of the <see cref="SparseKeyedCollectionInspection"/> class.</summary>
    /// <param name="added">The added elements (boxed).</param>
    /// <param name="removedKeys">The removed element keys (boxed).</param>
    /// <param name="edited">The edited elements with their nested changes.</param>
    /// <param name="hasOrder">Whether the contribution carries an explicit key order.</param>
    /// <param name="keyOrder">The explicit key order (boxed keys); empty when <paramref name="hasOrder"/> is <c>false</c>.</param>
    public SparseKeyedCollectionInspection(
        IReadOnlyList<object?> added,
        IReadOnlyList<object?> removedKeys,
        IReadOnlyList<SparseKeyedEdit> edited,
        bool hasOrder,
        IReadOnlyList<object?> keyOrder
    )
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(removedKeys);
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(keyOrder);

        Added = Array.AsReadOnly(added.ToArray());
        RemovedKeys = Array.AsReadOnly(removedKeys.ToArray());
        Edited = Array.AsReadOnly(edited.ToArray());
        HasOrder = hasOrder;
        KeyOrder = Array.AsReadOnly(keyOrder.ToArray());
    }

    /// <summary>Gets the added elements (boxed).</summary>
    public IReadOnlyList<object?> Added { get; }

    /// <summary>Gets the removed element keys (boxed).</summary>
    public IReadOnlyList<object?> RemovedKeys { get; }

    /// <summary>Gets the edited elements with their nested changes.</summary>
    public IReadOnlyList<SparseKeyedEdit> Edited { get; }

    /// <summary>Gets whether the contribution carries an explicit key order.</summary>
    public bool HasOrder { get; }

    /// <summary>Gets the explicit key order (boxed keys); empty when <see cref="HasOrder"/> is <c>false</c>.</summary>
    public IReadOnlyList<object?> KeyOrder { get; }
}
