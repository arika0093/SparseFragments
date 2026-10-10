using System.ComponentModel;

namespace SparseFragments;

/// <summary>Classifies one segment of a <see cref="SparsePath"/>.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparsePathSegmentKind
{
    /// <summary>A named model member.</summary>
    Member = 0,

    /// <summary>A keyed collection or dictionary entry with a typed key.</summary>
    Key = 1,

    /// <summary>A positional collection element.</summary>
    Index = 2,
}
