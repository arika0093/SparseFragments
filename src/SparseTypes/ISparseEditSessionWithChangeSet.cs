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
    /// <summary>Creates a change set describing the pending changes without mutating the session.</summary>
    /// <remarks>Sessions with child drafts include pending descendants as a pure preview; accepting stays explicit.</remarks>
    TChangeSet CreateChangeSet();

    /// <summary>Gets whether a captured change set is empty.</summary>
    /// <param name="changes">The change set to inspect.</param>
    bool IsEmpty(TChangeSet changes);

    /// <summary>Gets the typed paths changed by a captured change set.</summary>
    /// <param name="changes">The change set to inspect.</param>
    IReadOnlyList<SparsePath> EnumerateChangedPaths(TChangeSet changes);
}

/// <summary>A typed edit session that reports committed baseline revisions.</summary>
/// <remarks>The revision advances when an operation commits a new baseline, before change notifications are raised.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithBaselineRevision
{
    /// <summary>Gets the revision of the retained baseline.</summary>
    long BaselineRevision { get; }
}
