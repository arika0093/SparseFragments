using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// A DTO member handled by a specialized writer instead of a property write.
/// </summary>
internal sealed class WriteNode<TDto, TEntity, TDtoValue> : MemberNode
{
    private readonly Func<TDto, TDtoValue?> _dtoGetter;
    private readonly Action<TEntity, Optional<TDtoValue?>> _writer;

    public WriteNode(
        Expression<Func<TDto, TDtoValue?>> dto,
        Action<TEntity, Optional<TDtoValue?>> writer
    )
        : base(MemberPath.Parse(dto).SingleName("DTO"))
    {
        ArgumentNullException.ThrowIfNull(writer);
        var property = typeof(TDto).GetProperty(
            DtoName,
            BindingFlags.Public | BindingFlags.Instance
        );
        if (property is null || !property.CanRead)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO type '{typeof(TDto).Name}' has no readable property '{DtoName}'."
            );
        }

        if (PlanBuildSession.IsCollectionType(property.PropertyType))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO member '{DtoName}' is collection-shaped. Register it with Collection(), not Write()."
            );
        }

        _writer = writer;
        _dtoGetter = dto.Compile();
    }

    public override void Resolve(PlanBuildSession session, PlanLevel level)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(level);
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        if (!state.Changes.TryGetValueChange<TDtoValue>(DtoName, out var change))
        {
            if (state.HasRecords(DtoName))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    state.DisplayPath(DtoName),
                    $"The DTO member '{DtoName}' changed but its transition is unreadable. The change set does not match the mapped DTO type."
                );
            }

            return;
        }

        if (!change.IsChanged)
        {
            return;
        }

        var entity = (TEntity)state.Entity;
        var after = change.After;
        Optional<TDtoValue?> value = after.IsPresent
            ? Optional<TDtoValue?>.Present(after.Value)
            : Optional<TDtoValue?>.Missing;
        actions.Add(() => _writer(entity, value));
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        var entity = (TEntity)state.Entity;
        var value = Optional<TDtoValue?>.Present(_dtoGetter((TDto)state.Desired));
        actions.Add(() => _writer(entity, value));
    }
}

