using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SparseFragments;

namespace SparseFragments.EntityFrameworkCore.Mapping;

internal sealed class GeneratedCollectionElementDefaults
{
    public GeneratedCollectionElementDefaults(
        Action<SparseEntityMapBuilderBase> configure,
        Type? keyType = null,
        Delegate? dtoKeySelector = null,
        object? unassignedKeyValue = null,
        bool hasUnassignedKeyValue = false,
        Delegate? temporaryKeySelector = null,
        Delegate? temporaryKeySetter = null,
        string[]? keyMemberNames = null
    )
    {
        Configure = configure;
        KeyType = keyType;
        DtoKeySelector = dtoKeySelector;
        UnassignedKeyValue = unassignedKeyValue;
        HasUnassignedKeyValue = hasUnassignedKeyValue;
        TemporaryKeySelector = temporaryKeySelector;
        TemporaryKeySetter = temporaryKeySetter;
        KeyMemberNames = keyMemberNames ?? [];
    }

    public Action<SparseEntityMapBuilderBase> Configure { get; }

    public Type? KeyType { get; }

    public Delegate? DtoKeySelector { get; }

    public object? UnassignedKeyValue { get; }

    public bool HasUnassignedKeyValue { get; }

    public Delegate? TemporaryKeySelector { get; }

    public Delegate? TemporaryKeySetter { get; }

    public string[] KeyMemberNames { get; }
}

/// <summary>
/// The configured operations for one parent collection path.
/// </summary>
/// <remarks>
/// Carries the builder registrations to the plan so generic arity stays flat.
/// </remarks>
internal sealed class CollectionDeclaration<TDtoChild, TEntityChild>
    where TDtoChild : class
    where TEntityChild : class
{
    public CollectionDeclaration(
        Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> elementConfigure,
        Func<TDtoChild, TEntityChild>? createFactory,
        SparseEntityRemoveKind? removeKind,
        Action<DbContext, TEntityChild>? removeHandler,
        Action<TEntityChild>? softDeleteMark,
        Func<TEntityChild, bool>? isDeletedPredicate,
        Expression<Func<TEntityChild, bool>>? isDeletedExpression,
        Action<TEntityChild, int>? orderSetter,
        LambdaExpression? orderSelectorExpression,
        Type? keyType,
        Delegate? dtoKeySelector,
        Delegate? entityKeySelector,
        object? unassignedKeyValue = null,
        bool hasUnassignedKeyValue = false,
        GeneratedCollectionElementDefaults? generatedElementDefaults = null,
        Dictionary<
            (Type EntityType, Type DtoType, string Member),
            GeneratedCollectionElementDefaults
        >? elementStash = null,
        Dictionary<
            (Type EntityType, Type DtoType, string Member),
            Action<SparseEntityMapBuilderBase>
        >? objectStash = null
    )
    {
        ElementConfigure = elementConfigure;
        CreateFactory = createFactory;
        RemoveKind = removeKind;
        RemoveHandler = removeHandler;
        SoftDeleteMark = softDeleteMark;
        IsDeletedPredicate = isDeletedPredicate;
        IsDeletedExpression = isDeletedExpression;
        OrderSetter = orderSetter;
        OrderSelectorExpression = orderSelectorExpression;
        KeyType = keyType;
        DtoKeySelector = dtoKeySelector;
        EntityKeySelector = entityKeySelector;
        UnassignedKeyValue = unassignedKeyValue;
        HasUnassignedKeyValue = hasUnassignedKeyValue;
        GeneratedElementDefaults = generatedElementDefaults;
        ElementStash =
            elementStash
            ?? new Dictionary<
                (Type EntityType, Type DtoType, string Member),
                GeneratedCollectionElementDefaults
            >();
        ObjectStash =
            objectStash
            ?? new Dictionary<
                (Type EntityType, Type DtoType, string Member),
                Action<SparseEntityMapBuilderBase>
            >();
    }

    public Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> ElementConfigure { get; }

    public GeneratedCollectionElementDefaults? GeneratedElementDefaults { get; }

    public Dictionary<
        (Type EntityType, Type DtoType, string Member),
        GeneratedCollectionElementDefaults
    > ElementStash { get; }

    public Dictionary<
        (Type EntityType, Type DtoType, string Member),
        Action<SparseEntityMapBuilderBase>
    > ObjectStash { get; }

    public Func<TDtoChild, TEntityChild>? CreateFactory { get; }

    public SparseEntityRemoveKind? RemoveKind { get; }

    public Action<DbContext, TEntityChild>? RemoveHandler { get; }

    public Action<TEntityChild>? SoftDeleteMark { get; }

    public Func<TEntityChild, bool>? IsDeletedPredicate { get; }

    public Expression<Func<TEntityChild, bool>>? IsDeletedExpression { get; }

    public Action<TEntityChild, int>? OrderSetter { get; }

    public LambdaExpression? OrderSelectorExpression { get; }

    public Type? KeyType { get; }

    public Delegate? DtoKeySelector { get; }

    public Delegate? EntityKeySelector { get; }

    public object? UnassignedKeyValue { get; }

    public bool HasUnassignedKeyValue { get; }
}

