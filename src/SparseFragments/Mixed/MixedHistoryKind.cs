using System.ComponentModel;

namespace SparseFragments;

/// <summary>Whether a mixed member operation carries its before-state.</summary>
/// <remarks>
/// A transition carries a known before-state and stays baseline-aware. A blind set
/// carries only the requested after-state because the before-state was redacted or
/// otherwise unavailable (issue #119). The two kinds share a transport envelope
/// but give different information guarantees.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum MixedHistoryKind
{
    /// <summary>The operation carries a known before-state.</summary>
    Transition,

    /// <summary>The operation carries no before-state and overwrites blindly.</summary>
    BlindSet,
}
