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
        if (state.TracksAppliedPaths)
        {
            actions.Add(() =>
            {
                _writer(entity, value);
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        state.Context.Entry(entity),
                        null,
                        SparseEntityApplyOperationKind.CustomWrite
                    )
                );
            });
        }
        else
        {
            actions.Add(() => _writer(entity, value));
        }
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        var entity = (TEntity)state.Entity;
        var value = Optional<TDtoValue?>.Present(_dtoGetter((TDto)state.AfterSnapshot));
        if (state.AppliedPaths is not null)
        {
            actions.Add(() =>
            {
                _writer(entity, value);
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        state.Context.Entry(entity),
                        null,
                        SparseEntityApplyOperationKind.CustomWrite
                    )
                );
            });
        }
        else
        {
            actions.Add(() => _writer(entity, value));
        }
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
    private readonly SparseEntityMapBuilderBase _childRegistrations;
    private readonly Func<PlanBuildSession, IEntityType, EntityPlanBase> _planFactory;
    private readonly SparseEntityMissingPolicy _objectMissing;

    private EntityPlanBase _childPlan = null!;
    private bool _navigationNullable;
    private bool _navigationOwned;
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
        _childRegistrations = child;
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
        _navigationOwned = navigation.ForeignKey.IsOwnership;
        _childConstructor = ResolveConstructor(childEntityType.ClrType, DtoName);
        if (!_childRegistrations.HasExplicitDefaultMissing)
        {
            _childRegistrations.InheritDefaultMissing(level.Registrations.DefaultMissing);
        }

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

            IntermediateAccess.EnsureLoaded(
                state.Context,
                state.Entity,
                NavigationName,
                _navigationOwned,
                state.DisplayPath(DtoName)
            );
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

        if (!deeper && (child is null || child.IsEmpty) && whole is null)
        {
            return;
        }

        IntermediateAccess.EnsureLoaded(
            state.Context,
            state.Entity,
            NavigationName,
            _navigationOwned,
            state.DisplayPath(DtoName)
        );

        if (whole is not null && !deeper && whole.After.IsPresent && whole.After.Value is not null)
        {
            WriteAdded(state, actions, whole, child);
            return;
        }

        if (whole is not null && TryHandleWholeObject(state, actions, whole))
        {
            return;
        }

        // Direct ChangeSet sourcing: nested edits recurse through the child
        // ChangeSet. No desired DTO is consulted. Validation runs here without
        // mutating the graph; creation/linking/tracking happen in execution.
        var existing = _entityGetter((TEntity)state.Entity);
        if (existing is not null)
        {
            var entry = PlanApplyState.ValidationEntry(state.Context, existing, actions);
            var sub = new PlanApplyState(
                state.Context,
                existing,
                entry,
                child!,
                state.DisplayPath(DtoName),
                isNested: true,
                sharedCorrelation: state.Correlation,
                appliedPaths: state.AppliedPaths,
                collectionScopeKeys: state.CollectionScopeKeys
            );
            _childPlan.Prepare(sub, actions);
            return;
        }

        // Target is null: validate the child transition against a detached
        // prospective instance, then defer real creation to execution.
        ValidateChildOnProspective(state, child!);
        actions.Add(() =>
        {
            var target = MaterializeTarget(state.Context, state.Entity);
            var entry = PlanApplyState.AttachSingle(state.Context, target);
            var sub = new PlanApplyState(
                state.Context,
                target,
                entry,
                child!,
                state.DisplayPath(DtoName),
                isNested: true,
                sharedCorrelation: state.Correlation,
                appliedPaths: state.AppliedPaths,
                collectionScopeKeys: state.CollectionScopeKeys
            );
            var nestedActions = new List<Action>();
            _childPlan.Prepare(sub, nestedActions);
            foreach (var nested in nestedActions)
            {
                nested();
            }
        });
    }

    public override void CollectIncludes(
        ISparseChangeSet changes,
        IncludePathCollector collector,
        bool writeAll,
        object? afterSnapshot
    )
    {
        ISparseChangeSet? child = null;
        SparseFragments.Generated.SparseChangeRecord? whole = null;
        if (writeAll)
        {
            if (afterSnapshot is not TDto parent)
            {
                return;
            }

            var snapshot = _dtoGetter(parent);
            if (snapshot is null)
            {
                return;
            }

            PushObjectPath(
                collector,
                () =>
                    _childPlan.CollectIncludes(
                        changes,
                        collector,
                        writeAll: true,
                        afterSnapshot: snapshot
                    )
            );
            return;
        }

        var hasRecords = false;
        foreach (var record in changes.EnumerateChangeRecords())
        {
            var segments = record.Path.Segments;
            if (
                segments.Count == 0
                || segments[0].Kind != SparsePathSegmentKind.Member
                || segments[0].Name != DtoName
            )
            {
                continue;
            }

            hasRecords = true;
            if (segments.Count == 1)
            {
                whole = record;
            }
        }

        if (!hasRecords)
        {
            return;
        }

        changes.TryGetNestedChange(DtoName, out child);
        PushObjectPath(
            collector,
            () =>
            {
                if (
                    whole is not null
                    && whole.After.IsPresent
                    && whole.After.Value is not null
                    && TryResolveAfterSnapshot(whole, out var snapshot)
                    && snapshot is not null
                )
                {
                    _childPlan.CollectIncludes(
                        child ?? changes,
                        collector,
                        writeAll: true,
                        snapshot
                    );
                }
                else if (child is not null && !child.IsEmpty)
                {
                    _childPlan.CollectIncludes(
                        child,
                        collector,
                        writeAll: false,
                        afterSnapshot: null
                    );
                }
            }
        );
    }

    public override Expression? BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    )
    {
        if (!context.TryBuildMemberPath(entity, [NavigationName], dtoPath, out var navigation))
        {
            return null;
        }

        var childEntity =
            navigation.Type == typeof(TEntityChild)
                ? navigation
                : Expression.Convert(navigation, typeof(TEntityChild));
        var childProjection = _childPlan.BuildProjection(childEntity, context, dtoPath);
        return Expression.Condition(
            Expression.Equal(navigation, Expression.Constant(null, navigation.Type)),
            Expression.Default(typeof(TDtoChild)),
            childProjection
        );
    }

    private void PushObjectPath(IncludePathCollector collector, Action collectChild)
    {
        collector.PushReference(NavigationName, typeof(TEntityChild), _navigationOwned);
        try
        {
            if (!_navigationOwned)
            {
                collector.IncludeCurrent();
            }

            collectChild();
        }
        finally
        {
            collector.Pop();
        }
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        // Wholesale writes project the nested snapshot from the parent-level
        // After snapshot through the DTO selector, never a reconstructed root
        // DTO and never a blind cast: deeper levels carry their own DTO type.
        if (state.AfterSnapshot is not TDto parent)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(DtoName),
                $"The wholesale snapshot for '{DtoName}' is not '{typeof(TDto).Name}'."
            );
        }

        var snapshot = _dtoGetter(parent);
        if (snapshot is null)
        {
            return;
        }

        var existing = _entityGetter((TEntity)state.Entity);
        if (existing is not null)
        {
            var entry = PlanApplyState.ValidationEntry(state.Context, existing, actions);
            var sub = new PlanWriteState(
                state.Context,
                existing,
                entry,
                snapshot,
                state.DisplayPath(DtoName),
                state.Correlation,
                state.AppliedPaths,
                state.CollectionScopeKeys
            );
            _childPlan.WriteAll(sub, actions);
            return;
        }

        // Validate wholesale content preflight on a detached instance so a
        // later failure leaves the graph untouched; create for real at execution.
        ValidateWholesaleOnProspective(state.Context, state.DisplayPath(DtoName), snapshot);
        actions.Add(() =>
        {
            var target = MaterializeTarget(state.Context, state.Entity);
            var entry = PlanApplyState.AttachSingle(state.Context, target);
            var sub = new PlanWriteState(
                state.Context,
                target,
                entry,
                snapshot,
                state.DisplayPath(DtoName),
                state.Correlation,
                state.AppliedPaths,
                state.CollectionScopeKeys
            );
            var nestedActions = new List<Action>();
            _childPlan.WriteAll(sub, nestedActions);
            foreach (var nested in nestedActions)
            {
                nested();
            }
        });
    }

    public override void AssignTemporaryKeys(
        DbContext context,
        object? baseline,
        object authoritative,
        SparseEntityApplyResult result,
        string path
    )
    {
        var prior = baseline is TDto priorParent ? _dtoGetter(priorParent) : null;
        var current = _dtoGetter((TDto)authoritative);
        if (current is null)
        {
            return;
        }

        var childPath = string.IsNullOrEmpty(path) ? DtoName : $"{path}.{DtoName}";
        _childPlan.AssignTemporaryKeys(context, prior, current, result, childPath);
    }

    private void WriteAdded(
        PlanApplyState state,
        List<Action> actions,
        SparseFragments.Generated.SparseChangeRecord whole,
        ISparseChangeSet? child
    )
    {
        // Nested adds use the complete After snapshot carried by the
        // whole-object record (the DTO itself or its Fragment converted
        // through ToModel); no desired or reconstructed root DTO is consulted.
        // The child ChangeSet of a null-to-object transition carries only the
        // whole-root entry, so member recursion alone would write nothing.
        if (TryResolveAfterSnapshot(whole, out var added) && added is not null)
        {
            ApplySnapshotToInstance(state, actions, added);
            return;
        }

        if (child is not null && !child.IsEmpty)
        {
            ApplyChildToNewInstance(state, actions, child);
            return;
        }

        var actual = whole.After.Value is null ? "null" : whole.After.Value.GetType().FullName;
        throw new SparseEntityMappingException(
            SparseEntityMappingFailure.MetadataMismatch,
            state.DisplayPath(DtoName),
            $"The DTO member '{DtoName}' was added but its After snapshot ('{actual}') carries no usable '{typeof(TDtoChild).Name}' state."
        );
    }

    private void ApplySnapshotToInstance(
        PlanApplyState state,
        List<Action> actions,
        TDtoChild added
    )
    {
        var existing = _entityGetter((TEntity)state.Entity);
        if (existing is not null)
        {
            var entry = PlanApplyState.ValidationEntry(state.Context, existing, actions);
            var sub = new PlanWriteState(
                state.Context,
                existing,
                entry,
                added,
                state.DisplayPath(DtoName),
                state.Correlation,
                state.AppliedPaths,
                state.CollectionScopeKeys
            );
            _childPlan.WriteAll(sub, actions);
            return;
        }

        ValidateWholesaleOnProspective(state.Context, state.DisplayPath(DtoName), added);
        actions.Add(() =>
        {
            var target = MaterializeTarget(state.Context, state.Entity);
            var entry = PlanApplyState.AttachSingle(state.Context, target);
            var sub = new PlanWriteState(
                state.Context,
                target,
                entry,
                added,
                state.DisplayPath(DtoName),
                state.Correlation,
                state.AppliedPaths,
                state.CollectionScopeKeys
            );
            var nestedActions = new List<Action>();
            _childPlan.WriteAll(sub, nestedActions);
            foreach (var nested in nestedActions)
            {
                nested();
            }
        });
    }

    private void ApplyChildToNewInstance(
        PlanApplyState state,
        List<Action> actions,
        ISparseChangeSet child
    )
    {
        var existing = _entityGetter((TEntity)state.Entity);
        if (existing is not null)
        {
            var entry = PlanApplyState.ValidationEntry(state.Context, existing, actions);
            var sub = new PlanApplyState(
                state.Context,
                existing,
                entry,
                child,
                state.DisplayPath(DtoName),
                isNested: true,
                sharedCorrelation: state.Correlation,
                appliedPaths: state.AppliedPaths,
                collectionScopeKeys: state.CollectionScopeKeys
            );
            _childPlan.Prepare(sub, actions);
            return;
        }

        ValidateChildOnProspective(state, child);
        actions.Add(() =>
        {
            var target = MaterializeTarget(state.Context, state.Entity);
            var targetEntry = PlanApplyState.AttachSingle(state.Context, target);
            var sub = new PlanApplyState(
                state.Context,
                target,
                targetEntry,
                child,
                state.DisplayPath(DtoName),
                isNested: true,
                sharedCorrelation: state.Correlation,
                appliedPaths: state.AppliedPaths,
                collectionScopeKeys: state.CollectionScopeKeys
            );
            var nestedActions = new List<Action>();
            _childPlan.Prepare(sub, nestedActions);
            foreach (var nested in nestedActions)
            {
                nested();
            }
        });
    }

    private static bool TryResolveAfterSnapshot(
        SparseFragments.Generated.SparseChangeRecord whole,
        out TDtoChild? snapshot
    )
    {
        if (whole.After.Value is TDtoChild direct)
        {
            snapshot = direct;
            return true;
        }

        // The model-independent projection may carry the generated Fragment
        // instead of the DTO itself; convert through its public ToModel().
        var value = whole.After.Value;
        if (value is not null)
        {
            var toModel = value
                .GetType()
                .GetMethod(
                    "ToModel",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null
                );
            if (toModel is not null && typeof(TDtoChild).IsAssignableFrom(toModel.ReturnType))
            {
                snapshot = (TDtoChild?)toModel.Invoke(value, null);
                return snapshot is not null;
            }
        }

        snapshot = null;
        return false;
    }

    private void ValidateChildOnProspective(PlanApplyState state, ISparseChangeSet child)
    {
        var prospective =
            (TEntityChild?)_childConstructor!.Invoke(null)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                state.DisplayPath(DtoName),
                $"The nested entity for '{DtoName}' is null and its parameterless constructor returned null."
            );
        // Dry-run validation only: the prospective instance is never linked or
        // tracked, so preflight leaves the real graph untouched.
        var probeEntry = state.Context.Entry(prospective);
        var probe = new PlanApplyState(
            state.Context,
            prospective,
            probeEntry,
            child,
            state.DisplayPath(DtoName),
            isNested: true,
            sharedCorrelation: new Dictionary<string, Dictionary<Guid, object>>(
                StringComparer.Ordinal
            )
        );
        _childPlan.Prepare(probe, new List<Action>());
    }

    private void ValidateWholesaleOnProspective(
        DbContext context,
        string displayPath,
        TDtoChild snapshot
    )
    {
        var prospective =
            (TEntityChild?)_childConstructor!.Invoke(null)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                displayPath,
                $"The nested entity for '{DtoName}' is null and its parameterless constructor returned null."
            );
        var probe = new PlanWriteState(
            context,
            prospective,
            context.Entry(prospective),
            snapshot,
            displayPath,
            new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal)
        );
        _childPlan.WriteAll(probe, new List<Action>());
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
                    if (state.TracksAppliedPaths)
                    {
                        actions.Add(() =>
                            state.RecordAppliedPath(
                                new SparseEntityAppliedPath(
                                    state.DisplayPath(DtoName),
                                    SparseEntityApplyOperationKind.MissingIgnored,
                                    typeof(TEntity).FullName,
                                    NavigationName
                                )
                            )
                        );
                    }

                    return true;
                case SparseEntityMissingPolicy.SetNull:
                case SparseEntityMissingPolicy.SetDefault:
                    RequireNavigationNullable(state);
                    if (state.TracksAppliedPaths)
                    {
                        actions.Add(() =>
                        {
                            _entitySetter((TEntity)state.Entity, null);
                            state.RecordAppliedPath(
                                AppliedPathFactory.Create(
                                    state.DisplayPath(DtoName),
                                    state.Context.Entry(state.Entity),
                                    NavigationName,
                                    SparseEntityApplyOperationKind.SetNull
                                )
                            );
                        });
                    }
                    else
                    {
                        actions.Add(() => _entitySetter((TEntity)state.Entity, null));
                    }
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
        if (state.TracksAppliedPaths)
        {
            actions.Add(() =>
            {
                _entitySetter((TEntity)state.Entity, null);
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        state.Context.Entry(state.Entity),
                        NavigationName,
                        SparseEntityApplyOperationKind.SetNull
                    )
                );
            });
        }
        else
        {
            actions.Add(() => _entitySetter((TEntity)state.Entity, null));
        }
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
        // Requiredness comes from EF relationship metadata, which stays
        // correct without nullable annotations; CLR reflection is the fallback.
        try
        {
            var foreignKey = navigation.ForeignKey;
            if (foreignKey is not null)
            {
                return navigation.IsOnDependent
                    ? !foreignKey.IsRequired
                    : !foreignKey.IsRequiredDependent;
            }
        }
        catch (InvalidOperationException)
        {
            // Relationships without a direct foreign key fall through below.
        }

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
