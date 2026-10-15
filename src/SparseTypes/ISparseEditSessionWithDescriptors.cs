using System.ComponentModel;

namespace SparseTypes;

/// <summary>Framework-neutral contract for descriptors bound to a session's observable model.</summary>
/// <remarks>Generated sessions implement this interface only when the owning generator binds a session-capability contract and emits descriptors; hosts probe for it instead of reflecting over session members.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseEditSessionWithDescriptors
{
    /// <summary>Gets descriptors bound to this session's observable model.</summary>
    Generated.IDescriptorSet Descriptors { get; }
}
