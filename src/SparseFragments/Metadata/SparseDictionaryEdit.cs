namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes a single edited entry inside a dictionary contribution.
/// Instances are produced by generated patch inspection code; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDictionaryEdit
{
    /// <summary>Initializes a new instance of the <see cref="SparseDictionaryEdit"/> class.</summary>
    /// <param name="key">The edited entry key (boxed; may be null).</param>
    /// <param name="value">The assigned value (boxed; may be null). Only meaningful when <paramref name="hasNestedChanges"/> is <c>false</c>.</param>
    /// <param name="nestedChanges">The nested changes applied to the entry value. Only meaningful when <paramref name="hasNestedChanges"/> is <c>true</c>.</param>
    /// <param name="hasNestedChanges">Whether the edit carries nested changes instead of a plain assigned value.</param>
    public SparseDictionaryEdit(
        object? key,
        object? value,
        IReadOnlyList<SparsePatchChange> nestedChanges,
        bool hasNestedChanges
    )
    {
        ArgumentNullException.ThrowIfNull(nestedChanges);

        Key = key;
        Value = value;
        NestedChanges = Array.AsReadOnly(nestedChanges.ToArray());
        HasNestedChanges = hasNestedChanges;
    }

    /// <summary>Gets the edited entry key (boxed; may be null).</summary>
    public object? Key { get; }

    /// <summary>
    /// Gets the assigned value (boxed; may be null).
    /// Only meaningful when <see cref="HasNestedChanges"/> is <c>false</c>.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the nested changes applied to the entry value.
    /// Only meaningful when <see cref="HasNestedChanges"/> is <c>true</c>; otherwise empty.
    /// </summary>
    public IReadOnlyList<SparsePatchChange> NestedChanges { get; }

    /// <summary>Gets whether the edit carries nested changes instead of a plain assigned value.</summary>
    public bool HasNestedChanges { get; }
}
