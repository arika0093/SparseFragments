namespace SparseFragments;

using System.ComponentModel;

/// <summary>Describes how a single sparse patch contribution changes a property.</summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseChangeKind
{
    /// <summary>The property is assigned a value (boxed in <c>Value</c>; may be null for SetNull).</summary>
    Set,

    /// <summary>The property presence is removed.</summary>
    Unset,

    /// <summary>A nested sparse model is partially changed; see <c>NestedChanges</c>.</summary>
    Nested,

    /// <summary>A keyed collection is changed; see <c>Keyed</c>.</summary>
    KeyedCollection,

    /// <summary>A dictionary is changed; see <c>Dictionary</c>.</summary>
    Dictionary,
}
