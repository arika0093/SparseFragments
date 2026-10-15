using System.ComponentModel;

namespace SparseTypes;

/// <summary>Describes whether a child edit session is registered for a typed path.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseChildSessionStatus
{
    /// <summary>No child session is registered for the path.</summary>
    NotOpen,

    /// <summary>A child session is registered but has no pending changes.</summary>
    OpenUnchanged,

    /// <summary>A child session or one of its descendants has pending changes.</summary>
    OpenChanged,

    /// <summary>A child session is registered but its path no longer resolves to a model.</summary>
    /// <remarks>The draft is retained with its pending edits until the owner discards, accepts after the target returns, or disposes it.</remarks>
    TargetMissing,
}
