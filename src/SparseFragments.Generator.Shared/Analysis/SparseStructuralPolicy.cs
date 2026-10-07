namespace SparseFragments.Generator.Shared;

/// <summary>Product-neutral structural-type policy selected by the owning generator.</summary>
/// <remarks>
/// <see cref="AtomicReplace"/> treats non-partial nested POCOs as atomic values;
/// <see cref="StructuralHosts"/> generates structural hosts for class-like nested POCOs.
/// </remarks>
internal enum SparseStructuralPolicy
{
    AtomicReplace = 0,
    StructuralHosts = 1,
}
