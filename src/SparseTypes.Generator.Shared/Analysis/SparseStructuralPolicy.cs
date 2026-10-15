namespace SparseTypes.Generator.Shared;

/// <summary>Product-neutral structural-type policy selected by the owning generator.</summary>
/// <remarks><see cref="AtomicReplace"/> treats non-partial nested POCOs as atomic values.</remarks>
internal enum SparseStructuralPolicy
{
    AtomicReplace = 0,
    StructuralHosts = 1,
}
