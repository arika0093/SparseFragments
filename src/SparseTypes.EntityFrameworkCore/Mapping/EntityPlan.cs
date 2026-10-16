using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>Per-level build workspace: the entity type plus resolved and inferred members.</summary>
internal sealed class PlanLevel
{
    private readonly HashSet<string> _consumedAllowed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumedMissing = new(StringComparer.Ordinal);

    public PlanLevel(IEntityType entityType, SparseEntityMapBuilderBase registrations)
    {
        EntityType = entityType;
        Registrations = registrations;
    }

    public IEntityType EntityType { get; }

    public SparseEntityMapBuilderBase Registrations { get; }

    public List<MemberNode> Steps { get; } = [];

    public Dictionary<string, MemberNode> ByDtoName { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ProtectedNames { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Collections { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> NavigationConflicts { get; } = new(StringComparer.Ordinal);

    public HashSet<string> FailedMembers { get; } = new(StringComparer.Ordinal);

    public void AddResolved(MemberNode node)
    {
        Steps.Add(node);
        ByDtoName[node.DtoName] = node;
    }

    public void ConsumeAllowedWrite(string dtoName) => _consumedAllowed.Add(dtoName);

    public void ConsumeMissingOverride(string dtoName) => _consumedMissing.Add(dtoName);

    /// <summary>Records a same-name DTO member that inference must not write.</summary>
    public void ReportProtected(string dtoName, string detail) => ProtectedNames[dtoName] = detail;

    /// <summary>Rejects modifier registrations that target nothing mappable.</summary>
    public void ValidateModifiers()
    {
        var danglingMissing = Registrations
            .MissingOverrides.Keys.Where(name => !_consumedMissing.Contains(name))
            .ToList();
        if (danglingMissing.Count > 0)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                danglingMissing[0],
                $"OnMissing('{danglingMissing[0]}') does not target a mapped scalar member."
            );
        }

        var danglingAllowed = Registrations
            .AllowedWrites.Where(name => !_consumedAllowed.Contains(name))
            .ToList();
        if (danglingAllowed.Count > 0)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                danglingAllowed[0],
                $"AllowWrite('{danglingAllowed[0]}') does not target an identity, generated, concurrency-token, or shadow member."
            );
        }
    }
}

/// <summary>
/// A validated, executable mapping for one entity level.
/// </summary>
internal abstract class EntityPlanBase
{
    /// <summary>Probes the change set and queues writes without mutating the entity.</summary>
    public abstract void Prepare(PlanApplyState state, List<Action> actions);

    /// <summary>Queues writes for every mapped member from the After snapshot.</summary>
    public abstract void WriteAll(PlanWriteState state, List<Action> actions);

    /// <summary>Collects the Include paths needed to apply the current transition.</summary>
    public abstract void CollectIncludes(
        ISparseChangeSet changes,
        IncludePathCollector collector,
        bool writeAll,
        object? afterSnapshot
    );

    /// <summary>Builds the DTO value expression and collects projection errors.</summary>
    public abstract Expression BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    );

    /// <summary>Copies baseline temporary identities and correlates created entities into the DTO.</summary>
    public abstract void AssignTemporaryKeys(
        Microsoft.EntityFrameworkCore.DbContext context,
        object? baseline,
        object authoritative,
        SparseEntityApplyResult result,
        string path,
        TemporaryKeyAssignmentPlan assignments,
        IReadOnlyList<CollectionScopePath>? parentScopes = null
    );

    /// <summary>Validates the change set, then executes the queued writes.</summary>
    public void Apply(PlanApplyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var actions = new List<Action>();
        Prepare(state, actions);
        foreach (var action in actions)
        {
            action();
        }
    }
}

