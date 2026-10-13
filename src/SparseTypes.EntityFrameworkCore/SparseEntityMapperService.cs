using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseTypes.EntityFrameworkCore;

internal interface ISparseEntityMapperPlanCache
{
    void Validate(DbContext context);
}

internal sealed class SparseEntityMapperService<TEntity, TDto>(
    IEntityModelMapping<TEntity, TDto> mapping
) : ISparseEntityMapper<TEntity, TDto>, ISparseEntityMapperPlanCache
    where TEntity : class
    where TDto : class
{
    private readonly ConditionalWeakTable<IModel, Lazy<SparseEntityMapper<TEntity, TDto>>> _plans =
        new();

    public void Apply(DbContext context, TEntity entity, ISparseChangeSet<TDto> changes) =>
        GetMapper(context).Apply(context, entity, changes);

    public SparseEntityApplyResult ApplyWithResult(
        DbContext context,
        TEntity entity,
        ISparseChangeSet<TDto> changes
    ) => GetMapper(context).ApplyWithResult(context, entity, changes);

    public TDto ToDto(DbContext context, TEntity entity) => GetMapper(context).ToDto(entity);

    public void AssignTemporaryKeys(
        DbContext context,
        SparseEntityApplyResult result,
        TDto baseline,
        TDto authoritative
    ) => GetMapper(context).AssignTemporaryKeys(context, result, baseline, authoritative);

    public void Validate(DbContext context)
    {
        _ = GetMapper(context);
    }

    private SparseEntityMapper<TEntity, TDto> GetMapper(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _plans
            .GetValue(
                context.Model,
                model => new Lazy<SparseEntityMapper<TEntity, TDto>>(
                    () => SparseEntityMapper<TEntity, TDto>.Create(mapping, model),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;
    }
}
