namespace SparseFragments;

/// <summary>Declares an opt-in temporary identity for keyed elements with unassigned permanent keys.</summary>
/// <remarks>
/// <para>
/// Mark exactly one property per keyed element type with parameterless
/// <c>[SparseTemporaryKey]</c>. The property type must be <c>Guid?</c> and the
/// element type must also declare exactly one property-level <c>[SparseKey]</c>.
/// </para>
/// <para>
/// A valid assigned permanent key always wins: the temporary value never
/// overrides assigned-key identity, equality, or diffing. When the permanent
/// key is unassigned, a non-null, non-empty temporary GUID acts as the stable
/// element identity within its keyed collection. Do not assign an explicit
/// property initializer; assign temporary values in constructors or object
/// initializers instead.
/// </para>
/// </remarks>
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
