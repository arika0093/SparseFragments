using System.ComponentModel;

namespace SparseTypes;

/// <summary>Deterministic failure reasons for path-addressed collection edits.</summary>
/// <remarks>
/// Every <c>Try*</c> member of the collection editing facade reports one of
/// these values instead of a bare boolean, so UI clients can distinguish a
/// missing collection from a duplicate key without guessing. Values describe
/// the collection state at operation time; the facade holds no collection
/// snapshot, so a later retry may report a different value.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseCollectionEditError
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>The addressed member name is not a known member of the session model.</summary>
    UnknownCollection = 1,

    /// <summary>The addressed member exists but is not a sequence collection.</summary>
    NotACollection = 2,

    /// <summary>The addressed sequence member currently holds no collection instance.</summary>
    NullCollection = 3,

    /// <summary>The backing collection cannot be resized through the observable pipeline.</summary>
    NotMutable = 4,

    /// <summary>A positional index lies outside the current collection bounds.</summary>
    IndexOutOfRange = 5,

    /// <summary>An addition or replacement would duplicate an assigned key or temporary identity.</summary>
    DuplicateKey = 6,

    /// <summary>No current item carries the requested assigned key.</summary>
    UnknownKey = 7,

    /// <summary>An unassigned sentinel or missing temporary identity was used as durable identity.</summary>
    UnassignedKey = 8,

    /// <summary>No current item carries the requested temporary identity.</summary>
    UnknownTemporaryKey = 9,

    /// <summary>The supplied item cannot be converted to the collection element type.</summary>
    InvalidItem = 10,
}
