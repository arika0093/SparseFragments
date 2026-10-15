using System.Collections.Generic;
using System.ComponentModel;

namespace SparseTypes;

/// <summary>A typed edit session with the generic operations required by edit-session groups.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TChangeSet">The generated immutable change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithChangeSet<TModel, TChangeSet>
    : ISparseEditSession<TModel, TChangeSet>
    where TModel : class
    where TChangeSet : class
{
    /// <summary>Prepares a sendable snapshot of the pending changes.</summary>
    /// <remarks>Sessions with child drafts may accept them while preparing the snapshot.</remarks>
    TChangeSet PrepareChangeSet();

    /// <summary>Gets whether a captured change set is empty.</summary>
    /// <param name="changes">The change set to inspect.</param>
    bool IsEmpty(TChangeSet changes);

    /// <summary>Gets the typed paths changed by a captured change set.</summary>
    /// <param name="changes">The change set to inspect.</param>
    IReadOnlyList<SparsePath> EnumerateChangedPaths(TChangeSet changes);
}
