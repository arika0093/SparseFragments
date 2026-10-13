using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseTypes.EntityFrameworkCore.Mapping;

internal sealed class PrimitiveCollectionNode<TDto, TDtoCollection> : MemberNode
    where TDto : class
{
    private readonly Func<TDto, TDtoCollection?> _dtoGetter;
    private readonly IProperty _entityProperty;
    private Func<object, object> _copyCollection = null!;
    private SparseEntityMissingPolicy _missing;

    public PrimitiveCollectionNode(PropertyInfo dtoProperty, IProperty entityProperty)
        : base(dtoProperty.Name)
    {
        ArgumentNullException.ThrowIfNull(dtoProperty);
        ArgumentNullException.ThrowIfNull(entityProperty);
        if (!dtoProperty.CanRead)
        {
            throw MappingError(
                $"The DTO type '{typeof(TDto).Name}' has no readable collection property '{dtoProperty.Name}'."
            );
        }

        _entityProperty = entityProperty;
        var parameter = Expression.Parameter(typeof(TDto), "dto");
        _dtoGetter = Expression
            .Lambda<Func<TDto, TDtoCollection?>>(
                Expression.Convert(
                    Expression.Property(parameter, dtoProperty),
                    typeof(TDtoCollection)
                ),
                parameter
            )
            .Compile();
    }

    public override void Resolve(PlanBuildSession session, PlanLevel level)
    {
        if (!_entityProperty.IsPrimitiveCollection)
        {
            throw MappingError(
                $"The entity member '{_entityProperty.DeclaringType.ClrType.Name}.{_entityProperty.Name}' is not an EF primitive collection."
            );
        }

        var entityElement = _entityProperty.GetElementType()?.ClrType;
        var dtoElement = GetElementType(typeof(TDtoCollection));
        if (
            entityElement is null
            || dtoElement is null
            || entityElement != dtoElement
            || !PlanBuildSession.IsScalarType(entityElement)
        )
        {
            throw MappingError(
                $"The DTO and entity collections for '{DtoName}' must have the same scalar element type. Collection element conversions are not supported."
            );
        }

        try
        {
            _copyCollection = PrimitiveCollectionFactory.Create(
                _entityProperty.ClrType,
                entityElement
            );
        }
        catch (InvalidOperationException exception)
        {
            throw MappingError(exception.Message);
        }

        if (_entityProperty.DeclaringType is not IEntityType entityType)
        {
            throw MappingError(
                $"The entity member '{_entityProperty.Name}' is not declared directly on an entity type."
            );
        }

        session.Claim(entityType, _entityProperty.Name, DtoName);
        if (level.Registrations.MissingOverrides.TryGetValue(DtoName, out var missingOverride))
        {
            level.ConsumeMissingOverride(DtoName);
            _missing = missingOverride;
        }
        else
        {
            _missing = level.Registrations.DefaultMissing;
        }
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        if (!state.Changes.TryGetValueChange<TDtoCollection>(DtoName, out var change))
        {
            if (state.HasRecords(DtoName))
            {
                throw MappingError(
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

        if (!change.After.IsPresent)
        {
            QueueMissing(state, actions);
            return;
        }

        var replacement = CopyForWrite(change.After.Value, state.DisplayPath(DtoName));
        if (state.TracksAppliedPaths)
        {
            actions.Add(() =>
            {
                state.Entry.Property(_entityProperty.Name).CurrentValue = replacement;
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        state.Entry,
                        _entityProperty.Name,
                        replacement is null
                            ? SparseEntityApplyOperationKind.SetNull
                            : SparseEntityApplyOperationKind.Set
                    )
                );
            });
        }
        else
        {
            actions.Add(() =>
                state.Entry.Property(_entityProperty.Name).CurrentValue = replacement
            );
        }
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        var value = _dtoGetter((TDto)state.AfterSnapshot);
        var replacement = CopyForWrite(value, state.DisplayPath(DtoName));
        if (state.AppliedPaths is not null)
        {
            actions.Add(() =>
            {
                state.Entry.Property(_entityProperty.Name).CurrentValue = replacement;
                state.RecordAppliedPath(
                    AppliedPathFactory.Create(
                        state.DisplayPath(DtoName),
                        state.Entry,
                        _entityProperty.Name,
                        replacement is null
                            ? SparseEntityApplyOperationKind.SetNull
                            : SparseEntityApplyOperationKind.Set
                    )
                );
            });
        }
        else
        {
            actions.Add(() =>
                state.Entry.Property(_entityProperty.Name).CurrentValue = replacement
            );
        }
    }

    public override Expression? BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    )
    {
        if (!context.TryBuildMemberPath(entity, [_entityProperty.Name], dtoPath, out var source))
        {
            return null;
        }

        var dtoProperty = typeof(TDto).GetProperty(
            DtoName,
            BindingFlags.Public | BindingFlags.Instance
        );
        var elementType = _entityProperty.GetElementType()?.ClrType;
        if (dtoProperty is null || elementType is null)
        {
            context.AddError(
                dtoPath,
                $"The primitive collection '{DtoName}' has no usable DTO property or EF element type."
            );
            return null;
        }

        if (
            !dtoProperty.PropertyType.IsAssignableFrom(source.Type)
            || GetElementType(dtoProperty.PropertyType) != elementType
        )
        {
            context.AddError(
                dtoPath,
                $"The primitive collection type '{source.Type.FullName}' cannot be assigned to DTO member '{dtoProperty.Name}' of type '{dtoProperty.PropertyType.FullName}'.",
                "Use compatible collection types or provide an explicit Read() expression."
            );
            return null;
        }

        return source;
    }

    private object? CopyForWrite(TDtoCollection? value, string displayPath)
    {
        if (value is null)
        {
            if (!_entityProperty.IsNullable)
            {
                throw MappingError(
                    displayPath,
                    $"The DTO member '{DtoName}' resolved to null, but the entity member '{_entityProperty.DeclaringType.ClrType.Name}.{_entityProperty.Name}' does not accept null."
                );
            }

            return null;
        }

        return _copyCollection(value);
    }

    private void QueueMissing(PlanApplyState state, List<Action> actions)
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
                                state.Entry.Metadata.ClrType.FullName,
                                _entityProperty.Name
                            )
                        )
                    );
                }

                return;
            case SparseEntityMissingPolicy.SetDefault:
            case SparseEntityMissingPolicy.SetNull:
                if (!_entityProperty.IsNullable)
                {
                    throw MappingError(
                        state.DisplayPath(DtoName),
                        $"The DTO member '{DtoName}' resolved to missing with a {_missing} policy, but the entity member '{_entityProperty.DeclaringType.ClrType.Name}.{_entityProperty.Name}' does not accept null."
                    );
                }

                if (state.TracksAppliedPaths)
                {
                    actions.Add(() =>
                    {
                        state.Entry.Property(_entityProperty.Name).CurrentValue = null;
                        state.RecordAppliedPath(
                            AppliedPathFactory.Create(
                                state.DisplayPath(DtoName),
                                state.Entry,
                                _entityProperty.Name,
                                SparseEntityApplyOperationKind.SetNull
                            )
                        );
                    });
                }
                else
                {
                    actions.Add(() =>
                        state.Entry.Property(_entityProperty.Name).CurrentValue = null
                    );
                }
                return;
            default:
                throw MappingError(
                    state.DisplayPath(DtoName),
                    $"The DTO member '{DtoName}' changed to missing. Missing is not null: declare OnMissing() or MissingDefault() to handle it."
                );
        }
    }

    private static Type? GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType();
        }

        return collectionType
            .GetInterfaces()
            .Append(collectionType)
            .Where(static type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            .Select(static type => type.GetGenericArguments()[0])
            .Distinct()
            .SingleOrDefault();
    }

    private SparseEntityMappingException MappingError(string message) =>
        new(SparseEntityMappingFailure.MetadataMismatch, DtoName, message);

    private static SparseEntityMappingException MappingError(string path, string message) =>
        new(SparseEntityMappingFailure.MetadataMismatch, path, message);
}

