using System.ComponentModel;

namespace SparseTypes;

/// <summary>Framework-neutral contract for path-first live collection-state queries.</summary>
/// <remarks>Generated sessions implement this interface only when the owning generator binds a session-capability contract and emits the state surface; hosts probe for it instead of reflecting over session members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithCollectionState
{
    /// <summary>Queries the keyed or dictionary collection state at a root collection path.</summary>
    /// <param name="collectionPath">The root collection member path.</param>
    /// <returns>The live collection state view.</returns>
    /// <exception cref="System.ArgumentException">The path does not address a root keyed or dictionary collection member.</exception>
    SparseCollectionState CollectionState(SparsePath collectionPath);
}
