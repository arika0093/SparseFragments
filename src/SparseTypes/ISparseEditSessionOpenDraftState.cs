using System.ComponentModel;

namespace SparseTypes;

/// <summary>Reports whether an edit session has any open child drafts.</summary>
/// <remarks>
/// Generated edit sessions expose this state so framework coordinators can
/// determine whether edits remain isolated in open scopes.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionOpenDraftState
{
    /// <summary>Gets whether this session or one of its open scopes has an open draft.</summary>
    bool HasOpenDrafts { get; }
}
