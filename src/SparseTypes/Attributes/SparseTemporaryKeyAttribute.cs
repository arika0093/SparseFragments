namespace SparseTypes;

/// <summary>Declares an opt-in temporary identity for keyed elements with unassigned permanent keys.</summary>
/// <remarks>Mark exactly one property per keyed element type with parameterless <c>[SparseTemporaryKey]</c>.</remarks>
/// <example>
/// <code>
/// public partial class OrderLineDto
/// {
///     public int Id { get; set; }
///     [SparseKey(Unassigned = 0)]
///     public int Key => Id;
///     [SparseTemporaryKey]
///     public Guid? TemporaryId { get; set; }
///     public string Name { get; set; } = "";
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseTemporaryKeyAttribute : Attribute
{
    /// <summary>Marks a property as the temporary identity.</summary>
    public SparseTemporaryKeyAttribute() { }
}
