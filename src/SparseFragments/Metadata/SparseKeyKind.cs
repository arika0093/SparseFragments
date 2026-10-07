namespace SparseFragments;

using System.ComponentModel;

/// <summary>Describes how the stable identity of a keyed collection element is declared.</summary>
/// <remarks>Advanced vocabulary: generic model inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseKeyKind
{
    /// <summary>The property has no key metadata.</summary>
    None,

    /// <summary>Identity comes from a single property marked with parameterless <c>[SparseKey]</c>.</summary>
    Property,

    /// <summary>Identity comes from a type-level composite <c>[SparseKey(...)]</c> declaration.</summary>
    Composite,

    /// <summary>Identity comes from an <see cref="ISparseKeyed{TKey}"/> implementation.</summary>
    Interface,
}