/// <summary>
/// Compiled mapping for one entity/DTO pair against one EF model.
/// </summary>
internal sealed class EntityPlan<TEntity, TDto> : EntityPlanBase
    where TEntity : class
    where TDto : class
{
    private static readonly MethodInfo PrimitiveCollectionFactory =
        typeof(EntityPlan<TEntity, TDto>).GetMethod(
            nameof(CreatePrimitiveCollectionNodeCore),
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException("The primitive collection factory is missing.");

    private EntityPlan(
        List<MemberNode> steps,
        Dictionary<string, MemberNode> byDtoName,
        HashSet<string> ignored,
        Dictionary<string, string> collections,
        Dictionary<string, string> protectedNames,
        Dictionary<string, string> navigationConflicts,
        Dictionary<string, Func<object, object?>> collectionReaders,
        Dictionary<string, LambdaExpression> reads
    )
    {
        Steps = steps;
        ByDtoName = byDtoName;
        Ignored = ignored;
        Collections = collections;
        ProtectedNames = protectedNames;
        NavigationConflicts = navigationConflicts;
        CollectionReaders = collectionReaders;
        Reads = reads;
    }

    private List<MemberNode> Steps { get; }

    private Dictionary<string, MemberNode> ByDtoName { get; }

    private HashSet<string> Ignored { get; }

    private Dictionary<string, string> Collections { get; }

    private Dictionary<string, string> ProtectedNames { get; }

    private Dictionary<string, string> NavigationConflicts { get; }

    private Dictionary<string, Func<object, object?>> CollectionReaders { get; }

    private Dictionary<string, LambdaExpression> Reads { get; }

    /// <summary>Builds and validates the plan for one EF entity type.</summary>
    /// <remarks>Coverage is fail-fast: every readable DTO member must resolve to a generated default.</remarks>
    public static EntityPlan<TEntity, TDto> Build(
        PlanBuildSession session,
        SparseEntityMapBuilderBase registrations,
        IEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(entityType);
        using var buildScope = session.BeginBuild();
        if (!registrations.GeneratedDefaultsApplied && !registrations.IsNestedScope)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The mapper for '{typeof(TEntity).Name}' and '{typeof(TDto).Name}' has no source-generated defaults registration. Add [SparseFragmentMapper<TEntity, TDto>] and reference SparseTypes.Generator as an analyzer; runtime member inference is not supported."
            );
        }

        var level = new PlanLevel(entityType, registrations);
        foreach (var node in registrations.Nodes.Where(node => !ResolveNode(session, level, node)))
        {
            level.FailedMembers.Add(node.DtoName);
        }

        foreach (
            var entry in registrations.Collections.Where(entry =>
                !ResolveCollection(session, level, entityType, entry)
            )
        )
        {
            level.FailedMembers.Add(entry.Key);
        }

        session.TryCollect(level.ValidateModifiers);
        ValidateCoverage(session, level);
        Dictionary<string, Func<object, object?>> collectionReaders = [];
        session.TryCollect(() => collectionReaders = BuildCollectionReaders(registrations, level));
        return new EntityPlan<TEntity, TDto>(
            level.Steps,
            level.ByDtoName,
            new HashSet<string>(registrations.Ignored, StringComparer.Ordinal),
            level.Collections,
            level.ProtectedNames,
            level.NavigationConflicts,
            collectionReaders,
            new Dictionary<string, LambdaExpression>(registrations.Reads, StringComparer.Ordinal)
        );
    }

    private static bool ResolveNode(PlanBuildSession session, PlanLevel level, MemberNode node)
    {
        return session.TryCollect(() =>
        {
            node.Resolve(session, level);
            level.AddResolved(node);
        });
    }

    private static bool ResolveCollection(
        PlanBuildSession session,
        PlanLevel level,
        IEntityType entityType,
        KeyValuePair<string, string> entry
    )
    {
        return session.TryCollect(() =>
        {
            var entityProperty = FindProperty(entityType, entry.Value);
            if (entityProperty?.IsPrimitiveCollection == true)
            {
                var dtoProperty = typeof(TDto).GetProperty(
                    entry.Key,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (dtoProperty is null || !dtoProperty.CanRead)
                {
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.MetadataMismatch,
                        entry.Key,
                        $"The DTO type '{typeof(TDto).Name}' has no readable collection property '{entry.Key}'."
                    );
                }

                var primitiveNode = CreatePrimitiveCollectionNode(dtoProperty, entityProperty);
                primitiveNode.Resolve(session, level);
                level.AddResolved(primitiveNode);
                return;
            }

            ValidateCollectionMember(entityType, entry.Key, entry.Value);
            level.Collections[entry.Key] = entry.Value;
        });
    }

    private static MemberNode CreatePrimitiveCollectionNode(
        PropertyInfo dtoProperty,
        IProperty entityProperty
    )
    {
        try
        {
            return (MemberNode)
                PrimitiveCollectionFactory
                    .MakeGenericMethod(dtoProperty.PropertyType)
                    .Invoke(null, [dtoProperty, entityProperty])!;
        }
        catch (ArgumentException exception)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoProperty.Name,
                $"The DTO member '{dtoProperty.Name}' cannot be mapped as an EF primitive collection: {exception.Message}"
            );
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is SparseEntityMappingException mappingException)
        {
            throw mappingException;
        }
    }

    /// <summary>Closes a primitive collection node over the DTO collection type.</summary>
    public static PrimitiveCollectionNode<
        TDto,
        TDtoCollection
    > CreatePrimitiveCollectionNodeCore<TDtoCollection>(
        PropertyInfo dtoProperty,
        IProperty entityProperty
    )
        where TDtoCollection : class => new(dtoProperty, entityProperty);

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var root in state.Groups.Keys)
        {
            state.CollectValidation(() => PrepareRoot(state, actions, root));
        }

        state.ThrowIfValidationErrors();
    }

    private void PrepareRoot(PlanApplyState state, List<Action> actions, string root)
    {
        if (Ignored.Contains(root))
        {
            return;
        }

        if (ByDtoName.TryGetValue(root, out var node))
        {
            node.Prepare(state, actions);
            return;
        }

        if (Collections.ContainsKey(root))
        {
            throw CollectionError(state, root);
        }

        if (ProtectedNames.TryGetValue(root, out var detail))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Protected,
                state.DisplayPath(root),
                $"The DTO member '{root}' targets {detail}. It is never written by default; allow it explicitly with AllowWrite()."
            );
        }

        throw UnmappedError(state, root);
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var step in Steps)
        {
            step.WriteAll(state, actions);
        }
    }

    public override void AssignTemporaryKeys(
        Microsoft.EntityFrameworkCore.DbContext context,
        object? baseline,
        object authoritative,
        SparseEntityApplyResult result,
        string path,
        TemporaryKeyAssignmentPlan assignments,
        IReadOnlyList<CollectionScopePath>? parentScopes = null
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(assignments);
        foreach (var step in Steps)
        {
            step.AssignTemporaryKeys(
                context,
                baseline,
                authoritative,
                result,
                path,
                assignments,
                parentScopes
            );
        }
    }

    public override void CollectIncludes(
        ISparseChangeSet changes,
        IncludePathCollector collector,
        bool writeAll,
        object? afterSnapshot
    )
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(collector);
        if (writeAll && afterSnapshot is not TDto)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The wholesale Include snapshot is not '{typeof(TDto).Name}'."
            );
        }

        foreach (var step in Steps)
        {
            step.CollectIncludes(changes, collector, writeAll, afterSnapshot);
        }
    }

    public override Expression BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dtoPath);
        var constructor = typeof(TDto).GetConstructor(Type.EmptyTypes);
        if (constructor is null)
        {
            context.AddError(
                dtoPath,
                $"The DTO type '{typeof(TDto).FullName}' needs a public parameterless constructor for read projection.",
                "Add a parameterless constructor and writable properties, or do not request Projection/ToDto."
            );
            return Expression.Default(typeof(TDto));
        }

        var bindings = new List<MemberBinding>();
        var usedReads = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var property in typeof(TDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.CanRead && property.CanWrite)
                .OrderBy(static property => property.MetadataToken)
        )
        {
            var memberPath = ReadProjectionBuildContext.JoinPath(dtoPath, property.Name);
            if (Reads.TryGetValue(property.Name, out var read))
            {
                usedReads.Add(property.Name);
                if (
                    context.TryInlineRead(
                        read,
                        entity,
                        property.PropertyType,
                        memberPath,
                        out var readValue
                    ) && context.TryBind(property, readValue, memberPath, out var readBinding)
                )
                {
                    bindings.Add(readBinding!);
                }

                continue;
            }

            if (ByDtoName.TryGetValue(property.Name, out var node))
            {
                var errorCount = context.Errors.Count;
                var value = node.BuildProjection(entity, context, memberPath);
                if (value is null)
                {
                    if (context.Errors.Count == errorCount)
                    {
                        context.AddError(
                            memberPath,
                            $"The mapped DTO member '{property.Name}' has no inferred read expression.",
                            "Register Read(dto => dto.Member, entity => entity.Value) for this member."
                        );
                    }

                    continue;
                }

                if (context.TryBind(property, value, memberPath, out var binding))
                {
                    bindings.Add(binding!);
                }

                continue;
            }

            if (Ignored.Contains(property.Name))
            {
                continue;
            }

            if (Collections.ContainsKey(property.Name))
            {
                context.AddError(
                    memberPath,
                    $"The collection '{property.Name}' is declared without keyed projection metadata.",
                    "Use a keyed Collection() registration or provide an explicit Read() expression."
                );
                continue;
            }

            context.AddError(
                memberPath,
                $"The DTO member '{property.Name}' has no read projection mapping.",
                "Register Read(dto => dto.Member, entity => entity.Value), map it, or Ignore() it."
            );
        }

        foreach (var readName in Reads.Keys.Where(name => !usedReads.Contains(name)))
        {
            context.AddError(
                ReadProjectionBuildContext.JoinPath(dtoPath, readName),
                $"The Read() registration for '{readName}' does not target a writable DTO property."
            );
        }

        return Expression.MemberInit(Expression.New(constructor), bindings);
    }

    private static Dictionary<string, Func<object, object?>> BuildCollectionReaders(
        SparseEntityMapBuilderBase registrations,
        PlanLevel level
    )
    {
        var readers = new Dictionary<string, Func<object, object?>>(StringComparer.Ordinal);
        foreach (var name in level.Collections.Keys)
        {
            var property = typeof(TDto).GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance
            );
            if (property is null || !property.CanRead)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    name,
                    $"The DTO type '{typeof(TDto).Name}' has no readable property '{name}'."
                );
            }

            if (
                registrations.Collections.ContainsKey(name)
                && !PlanBuildSession.IsCollectionType(property.PropertyType)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    name,
                    $"The DTO member '{name}' is not collection-shaped. Register it with Member()."
                );
            }

            readers[name] = CompileObjectReader(property);
        }

        return readers;
    }

    private static Func<object, object?> CompileObjectReader(PropertyInfo property)
    {
        var parameter = Expression.Parameter(typeof(object), "dto");
        var access = Expression.Property(Expression.Convert(parameter, typeof(TDto)), property);
        return Expression
            .Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), parameter)
            .Compile();
    }

    private static void ValidateCoverage(PlanBuildSession session, PlanLevel level)
    {
        // Require coverage for every readable DTO member before the first edit.
        // Read-only computed keys need no scalar write.
        var dtoProperties = typeof(TDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead && property.CanWrite)
            .OrderBy(static property => property.MetadataToken);
        foreach (var dtoProperty in dtoProperties)
        {
            session.TryCollect(() => ValidateCoverage(level, dtoProperty));
        }
    }

    private static void ValidateCoverage(PlanLevel level, PropertyInfo dtoProperty)
    {
        var name = dtoProperty.Name;
        if (
            level.FailedMembers.Contains(name)
            || level.ByDtoName.ContainsKey(name)
            || level.Registrations.Ignored.Contains(name)
        )
        {
            return;
        }

        // Only an explicit full Collection mapping (or Ignore) covers a
        // collection path.
        if (level.Collections.ContainsKey(name) && !level.Registrations.IsGeneratedDefault(name))
        {
            return;
        }

        if (
            level.Collections.ContainsKey(name)
            && PlanBuildSession.IsCollectionType(dtoProperty.PropertyType)
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Unmapped,
                name,
                $"The DTO member '{name}' has a conventional DeclareCollection marker but no collection mapping. Declare Collection() with element mapping, identity, factory, removal, and order policies, or Ignore() the member."
            );
        }

        if (level.ProtectedNames.TryGetValue(name, out var detail))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Protected,
                name,
                $"The DTO member '{name}' targets {detail}. It is never written by default; allow it explicitly with AllowWrite()."
            );
        }

        if (level.NavigationConflicts.TryGetValue(name, out var conflict))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Unmapped,
                name,
                $"The DTO member '{name}' has no mapping, and the same-name entity member is {conflict}. Declare Object() or Collection(), or Ignore() the member."
            );
        }

        var entityProperty = FindProperty(level.EntityType, name);
        if (entityProperty is not null)
        {
            var (protection, protectedDetail) = PlanBuildSession.GetProtection(
                level.EntityType,
                entityProperty
            );
            if (
                protection != EntityProtection.None
                && !level.Registrations.AllowedWrites.Contains(name)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.Protected,
                    name,
                    $"The DTO member '{name}' targets {protectedDetail}. It is never written by default; allow it explicitly with AllowWrite()."
                );
            }
        }

        if (
            entityProperty is not null
            && PlanBuildSession.IsScalarType(dtoProperty.PropertyType)
            && !entityProperty.ClrType.Equals(dtoProperty.PropertyType)
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                name,
                $"The DTO member '{name}' ({Friendly(dtoProperty.PropertyType)}) does not match the entity member '{level.EntityType.ClrType.Name}.{entityProperty.Name}' ({Friendly(entityProperty.ClrType)}). Supply a Member() convert callback or Ignore() the member."
            );
        }

        string hint;
        if (PlanBuildSession.IsScalarType(dtoProperty.PropertyType))
        {
            hint = "Declare Member() or Ignore() the member.";
        }
        else if (PlanBuildSession.IsCollectionType(dtoProperty.PropertyType))
        {
            hint = "Declare Collection() or Ignore() the member.";
        }
        else
        {
            hint = "Declare Object() for the nested member, or Ignore() it.";
        }
        throw new SparseEntityMappingException(
            SparseEntityMappingFailure.Unmapped,
            name,
            $"The DTO member '{name}' has no mapping. {hint}"
        );
    }

    private static string Friendly(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying is not null ? underlying.Name + "?" : type.Name;
    }

    private static void ValidateCollectionMember(
        IEntityType entityType,
        string dtoName,
        string entityName
    )
    {
        if (
            entityType.FindNavigation(entityName) is null
            && FindProperty(entityType, entityName) is null
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoName,
                $"The entity type '{entityType.ClrType.Name}' has no collection member '{entityName}'."
            );
        }
    }

    private static IProperty? FindProperty(IEntityType type, string name)
    {
        foreach (var property in type.GetProperties())
        {
            if (property.Name == name)
            {
                return property;
            }
        }

        return null;
    }

    private static SparseEntityMappingException CollectionError(PlanApplyState state, string root)
    {
        return CollectionErrorCore(state.DisplayPath(root), root);
    }

    private static SparseEntityMappingException CollectionErrorCore(string displayPath, string root)
    {
        return new SparseEntityMappingException(
            SparseEntityMappingFailure.CollectionNotSupported,
            displayPath,
            $"The DTO member '{root}' addresses a declared-only collection with no synchronization behavior. Declare the keyed Collection(dto, entity, configure) overload with element mapping, identity, factory, removal, and order policies, or Ignore() the member."
        );
    }

    private SparseEntityMappingException UnmappedError(PlanApplyState state, string root)
    {
        var dtoProperty = typeof(TDto).GetProperty(
            root,
            BindingFlags.Public | BindingFlags.Instance
        );
        if (dtoProperty is null)
        {
            return new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(root),
                $"The change set addresses '{root}', which is not a member of the DTO '{typeof(TDto).Name}'."
            );
        }

        if (NavigationConflicts.TryGetValue(root, out var conflict))
        {
            return new SparseEntityMappingException(
                SparseEntityMappingFailure.Unmapped,
                state.DisplayPath(root),
                $"The DTO member '{root}' has no mapping, and the same-name entity member is {conflict}. Declare Object() or Collection(), or Ignore() the member."
            );
        }

        var hint = PlanBuildSession.IsScalarType(dtoProperty.PropertyType)
            ? "Declare Member() or Ignore() the member."
            : "Declare Object() for the nested member, or Ignore() it.";
        return new SparseEntityMappingException(
            SparseEntityMappingFailure.Unmapped,
            state.DisplayPath(root),
            $"The DTO member '{root}' changed but has no mapping. {hint}"
        );
    }
}
