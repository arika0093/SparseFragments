namespace SparseFragments;

using System.ComponentModel;

/// <summary>Describes the concrete collection shape of a sparse model property.</summary>
/// <remarks>Advanced vocabulary: generic model inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseCollectionKind
{
    /// <summary>The property is not a collection.</summary>
    None,

    /// <summary>The property is a single-dimensional array.</summary>
    Array,

    /// <summary>The property is a list-style sequence.</summary>
    List,

    /// <summary>The property is a set with set-union merge semantics.</summary>
    Set,

    /// <summary>The property is a dictionary keyed by a key type.</summary>
    Dictionary,
}
