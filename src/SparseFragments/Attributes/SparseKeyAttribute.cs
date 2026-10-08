namespace SparseFragments;

/// <summary>Declares stable identity for keyed structural collections.</summary>
/// <remarks>
/// <para>
/// Exactly one key-definition mechanism may apply to a structural type: one property marked with
/// parameterless <c>[SparseKey]</c>, one type-level <c>[SparseKey(nameof(...), ...)]</c> composite
/// declaration, or one <see cref="ISparseKeyed{TKey}"/> implementation. Conflicting mechanisms
/// are a generator error.
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
/// A property-level key may opt into a non-null unassigned sentinel with the named
/// <see cref="Unassigned"/> property. Sentinel-valued elements in the after-state are
/// independent additions, never stable identities: order and payload round-trips preserve
/// each occurrence, while baselines and current keyed states reject sentinels.
/// <see cref="Unassigned"/> lookups are not stable; accepting a change set that would
/// promote sentinels into a baseline fails atomically. When a server assigns permanent
/// IDs, reload the authoritative model and create a fresh edit session instead of
/// correlating by sentinel, name, index, or equality.
/// </para>
/// <para>Composite keys are declared on the type with order-significant components:</para>
/// <code>
/// [SparseKey(nameof(TenantId), nameof(Id))]
/// public partial class Server
/// {
///     public Guid TenantId { get; set; }
///     public int Id { get; set; }
///     public string Host { get; set; } = "";
/// }
/// </code>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class SparseKeyAttribute : Attribute
{
    /// <summary>
    /// Optional key value reserved for elements whose identity has not yet been assigned.
    /// Property-level use only.
    /// </summary>
    public object? Unassigned { get; set; }

    /// <summary>Marks a property as the stable key. Property-level use only.</summary>
    public SparseKeyAttribute() { }

    /// <summary>Declares a composite key by property name, in key order. Type-level use only.</summary>
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
