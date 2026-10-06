namespace SparseFragments;

/// <summary>Declares stable identity for keyed structural collections.</summary>
/// <remarks>
/// <para>
/// SparseFragments uses its own compile-time key metadata, independent of persistence
/// frameworks. The source generator discovers this metadata and generates strongly typed
/// keyed-collection support from it. Key equality uses the normal equality semantics of
/// the key type (equivalent to <see cref="EqualityComparer{T}.Default"/>).
/// </para>
/// <para>Exactly one key-definition mechanism may apply to a structural type:</para>
/// <list type="bullet">
/// <item>one property marked with parameterless <c>[SparseKey]</c>;</item>
/// <item>one type-level <c>[SparseKey(nameof(...), ...)]</c> composite declaration;</item>
/// <item>one <see cref="ISparseKeyed{TKey}"/> implementation.</item>
/// </list>
/// <para>
/// There is no precedence between conflicting mechanisms: applying more than one
/// mechanism to the same type is a generator error. Multiple property-level
/// <c>[SparseKey]</c> attributes are not interpreted as a composite key; use the
/// type-level form instead.
/// </para>
/// <para>Single key property (the property type is the key type):</para>
/// <code>
/// public partial class Server
/// {
///     [SparseKey]
///     public Guid Id { get; set; }
///     public string Host { get; set; } = "";
/// }
/// </code>
/// <para>
/// A computed/read-only property is valid as long as it is publicly readable and
/// otherwise satisfies the key constraints:
/// </para>
/// <code>
/// public readonly record struct ServerKey(Guid TenantId, int Id);
/// public partial class Server
/// {
///     public Guid TenantId { get; set; }
///     public int Id { get; set; }
///     [SparseKey]
///     public ServerKey Key => new(TenantId, Id);
/// }
/// </code>
/// <para>
/// Composite keys are declared on the type with explicit, order-significant components:
/// </para>
/// <code>
/// [SparseKey(nameof(TenantId), nameof(Id))]
/// public partial class Server
/// {
///     public Guid TenantId { get; set; }
///     public int Id { get; set; }
///     public string Host { get; set; } = "";
/// }
/// </code>
/// <para>
/// The generated composite key is a strongly typed tuple of the component values in
/// declaration order, compared component-wise. Changing an element's identity through
/// an edit is semantically remove-old + add-new and must never silently retarget.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class SparseKeyAttribute : Attribute
{
    /// <summary>Marks a property as the stable key. Property-level use only.</summary>
    /// <remarks>
    /// Property-level <c>[SparseKey]</c> takes no constructor arguments. Applying it to
    /// a type, or passing property names to a property-level usage, is invalid.
    /// </remarks>
    public SparseKeyAttribute() { }

    /// <summary>Declares a composite key by property name, in key order.</summary>
    /// <remarks>
    /// Type-level use only; component order is significant and preserved by generated
    /// key equality and APIs. A parameterless type-level usage is invalid.
    /// </remarks>
    public SparseKeyAttribute(string propertyName, params string[] additionalPropertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(additionalPropertyNames);
        var names = new string[1 + additionalPropertyNames.Length];
        names[0] = propertyName;
        Array.Copy(additionalPropertyNames, 0, names, 1, additionalPropertyNames.Length);
        PropertyNames = names;
    }

    /// <summary>Type-level key property names in key order; empty for property-level usage.</summary>
    public string[] PropertyNames { get; } = Array.Empty<string>();
}
