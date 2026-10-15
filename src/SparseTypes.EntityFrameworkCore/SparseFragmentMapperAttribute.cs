namespace SparseTypes.EntityFrameworkCore;

/// <summary>Marks a partial mapper class as a SparseTypes EF Core entity mapping for one entity/DTO pair.</summary>
/// <remarks>The attribute is the single authoritative Entity/DTO declaration.</remarks>
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
