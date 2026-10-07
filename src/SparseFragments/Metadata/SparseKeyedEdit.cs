namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes a single edited element inside a keyed collection contribution.
/// Instances are produced by generated patch inspection code; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseKeyedEdit
{
    /// <summary>Initializes a new instance of the <see cref="SparseKeyedEdit"/> class.</summary>
    /// <param name="key">The edited element key (boxed; may be null).</param>
    /// <param name="nestedChanges">The nested changes applied to the element.</param>
    public SparseKeyedEdit(object? key, IReadOnlyList<SparsePatchChange> nestedChanges)
    {
        ArgumentNullException.ThrowIfNull(nestedChanges);

        Key = key;
        NestedChanges = Array.AsReadOnly(nestedChanges.ToArray());
    }

    /// <summary>Gets the edited element key (boxed; may be null).</summary>
    public object? Key { get; }

    /// <summary>Gets the nested changes applied to the element.</summary>
    public IReadOnlyList<SparsePatchChange> NestedChanges { get; }
}
