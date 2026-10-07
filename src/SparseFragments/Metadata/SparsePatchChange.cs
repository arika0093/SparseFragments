namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes a single property contribution inside a sparse patch.
/// Instances are produced by generated patch inspection code; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparsePatchChange
{
    /// <summary>Initializes a new instance of the <see cref="SparsePatchChange"/> class.</summary>
    /// <param name="property">The described property.</param>
    /// <param name="kind">How the property is changed.</param>
    /// <param name="value">
    /// The assigned value (boxed; may be null for SetNull). Only meaningful when
    /// <paramref name="kind"/> is <see cref="SparseChangeKind.Set"/>; otherwise <c>null</c>.
    /// </param>
    /// <param name="nestedChanges">
    /// The nested changes. Only meaningful when <paramref name="kind"/> is
    /// <see cref="SparseChangeKind.Nested"/>; otherwise empty. A <c>null</c> value is
    /// treated as empty.
    /// </param>
    /// <param name="keyed">
    /// The keyed collection inspection. Only meaningful when <paramref name="kind"/> is
    /// <see cref="SparseChangeKind.KeyedCollection"/>; otherwise <c>null</c>.
    /// </param>
    /// <param name="dictionary">
    /// The dictionary inspection. Only meaningful when <paramref name="kind"/> is
    /// <see cref="SparseChangeKind.Dictionary"/>; otherwise <c>null</c>.
    /// </param>
    public SparsePatchChange(
        SparsePropertyInfo property,
        SparseChangeKind kind,
        object? value,
        IReadOnlyList<SparsePatchChange>? nestedChanges,
        SparseKeyedCollectionInspection? keyed,
        SparseDictionaryInspection? dictionary
    )
    {
        ArgumentNullException.ThrowIfNull(property);

        Property = property;
        Kind = kind;
        Value = value;
        NestedChanges = Array.AsReadOnly(
            nestedChanges?.ToArray() ?? Array.Empty<SparsePatchChange>()
        );
        Keyed = keyed;
        Dictionary = dictionary;
    }

    /// <summary>Gets the described property.</summary>
    public SparsePropertyInfo Property { get; }

    /// <summary>Gets how the property is changed.</summary>
    public SparseChangeKind Kind { get; }

    /// <summary>
    /// Gets the assigned value (boxed; may be null for SetNull).
    /// Only meaningful when <see cref="Kind"/> is <see cref="SparseChangeKind.Set"/>;
    /// otherwise <c>null</c>.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Gets the nested changes. Only meaningful when <see cref="Kind"/> is
    /// <see cref="SparseChangeKind.Nested"/>; otherwise empty.
    /// </summary>
    public IReadOnlyList<SparsePatchChange> NestedChanges { get; }

    /// <summary>
    /// Gets the keyed collection inspection. Only meaningful when <see cref="Kind"/> is
    /// <see cref="SparseChangeKind.KeyedCollection"/>; otherwise <c>null</c>.
    /// </summary>
    public SparseKeyedCollectionInspection? Keyed { get; }

    /// <summary>
    /// Gets the dictionary inspection. Only meaningful when <see cref="Kind"/> is
    /// <see cref="SparseChangeKind.Dictionary"/>; otherwise <c>null</c>.
    /// </summary>
    public SparseDictionaryInspection? Dictionary { get; }
}
