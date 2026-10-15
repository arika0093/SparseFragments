using System.ComponentModel;

namespace SparseTypes;

/// <summary>How redacted-before operations apply during mixed requests.</summary>
/// <remarks>The default is pass-through.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum RedactedBeforePolicy
{
    /// <summary>Apply the requested after-state without baseline comparison.</summary>
    Passthrough = 0,
}
