namespace SparseFragments;

using System.ComponentModel;

/// <summary>Describes how a sparse model property participates in structural merge and keying.</summary>
/// <remarks>Advanced vocabulary: generic model inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseCollectionSemantic
{
    /// <summary>No collection semantics apply.</summary>
    None,

    /// <summary>An ordered sequence merged without stable element identity.</summary>
    ScalarSequence,

    /// <summary>A sequence whose elements carry stable identity for keyed merge.</summary>
    KeyedSequence,

    /// <summary>A dictionary keyed by a key type.</summary>
    Dictionary,
}