/// <summary>
/// A nested DTO object mapped onto a reference navigation with a scoped child plan.
/// </summary>
internal sealed class ObjectNode<TDto, TEntity, TDtoChild, TEntityChild> : MemberNode
    where TDtoChild : class
    where TEntityChild : class
{
    private readonly Func<TDto, TDtoChild?> _dtoGetter;
    private readonly Func<TEntity, TEntityChild?> _entityGetter;
    private readonly Action<TEntity, TEntityChild?> _entitySetter;
    private readonly Func<PlanBuildSession, IEntityType, EntityPlanBase> _planFactory;
    private readonly SparseEntityMissingPolicy _objectMissing;

    private EntityPlanBase _childPlan = null!;
    private bool _navigationNullable;
    private ConstructorInfo? _childConstructor;

    public ObjectNode(
        Expression<Func<TDto, TDtoChild?>> dto,
        Expression<Func<TEntity, TEntityChild?>> entity,
        SparseEntityMapBuilderBase child,
        Func<PlanBuildSession, IEntityType, EntityPlanBase> planFactory,
        SparseEntityMissingPolicy objectMissing
    )
        : base(MemberPath.Parse(dto).SingleName("DTO"))
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(planFactory);
        var navigationName = MemberPath.Parse(entity).SingleName("entity");
        var dtoProperty = typeof(TDto).GetProperty(
            DtoName,
            BindingFlags.Public | BindingFlags.Instance
        );
        if (dtoProperty is null || !dtoProperty.CanRead)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO type '{typeof(TDto).Name}' has no readable property '{DtoName}'."
            );
        }

        NavigationName = navigationName;
        _dtoGetter = dto.Compile();
        _entityGetter = entity.Compile();
        _entitySetter = CompileSetter(entity);
        _planFactory = planFactory;
        _objectMissing = objectMissing;
    }

    public string NavigationName { get; }

    public override void Resolve(PlanBuildSession session, PlanLevel level)
    {
        var navigation = level.EntityType.FindNavigation(NavigationName);
        if (navigation is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The entity type '{level.EntityType.ClrType.Name}' has no navigation '{NavigationName}'."
            );
        }

        if (navigation.IsCollection)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The entity member '{level.EntityType.ClrType.Name}.{NavigationName}' is a collection navigation. Register it with Collection(), not Object()."
            );
        }

        var childEntityType =
            session.Model.FindEntityType(typeof(TEntityChild))
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The EF model has no entity type '{typeof(TEntityChild).Name}'."
            );
        if (!typeof(TEntityChild).IsAssignableFrom(navigation.TargetEntityType.ClrType))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The navigation '{level.EntityType.ClrType.Name}.{NavigationName}' targets '{navigation.TargetEntityType.ClrType.Name}', not '{typeof(TEntityChild).Name}'."
            );
        }

        session.ClaimNavigation(level.EntityType, NavigationName, DtoName);
        _navigationNullable = ReadNavigationNullability(navigation);
        _childConstructor = ResolveConstructor(childEntityType.ClrType, DtoName);
        _childPlan = _planFactory(session, childEntityType);
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        var found = state.Changes.TryGetNestedChange(DtoName, out var child);
        var whole = state.SingleSegmentRecord(DtoName);
        var deeper = state.HasDeeperRecords(DtoName);
        if (!found)
        {
            if (!state.HasRecords(DtoName))
            {
                return;
            }

            if (whole is not null && !deeper && TryHandleWholeObject(state, actions, whole))
            {
                return;
            }

            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' changed but its nested transition is unreadable. The change set does not match the mapped DTO type."
            );
        }

        if (whole is not null && !deeper && whole.After.IsPresent && whole.After.Value is not null)
        {
            WriteAdded(state, actions);
            return;
        }

        if (whole is not null && TryHandleWholeObject(state, actions, whole))
        {
            return;
        }

        if (!deeper && (child is null || child.IsEmpty) && whole is null)
        {
            return;
        }

        var desired = _dtoGetter((TDto)state.Desired);
        if (desired is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' has nested edits but the desired value is null."
            );
        }

        var target = MaterializeTarget(state.Context, state.Entity);
        var entry = PlanApplyState.AttachSingle(state.Context, target);
        var sub = new PlanApplyState(
            state.Context,
            target,
            entry,
            desired,
            child!,
            state.DisplayPath(DtoName),
            isNested: true
        );
        _childPlan.Prepare(sub, actions);
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        var desired = _dtoGetter((TDto)state.Desired);
        if (desired is null)
        {
            return;
        }

        var target = MaterializeTarget(state.Context, state.Entity);
        var entry = PlanApplyState.AttachSingle(state.Context, target);
        var sub = new PlanWriteState(
            state.Context,
            target,
            entry,
            desired,
            state.DisplayPath(DtoName)
        );
        _childPlan.WriteAll(sub, actions);
    }

    private void WriteAdded(PlanApplyState state, List<Action> actions)
    {
        var desired =
            _dtoGetter((TDto)state.Desired)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' was added but the desired value is null."
            );
        var target = MaterializeTarget(state.Context, state.Entity);
        var entry = PlanApplyState.AttachSingle(state.Context, target);
        var sub = new PlanWriteState(
            state.Context,
            target,
            entry,
            desired,
            state.DisplayPath(DtoName)
        );
        _childPlan.WriteAll(sub, actions);
    }

    private bool TryHandleWholeObject(
        PlanApplyState state,
        List<Action> actions,
        SparseFragments.Generated.SparseChangeRecord whole
    )
    {
        if (whole.After.IsPresent && whole.After.Value is not null)
        {
            return false;
        }

        if (!whole.After.IsPresent)
        {
            switch (_objectMissing)
            {
                case SparseEntityMissingPolicy.Ignore:
                    return true;
                case SparseEntityMissingPolicy.SetNull:
                case SparseEntityMissingPolicy.SetDefault:
                    RequireNavigationNullable(state);
                    actions.Add(() => _entitySetter((TEntity)state.Entity, null));
                    return true;
                default:
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.MissingNotHandled,
                        state.DisplayPath(DtoName),
                        $"The DTO member '{DtoName}' changed to missing. Missing is not null: declare Object() with an objectMissing policy or MissingDefault() to handle it."
                    );
            }
        }

        RequireNavigationNullable(state);
        actions.Add(() => _entitySetter((TEntity)state.Entity, null));
        return true;
    }

    private TEntityChild MaterializeTarget(DbContext context, object entityObj)
    {
        var entity = (TEntity)entityObj;
        var target = _entityGetter(entity);
        if (target is not null)
        {
            return target;
        }

        var created =
            (TEntityChild?)_childConstructor!.Invoke(null)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                DtoName,
                $"The nested entity for '{DtoName}' is null and its parameterless constructor returned null."
            );
        _entitySetter(entity, created);
        var entry = context.Entry(created);
        if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            context.Add(created);
        }

        return created;
    }

    private void RequireNavigationNullable(PlanApplyState state)
    {
        if (!_navigationNullable)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NullNotAllowed,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' resolved to null, but the navigation '{typeof(TEntity).Name}.{NavigationName}' does not accept null."
            );
        }
    }

    private static Action<TEntity, TEntityChild?> CompileSetter(
        Expression<Func<TEntity, TEntityChild?>> entity
    )
    {
        var body = entity.Body;
        if (
            body is UnaryExpression unary
            && (
                unary.NodeType == ExpressionType.Convert
                || unary.NodeType == ExpressionType.ConvertChecked
            )
        )
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression member || member.Expression is not ParameterExpression)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                "The entity navigation selector must be a single property or field."
            );
        }

        var value = Expression.Parameter(typeof(TEntityChild), "value");
        var assign = Expression.Assign(
            Expression.MakeMemberAccess(entity.Parameters[0], member.Member),
            value
        );
        return Expression
            .Lambda<Action<TEntity, TEntityChild?>>(assign, entity.Parameters[0], value)
            .Compile();
    }

    private static bool ReadNavigationNullability(INavigation navigation)
    {
        var property = navigation.PropertyInfo;
        if (property is null)
        {
            return false;
        }

        try
        {
            return new NullabilityInfoContext().Create(property).WriteState
                == NullabilityState.Nullable;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ConstructorInfo ResolveConstructor(Type type, string dtoName)
    {
        return type.GetConstructor(Type.EmptyTypes)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                dtoName,
                $"The nested entity type '{type.Name}' has no public parameterless constructor, so a missing instance cannot be materialized."
            );
    }
}
