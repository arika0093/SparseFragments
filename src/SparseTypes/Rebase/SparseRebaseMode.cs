using System.ComponentModel;

namespace SparseTypes;

/// <summary>How a rebase reconciles one member when no explicit policy applies.</summary>
/// <remarks>This mode never replaces member-level configuration silently.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseRebaseMode
{
    /// <summary>Use member-level configuration, else the built-in three-way reconciliation.</summary>
    Default = 0,

    /// <summary>Report a conflict when the current value matches neither the edit base nor the desired value.</summary>
    FailOnConflict = 1,

    /// <summary>On divergence keep the desired (incoming) value without reporting a conflict.</summary>
    PreferIncoming = 2,

    /// <summary>On divergence keep the current value without reporting a conflict.</summary>
    PreferCurrent = 3,
}
