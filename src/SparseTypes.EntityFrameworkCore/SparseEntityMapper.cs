using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SparseTypes.EntityFrameworkCore.Mapping;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// Applies accepted DTO change sets to tracked EF Core entities.
/// </summary>
/// <remarks>Create instances with the static <c>Create</c> methods.</remarks>
/// <typeparam name="TEntity">The tracked EF Core entity type.</typeparam>
/// <typeparam name="TDto">The DTO (fragment model) type carrying the change set.</typeparam>
public sealed class SparseEntityMapper<TEntity, TDto>
    where TEntity : class
    where TDto : class
{
    private readonly Lazy<(
        Expression<Func<TEntity, TDto>> Expression,
        Func<TEntity, TDto> Compiled
    )> _projection;

    private SparseEntityMapper(IModel model, EntityPlanBase plan, string mapper)
    {
        Model = model;
        Plan = plan;
        MapperName = mapper;
        _projection = new Lazy<(
            Expression<Func<TEntity, TDto>> Expression,
            Func<TEntity, TDto> Compiled
        )>(CreateProjection, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal IModel Model { get; }

    internal EntityPlanBase Plan { get; }

    internal string MapperName { get; }

    /// <summary>
    /// Gets the expression that projects an entity into the mapped DTO.
    /// </summary>
    /// <remarks>Read-side validation is deferred until this property is first accessed.</remarks>
    public Expression<Func<TEntity, TDto>> Projection => _projection.Value.Expression;

    /// <summary>
    /// Projects an entity in memory into the mapped DTO.
    /// </summary>
    /// <remarks>This method performs no database access.</remarks>
    /// <param name="entity">The entity to project.</param>
    /// <returns>The DTO projected from the entity.</returns>
    public TDto ToDto(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _projection.Value.Compiled(entity);
    }

    /// <summary>
    /// Preserves baseline temporary identities and assigns the identities of
    /// newly created entities to the authoritative DTO.
    /// </summary>
    /// <remarks>Call after <c>SaveChanges</c>, when generated entity keys are available.</remarks>
    /// <param name="context">The context used to read mapped entity keys.</param>
    /// <param name="result">The result from applying the change set.</param>
    /// <param name="baseline">The prior DTO whose temporary identities must be preserved.</param>
    /// <param name="authoritative">The newly projected or constructed authoritative DTO to update.</param>
    public void AssignTemporaryKeys(
        DbContext context,
        SparseEntityApplyResult result,
        TDto baseline,
        TDto authoritative
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(authoritative);
        if (!ReferenceEquals(context.Model, Model))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                "The context model is not the model this mapper was created for. Create a mapper per EF model."
            );
        }

        var assignments = new TemporaryKeyAssignmentPlan();
        Plan.AssignTemporaryKeys(
            context,
            baseline,
            authoritative,
            result,
            string.Empty,
            assignments
        );
        assignments.Apply();
    }

    /// <summary>
    /// Creates the mapper for one mapping instance plus EF model.
    /// </summary>
    /// <remarks><c>Configure</c> runs now and the resulting plan is validated against the model.</remarks>
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
    /// <remarks>Reads EF metadata and writes through property entries.</remarks>
    /// <param name="context">The context tracking the entity. Its model must be the creation model.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="changes">The accepted/rebased change set; the strict write mask and value source.</param>
    public void Apply(DbContext context, TEntity entity, ISparseChangeSet<TDto> changes)
    {
        ApplyCore(context, entity, changes, trackResult: false);
    }

    /// <summary>Applies the accepted change set to a tracked entity and correlates created children.</summary>
    /// <remarks>Uses the same apply lifecycle.</remarks>
    /// <param name="context">The context tracking the entity. Its model must be the creation model.</param>
    /// <param name="entity">The tracked entity to update.</param>
    /// <param name="changes">The accepted/rebased change set; the strict write mask and value source.</param>
    /// <returns>The path-scoped temporary-identity correlation for created child entities.</returns>
    public SparseEntityApplyResult ApplyWithResult(
        DbContext context,
        TEntity entity,
        ISparseChangeSet<TDto> changes
    )
    {
        return ApplyCore(context, entity, changes, trackResult: true)!;
    }

    private SparseEntityApplyResult? ApplyCore(
        DbContext context,
        TEntity entity,
        ISparseChangeSet<TDto> changes,
        bool trackResult
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

        var correlation = trackResult
            ? new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal)
            : null;
        var appliedPaths = trackResult ? new List<SparseEntityAppliedPath>() : null;
        var collectionScopeKeys = trackResult
            ? new Dictionary<string, Dictionary<object, string>>(StringComparer.Ordinal)
            : null;
        var nestedScopes = trackResult
            ? new Dictionary<string, NestedScopeTable>(StringComparer.Ordinal)
            : null;
        var state = new PlanApplyState(
            context,
            entity,
            entry,
            changes,
            string.Empty,
            false,
            correlation,
            appliedPaths: appliedPaths,
            collectionScopeKeys: collectionScopeKeys,
            nestedScopes: nestedScopes
        );
        var actions = new List<Action>();
        // Read-only preflight detects once up front instead of scanning per
        // collection; execution below restores the caller policy first so
        // user callbacks keep their detection behavior. Empty change sets
        // read nothing, so they keep the caller's policy throughout.
        using (OpenPreflightScope(context, changes))
        {
            try
            {
                Plan.Prepare(state, actions);
            }
            catch (SparseEntityMappingException exception)
            {
                throw ScopeToMapper(exception);
            }
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

        return trackResult
            ? new SparseEntityApplyResult(
                correlation!,
                appliedPaths,
                collectionScopeKeys,
                nestedScopes
            )
            : null;
    }

    /// <summary>Opens the preflight detection scope unless the change set is empty.</summary>
    private static IDisposable? OpenPreflightScope(
        DbContext context,
        ISparseChangeSet<TDto> changes
    ) => changes.IsEmpty ? null : ChangeTrackerDetectionScope.BeginPreflight(context);

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

    private (
        Expression<Func<TEntity, TDto>> Expression,
        Func<TEntity, TDto> Compiled
    ) CreateProjection()
    {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var context = new ReadProjectionBuildContext();
        var body = Plan.BuildProjection(entity, context, string.Empty);
        if (context.Errors.Count > 0)
        {
            throw ScopeToMapper(SparseEntityMappingException.FromErrors(context.Errors));
        }

        var expression = Expression.Lambda<Func<TEntity, TDto>>(body, entity);
        return (expression, expression.Compile());
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
                    $"The mapper '{mapperName}' is marked [SparseFragmentMapper<TEntity, TDto>] but {reason}. Reference SparseTypes.Generator as an analyzer in the mapper compilation so conventional defaults generate."
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
