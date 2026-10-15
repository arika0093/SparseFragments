using System.ComponentModel;

namespace SparseTypes;

/// <summary>Framework-neutral contract for grouping observable edits into one transition notification.</summary>
/// <remarks>Generated sessions implement this interface only when the owning generator binds a session-capability contract; hosts probe for it instead of reflecting over session members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithBatchEdit
{
    /// <summary>Runs observable edits as one transition notification, including nested batches.</summary>
    /// <param name="edit">The observable edits to run.</param>
    void BatchEdit(System.Action edit);
}
