using System.ComponentModel;

namespace SparseFragments;

/// <summary>The requested after-state vocabulary of a mixed member operation.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum MixedAfterKind
{
    /// <summary>The operation sets a concrete value.</summary>
    Value,

    /// <summary>The operation sets an explicit null where null is valid.</summary>
    Null,

    /// <summary>The operation removes or unassigns the member where removal is valid.</summary>
    Missing,
}
