namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// Marks a partial mapper class as a SparseTypes EF Core entity mapping for one entity/DTO pair.
/// </summary>
/// <remarks>
/// The attribute is the single authoritative Entity/DTO declaration. The ordinary
/// <c>SparseTypes.Generator</c> reads its type arguments, emits an external
/// conventional-defaults companion for the pair, and generates the typed
/// <see cref="IEntityModelMapping{TEntity, TDto}" /> implementation plus a
/// strongly typed <c>Map</c> member backed by the builder active for the
/// current plan build. Neither the mapper class nor <c>TEntity</c> needs extra
/// declarations: an optional parameterless <c>Configure()</c> uses
/// <c>Map</c>, and explicit entries replace each generated default exactly
/// once. The mapper and every enclosing type that must be augmented have to be
/// <c>partial</c>.
/// </remarks>
/// <typeparam name="TEntity">The tracked EF Core entity type.</typeparam>
/// <typeparam name="TDto">The DTO (fragment model) type carrying the change set.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SparseFragmentMapperAttribute<TEntity, TDto> : Attribute
    where TEntity : class
    where TDto : class
{
    /// <summary>Gets the tracked EF Core entity type.</summary>
    public Type EntityType => typeof(TEntity);

    /// <summary>Gets the DTO (fragment model) type carrying the change set.</summary>
    public Type DtoType => typeof(TDto);
}
