using System.ComponentModel;

namespace SparseTypes;

/// <summary>Whether a mixed member operation carries its before-state.</summary>
/// <remarks>A transition carries a known before-state and stays baseline-aware.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum MixedHistoryKind
{
    /// <summary>The operation carries a known before-state.</summary>
    Transition,

    /// <summary>The operation carries no before-state and overwrites blindly.</summary>
    BlindSet,
}
