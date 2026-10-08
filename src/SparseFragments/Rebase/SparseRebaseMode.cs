using System.ComponentModel;

namespace SparseFragments;

/// <summary>How a rebase reconciles one member when no explicit policy applies.</summary>
/// <remarks>
/// This mode never replaces member-level configuration silently: an explicit
/// <see cref="FragmentRebasePolicy{T}"/> wins first, then
/// <see cref="FragmentMergeStrategy{T}.TryRebase"/>, and only then this mode.
/// <see cref="Default"/> keeps the built-in three-way reconciliation.
/// <see cref="PreferIncoming"/> and <see cref="PreferCurrent"/> resolve
/// divergence by overwriting; they are last-write-wins shortcuts, not
/// conflict-free reconciliations, and generated documentation says so.
/// </remarks>
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
