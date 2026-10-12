using System.Linq.Expressions;
using System.Reflection;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// Object-typed access to one flattened navigation segment.
/// </summary>
/// <remarks>
/// Compiled once per plan so flattened writes reach the navigation's own EF
/// entry instead of the root entry. Struct declaring types are rejected:
/// member assignment through a boxed copy would silently discard the write.
/// </remarks>
internal sealed class IntermediateAccess
{
    private IntermediateAccess(
        Func<object, object?> getter,
        Action<object, object?> setter,
        ConstructorInfo constructor
    )
    {
        Getter = getter;
        Setter = setter;
        Constructor = constructor;
    }

    public Func<object, object?> Getter { get; }

    public Action<object, object?> Setter { get; }

    public ConstructorInfo Constructor { get; }

    /// <summary>Builds accessors for a navigation member on a declaring CLR type.</summary>
    public static IntermediateAccess Build(Type declaringType, string name, string dtoPath)
    {
        if (declaringType.IsValueType)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoPath,
                $"Flattening through '{declaringType.Name}.{name}' is not supported: value-type members cannot be materialized by reference."
            );
        }

        var property = declaringType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        var field = property is null
            ? declaringType.GetField(name, BindingFlags.Public | BindingFlags.Instance)
            : null;
        if (property is null && field is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoPath,
                $"The entity type '{declaringType.Name}' has no accessible member '{name}'."
            );
        }

        var memberType = property is not null ? property.PropertyType : field!.FieldType;
        if (property is not null && !property.CanWrite)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoPath,
                $"The entity member '{declaringType.Name}.{name}' is read-only, so a missing instance cannot be materialized."
            );
        }

        var instance = Expression.Parameter(typeof(object), "instance");
        var typed = Expression.Convert(instance, declaringType);
        var access = property is not null
            ? (Expression)Expression.Property(typed, property)
            : Expression.Field(typed, field!);
        var getter = Expression
            .Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), instance)
            .Compile();

        var value = Expression.Parameter(typeof(object), "value");
        var assign = Expression.Assign(access, Expression.Convert(value, memberType));
        var setter = Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();

        var constructor =
            memberType.GetConstructor(Type.EmptyTypes)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                dtoPath,
                $"The nested entity type '{memberType.Name}' has no public parameterless constructor, so a missing instance cannot be materialized."
            );
        return new IntermediateAccess(getter, setter, constructor);
    }

    /// <summary>Creates a missing intermediate instance.</summary>
    public object Create(string dtoPath)
    {
        return Constructor.Invoke(null)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                dtoPath,
                $"The parameterless constructor of '{Constructor.DeclaringType?.Name}' returned null."
            );
    }
}
