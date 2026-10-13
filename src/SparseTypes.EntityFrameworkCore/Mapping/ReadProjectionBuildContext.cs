using System.Linq.Expressions;
using System.Reflection;

namespace SparseTypes.EntityFrameworkCore.Mapping;

internal sealed class ReadProjectionBuildContext
{
    private static readonly MethodInfo EnumerableEmptyMethod = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(Enumerable.Empty)
            && method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Length == 0
        );

    private static readonly MethodInfo EnumerableToArrayMethod = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(Enumerable.ToArray)
            && method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Length == 1
        );

    private static readonly MethodInfo EnumerableToListMethod = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(Enumerable.ToList)
            && method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Length == 1
        );

    private readonly List<SparseEntityMappingError> _errors = [];

    public IReadOnlyList<SparseEntityMappingError> Errors => _errors;

    public void AddError(string? dtoPath, string message, string? suggestedAction = null)
    {
        _errors.Add(
            new SparseEntityMappingError(
                SparseEntityMappingFailure.MetadataMismatch,
                dtoPath,
                message,
                suggestedAction: suggestedAction
            )
        );
    }

    public bool TryBuildMemberPath(
        Expression source,
        IReadOnlyList<string> memberNames,
        string dtoPath,
        out Expression value
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(memberNames);
        ArgumentNullException.ThrowIfNull(dtoPath);
        try
        {
            var accesses = new List<Expression>(memberNames.Count + 1) { source };
            var current = source;
            foreach (var name in memberNames)
            {
                var member = FindMember(current.Type, name);
                current = Expression.MakeMemberAccess(current, member);
                accesses.Add(current);
            }

            value = MakeNullSafe(accesses, source is not ParameterExpression);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidOperationException
                        or AmbiguousMatchException
            )
        {
            value = Expression.Default(typeof(object));
            AddError(
                dtoPath,
                $"The entity path could not be used for a read projection: {exception.Message}",
                "Register an explicit Read() expression for this DTO member."
            );
            return false;
        }
    }

    public bool TryInlineRead(
        LambdaExpression selector,
        Expression source,
        Type targetType,
        string dtoPath,
        out Expression value
    )
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(dtoPath);
        if (
            selector.Parameters.Count != 1
            || !selector.Parameters[0].Type.IsAssignableFrom(source.Type)
        )
        {
            value = Expression.Default(targetType);
            AddError(
                dtoPath,
                $"The Read() expression has source type '{selector.Parameters.FirstOrDefault()?.Type.FullName}', which does not accept '{source.Type.FullName}'."
            );
            return false;
        }

        try
        {
            var body = new ParameterReplacement(selector.Parameters[0], source).Visit(
                selector.Body
            );
            body = new NullSafeMemberAccess(source).Visit(body);
            if (!targetType.IsAssignableFrom(body.Type))
            {
                value = Expression.Default(targetType);
                AddError(
                    dtoPath,
                    $"The Read() expression returns '{body.Type.FullName}', which cannot be assigned to '{targetType.FullName}'."
                );
                return false;
            }

            value = body.Type == targetType ? body : Expression.Convert(body, targetType);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or InvalidOperationException
                        or AmbiguousMatchException
            )
        {
            value = Expression.Default(targetType);
            AddError(
                dtoPath,
                $"The Read() expression could not be used in a projection: {exception.Message}"
            );
            return false;
        }
    }

    internal static class ProjectionEnumerableMethods
    {
        public static MethodInfo Where { get; } = Find(nameof(Enumerable.Where), 1);

        public static MethodInfo OrderBy { get; } = Find(nameof(Enumerable.OrderBy), 2);

        public static MethodInfo Select { get; } = Find(nameof(Enumerable.Select), 2);

        private static MethodInfo Find(string name, int genericArgumentCount) =>
            typeof(Enumerable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                {
                    if (
                        method.Name != name
                        || !method.IsGenericMethodDefinition
                        || method.GetGenericArguments().Length != genericArgumentCount
                    )
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 2
                        && parameters[1].ParameterType.IsGenericType
                        && parameters[1].ParameterType.GetGenericTypeDefinition()
                            == typeof(Func<,>);
                });
    }

    public bool TryBind(
        PropertyInfo property,
        Expression value,
        string dtoPath,
        out MemberBinding? binding
    )
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(dtoPath);
        if (!property.CanWrite || !property.PropertyType.IsAssignableFrom(value.Type))
        {
            binding = null;
            AddError(
                dtoPath,
                $"The projected value of type '{value.Type.FullName}' cannot be assigned to DTO member '{property.Name}' of type '{property.PropertyType.FullName}'."
            );
            return false;
        }

        binding = Expression.Bind(property, value);
        return true;
    }

    public bool TryMaterializeCollection(
        Expression sequence,
        Type elementType,
        Type targetType,
        string dtoPath,
        out Expression materialized,
        out Expression empty
    )
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(dtoPath);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        if (!enumerableType.IsAssignableFrom(sequence.Type))
        {
            materialized = Expression.Default(targetType);
            empty = Expression.Default(targetType);
            AddError(
                dtoPath,
                $"The projected collection has type '{sequence.Type.FullName}', which is not an enumerable of '{elementType.FullName}'."
            );
            return false;
        }

        if (targetType.IsArray && targetType.GetElementType() == elementType)
        {
            var toArray = Expression.Call(
                EnumerableToArrayMethod.MakeGenericMethod(elementType),
                sequence
            );
            materialized = toArray;
            empty = Expression.NewArrayBounds(elementType, Expression.Constant(0));
            return true;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        if (targetType.IsAssignableFrom(listType))
        {
            materialized = Expression.Call(
                EnumerableToListMethod.MakeGenericMethod(elementType),
                sequence
            );
            empty = Expression.New(listType);
            return true;
        }

        var enumerableConstructor = targetType.GetConstructor([enumerableType]);
        if (enumerableConstructor is not null)
        {
            var emptySequence = Expression.Call(
                EnumerableEmptyMethod.MakeGenericMethod(elementType)
            );
            materialized = Expression.New(enumerableConstructor, sequence);
            empty = Expression.New(enumerableConstructor, emptySequence);
            return true;
        }

        materialized = Expression.Default(targetType);
        empty = Expression.Default(targetType);
        AddError(
            dtoPath,
            $"The DTO collection type '{targetType.FullName}' cannot be created from an entity collection projection.",
            "Use a DTO collection assignable from List<T>, an array, or a collection with a public IEnumerable<T> constructor."
        );
        return false;
    }

    public static string JoinPath(string prefix, string member) =>
        string.IsNullOrEmpty(prefix) ? member : $"{prefix}.{member}";

    private static MemberInfo FindMember(Type declaringType, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        return (MemberInfo?)declaringType.GetProperty(name, flags)
            ?? declaringType.GetField(name, flags)
            ?? throw new InvalidOperationException(
                $"The CLR type '{declaringType.FullName}' has no member named '{name}'."
            );
    }

    private static Expression MakeNullSafe(IReadOnlyList<Expression> accesses, bool checkRoot)
    {
        var value = accesses[^1];
        for (var index = accesses.Count - 2; index >= (checkRoot ? 0 : 1); index--)
        {
            var parent = accesses[index];
            if (!CanBeNull(parent.Type))
            {
                continue;
            }

            value = Expression.Condition(IsNull(parent), Expression.Default(value.Type), value);
        }

        return value;
    }

    private static bool CanBeNull(Type type) =>
        !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private static Expression IsNull(Expression value)
    {
        var underlying = Nullable.GetUnderlyingType(value.Type);
        return underlying is null
            ? Expression.Equal(value, Expression.Constant(null, value.Type))
            : Expression.Not(Expression.Property(value, nameof(Nullable<int>.HasValue)));
    }

    private sealed class ParameterReplacement(ParameterExpression parameter, Expression replacement)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }

    private sealed class NullSafeMemberAccess(Expression root) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryGetRootedPath(node, root, out var names))
            {
                var accesses = new List<Expression>(names.Count + 1) { root };
                var current = root;
                foreach (var name in names)
                {
                    current = Expression.MakeMemberAccess(current, FindMember(current.Type, name));
                    accesses.Add(current);
                }

                return MakeNullSafe(accesses, root is not ParameterExpression);
            }

            return base.VisitMember(node);
        }

        private static bool TryGetRootedPath(
            MemberExpression expression,
            Expression root,
            out List<string> names
        )
        {
            names = [];
            Expression? current = expression;
            while (current is MemberExpression member)
            {
                names.Insert(0, member.Member.Name);
                current = member.Expression;
            }

            return names.Count > 0 && ReferenceEquals(current, root);
        }
    }
}
