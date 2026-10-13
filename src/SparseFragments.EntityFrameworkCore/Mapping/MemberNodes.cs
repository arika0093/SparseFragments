using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// One mapped DTO member: resolved once against EF metadata, then applied per change set.
/// </summary>
internal abstract class MemberNode
{
    protected MemberNode(string dtoName)
    {
        DtoName = dtoName;
    }

    /// <summary>Gets the root DTO member name.</summary>
    public string DtoName { get; }

    /// <summary>Validates the node against EF metadata and records target claims.</summary>
    public abstract void Resolve(PlanBuildSession session, PlanLevel level);

    /// <summary>Probes the change set and queues writes without mutating the entity.</summary>
    public abstract void Prepare(PlanApplyState state, List<Action> actions);

    /// <summary>Queues writes for every mapped member from the After snapshot.</summary>
    public abstract void WriteAll(PlanWriteState state, List<Action> actions);

    /// <summary>Collects the Include paths needed to apply one mapped member.</summary>
    public virtual void CollectIncludes(
        ISparseChangeSet changes,
        IncludePathCollector collector,
        bool writeAll,
        object? afterSnapshot
    ) { }

    /// <summary>Builds a read expression for this DTO member when it is invertible.</summary>
    public virtual Expression? BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    ) => null;

    /// <summary>Copies baseline temporary identities and correlates created entities into the DTO.</summary>
    public virtual void AssignTemporaryKeys(
        DbContext context,
        object? baseline,
        object authoritative,
        SparseEntityApplyResult result,
        string path
    ) { }
}

/// <summary>
/// A DTO scalar member written through the EF property entry.
/// </summary>
internal sealed class ScalarNode<TDto, TEntity, TDtoValue, TEntityValue> : MemberNode
{
    private readonly Func<TDto, TDtoValue?> _dtoGetter;
    private readonly Func<TDtoValue?, TEntityValue?>? _convert;
    private readonly SparseEntityMissingPolicy? _inlineMissing;
    private readonly string[] _entityChain;

    private IEntityType _targetType = null!;
    private string _entityProperty = string.Empty;
    private bool _entityNullable;
    private SparseEntityMissingPolicy _missing;
    private bool _skipGeneratedKeyMember;
    private readonly List<IntermediateAccess> _intermediates = [];

    public ScalarNode(
        Expression<Func<TDto, TDtoValue?>> dto,
        Expression<Func<TEntity, TEntityValue?>> entity,
        Func<TDtoValue?, TEntityValue?>? convert,
        SparseEntityMissingPolicy? onMissing
    )
        : base(MemberPath.Parse(dto).SingleName("DTO"))
    {
        ArgumentNullException.ThrowIfNull(entity);
        DtoProperty = ResolveDtoProperty(MemberPath.Parse(dto));
        _dtoGetter = dto.Compile();
        _entityChain = MemberPath.Parse(entity).Names;
        _convert = convert;
        _inlineMissing = onMissing;
    }

    public ScalarNode(
        PropertyInfo dtoProperty,
        Func<TDto, TDtoValue?> dtoGetter,
        string entityProperty,
        Func<TDtoValue?, TEntityValue?>? convert,
        SparseEntityMissingPolicy? onMissing
    )
        : base(dtoProperty.Name)
    {
        DtoProperty = dtoProperty;
        _dtoGetter = dtoGetter;
        _entityChain = [entityProperty];
        _convert = convert;
        _inlineMissing = onMissing;
    }

    internal PropertyInfo DtoProperty { get; }