/// <summary>
/// The resolved, executable state for one keyed parent collection.
/// </summary>
/// <remarks>
/// Built once per plan against EF metadata and the generated keyed-transition
/// contract. Parent CLR types appear only in <see cref="Create{TDto,TEntity}" />;
/// synchronization itself runs object-based through EF collection entries.
/// </remarks>
internal sealed class CollectionRuntime<TDtoChild, TEntityChild>
    where TDtoChild : class
    where TEntityChild : class
{
    public string DtoName { get; private set; } = string.Empty;

    public string NavigationName { get; private set; } = string.Empty;

    public string RelationshipDetail { get; private set; } = string.Empty;

    public Func<object, IEnumerable<TDtoChild>?> SnapshotReader { get; private set; } = null!;

    public Func<ISparseChangeSet, string, KeyedCollectionView<TDtoChild>?> Fetch
    {
        get;
        private set;
    } = null!;

    public Func<TDtoChild, object?> DtoKey { get; private set; } = null!;

    public Func<DbContext, TEntityChild, object?> EntityKey { get; private set; } = null!;

    public IEqualityComparer<object> KeyComparer { get; private set; } =
        EqualityComparer<object>.Default;

    public object? UnassignedSentinel { get; private set; }

    public bool HasUnassignedSentinel { get; private set; }

    public Func<TDtoChild, Guid?>? TemporaryGetter { get; private set; }

    public Action<TDtoChild, Guid?>? TemporarySetter { get; private set; }

    public bool TemporaryDeclared { get; private set; }

    public EntityPlanBase ChildPlan { get; private set; } = null!;

    public SparseEntityRemoveKind RemoveKind { get; private set; }

    public Action<DbContext, TEntityChild>? RemoveHandler { get; private set; }

    public Action<TEntityChild>? SoftDeleteMark { get; private set; }

    public Func<TEntityChild, bool>? IsDeletedPredicate { get; private set; }

    public Action<TEntityChild, int>? OrderSetter { get; private set; }

    public Func<TDtoChild, TEntityChild> CreateFactory { get; private set; } = null!;

    public Action<object, object?>? CollectionSetter { get; private set; }

    public Func<object> CollectionFactory { get; private set; } =
        static () => new List<TEntityChild>();

    public bool ForeignKeyRequired { get; private set; }

    public bool Owned { get; private set; }

    public IEntityType ChildEntityType { get; private set; } = null!;

    public Type KeyType { get; private set; } = null!;

    public LambdaExpression? EntityKeySelectorExpression { get; private set; }

    public Expression<Func<TEntityChild, bool>>? IsDeletedExpression { get; private set; }

    public LambdaExpression? OrderSelectorExpression { get; private set; }

    /// <summary>Restores prior identities and assigns keys for created entities in this scope.</summary>
    public void AssignTemporaryKeys(
        DbContext context,
        IEnumerable<TDtoChild>? baseline,
        IEnumerable<TDtoChild>? authoritative,
        SparseEntityApplyResult result,
        string parentPath
    )
    {
        if (authoritative is null)
        {
            return;
        }

        var collectionPath = string.IsNullOrEmpty(parentPath) ? DtoName : $"{parentPath}.{DtoName}";
        var baselineByKey = new Dictionary<object, TDtoChild>(KeyComparer);
        if (baseline is not null)
        {
            foreach (var item in baseline)
            {
                var key = DtoKey(item);
                if (key is not null)
                {
                    baselineByKey.TryAdd(key, item);
                }
            }
        }

        var createdByKey = new Dictionary<object, (TEntityChild Entity, string ScopeKey)>(
            KeyComparer
        );
        foreach (var scope in result.GetCreatedScopeKeys(collectionPath))
        {
            if (scope.Key is not TEntityChild entity)
            {
                continue;
            }

            var key = EntityKey(context, entity);
            if (key is not null)
            {
                createdByKey.TryAdd(key, (entity, scope.Value));
            }
        }

        var temporaryByEntity = new Dictionary<object, Guid>(ReferenceEqualityComparer.Instance);
        foreach (var temporary in result.GetCreated(collectionPath))
        {
            temporaryByEntity[temporary.Value] = temporary.Key;
        }

        var index = 0;
        foreach (var item in authoritative)
        {
            var key = DtoKey(item);
            if (key is not null && baselineByKey.TryGetValue(key, out var prior))
            {
                var priorTemporary = TemporaryGetter?.Invoke(prior);
                if (priorTemporary is not null && priorTemporary != Guid.Empty)
                {
                    SetTemporaryKey(item, priorTemporary.Value, collectionPath);
                }
            }

            string? createdScopeKey = null;
            TEntityChild? createdEntity = null;
            if (key is not null && createdByKey.TryGetValue(key, out var created))
            {
                createdEntity = created.Entity;
                createdScopeKey = created.ScopeKey;
            }

            if (
                createdEntity is not null
                && temporaryByEntity.TryGetValue(createdEntity, out var createdTemporary)
            )
            {
                SetTemporaryKey(item, createdTemporary, collectionPath);
            }

            TDtoChild? baselineItem = null;
            if (key is not null)
            {
                baselineByKey.TryGetValue(key, out baselineItem);
            }
            var temporary = TemporaryGetter?.Invoke(item);
            string childScopeKey;
            if (createdScopeKey is not null)
            {
                childScopeKey = createdScopeKey;
            }
            else if (key is not null && !IsUnassigned(key))
            {
                childScopeKey = key.ToString()!;
            }
            else if (temporary is not null && temporary != Guid.Empty)
            {
                childScopeKey = temporary.Value.ToString();
            }
            else
            {
                childScopeKey = index.ToString();
            }

            ChildPlan.AssignTemporaryKeys(
                context,
                baselineItem,
                item,
                result,
                $"{collectionPath}[{childScopeKey}]"
            );
            index++;
        }
    }

    /// <summary>Builds and validates the runtime for one parent collection.</summary>
    public static CollectionRuntime<TDtoChild, TEntityChild> Create<TDto, TEntity>(
        string dtoName,
        Expression<Func<TDto, IEnumerable<TDtoChild>?>> dtoSelector,
        Expression<Func<TEntity, IEnumerable<TEntityChild>?>> entitySelector,
        CollectionDeclaration<TDtoChild, TEntityChild> declaration,
        PlanBuildSession session,
        PlanLevel level
    )
        where TDto : class
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(dtoSelector);
        ArgumentNullException.ThrowIfNull(entitySelector);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(level);
        var runtime = new CollectionRuntime<TDtoChild, TEntityChild>
        {
            DtoName = dtoName,
            NavigationName = MemberPath.Parse(entitySelector).SingleName("entity"),
        };
        runtime.ResolveNavigation(session, level);
        var transitionKeyType = runtime.ResolveChangeSetContract<TDto>();
        runtime.ResolveKeys(session, transitionKeyType, declaration);
        runtime.ResolveChildPlan(session, level, declaration);
        runtime.ResolvePolicies(declaration);
        runtime.ResolveCollectionSetter<TEntity>();
        runtime.IsDeletedExpression = declaration.IsDeletedExpression;
        runtime.OrderSelectorExpression = declaration.OrderSelectorExpression;
        return runtime;
    }

    /// <summary>Gets whether a boxed key is the explicitly declared unassigned sentinel.</summary>
    /// <remarks>
    /// Never inferred from <c>default(TKey)</c>: without an explicit
    /// declaration this always returns false, so a stable default key is a
    /// valid assigned identity.
    /// </remarks>
    public bool IsUnassigned(object? key) =>
        HasUnassignedSentinel && KeyComparer.Equals(key!, UnassignedSentinel!);

    private void ResolveNavigation(PlanBuildSession session, PlanLevel level)
    {
        // Skip navigations live outside FindNavigation: check them first so a
        // many-to-many member fails with actionable guidance instead of a
        // missing-navigation report.
        if (level.EntityType.FindSkipNavigation(NavigationName) is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionNotSupported,
                DtoName,
                $"The entity member '{level.EntityType.ClrType.Name}.{NavigationName}' is a many-to-many skip navigation. Keyed synchronization supports only navigations with a direct foreign key; map the join entity explicitly or Ignore() the member."
            );
        }

        var navigation = level.EntityType.FindNavigation(NavigationName);
        if (navigation is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The entity type '{level.EntityType.ClrType.Name}' has no navigation '{NavigationName}'."
            );
        }

        if (!navigation.IsCollection)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The entity member '{level.EntityType.ClrType.Name}.{NavigationName}' is not a collection navigation. Register it with Object() or Member()."
            );
        }

        if (session.Model.FindEntityType(typeof(TEntityChild)) is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The EF model has no entity type '{typeof(TEntityChild).Name}'."
            );
        }

        if (!typeof(TEntityChild).IsAssignableFrom(navigation.TargetEntityType.ClrType))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The navigation '{level.EntityType.ClrType.Name}.{NavigationName}' targets '{navigation.TargetEntityType.ClrType.Name}', not '{typeof(TEntityChild).Name}'."
            );
        }

        session.ClaimNavigation(level.EntityType, NavigationName, DtoName);
        ChildEntityType = navigation.TargetEntityType;
        var foreignKey = navigation.ForeignKey;
        Owned = foreignKey.IsOwnership;
        // Requiredness comes from the relationship (IForeignKey.IsRequired), not
        // from individual composite FK property nullability.
        ForeignKeyRequired = foreignKey.IsRequired;
        RelationshipDetail = DescribeRelationship(foreignKey);
    }

    private static string DescribeRelationship(IForeignKey foreignKey)
    {
        if (foreignKey.IsOwnership)
        {
            return $"owned relationship with DeleteBehavior {foreignKey.DeleteBehavior}";
        }

        if (foreignKey.IsRequired)
        {
            return $"required relationship with DeleteBehavior {foreignKey.DeleteBehavior}";
        }

        return $"optional relationship with DeleteBehavior {foreignKey.DeleteBehavior}";
    }

    private Type ResolveChangeSetContract<TDto>()
        where TDto : class
    {
        var dtoProperty = typeof(TDto).GetProperty(
            DtoName,
            BindingFlags.Public | BindingFlags.Instance
        );
        if (
            dtoProperty is null
            || !dtoProperty.CanRead
            || !typeof(IEnumerable<TDtoChild>).IsAssignableFrom(dtoProperty.PropertyType)
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO type '{typeof(TDto).Name}' has no readable collection property '{DtoName}' of '{typeof(TDtoChild).Name}' elements."
            );
        }

        var parameter = Expression.Parameter(typeof(object), "dto");
        SnapshotReader = Expression
            .Lambda<Func<object, IEnumerable<TDtoChild>?>>(
                Expression.Convert(
                    Expression.Property(Expression.Convert(parameter, typeof(TDto)), dtoProperty),
                    typeof(IEnumerable<TDtoChild>)
                ),
                parameter
            )
            .Compile();

        var changeSetType = typeof(TDto)
            .GetNestedTypes()
            .FirstOrDefault(static type =>
                type.Name == "ChangeSet" && typeof(ISparseChangeSet).IsAssignableFrom(type)
            );
        var transition = changeSetType?.GetProperty(
            DtoName,
            BindingFlags.Public | BindingFlags.Instance
        );
        var keyedInterface = transition is null
            ? null
            : KeyedCollectionView.FindKeyedInterface(transition.PropertyType);
        if (changeSetType is null || transition is null || keyedInterface is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO member '{DtoName}' has no generated keyed transition. Keyed synchronization needs a [SparseKey] element with generator support; unkeyed or scalar sequences must fail explicitly instead of replacing wholesale."
            );
        }

        var arguments = keyedInterface.GetGenericArguments();
        if (arguments[1] != typeof(TDtoChild))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The generated transition for '{DtoName}' carries '{arguments[1].Name}' elements, not '{typeof(TDtoChild).Name}'."
            );
        }

        if (!typeof(ISparseChangeSet).IsAssignableFrom(arguments[2]))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The element transition '{arguments[2].Name}' is not a change set, so nested child edits cannot recurse."
            );
        }

        Fetch = KeyedCollectionView.CreateFetcher<TDtoChild>(keyedInterface);
        return arguments[0];
    }

    private void ResolveKeys(
        PlanBuildSession session,
        Type transitionKeyType,
        CollectionDeclaration<TDtoChild, TEntityChild> declaration
    )
    {
        KeyType = transitionKeyType;
        var generated = declaration.GeneratedElementDefaults;
        if (generated?.KeyType is null || generated.DtoKeySelector is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO element '{typeof(TDtoChild).Name}' has no generated key metadata. Mark exactly one readable property with [SparseKey] and reference SparseFragments.Generator as an analyzer."
            );
        }

        if (generated.KeyType != transitionKeyType)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The generated DTO key type '{generated.KeyType.Name}' does not match the keyed transition type '{transitionKeyType.Name}'."
            );
        }

        if (declaration.KeyType is not null)
        {
            if (declaration.KeyType != transitionKeyType)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    DtoName,
                    $"The configured Key() type '{declaration.KeyType.Name}' does not match the generated transition key '{transitionKeyType.Name}'."
                );
            }

            DtoKey = BoxSelector<TDtoChild>(declaration.DtoKeySelector!);
            var boxedEntityKey = BoxSelector<TEntityChild>(declaration.EntityKeySelector!);
            EntityKey = (_, child) => boxedEntityKey(child);
            if (
                EntityKeySelectorExpressions.TryGet(
                    declaration.EntityKeySelector!,
                    out var entityKeyExpression
                )
            )
            {
                EntityKeySelectorExpression = entityKeyExpression;
            }

            KeyComparer = CreateComparer(transitionKeyType);
        }
        else
        {
            DtoKey = BoxSelector<TDtoChild>(generated.DtoKeySelector);
            EntityKey = ResolveDefaultEntityKey(session, transitionKeyType);
            KeyComparer = CreateComparer(transitionKeyType);
        }

        // Never infer from default(TKey): only an explicit model or builder
        // declaration marks a key unassigned. Builder declaration wins so
        // computed/composite keys can declare a sentinel without an attribute.
        if (declaration.HasUnassignedKeyValue)
        {
            UnassignedSentinel = declaration.UnassignedKeyValue;
            HasUnassignedSentinel = true;
        }
        else if (generated.HasUnassignedKeyValue)
        {
            UnassignedSentinel = generated.UnassignedKeyValue;
            HasUnassignedSentinel = true;
        }
        else
        {
            UnassignedSentinel = null;
            HasUnassignedSentinel = false;
        }
        if (generated.TemporaryKeySelector is not null)
        {
            TemporaryDeclared = true;
            TemporaryGetter = (Func<TDtoChild, Guid?>)generated.TemporaryKeySelector;
            TemporarySetter = generated.TemporaryKeySetter as Action<TDtoChild, Guid?>;
        }
    }

    private void SetTemporaryKey(TDtoChild dto, Guid temporaryKey, string collectionPath)
    {
        if (TemporarySetter is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                collectionPath,
                $"The DTO element '{typeof(TDtoChild).Name}' declares [SparseTemporaryKey] but its property has no public setter."
            );
        }

        TemporarySetter(dto, temporaryKey);
    }

    private Func<DbContext, TEntityChild, object?> ResolveDefaultEntityKey(
        PlanBuildSession session,
        Type keyType
    )
    {
        var entityType = session.Model.FindEntityType(typeof(TEntityChild));
        var childPrimary = entityType?.FindPrimaryKey();
        if (childPrimary is not null && childPrimary.Properties.Count == 1)
        {
            var property = childPrimary.Properties[0];
            if (property.ClrType == keyType)
            {
                var propertyName = property.Name;
                var parameter = Expression.Parameter(typeof(TEntityChild), "entity");
                var propertyMethod = typeof(EF)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method =>
                        method.Name == nameof(EF.Property)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 2
                    )
                    .MakeGenericMethod(keyType);
                var body = Expression.Call(
                    propertyMethod,
                    Expression.Convert(parameter, typeof(object)),
                    Expression.Constant(propertyName)
                );
                EntityKeySelectorExpression = Expression.Lambda(body, parameter);
                return (context, child) => context.Entry(child).Property(propertyName).CurrentValue;
            }
        }

        throw new SparseEntityMappingException(
            SparseEntityMappingFailure.MetadataMismatch,
            DtoName,
            $"The DTO key '{keyType.Name}' does not match the single-property primary key of '{typeof(TEntityChild).Name}'. Declare Key() to map a computed, composite, or alternate identity."
        );
    }

    private void ResolveChildPlan(
        PlanBuildSession session,
        PlanLevel level,
        CollectionDeclaration<TDtoChild, TEntityChild> declaration
    )
    {
        var child = new SparseEntityMapBuilder<TEntityChild, TDtoChild>
        {
            GeneratedElementConfigurators = declaration.ElementStash,
            GeneratedObjectConfigurators = declaration.ObjectStash,
            GeneratedKeyMembers =
                declaration.GeneratedElementDefaults?.KeyMemberNames.ToHashSet(
                    StringComparer.Ordinal
                ) ?? new HashSet<string>(StringComparer.Ordinal),
            GeneratedKeyDeclared = declaration.GeneratedElementDefaults?.KeyType is not null,
            IsNestedScope = true,
        };
        child.InheritDefaultMissing(level.Registrations.DefaultMissing);
        if (declaration.GeneratedElementDefaults is not null)
        {
            child.RunGenerated(() => declaration.GeneratedElementDefaults.Configure(child));
            child.GeneratedDefaultsApplied = true;
        }

        declaration.ElementConfigure(child);
        var childEntityType = session.Model.FindEntityType(typeof(TEntityChild))!;
        ChildPlan = EntityPlan<TEntityChild, TDtoChild>.Build(
            new PlanBuildSession(session.Model),
            child,
            childEntityType
        );
    }

    private void ResolvePolicies(CollectionDeclaration<TDtoChild, TEntityChild> declaration)
    {
        if (declaration.RemoveKind is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The collection '{DtoName}' declares no removal policy. Collection removal is never physical deletion by default: declare Delete(), Detach(), SoftDelete(), or Remove()."
            );
        }

        if (
            declaration.RemoveKind == SparseEntityRemoveKind.Detach
            && (ForeignKeyRequired || Owned)
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The collection '{DtoName}' cannot Detach(): it is a {RelationshipDetail}. Declare Delete(), SoftDelete(), or Remove()."
            );
        }

        if (declaration.RemoveKind == SparseEntityRemoveKind.SoftDelete && Owned)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The collection '{DtoName}' cannot SoftDelete(): it is a {RelationshipDetail} whose rows live and die with the parent."
            );
        }

        RemoveKind = declaration.RemoveKind.Value;
        RemoveHandler = declaration.RemoveHandler;
        SoftDeleteMark = declaration.SoftDeleteMark;
        IsDeletedPredicate = declaration.IsDeletedPredicate;
        OrderSetter = declaration.OrderSetter;
        CreateFactory = declaration.CreateFactory ?? CreateWithConstructor();
    }

    private void ResolveCollectionSetter<TEntity>()
        where TEntity : class
    {
        var property = typeof(TEntity).GetProperty(
            NavigationName,
            BindingFlags.Public | BindingFlags.Instance
        );
        if (property is null || !property.CanWrite)
        {
            return;
        }

        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        CollectionSetter = Expression
            .Lambda<Action<object, object?>>(
                Expression.Assign(
                    Expression.Property(Expression.Convert(instance, typeof(TEntity)), property),
                    Expression.Convert(value, property.PropertyType)
                ),
                instance,
                value
            )
            .Compile();
        ResolveCollectionFactory(property.PropertyType);
    }

    /// <summary>Builds the null-navigation factory for the declared type.</summary>
    /// <remarks>
    /// Interface-typed navigations take a plain list. Concrete collection
    /// types are instantiated as declared (so an ObservableCollection stays
    /// one) when they carry a public parameterless constructor; anything
    /// else fails here at plan creation instead of casting blindly during
    /// execution after the root already attached.
    /// </remarks>
    private void ResolveCollectionFactory(Type declared)
    {
        if (declared.IsInterface || declared == typeof(List<TEntityChild>))
        {
            CollectionFactory = static () => new List<TEntityChild>();
            return;
        }

        if (!typeof(System.Collections.IList).IsAssignableFrom(declared))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The navigation '{NavigationName}' is declared as '{declared.Name}', which is not an IList collection, so keyed synchronization cannot materialize or reorder it."
            );
        }

        var constructor = declared.GetConstructor(Type.EmptyTypes);
        if (constructor is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The navigation '{NavigationName}' is null and declared as '{declared.Name}' with no public parameterless constructor, so added children have nowhere to attach. Declare it as List<{typeof(TEntityChild).Name}> or give it a public parameterless constructor."
            );
        }

        CollectionFactory = Expression
            .Lambda<Func<object>>(Expression.Convert(Expression.New(constructor), typeof(object)))
            .Compile();
    }

    private static Func<TDtoChild, TEntityChild> CreateWithConstructor()
    {
        var constructor = typeof(TEntityChild).GetConstructor(Type.EmptyTypes);
        if (constructor is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                null,
                $"The child entity type '{typeof(TEntityChild).Name}' has no public parameterless constructor, so an added child cannot be materialized. Declare Create()."
            );
        }

        return _ =>
            (TEntityChild)constructor.Invoke(null)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                null,
                $"The parameterless constructor of '{typeof(TEntityChild).Name}' returned null."
            );
    }

    private static Func<TElement, object?> BoxSelector<TElement>(Delegate selector)
    {
        var parameter = Expression.Parameter(typeof(TElement), "element");
        var call = Expression.Invoke(Expression.Constant(selector), parameter);
        return Expression
            .Lambda<Func<TElement, object?>>(Expression.Convert(call, typeof(object)), parameter)
            .Compile();
    }

    /// <summary>Builds a boxed key comparer for one stable key type.</summary>
    public static IEqualityComparer<object> CreateComparer(Type keyType)
    {
        var method = typeof(CollectionRuntime<TDtoChild, TEntityChild>)
            .GetMethod(nameof(CreateComparerCore), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(keyType);
        return (IEqualityComparer<object>)method.Invoke(null, null)!;
    }

    /// <summary>Public factory entry so plan-time generic closing needs no accessibility bypass.</summary>
    public static IEqualityComparer<object> CreateComparerCore<TKey>() =>
        new BoxedKeyComparer<TKey>(EqualityComparer<TKey>.Default);

    private sealed class BoxedKeyComparer<TKey> : IEqualityComparer<object>
    {
        private readonly IEqualityComparer<TKey> _inner;

        public BoxedKeyComparer(IEqualityComparer<TKey> inner)
        {
            _inner = inner;
        }

        bool IEqualityComparer<object>.Equals(object? left, object? right)
        {
            if (left is not TKey typedLeft || right is not TKey typedRight)
            {
                return ReferenceEquals(left, right);
            }

            return _inner.Equals(typedLeft, typedRight);
        }

        int IEqualityComparer<object>.GetHashCode(object value) =>
            value is TKey typed ? _inner.GetHashCode(typed) : 0;
    }
}
