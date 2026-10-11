using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseFragments.EntityFrameworkCore.Mapping;

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
        Action<TEntityChild, int>? orderSetter,
        Type? keyType,
        Delegate? dtoKeySelector,
        Delegate? entityKeySelector,
        object? unassignedKeyValue = null,
        bool hasUnassignedKeyValue = false
    )
    {
        ElementConfigure = elementConfigure;
        CreateFactory = createFactory;
        RemoveKind = removeKind;
        RemoveHandler = removeHandler;
        SoftDeleteMark = softDeleteMark;
        OrderSetter = orderSetter;
        KeyType = keyType;
        DtoKeySelector = dtoKeySelector;
        EntityKeySelector = entityKeySelector;
        UnassignedKeyValue = unassignedKeyValue;
        HasUnassignedKeyValue = hasUnassignedKeyValue;
    }

    public Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> ElementConfigure { get; }

    public Func<TDtoChild, TEntityChild>? CreateFactory { get; }

    public SparseEntityRemoveKind? RemoveKind { get; }

    public Action<DbContext, TEntityChild>? RemoveHandler { get; }

    public Action<TEntityChild>? SoftDeleteMark { get; }

    public Action<TEntityChild, int>? OrderSetter { get; }

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

    public bool TemporaryDeclared { get; private set; }

    public EntityPlanBase ChildPlan { get; private set; } = null!;

    public SparseEntityRemoveKind RemoveKind { get; private set; }

    public Action<DbContext, TEntityChild>? RemoveHandler { get; private set; }

    public Action<TEntityChild>? SoftDeleteMark { get; private set; }

    public Action<TEntityChild, int>? OrderSetter { get; private set; }

    public Func<TDtoChild, TEntityChild> CreateFactory { get; private set; } = null!;

    public Action<object, object?>? CollectionSetter { get; private set; }

    public bool ForeignKeyRequired { get; private set; }

    public bool Owned { get; private set; }

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
        var keyProperty =
            typeof(TDtoChild)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(static property =>
                    property.GetCustomAttributes(typeof(SparseKeyAttribute), inherit: true).Length
                    > 0
                )
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO element '{typeof(TDtoChild).Name}' declares no [SparseKey]. Keyed synchronization needs stable DTO identity."
            );
        var keyAttribute = (SparseKeyAttribute)
            keyProperty.GetCustomAttributes(typeof(SparseKeyAttribute), true)[0];
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
            KeyComparer = CreateComparer(transitionKeyType);
        }
        else
        {
            if (keyProperty.PropertyType != transitionKeyType)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    DtoName,
                    $"The DTO key '{typeof(TDtoChild).Name}.{keyProperty.Name}' ({keyProperty.PropertyType.Name}) does not match the generated transition key '{transitionKeyType.Name}'. Declare Key() to map a computed or composite DTO key."
                );
            }

            DtoKey = ClosePropertyReader<TDtoChild>(keyProperty);
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
        else if (keyAttribute.Unassigned is not null)
        {
            UnassignedSentinel = keyAttribute.Unassigned;
            HasUnassignedSentinel = true;
        }
        else
        {
            UnassignedSentinel = null;
            HasUnassignedSentinel = false;
        }
        var temporaries = typeof(TDtoChild)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property =>
                property.GetCustomAttributes(typeof(SparseTemporaryKeyAttribute), true).Length > 0
            )
            .ToList();
        if (temporaries.Count > 1)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO element '{typeof(TDtoChild).Name}' declares multiple [SparseTemporaryKey] properties."
            );
        }

        if (temporaries.Count == 1)
        {
            if (temporaries[0].PropertyType != typeof(Guid?))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    DtoName,
                    $"The temporary identity '{typeof(TDtoChild).Name}.{temporaries[0].Name}' must be Guid?."
                );
            }

            TemporaryDeclared = true;
            var parameter = Expression.Parameter(typeof(TDtoChild), "dto");
            TemporaryGetter = Expression
                .Lambda<Func<TDtoChild, Guid?>>(
                    Expression.Property(parameter, temporaries[0]),
                    parameter
                )
                .Compile();
        }
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
        var child = new SparseEntityMapBuilder<TEntityChild, TDtoChild>();
        child.InheritDefaultMissing(level.Registrations.DefaultMissing);
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

    private static Func<TElement, object?> ClosePropertyReader<TElement>(PropertyInfo property)
    {
        var target = Expression.Parameter(typeof(TElement), "element");
        return Expression
            .Lambda<Func<TElement, object?>>(
                Expression.Convert(Expression.Property(target, property), typeof(object)),
                target
            )
            .Compile();
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
