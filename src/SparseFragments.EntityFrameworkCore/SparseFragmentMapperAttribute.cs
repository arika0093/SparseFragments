namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Marks a mapper class as a SparseFragments EF Core entity mapping.
/// </summary>
/// <remarks>
/// Discovered by the ordinary <c>SparseFragments.Generator</c>, which emits an
/// external conventional-defaults companion for the
/// <see cref="IEntityModelMapping{TEntity, TDto}" /> pair. Neither the mapper
/// class nor <c>TEntity</c> needs to be partial. Explicit entries in
/// <c>Configure</c> replace each generated default exactly once.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SparseFragmentMapperAttribute : Attribute;
