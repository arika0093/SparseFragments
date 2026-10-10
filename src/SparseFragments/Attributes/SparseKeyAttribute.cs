namespace SparseFragments;

/// <summary>Declares stable identity for keyed structural collections.</summary>
/// <remarks>
/// <para>
/// Mark exactly one publicly readable, non-static, non-indexer property per
/// keyed element type with parameterless <c>[SparseKey]</c>. The property type
/// is the key type, compared with <c>EqualityComparer&lt;TKey&gt;.Default</c>.
/// Getter-only computed properties are accepted: they are extracted for
/// identity and never constructed or overlaid.
/// </para>
/// <code>
/// public partial class Server
/// {
///     [SparseKey]
///     public Guid Id { get; set; }
///     public string Host { get; set; } = "";
/// }
/// </code>
/// <para>Tuple and value-object keys use a computed key property:</para>
/// <code>
/// public partial class OrderLine
/// {
///     public string TenantId { get; set; } = "";
///     public int LineNumber { get; set; }
///
///     [SparseKey]
///     public (string TenantId, int LineNumber) Key => (TenantId, LineNumber);
/// }
/// </code>
/// <para>
/// A property-level key may opt into a non-null unassigned sentinel with the
/// named <see cref="Unassigned"/> property. Sentinel-valued elements in the
/// after-state are independent additions, never stable identities: order and
/// payload round-trips preserve each occurrence, while baselines and current
/// keyed states reject sentinels. <see cref="Unassigned"/> lookups are not
/// stable; accepting a change set that would promote sentinels into a baseline
/// fails atomically. When a server assigns permanent IDs, reload the
/// authoritative model and create a fresh edit session instead of correlating
/// by sentinel, name, index, or equality.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseKeyAttribute : Attribute
{
    /// <summary>
    /// Optional key value reserved for elements whose identity has not yet been assigned.
    /// </summary>
    public object? Unassigned { get; set; }

    /// <summary>Marks a property as the stable key.</summary>
    public SparseKeyAttribute() { }
}
