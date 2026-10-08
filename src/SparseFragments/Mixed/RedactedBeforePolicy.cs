using System.ComponentModel;

namespace SparseFragments;

/// <summary>How redacted-before operations apply during mixed requests.</summary>
/// <remarks>
/// The default is pass-through: a redacted-before operation applies its requested
/// after-state without historical comparison or three-way rebase, like an explicit
/// patch set. A strict failure option for redacted-before changes belongs to the
/// rebase-policy follow-up (issue #120); until then this policy stays a seam that
/// future values extend, and unknown values fail closed.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum RedactedBeforePolicy
{
    /// <summary>Apply the requested after-state without baseline comparison.</summary>
    Passthrough = 0,
}
