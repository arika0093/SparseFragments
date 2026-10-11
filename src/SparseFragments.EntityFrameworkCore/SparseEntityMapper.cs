using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SparseFragments.EntityFrameworkCore.Mapping;

namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Applies accepted DTO change sets to tracked EF Core entities.
/// </summary>
/// <remarks>
/// A mapper is created once per EF <see cref="IModel" /> plus mapping type and
/// reused: <see cref="Create{TMapping}(TMapping, IModel)" /> returns the cached
/// instance. <see cref="Apply" /> never queries, never calls
/// <c>SaveChanges</c>, and never rebases; orchestration stays application-owned.
/// Only members marked changed by the accepted change set execute their
/// setters, so untouched EF properties stay unmodified.
/// </remarks>
/// <typeparam name="TEntity">The tracked EF Core entity type.</typeparam>
/// <typeparam name="TDto">The DTO (fragment model) type carrying the change set.</typeparam>
public sealed class SparseEntityMapper<TEntity, TDto>
    where TEntity : class
    where TDto : class
{
    private static readonly ConcurrentDictionary<
        PlanCacheKey,
        SparseEntityMapper<TEntity, TDto>
    > Cache = new();

    private SparseEntityMapper(IModel model, EntityPlanBase plan)
    {
        Model = model;
        Plan = plan;
    }

    internal IModel Model { get; }

    internal EntityPlanBase Plan { get; }

    /// <summary>
    /// Creates (or reuses) the mapper for one EF model plus mapping type.
    /// </summary>
    /// <remarks>
    /// The mapping's <c>Configure</c> must be deterministic for its type: the
    /// compiled plan is cached per model instance plus mapping type, never
    /// process-wide. A different EF model builds a different plan.
    /// </remarks>
    /// <param name="mapping">The mapping declaring overrides through Configure.</param>
    /// <param name="model">The EF model the plan is validated against.</param>
    /// <typeparam name="TMapping">The mapping implementation type.</typeparam>
    /// <returns>The cached mapper for this model and mapping type.</returns>
    public static SparseEntityMapper<TEntity, TDto> Create<TMapping>(TMapping mapping, IModel model)
        where TMapping : IEntityModelMapping<TEntity, TDto>
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(model);
        return Cache.GetOrAdd(
            new PlanCacheKey(model, typeof(TMapping)),
            static (_, args) => Build(args.Mapping, args.Model),
            (Mapping: mapping, Model: model)
        );
    }

    /// <summary>
    /// Applies the accepted change set to a tracked entity.
    /// </summary>
    /// <remarks>
    /// Reads EF metadata and writes through property entries, so shadow,
    /// field-backed, and generated members behave per the validated plan. A
    /// detached entity is attached as unchanged first; no query and no
    /// <c>SaveChanges</c> take part. Validation runs before any write, so a
    /// deterministic mapping failure leaves scalar state untouched.
    /// </remarks>
    /// <param name="context">The context tracking the entity. Its model must be the creation model.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="desired">The accepted after-state DTO supplying present values.</param>
    /// <param name="changes">The accepted change set acting as the strict write mask.</param>
    public void Apply(DbContext context, TEntity entity, TDto desired, ISparseChangeSet changes)
    {
        ApplyWithResult(context, entity, desired, changes);
    }

    /// <summary>
    /// Applies the accepted change set to a tracked entity and correlates created children.
    /// </summary>
    /// <remarks>
    /// Same lifecycle as <see cref="Apply" />. The returned result maps every
    /// DTO <c>[SparseTemporaryKey]</c> to its created child entity, so the
    /// application can read database-generated permanent keys after the
    /// application-owned <c>SaveChanges</c> and build the authoritative DTO for
    /// client <c>Reconcile</c>.
    /// </remarks>
    /// <param name="context">The context tracking the entity. Its model must be the creation model.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="desired">The accepted after-state DTO supplying present values.</param>
    /// <param name="changes">The accepted change set acting as the strict write mask.</param>
    /// <returns>The temporary-identity correlation for created child entities.</returns>
    public SparseEntityApplyResult ApplyWithResult(
        DbContext context,
        TEntity entity,
        TDto desired,
        ISparseChangeSet changes
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(changes);
        if (!ReferenceEquals(context.Model, Model))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                "The context model is not the model this mapper was created for. Create a mapper per EF model."
            );
        }

        var entry = PlanApplyState.AttachSingle(context, entity);
        var planned = Model.FindEntityType(typeof(TEntity));
        if (
            entry.Metadata is not IEntityType actual
            || planned is null
            || (
                !ReferenceEquals(actual, planned)
                && !planned.ClrType.IsAssignableFrom(actual.ClrType)
            )
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The entity is tracked as '{entry.Metadata.Name}', not as '{typeof(TEntity).Name}'."
            );
        }

        var correlation = new Dictionary<Guid, (string Path, object Entity)>();
        Plan.Apply(
            new PlanApplyState(
                context,
                entity,
                entry,
                desired,
                changes,
                string.Empty,
                false,
                correlation
            )
        );
        return new SparseEntityApplyResult(
            correlation
                .GroupBy(static entry => entry.Value.Path, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group =>
                        group.ToDictionary(
                            static entry => entry.Key,
                            static entry => entry.Value.Entity
                        ),
                    StringComparer.Ordinal
                )
        );
    }

    private static SparseEntityMapper<TEntity, TDto> Build(
        IEntityModelMapping<TEntity, TDto> mapping,
        IModel model
    )
    {
        var builder = new SparseEntityMapBuilder<TEntity, TDto>();
        mapping.Configure(builder);
        var entityType =
            model.FindEntityType(typeof(TEntity))
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The EF model has no entity type '{typeof(TEntity).Name}'."
            );
        var plan = EntityPlan<TEntity, TDto>.Build(
            new PlanBuildSession(model),
            builder,
            entityType
        );
        return new SparseEntityMapper<TEntity, TDto>(model, plan);
    }

    private sealed class PlanCacheKey : IEquatable<PlanCacheKey>
    {
        public PlanCacheKey(IModel model, Type mapping)
        {
            Model = model;
            Mapping = mapping;
        }

        public IModel Model { get; }

        public Type Mapping { get; }

        public bool Equals(PlanCacheKey? other) =>
            other is not null && ReferenceEquals(Model, other.Model) && Mapping == other.Mapping;

        public override bool Equals(object? obj) => obj is PlanCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(Model), Mapping);
    }
}
