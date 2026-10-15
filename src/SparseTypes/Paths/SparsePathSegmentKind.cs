using System.ComponentModel;

namespace SparseTypes;

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

    /// <summary>A temporary-identity entry of a keyed collection.</summary>
    /// <remarks>Temporary segments address elements whose permanent key is unassigned through their stable <c>Guid</c> identity.</remarks>
    TemporaryKey = 3,
}
