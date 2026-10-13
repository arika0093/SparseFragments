namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// Infrastructure contract implemented by generated mapper bridges.
/// </summary>
/// <remarks>
/// Consumers declare <see cref="SparseFragmentMapperAttribute{TEntity, TDto}" />
/// on a partial mapper class with an optional parameterless
/// <c>Configure()</c> that uses the generated <c>Map</c> member; the generator
/// implements this interface. Do not implement it manually: the generated
/// bridge owns the thread-safe builder scope (defaults first, then explicit
/// overrides once per DTO path) and the trimming-safe companion rooting.
/// The interface stays public so cross-assembly generated code and public
/// generic method constraints can name it.
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
