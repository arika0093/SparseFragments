namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// Infrastructure contract implemented by generated mapper bridges.
/// </summary>
/// <remarks>Consumers declare mappings with <c>SparseFragmentMapperAttribute</c>.</remarks>
/// <typeparam name="TEntity">The tracked EF Core entity type.</typeparam>
/// <typeparam name="TDto">The DTO (fragment model) type carrying the change set.</typeparam>
public interface IEntityModelMapping<TEntity, TDto>
    where TEntity : class
    where TDto : class
{
    /// <summary>
    /// Registers mapping overrides for this entity/DTO pair.
    /// </summary>
    /// <param name="map">The builder receiving the declarative registrations.</param>
    void Configure(SparseEntityMapBuilder<TEntity, TDto> map);
}
