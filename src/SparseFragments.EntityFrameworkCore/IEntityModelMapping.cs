namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Declares the ChangeSet-aware mapping between one entity and one DTO type.
/// </summary>
/// <remarks>
/// The entire mapping lives in <see cref="Configure" />: compatible same-name
/// scalars are inferred, and only overrides, ignores, nested scopes, and
/// specialized operations are registered explicitly. Entity types are never
/// annotated, and no mapping is passed as a factory-creation argument.
/// </remarks>
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