    public override void Resolve(PlanBuildSession session, PlanLevel level)
    {
        if (!PlanBuildSession.IsScalarType(DtoProperty.PropertyType))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO member '{DtoName}' is not scalar. Map nested objects with Object() and collections with Collection()."
            );
        }

        var target = level.EntityType;
        var declarers = new List<(Type Declaring, string Name, bool Owned)>();
        for (var index = 0; index < _entityChain.Length - 1; index++)
        {
            var navigation = target.FindNavigation(_entityChain[index]);
            declarers.Add(
                (target.ClrType, _entityChain[index], navigation?.ForeignKey.IsOwnership == true)
            );
            target = ResolveNavigation(target, _entityChain[index], DtoName);
        }

        var propertyName = _entityChain[^1];
        var property = FindProperty(target, propertyName);
        if (property is null)
        {
            var navigation = target.FindNavigation(propertyName);
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                navigation is null
                    ? $"The entity type '{target.ClrType.Name}' has no property '{propertyName}'."
                    : $"The entity member '{target.ClrType.Name}.{propertyName}' is a navigation. Map it with Object() or Collection()."
            );
        }

        var (protection, detail) = PlanBuildSession.GetProtection(target, property);
        var allowed = level.Registrations.AllowedWrites.Contains(DtoName);
        if (
            !allowed
            && level.Registrations.IsGeneratedDefault(DtoName)
            && (
                level.Registrations.GeneratedKeyMembers.Contains(DtoName)
                || (level.Registrations.GeneratedKeyDeclared && protection == EntityProtection.Key)
            )
        )
        {
            _skipGeneratedKeyMember = true;
            return;
        }

        if (protection != EntityProtection.None && !allowed)
        {
            level.ReportProtected(DtoName, detail);
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Protected,
                DtoName,
                $"The DTO member '{DtoName}' targets {detail} '{target.ClrType.Name}.{property.Name}'. It is never written by default; allow it explicitly with AllowWrite().",
                $"{target.ClrType.Name}.{property.Name}"
            );
        }

        if (protection != EntityProtection.None)
        {
            level.ConsumeAllowedWrite(DtoName);
        }

        if (_convert is null && !ReferenceEquals(property.ClrType, DtoProperty.PropertyType))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The DTO member '{DtoName}' ({Friendly(DtoProperty.PropertyType)}) does not match the entity member '{target.ClrType.Name}.{property.Name}' ({Friendly(property.ClrType)}). Supply a convert callback."
            );
        }

        session.Claim(target, property.Name, DtoName);
        foreach (var declarer in declarers)
        {
            _intermediates.Add(
                IntermediateAccess.Build(declarer.Declaring, declarer.Name, DtoName, declarer.Owned)
            );
        }

        _targetType = target;
        _entityProperty = property.Name;
        _entityNullable = property.IsNullable;

        var missingOverride = ResolveMissingOverride(level);
        _missing = missingOverride ?? _inlineMissing ?? level.Registrations.DefaultMissing;
        ValidateMissingPolicy(
            target,
            property,
            explicitlyConfigured: missingOverride is not null || _inlineMissing is not null
        );
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        if (_skipGeneratedKeyMember)
        {
            return;
        }

        var changes = state.Changes;
        if (!changes.TryGetValueChange<TDtoValue>(DtoName, out var change))
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

        var after = change.After;
        if (!after.IsPresent)
        {
            switch (_missing)
            {
                case SparseEntityMissingPolicy.Ignore:
                    if (state.TracksAppliedPaths)
                    {
                        actions.Add(() =>
                            state.RecordAppliedPath(
                                new SparseEntityAppliedPath(
                                    state.DisplayPath(DtoName),
                                    SparseEntityApplyOperationKind.MissingIgnored,
                                    _targetType.ClrType.FullName,
                                    _entityProperty
                                )
                            )
                        );
                    }

                    return;
                case SparseEntityMissingPolicy.SetDefault:
                    if (default(TEntityValue) is null && !_entityNullable)
                    {
                        throw new SparseEntityMappingException(
                            SparseEntityMappingFailure.NullNotAllowed,
                            state.DisplayPath(DtoName),
                            $"The DTO member '{DtoName}' resolved to missing with a SetDefault policy, but the entity member '{_targetType.ClrType.Name}.{_entityProperty}' does not accept null."
                        );
                    }

                    EnsureIntermediatesLoaded(
                        state.Context,
                        state.Entity,
                        state.DisplayPath(DtoName)
                    );
                    // Preflight must not mutate the graph: intermediate creation
                    // and tracking happen inside the queued execution action.
                    if (state.TracksAppliedPaths)
                    {
                        actions.Add(() =>
                        {
                            var defaultEntry = MaterializeEntry(
                                state.Context,
                                state.Entity,
                                state.Entry
                            );
                            defaultEntry.Property(_entityProperty).CurrentValue =
                                default(TEntityValue);
                            state.RecordAppliedPath(
                                AppliedPathFactory.Create(
                                    state.DisplayPath(DtoName),
                                    defaultEntry,
                                    _entityProperty,
                                    default(TEntityValue) is null
                                        ? SparseEntityApplyOperationKind.SetNull
                                        : SparseEntityApplyOperationKind.Set
                                )
                            );
                        });
                    }
                    else
                    {
                        actions.Add(() =>
                        {
                            var defaultEntry = MaterializeEntry(
                                state.Context,
                                state.Entity,
                                state.Entry
                            );
                            defaultEntry.Property(_entityProperty).CurrentValue =
                                default(TEntityValue);
                        });
                    }
                    return;
                case SparseEntityMissingPolicy.SetNull:
                    RequireNullable(state);
                    EnsureIntermediatesLoaded(
                        state.Context,
                        state.Entity,
                        state.DisplayPath(DtoName)
                    );
                    if (state.TracksAppliedPaths)
                    {
                        actions.Add(() =>
                        {
                            var nullEntry = MaterializeEntry(
                                state.Context,
                                state.Entity,
                                state.Entry
                            );
                            nullEntry.Property(_entityProperty).CurrentValue = null;
                            state.RecordAppliedPath(
                                AppliedPathFactory.Create(
                                    state.DisplayPath(DtoName),
                                    nullEntry,
                                    _entityProperty,
                                    SparseEntityApplyOperationKind.SetNull
                                )
                            );
                        });
                    }
                    else
                    {
                        actions.Add(() =>
                        {
                            var nullEntry = MaterializeEntry(
                                state.Context,
                                state.Entity,
                                state.Entry
                            );
                            nullEntry.Property(_entityProperty).CurrentValue = null;
                        });
                    }
                    return;
                default:
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.MissingNotHandled,
                        state.DisplayPath(DtoName),
                        $"The DTO member '{DtoName}' changed to missing. Missing is not null: declare OnMissing() or MissingDefault() to handle it."
                    );
            }
        }

        // Run user conversion during preflight so invalid values fail before any
        // mutation, but defer navigation materialization to execution.
        TEntityValue? converted;
        try
        {
            converted = _convert is not null
                ? _convert(after.Value)
                : (TEntityValue?)(object?)after.Value;
        }
        catch (Exception exception)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(DtoName),
                $"The converter for '{DtoName}' failed during validation: {exception.Message}."
            );
        }

        if (converted is null && !_entityNullable)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NullNotAllowed,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' resolved to null, but the entity member '{_targetType.ClrType.Name}.{_entityProperty}' does not accept null."
            );
        }

        EnsureIntermediatesLoaded(state.Context, state.Entity, state.DisplayPath(DtoName));
        if (state.TracksAppliedPaths)
        {
            actions.Add(() =>
            {
                var targetEntry = MaterializeEntry(state.Context, state.Entity, state.Entry);
                targetEntry.Property(_entityProperty).CurrentValue = converted;
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        targetEntry,
                        _entityProperty,
                        converted is null
                            ? SparseEntityApplyOperationKind.SetNull
                            : SparseEntityApplyOperationKind.Set
                    )
                );
            });
        }
        else
        {
            actions.Add(() =>
            {
                var targetEntry = MaterializeEntry(state.Context, state.Entity, state.Entry);
                targetEntry.Property(_entityProperty).CurrentValue = converted;
            });
        }
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        if (_skipGeneratedKeyMember)
        {
            return;
        }

        // Wholesale writes copy from the explicit After snapshot carried by the
        // ChangeSet transition, never a reconstructed root DTO.
        var dtoValue = _dtoGetter((TDto)state.AfterSnapshot);
        TEntityValue? converted;
        try
        {
            converted = _convert is not null
                ? _convert(dtoValue)
                : (TEntityValue?)(object?)dtoValue;
        }
        catch (Exception exception)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(DtoName),
                $"The converter for '{DtoName}' failed during validation: {exception.Message}."
            );
        }

        if (converted is null && !_entityNullable)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NullNotAllowed,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' resolved to null, but the entity member '{_targetType.ClrType.Name}.{_entityProperty}' does not accept null."
            );
        }

        EnsureIntermediatesLoaded(state.Context, state.Entity, state.DisplayPath(DtoName));
        if (state.AppliedPaths is not null)
        {
            actions.Add(() =>
            {
                var targetEntry = MaterializeEntry(state.Context, state.Entity, state.Entry);
                targetEntry.Property(_entityProperty).CurrentValue = converted;
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        targetEntry,
                        _entityProperty,
                        converted is null
                            ? SparseEntityApplyOperationKind.SetNull
                            : SparseEntityApplyOperationKind.Set
                    )
                );
            });
        }
        else
        {
            actions.Add(() =>
            {
                var targetEntry = MaterializeEntry(state.Context, state.Entity, state.Entry);
                targetEntry.Property(_entityProperty).CurrentValue = converted;
            });
        }
    }

    public override void CollectIncludes(
        ISparseChangeSet changes,
        IncludePathCollector collector,
        bool writeAll,
        object? afterSnapshot
    )
    {
        if (_skipGeneratedKeyMember)
        {
            return;
        }

        if (!writeAll)
        {
            if (!changes.TryGetValueChange<TDtoValue>(DtoName, out var change) || !change.IsChanged)
            {
                return;
            }

            if (!change.After.IsPresent && _missing == SparseEntityMissingPolicy.Ignore)
            {
                return;
            }
        }

        var pushed = 0;
        try
        {
            foreach (var intermediate in _intermediates)
            {
                collector.PushReference(
                    intermediate.NavigationName,
                    intermediate.TargetType,
                    intermediate.Owned
                );
                pushed++;
                if (!intermediate.Owned)
                {
                    collector.IncludeCurrent();
                }
            }
        }
        finally
        {
            while (pushed > 0)
            {
                collector.Pop();
                pushed--;
            }
        }
    }

    public override Expression? BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    )
    {
        if (_convert is not null)
        {
            return null;
        }

        return context.TryBuildMemberPath(entity, _entityChain, dtoPath, out var value)
            ? value
            : null;
    }

    private void EnsureIntermediatesLoaded(DbContext context, object entity, string displayPath)
    {
        if (_intermediates.Count == 0)
        {
            return;
        }

        object current = entity;
        foreach (var step in _intermediates)
        {
            step.EnsureLoaded(context, current, displayPath);
            var next = step.Getter(current);
            if (next is null)
            {
                return;
            }

            current = next;
        }
    }

    private EntityEntry MaterializeEntry(DbContext context, object entity, EntityEntry rootEntry)
    {
        if (_intermediates.Count == 0)
        {
            return rootEntry;
        }

        object current = entity;
        foreach (var step in _intermediates)
        {
            var next = step.Getter(current);
            if (next is null)
            {
                next = step.Create(DtoName);
                step.Setter(current, next);
                var created = context.Entry(next);
                if (created.State == EntityState.Detached)
                {
                    context.Add(next);
                }
            }
            else
            {
                PlanApplyState.AttachSingle(context, next);
            }

            current = next;
        }

        return PlanApplyState.AttachSingle(context, current);
    }

    private static PropertyInfo ResolveDtoProperty(MemberPath path)
    {
        var name = path.SingleName("DTO");
        var property = typeof(TDto).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                name,
                $"The DTO type '{typeof(TDto).Name}' has no readable property '{name}'."
            );
        }

        return property;
    }

    private static IEntityType ResolveNavigation(IEntityType type, string name, string dtoName)
    {
        var navigation = type.FindNavigation(name);
        if (navigation is null || navigation.IsCollection)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoName,
                navigation is null
                    ? $"The entity type '{type.ClrType.Name}' has no navigation '{name}'."
                    : $"The entity member '{type.ClrType.Name}.{name}' is a collection. Flattening through collections is not supported."
            );
        }

        return navigation.TargetEntityType;
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

    private SparseEntityMissingPolicy? ResolveMissingOverride(PlanLevel level)
    {
        if (level.Registrations.MissingOverrides.TryGetValue(DtoName, out var policy))
        {
            level.ConsumeMissingOverride(DtoName);
            return policy;
        }

        return null;
    }

    private void ValidateMissingPolicy(
        IEntityType target,
        IProperty property,
        bool explicitlyConfigured
    )
    {
        if (!explicitlyConfigured)
        {
            return;
        }

        var effective = _missing;
        if (effective == SparseEntityMissingPolicy.SetNull && !_entityNullable)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The missing policy SetNull cannot apply to '{target.ClrType.Name}.{property.Name}', which does not accept null."
            );
        }

        if (
            effective == SparseEntityMissingPolicy.SetDefault
            && !_entityNullable
            && property.ClrType.IsClass
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                DtoName,
                $"The missing policy SetDefault cannot apply to '{target.ClrType.Name}.{property.Name}', whose default is null but which does not accept null."
            );
        }
    }

    private void RequireNullable(PlanApplyState state)
    {
        if (!_entityNullable)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NullNotAllowed,
                state.DisplayPath(DtoName),
                $"The DTO member '{DtoName}' resolved to null, but the entity member '{_targetType.ClrType.Name}.{_entityProperty}' does not accept null."
            );
        }
    }

    private static string Friendly(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        return underlying is not null ? underlying.Name + "?" : type.Name;
    }
}
