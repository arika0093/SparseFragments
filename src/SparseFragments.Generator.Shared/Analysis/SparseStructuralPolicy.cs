namespace SparseFragments.Generator.Shared;

/// <summary>
/// Product-neutral structural-type policy selected by the owning generator.
/// </summary>
/// <remarks>
/// The analysis mechanics (structural classification, POCO clone discovery,
/// promoted-partial discovery, reachable-type traversal) are shared; only the
/// semantic treatment of non-partial nested POCOs differs per product:
/// <list type="bullet">
/// <item>
/// <see cref="AtomicReplace"/> treats non-partial nested POCOs as atomic replace
/// values: no structural hosts are generated and independently sparse/deep
/// behavior requires a partial type (promoted) or an explicit fragment model.
/// </item>
/// <item>
/// <see cref="StructuralHosts"/> generates structural hosts for class-like
/// nested POCOs so downstream generators can offer deep behavior without
/// requiring nested partials.
/// </item>
/// </list>
/// Generator-specific metadata (schema IDs, environment/secret metadata, type
/// names, registry/facade concerns) stays outside this policy and composes with
/// the shared model.
/// </remarks>
internal enum SparseStructuralPolicy
{
    AtomicReplace = 0,
    StructuralHosts = 1,
}
