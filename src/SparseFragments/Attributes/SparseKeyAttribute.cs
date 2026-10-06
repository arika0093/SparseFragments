namespace SparseFragments;

/// <summary>Declares stable identity for keyed structural collections.</summary>
/// <remarks>
/// <para>
/// A structural sequence such as <c>List&lt;Server&gt;</c> participates in granular
/// patching only when its element type has a stable key. Scalar sequences
/// (<c>List&lt;string&gt;</c>, <c>int[]</c>, …) remain atomic and must not declare keys.
/// </para>
/// <para>Property-level: annotate one or more properties of the element type.</para>
/// <code>
/// public partial class Server
/// {
///     [SparseKey] public string Id { get; set; } = string.Empty;
/// }
/// </code>
/// <para>
/// Composite keys: annotate several properties. Ordering is <see cref="Order"/>
/// then property name. Alternatively declare model-level keys:
/// </para>
/// <code>
/// [SparseKey("TenantId", "Id")]
/// public partial class Server { … }
/// </code>
/// <para>
/// Model-level declarations win when present; property-level attributes are then
/// ignored. Key property types must be scalar (no collections or structural
/// objects). Changing an element's identity through an edit is semantically
/// remove-old + add-new and must never silently retarget.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class SparseKeyAttribute : Attribute
{
    /// <summary>Marks a property as (part of) the stable key.</summary>
    public SparseKeyAttribute() { }

    /// <summary>Marks a property as (part of) the stable key with explicit ordering.</summary>
    public SparseKeyAttribute(int order)
    {
        Order = order;
    }

    /// <summary>Declares model-level keys by property name, in key order.</summary>
    public SparseKeyAttribute(string propertyName, params string[] additionalPropertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(additionalPropertyNames);
        var names = new string[1 + additionalPropertyNames.Length];
        names[0] = propertyName;
        Array.Copy(additionalPropertyNames, 0, names, 1, additionalPropertyNames.Length);
        PropertyNames = names;
    }

    /// <summary>Ordering among composite key properties (lower first).</summary>
    public int Order { get; set; }

    /// <summary>Model-level key property names in key order; empty for property-level usage.</summary>
    public string[] PropertyNames { get; } = Array.Empty<string>();
}