internal static class PrimitiveCollectionFactory
{
    private static readonly MethodInfo MutableCollectionCopier =
        typeof(PrimitiveCollectionFactory).GetMethod(
            nameof(CopyToMutableCollection),
            BindingFlags.Public | BindingFlags.Static
        )!;

    public static Func<object, object> Create(Type targetType, Type elementType)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        if (targetType.IsArray)
        {
            var source = Expression.Parameter(typeof(object), "source");
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
            var cast = Expression.Convert(source, enumerableType);
            var toArray = typeof(Enumerable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == nameof(Enumerable.ToArray) && method.IsGenericMethodDefinition
                )
                .MakeGenericMethod(elementType);
            return Expression
                .Lambda<Func<object, object>>(
                    Expression.Convert(Expression.Call(toArray, cast), typeof(object)),
                    source
                )
                .Compile();
        }

        if (targetType.IsAssignableFrom(listType))
        {
            return BuildListCopier(elementType);
        }

        var enumerable = typeof(IEnumerable<>).MakeGenericType(elementType);
        var constructor = targetType.GetConstructor([enumerable]);
        if (constructor is not null)
        {
            var source = Expression.Parameter(typeof(object), "source");
            var instance = Expression.New(constructor, Expression.Convert(source, enumerable));
            return Expression
                .Lambda<Func<object, object>>(Expression.Convert(instance, typeof(object)), source)
                .Compile();
        }

        var collectionInterface = typeof(ICollection<>).MakeGenericType(elementType);
        var emptyConstructor = targetType.GetConstructor(Type.EmptyTypes);
        if (
            emptyConstructor is not null
            && collectionInterface.IsAssignableFrom(targetType)
            && targetType.IsClass
            && !targetType.IsAbstract
        )
        {
            var method = MutableCollectionCopier.MakeGenericMethod(elementType, targetType);
            var source = Expression.Parameter(typeof(object), "source");
            return Expression
                .Lambda<Func<object, object>>(Expression.Call(method, source), source)
                .Compile();
        }

        throw new InvalidOperationException(
            $"The entity collection type '{targetType.Name}' cannot be copied into a fresh instance. Use an array, a collection assignable from List<{elementType.Name}>, a constructor taking IEnumerable<{elementType.Name}>, or a mutable collection with a public parameterless constructor."
        );
    }

    private static Func<object, object> BuildListCopier(Type elementType)
    {
        var method = typeof(PrimitiveCollectionFactory)
            .GetMethod(nameof(CopyToList), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(elementType);
        var source = Expression.Parameter(typeof(object), "source");
        return Expression
            .Lambda<Func<object, object>>(Expression.Call(method, source), source)
            .Compile();
    }

    public static object CopyToList<TElement>(object source) =>
        new List<TElement>((IEnumerable<TElement>)source);

    public static object CopyToMutableCollection<TElement, TCollection>(object source)
        where TCollection : class, ICollection<TElement>, new()
    {
        var collection = new TCollection();
        foreach (var item in (IEnumerable<TElement>)source)
        {
            collection.Add(item);
        }

        return collection;
    }
}
