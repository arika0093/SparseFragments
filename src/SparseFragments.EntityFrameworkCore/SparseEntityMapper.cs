using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SparseFragments.EntityFrameworkCore.Mapping;

namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Applies accepted DTO change sets to tracked EF Core entities.
/// </summary>
/// <remarks>
/// Each <see cref="Create{TMapping}(TMapping, IModel)" /> call builds and
/// validates a fresh plan for that mapping instance and EF model: unlike the
/// earlier proof of concept, plans are never shared across mapping instances,
/// so constructor-injected configuration cannot leak between instances.
/// Reuse the returned mapper instance for repeated applies against the same
/// model. <see cref="Apply" /> never queries, never calls <c>SaveChanges</c>,
/// and never rebases; orchestration stays application-owned. Only members
/// marked changed by the accepted change set execute their setters, so
/// untouched EF properties stay unmodified. New values always come from the
/// ChangeSet transition itself; no desired/root DTO argument exists.
/// </remarks>
/// <typeparam name="TEntity">The tracked EF Core entity type.</typeparam>
/// <typeparam name="TDto">The DTO (fragment model) type carrying the change set.</typeparam>
public sealed class SparseEntityMapper<TEntity, TDto>
    where TEntity : class
    where TDto : class
{
    private SparseEntityMapper(IModel model, EntityPlanBase plan, string mapper)
    {
        Model = model;
        Plan = plan;
        MapperName = mapper;
    }

    internal IModel Model { get; }

    internal EntityPlanBase Plan { get; }

    internal string MapperName { get; }

    /// <summary>
    /// Creates the mapper for one mapping instance plus EF model.
    /// </summary>
    /// <remarks>
    /// <c>Configure</c> runs now and the resulting plan is validated against
    /// the model, so dormant unmapped members fail here even when the first
    /// ChangeSet never edits them. Source-generated defaults (emitted by the
    /// ordinary generator for <c>[SparseFragmentMapper]</c> types) are applied
    /// before <c>Configure</c>, and explicit registrations replace them at the
    /// exact scoped DTO member path.
    /// </remarks>
    /// <param name="mapping">The mapping declaring overrides through Configure.</param>
    /// <param name="model">The EF model the plan is validated against.</param>
    /// <typeparam name="TMapping">The mapping implementation type.</typeparam>
    /// <returns>The validated mapper for this model and mapping instance.</returns>
    public static SparseEntityMapper<TEntity, TDto> Create<TMapping>(TMapping mapping, IModel model)
        where TMapping : IEntityModelMapping<TEntity, TDto>
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(model);
        return Build(mapping, model);
    }

    /// <summary>
    /// Applies the accepted change set to a tracked entity.
    /// </summary>
    /// <remarks>
    /// Reads EF metadata and writes through property entries, so shadow,
    /// field-backed, and generated members behave per the validated plan. A
    /// detached root entity stays detached while the change set validates and
    /// attaches as unchanged only once validation succeeds (explicit,
    /// documented handling); no query and no <c>SaveChanges</c> take part.
    /// Validation runs before any write, so a deterministic mapping failure
    /// leaves the entity graph and ChangeTracker untouched. Custom
    /// convert/write callbacks run during validation and execution; an
    /// exception from arbitrary user code after partial execution is the
    /// caller's to handle via transaction/disposal (documented, not rolled back).
    /// </remarks>
    /// <param name="context">The context tracking the entity. Its model must be the creation model.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="changes">The accepted/rebased change set; the strict write mask and value source.</param>
    public void Apply(DbContext context, TEntity entity, ISparseChangeSet changes)
    {
        ApplyWithResult(context, entity, changes);
    }

    /// <summary>
    /// Applies the accepted change set to a tracked entity and correlates created children.
    /// </summary>
    /// <remarks>
    /// Same lifecycle as <see cref="Apply" />. The returned result maps every
    /// DTO <c>[SparseTemporaryKey]</c> to its created child entity scoped by
    /// collection path, so the application can read database-generated
    /// permanent keys after the application-owned <c>SaveChanges</c> and build
    /// the authoritative DTO for client <c>Reconcile</c>.
    /// </remarks>
    /// <param name="context">The context tracking the entity. Its model must be the creation model.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="changes">The accepted/rebased change set; the strict write mask and value source.</param>
    /// <returns>The path-scoped temporary-identity correlation for created child entities.</returns>
    public SparseEntityApplyResult ApplyWithResult(
        DbContext context,
        TEntity entity,
        ISparseChangeSet changes
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(changes);
        if (!ReferenceEquals(context.Model, Model))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                "The context model is not the model this mapper was created for. Create a mapper per EF model."
            );
        }

        // Validation-safe entry: a detached root stays detached while the
        // change set validates, and attaches only once validation succeeds.
        var entry = context.Entry(entity);
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

        var correlation = new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal);
        var state = new PlanApplyState(
            context,
            entity,
            entry,
            changes,
            string.Empty,
            false,
            correlation
        );
        var actions = new List<Action>();
        try
        {
            Plan.Prepare(state, actions);
        }
        catch (SparseEntityMappingException exception)
        {
            throw ScopeToMapper(exception);
        }

        PlanApplyState.AttachSingle(context, entity);
        try
        {
            foreach (var action in actions)
            {
                action();
            }
        }
        catch (SparseEntityMappingException exception)
        {
            throw ScopeToMapper(exception);
        }

        return new SparseEntityApplyResult(correlation);
    }

    private SparseEntityMappingException ScopeToMapper(SparseEntityMappingException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.Mapper is not null)
        {
            return exception;
        }

        return new SparseEntityMappingException(
            exception.Failure,
            exception.DtoPath,
            $"[{MapperName}] {exception.Message}",
            MapperName,
            exception
        );
    }

    private static SparseEntityMapper<TEntity, TDto> Build(
        IEntityModelMapping<TEntity, TDto> mapping,
        IModel model
    )
    {
        var mapperName = mapping.GetType().FullName ?? mapping.GetType().Name;
        try
        {
            var builder = new SparseEntityMapBuilder<TEntity, TDto>();
            var defaults = GeneratedDefaults.TryApply(mapping, builder);
            if (
                defaults != GeneratedDefaultsStatus.Applied
                && GeneratedDefaults.RequiresGeneratedDefaults(mapping.GetType())
            )
            {
                var reason =
                    defaults == GeneratedDefaultsStatus.MissingCompanion
                        ? "no generated companion was found"
                        : "the generated companion exposes no compatible RegisterDefaults entry";
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    null,
                    $"The mapper '{mapperName}' is marked [SparseFragmentMapper] but {reason}. Reference SparseFragments.Generator as an analyzer in the mapper compilation so conventional defaults generate; mappings without the marker keep the scoped runtime inference fallback."
                );
            }

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
            return new SparseEntityMapper<TEntity, TDto>(model, plan, mapperName);
        }
        catch (SparseEntityMappingException exception) when (exception.Mapper is null)
        {
            throw new SparseEntityMappingException(
                exception.Failure,
                exception.DtoPath,
                $"[{mapperName}] {exception.Message}",
                mapperName,
                exception
            );
        }
    }
}
