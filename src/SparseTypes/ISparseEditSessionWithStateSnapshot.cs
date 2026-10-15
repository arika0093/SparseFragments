using System.ComponentModel;

namespace SparseTypes;

/// <summary>Framework-neutral contract for capturing one reusable state-query snapshot.</summary>
/// <remarks>Generated sessions implement this interface only when the owning generator binds a session-capability contract and emits the snapshot surface; hosts probe for it instead of reflecting over session members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithStateSnapshot
{
    /// <summary>Captures one reusable state-query snapshot over the pending changes.</summary>
    /// <returns>A view serving repeated field queries without re-deriving the diff.</returns>
    /// <remarks>Snapshot: derives one change set from the retained baseline and retains it for repeated point-in-time lookups.</remarks>
    SparseChangeSetQueryView CaptureStateSnapshot();
}
