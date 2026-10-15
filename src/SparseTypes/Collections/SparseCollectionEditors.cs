using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>Entry points for the path-addressed collection editing facade.</summary>
/// <remarks>Uses generated descriptors to edit collection members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseCollectionEditors
{
    /// <summary>Opens a positional editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="collectionPath">A single-member collection path.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <returns>A facade bound to the addressed member.</returns>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address one member.</exception>
    public static SparseCollectionEditor<TElement> EditCollection<TElement>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        return new SparseCollectionEditor<TElement>(descriptors, collectionPath, batch);
    }

    /// <summary>Opens a keyed editing facade for one session collection member.</summary>
    /// <param name="descriptors">The session descriptor set, read live per operation.</param>
    /// <param name="collectionPath">A single-member collection path.</param>
    /// <param name="batch">The session batch runner.</param>
    /// <typeparam name="TElement">The collection element model type.</typeparam>
    /// <typeparam name="TKey">The assigned key type declared by <c>[SparseKey]</c>.</typeparam>
    /// <returns>A facade bound to the addressed member.</returns>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The path does not address one member.</exception>
    public static SparseKeyedCollectionEditor<TElement, TKey> EditKeyedCollection<TElement, TKey>(
        this IDescriptorSet descriptors,
        SparsePath collectionPath,
        Action<Action> batch
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(batch);
        return new SparseKeyedCollectionEditor<TElement, TKey>(descriptors, collectionPath, batch);
    }
}
