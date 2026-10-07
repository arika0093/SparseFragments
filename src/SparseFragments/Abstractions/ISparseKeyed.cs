namespace SparseFragments;

/// <summary>Advanced escape hatch for computed/custom structural identity.</summary>
/// <remarks>
/// <para>
/// Implement this interface when identity cannot be expressed as a single key property
/// or an ordered type-level composite key. The generator discovers the implementation
/// and extracts keys without runtime reflection via the <see cref="SparseKey"/> getter.
/// </para>
/// <code>
/// public partial class Server : ISparseKeyed&lt;ServerKey&gt;
/// {
///     public string Tenant { get; set; } = "";
///     public int Id { get; set; }
///     public ServerKey SparseKey => new(Tenant.ToUpperInvariant(), Id);
/// }
/// </code>
/// <para>
/// Exactly one key-definition mechanism may apply to a structural type: implementing
/// this interface conflicts with any <see cref="SparseKeyAttribute"/> declaration on
/// the same type.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The stable key type.</typeparam>
public interface ISparseKeyed<out TKey>
{
    /// <summary>Gets the stable key of this instance.</summary>
    TKey SparseKey { get; }
}
