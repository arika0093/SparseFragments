namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Marks a mapper class as a SparseFragments EF Core entity mapping.
/// </summary>
/// <remarks>
/// Informational in this proof of concept: mapper discovery is interface-based
/// (see <see cref="IEntityModelMapping{TEntity, TDto}" />), and the existing
/// <c>SparseFragments.Generator</c> does not yet emit mapping bodies.
/// A future generator may read this marker to emit conventional assignments;
/// the runtime plan built from <c>Configure</c> remains the validated source.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SparseFragmentMapperAttribute : Attribute;
