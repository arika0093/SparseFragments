using System.Linq.Expressions;
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
/// model. <see cref="Apply(DbContext, TEntity, ISparseChangeSet{TDto})" /> never queries, never calls <c>SaveChanges</c>,
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
    /// <remarks>
    /// Read-side validation is deferred until this property is first accessed.
    /// The expression can be passed to LINQ providers such as EF Core.
    /// </remarks>
    public Expression<Func<TEntity, TDto>> Projection => _projection.Value.Expression;

    /// <summary>
    /// Projects an entity in memory into the mapped DTO.
    /// </summary>
    /// <remarks>
    /// This method performs no database access. Load mapped reference and
    /// collection navigations first when their values are needed in the DTO.
    /// </remarks>
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
    /// <remarks>
    /// Call after <c>SaveChanges</c>, when generated entity keys are available.
    /// Collection key projections configured with <c>Key()</c> are used for
    /// matching, and nested collection paths remain scoped to their parent.
    /// Every required assignment across all nested collections is validated
    /// before any authoritative DTO mutation: duplicate, missing, or ambiguous
    /// correspondence fails with <see cref="SparseEntityMappingException" />
    /// carrying the offending DTO/collection path and leaves the authoritative
    /// DTO unmodified. Removed baseline rows are ignored, but every created
    /// row must reconcile. When a user-defined temporary-key setter throws,
    /// earlier staged assignments are rolled back before the error propagates.
    /// </remarks>
    /// <param name="context">The context used to read mapped entity keys.</param>
    /// <param name="result">The result returned by <see cref="ApplyWithResult(DbContext, TEntity, ISparseChangeSet{TDto})"/>.</param>
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

        var staging = TemporaryKeyStagingScope.Begin();
        try
        {
            Plan.AssignTemporaryKeys(context, baseline, authoritative, result, string.Empty);
        }
        catch
        {
            staging.Rollback();
            throw;
        }
        finally
        {
            staging.Close();
        }
    }

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
    public void Apply(DbContext context, TEntity entity, ISparseChangeSet<TDto> changes)
    {
        ApplyCore(context, entity, changes, trackResult: false);
    }

    /// <summary>
    /// Applies the accepted change set to a tracked entity and correlates created children.
    /// </summary>
    /// <remarks>
    /// Same lifecycle as <see cref="Apply(DbContext, TEntity, ISparseChangeSet{TDto})" />. The returned result maps every
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

        return trackResult
            ? new SparseEntityApplyResult(
                correlation!,
                appliedPaths,
                collectionScopeKeys,
                nestedScopes
            )
            : null;
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
