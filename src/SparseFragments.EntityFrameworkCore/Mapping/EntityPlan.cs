using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// Per-level build workspace: the entity type plus resolved and inferred members.
/// </summary>
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
{
    private static readonly MethodInfo InferredFactory =
        typeof(EntityPlan<TEntity, TDto>).GetMethod(
            nameof(CreateInferredScalarCore),
            BindingFlags.Public | BindingFlags.Static
        ) ?? throw new InvalidOperationException("The inferred scalar factory is missing.");

    private EntityPlan(
        List<MemberNode> steps,
        Dictionary<string, MemberNode> byDtoName,
        HashSet<string> ignored,
        Dictionary<string, string> collections,
        Dictionary<string, string> protectedNames,
        Dictionary<string, string> navigationConflicts,
        Dictionary<string, Func<object, object?>> collectionReaders
    )
    {
        Steps = steps;
        ByDtoName = byDtoName;
        Ignored = ignored;
        Collections = collections;
        ProtectedNames = protectedNames;
        NavigationConflicts = navigationConflicts;
        CollectionReaders = collectionReaders;
    }

    private List<MemberNode> Steps { get; }

    private Dictionary<string, MemberNode> ByDtoName { get; }

    private HashSet<string> Ignored { get; }

    private Dictionary<string, string> Collections { get; }

    private Dictionary<string, string> ProtectedNames { get; }

    private Dictionary<string, string> NavigationConflicts { get; }

    private Dictionary<string, Func<object, object?>> CollectionReaders { get; }

    /// <summary>Builds and validates the plan for one EF entity type.</summary>
    /// <remarks>
    /// Coverage is fail-fast: every readable DTO member must resolve to a
    /// generated default, an explicit mapping, an explicit ignore, or an
    /// explicit collection/object registration. A dormant unmapped member fails
    /// here even when the first ChangeSet never edits it.
    /// </remarks>
    public static EntityPlan<TEntity, TDto> Build(
        PlanBuildSession session,
        SparseEntityMapBuilderBase registrations,
        IEntityType entityType
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(entityType);
        var level = new PlanLevel(entityType, registrations);
        foreach (var node in registrations.Nodes)
        {
            node.Resolve(session, level);
            level.AddResolved(node);
        }

        foreach (var entry in registrations.Collections)
        {
            ValidateCollectionMember(entityType, entry.Key, entry.Value);
            level.Collections[entry.Key] = entry.Value;
        }

        // Conventional defaults arrived from the source-generated companion:
        // every same-name scalar, nested, and collection path the generator
        // could statically resolve is already registered (explicit Configure
        // entries replaced theirs exactly once). Runtime reflection scanning
        // stays off so generated discovery remains the single source.
        if (!registrations.GeneratedDefaultsApplied)
        {
            InferScalars(session, level);
        }

        level.ValidateModifiers();
        ValidateCoverage(level);
        return new EntityPlan<TEntity, TDto>(
            level.Steps,
            level.ByDtoName,
            new HashSet<string>(registrations.Ignored, StringComparer.Ordinal),
            level.Collections,
            level.ProtectedNames,
            level.NavigationConflicts,
            BuildCollectionReaders(registrations, level)
        );
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        foreach (var root in state.Groups.Keys)
        {
            if (Ignored.Contains(root))
            {
                continue;
            }

            if (ByDtoName.TryGetValue(root, out var node))
            {
                node.Prepare(state, actions);
                continue;
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

    private static void InferScalars(PlanBuildSession session, PlanLevel level)
    {
        var dtoProperties = typeof(TDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead)
            .OrderBy(static property => property.MetadataToken);
        foreach (var dtoProperty in dtoProperties)
        {
            var name = dtoProperty.Name;
            if (level.Registrations.Covers(name) || level.ByDtoName.ContainsKey(name))
            {
                continue;
            }

            var dtoType = dtoProperty.PropertyType;
            if (PlanBuildSession.IsCollectionType(dtoType))
            {
                level.Collections[name] = name;
                continue;
            }

            var entityProperty = FindProperty(level.EntityType, name);
            if (entityProperty is null)
            {
                var navigation = level.EntityType.FindNavigation(name);
                if (navigation is not null)
                {
                    level.NavigationConflicts[name] = navigation.IsCollection
                        ? "a collection navigation"
                        : "a reference navigation";
                }

                continue;
            }

            if (!PlanBuildSession.IsScalarType(dtoType))
            {
                continue;
            }

            var (protection, detail) = PlanBuildSession.GetProtection(
                level.EntityType,
                entityProperty
            );
            var allowed = level.Registrations.AllowedWrites.Contains(name);
            if (protection != EntityProtection.None && !allowed)
            {
                level.ProtectedNames[name] = detail;
                continue;
            }

            if (!entityProperty.ClrType.Equals(dtoType))
            {
                continue;
            }

            if (allowed)
            {
                level.ConsumeAllowedWrite(name);
            }

            var inferred = CreateInferredScalar(
                dtoProperty,
                entityProperty,
                ResolvePolicy(level, name)
            );
            inferred.Resolve(session, level);
            level.AddResolved(inferred);
        }
    }

    private static SparseEntityMissingPolicy? ResolvePolicy(PlanLevel level, string dtoName)
    {
        if (level.Registrations.MissingOverrides.TryGetValue(dtoName, out var policy))
        {
            level.ConsumeMissingOverride(dtoName);
            return policy;
        }

        return null;
    }

    private static MemberNode CreateInferredScalar(
        PropertyInfo dtoProperty,
        IProperty entityProperty,
        SparseEntityMissingPolicy? onMissing
    )
    {
        var method = InferredFactory.MakeGenericMethod(
            dtoProperty.PropertyType,
            entityProperty.ClrType
        );
        return (MemberNode)method.Invoke(null, [dtoProperty, entityProperty.Name, onMissing])!;
    }

    /// <summary>Public factory entry so plan-time generic closing needs no accessibility bypass.</summary>
    public static ScalarNode<TDto, TEntity, TDtoVal, TEntityVal> CreateInferredScalarCore<
        TDtoVal,
        TEntityVal
    >(PropertyInfo dtoProperty, string entityProperty, SparseEntityMissingPolicy? onMissing)
    {
        var parameter = Expression.Parameter(typeof(TDto), "dto");
        var getter = Expression
            .Lambda<Func<TDto, TDtoVal>>(
                Expression.Convert(Expression.Property(parameter, dtoProperty), typeof(TDtoVal)),
                parameter
            )
            .Compile();
        return new ScalarNode<TDto, TEntity, TDtoVal, TEntityVal>(
            dtoProperty,
            getter,
            entityProperty,
            null,
            onMissing
        );
    }

    private static void ValidateCoverage(PlanLevel level)
    {
        // Fail-fast: every readable DTO member must be covered even when the
        // first ChangeSet never edits it. Inferred defaults already populate
        // ByDtoName/Collections; anything left is a dormant omission.
        // Only writable DTO members need scalar coverage: read-only computed
        // identity projections (for example [SparseKey] Key => Id) carry no
        // settable state and never take a scalar write.
        var dtoProperties = typeof(TDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead && property.CanWrite)
            .OrderBy(static property => property.MetadataToken);
        foreach (var dtoProperty in dtoProperties)
        {
            var name = dtoProperty.Name;
            if (level.ByDtoName.ContainsKey(name) || level.Registrations.Ignored.Contains(name))
            {
                continue;
            }

            // A generated DeclareCollection marker alone never synchronizes:
            // only an explicit full Collection mapping (or Ignore) covers a
            // collection path. Explicit markers still count: the author took
            // responsibility and changes fail as CollectionNotSupported.
            if (
                level.Collections.ContainsKey(name) && !level.Registrations.IsGeneratedDefault(name)
            )
            {
                continue;
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
