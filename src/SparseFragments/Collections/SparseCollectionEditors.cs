using System.ComponentModel;
using SparseFragments.Generated;

namespace SparseFragments;

/// <summary>Entry points for the path-addressed collection editing facade.</summary>
/// <remarks>
/// The descriptor set (for example <c>session.Descriptors</c>) supplies live
/// resolution while the batch runner (for example <c>session.BatchEdit</c>)
/// groups each operation into one transition notification. Generated edit
/// sessions add a session-rooted <c>EditCollection</c> overload that forwards
/// here, so handwritten code keeps the same behavior without regeneration.
/// </remarks>
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
