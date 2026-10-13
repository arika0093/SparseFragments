using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace SparseTypes.EntityFrameworkCore.Mapping;

internal sealed class IncludePathCollector
{
    private static readonly MethodInfo EfPropertyMethod = typeof(EF)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(EF.Property)
            && method.IsGenericMethodDefinition
            && method.GetParameters().Length == 2
        );

    private static readonly MethodInfo EnumerableWhereMethod = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(Enumerable.Where)
            && method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters().Length == 2
            && method.GetParameters()[1].ParameterType.IsGenericType
            && method.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2
        );

    private static readonly MethodInfo IncludeMethod = FindIncludeMethod();

    private static readonly MethodInfo ThenIncludeReferenceMethod = FindThenIncludeMethod(false);

    private static readonly MethodInfo ThenIncludeCollectionMethod = FindThenIncludeMethod(true);

    private readonly Dictionary<string, IncludePathNode> _root = new(StringComparer.Ordinal);
    private readonly List<IncludePathNode> _stack = [];

    public void PushReference(string name, Type targetType, bool owned)
    {
        Push(name, targetType, isCollection: false, owned);
    }

    public void PushCollection(
        string name,
        Type targetType,
        bool owned,
        bool filterEdits,
        LambdaExpression? keySelector,
        Type? keyType,
        IEntityType? entityType,
        IReadOnlyList<object>? keys
    )
    {
        var filter =
            !owned
            && filterEdits
            && keySelector is not null
            && keyType is not null
            && entityType is not null
            && keys is not null
                ? IncludeFilter.TryCreate(keySelector, keyType, entityType, keys)
                : null;
        var node = Push(name, targetType, isCollection: true, owned);
        node.MergeFilter(filter, filterEdits && !owned);
    }

    public void Pop()
    {
        if (_stack.Count == 0)
        {
            throw new InvalidOperationException("The Include path stack is empty.");
        }

        _stack.RemoveAt(_stack.Count - 1);
    }

    public void IncludeCurrent()
    {
        if (_stack.Count == 0)
        {
            throw new InvalidOperationException("An Include path has no current navigation.");
        }

        var current = _stack[^1];
        if (!current.Owned)
        {
            current.IsIncluded = true;
        }
    }

    public IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> query)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(query);
        foreach (var path in GetPaths())
        {
            query = ApplyPath(query, path);
        }

        return query;
    }

    private IncludePathNode Push(string name, Type targetType, bool isCollection, bool owned)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(targetType);
        var siblings = _stack.Count == 0 ? _root : _stack[^1].Children;
        if (!siblings.TryGetValue(name, out var node))
        {
            node = new IncludePathNode(name, targetType, isCollection, owned);
            siblings.Add(name, node);
        }
        else if (
            node.TargetType != targetType
            || node.IsCollection != isCollection
            || node.Owned != owned
        )
        {
            throw new InvalidOperationException(
                $"The Include path '{name}' resolved to inconsistent navigation metadata."
            );
        }

        _stack.Add(node);
        return node;
    }

    private List<List<IncludePathNode>> GetPaths()
    {
        var paths = new List<List<IncludePathNode>>();
        var current = new List<IncludePathNode>();
        foreach (var node in _root.Values)
        {
            CollectLeafPaths(node, current, paths);
        }

        return paths;
    }

    private static bool HasIncludedPath(IncludePathNode node) =>
        node.IsIncluded || node.Children.Values.Any(HasIncludedPath);

    private static void CollectLeafPaths(
        IncludePathNode node,
        List<IncludePathNode> current,
        List<List<IncludePathNode>> paths
    )
    {
        current.Add(node);
        var includedChild = node.Children.Values.Any(HasIncludedPath);
        if (node.IsIncluded && !includedChild)
        {
            paths.Add([.. current]);
        }

        foreach (var child in node.Children.Values.Where(HasIncludedPath))
        {
            CollectLeafPaths(child, current, paths);
        }

        current.RemoveAt(current.Count - 1);
    }

    private static IQueryable<TEntity> ApplyPath<TEntity>(
        IQueryable<TEntity> query,
        IReadOnlyList<IncludePathNode> path
    )
        where TEntity : class
    {
        object chain = query;
        for (var index = 0; index < path.Count; index++)
        {
            var node = path[index];
            var parentType = index == 0 ? typeof(TEntity) : path[index - 1].TargetType;
            var navigation = BuildNavigationLambda(parentType, node);
            MethodInfo method;
            if (index == 0)
            {
                method = IncludeMethod.MakeGenericMethod(typeof(TEntity), navigation.ReturnType);
            }
            else
            {
                var previous = path[index - 1];
                var thenIncludeMethod = previous.IsCollection
                    ? ThenIncludeCollectionMethod
                    : ThenIncludeReferenceMethod;
                method = thenIncludeMethod.MakeGenericMethod(
                    typeof(TEntity),
                    previous.TargetType,
                    navigation.ReturnType
                );
            }
            chain = Invoke(method, chain, navigation);
        }

        return (IQueryable<TEntity>)chain;
    }

    private static LambdaExpression BuildNavigationLambda(
        Type parentType,
        IncludePathNode navigation
    )
    {
        var parameter = Expression.Parameter(parentType, "entity");
        var propertyType = navigation.IsCollection
            ? typeof(IEnumerable<>).MakeGenericType(navigation.TargetType)
            : navigation.TargetType;
        Expression property = Expression.Call(
            EfPropertyMethod.MakeGenericMethod(propertyType),
            Expression.Convert(parameter, typeof(object)),
            Expression.Constant(navigation.Name)
        );

        if (navigation.IsCollection && navigation.Filter is not null)
        {
            var predicate = navigation.Filter.BuildPredicate();
            property = Expression.Call(
                EnumerableWhereMethod.MakeGenericMethod(navigation.TargetType),
                property,
                predicate
            );
        }

        return Expression.Lambda(
            typeof(Func<,>).MakeGenericType(parentType, property.Type),
            property,
            parameter
        );
    }

    private static object Invoke(MethodInfo method, object source, LambdaExpression navigation)
    {
        try
        {
            return method.Invoke(null, [source, navigation])
                ?? throw new InvalidOperationException("EF Core returned a null Include query.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo FindIncludeMethod() =>
        typeof(EntityFrameworkQueryableExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
            {
                if (
                    method.Name != nameof(EntityFrameworkQueryableExtensions.Include)
                    || !method.IsGenericMethodDefinition
                    || method.GetGenericArguments().Length != 2
                )
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType.IsGenericType
                    && parameters[0].ParameterType.GetGenericTypeDefinition()
                        == typeof(IQueryable<>)
                    && parameters[1].ParameterType.IsGenericType
                    && parameters[1].ParameterType.GetGenericTypeDefinition()
                        == typeof(Expression<>);
            });

    private static MethodInfo FindThenIncludeMethod(bool collection)
    {
        return typeof(EntityFrameworkQueryableExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method =>
            {
                if (
                    method.Name != nameof(EntityFrameworkQueryableExtensions.ThenInclude)
                    || !method.IsGenericMethodDefinition
                    || method.GetGenericArguments().Length != 3
                )
                {
                    return false;
                }

                var parameters = method.GetParameters();
                if (
                    parameters.Length != 2
                    || !parameters[0].ParameterType.IsGenericType
                    || parameters[0].ParameterType.GetGenericTypeDefinition()
                        != typeof(IIncludableQueryable<,>)
                )
                {
                    return false;
                }

                var previousType = parameters[0].ParameterType.GetGenericArguments()[1];
                return collection
                    ? previousType.IsGenericType
                        && previousType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    : previousType.IsGenericParameter;
            });
    }

    private sealed class IncludePathNode
    {
        public IncludePathNode(string name, Type targetType, bool isCollection, bool owned)
        {
            Name = name;
            TargetType = targetType;
            IsCollection = isCollection;
            Owned = owned;
        }

        public string Name { get; }

        public Type TargetType { get; }

        public bool IsCollection { get; }

        public bool Owned { get; }

        public bool IsIncluded { get; set; }

        public bool IsUnfiltered { get; private set; }

        public IncludeFilter? Filter { get; private set; }

        public Dictionary<string, IncludePathNode> Children { get; } = new(StringComparer.Ordinal);

        public void MergeFilter(IncludeFilter? filter, bool filterRequested)
        {
            if (!IsCollection || Owned)
            {
                return;
            }

            if (!filterRequested || filter is null)
            {
                IsUnfiltered = true;
                Filter = null;
                return;
            }

            if (IsUnfiltered)
            {
                return;
            }

            if (Filter is null)
            {
                Filter = filter;
                return;
            }

            if (!Filter.CanMerge(filter))
            {
                IsUnfiltered = true;
                Filter = null;
                return;
            }

            Filter.Merge(filter);
        }
    }

    private sealed class IncludeFilter
    {
        private readonly HashSet<object> _keys = [];

        private IncludeFilter(LambdaExpression selector, Type keyType, IReadOnlyList<object> keys)
        {
            Selector = selector;
            KeyType = keyType;
            foreach (var key in keys)
            {
                _keys.Add(key);
            }
        }

        private LambdaExpression Selector { get; }

        private Type KeyType { get; }

        public static IncludeFilter? TryCreate(
            LambdaExpression selector,
            Type keyType,
            IEntityType entityType,
            IReadOnlyList<object> keys
        )
        {
            if (
                !PlanBuildSession.IsScalarType(keyType)
                || selector.ReturnType != keyType
                || !IsSimpleMappedSelector(selector, entityType)
                || keys.Count == 0
                || keys.Any(key => key is null)
            )
            {
                return null;
            }

            return new IncludeFilter(selector, keyType, keys);
        }

        public bool CanMerge(IncludeFilter other) =>
            Selector.Parameters[0].Type == other.Selector.Parameters[0].Type
            && KeyType == other.KeyType
            && Selector.ToString() == other.Selector.ToString();

        public void Merge(IncludeFilter other)
        {
            foreach (var key in other._keys)
            {
                _keys.Add(key);
            }
        }

        public LambdaExpression BuildPredicate()
        {
            var parameter = Selector.Parameters[0];
            var body = StripConvert(Selector.Body);
            Expression? predicate = null;
            foreach (var key in _keys)
            {
                var equality = Expression.Equal(body, CreateConstant(key, KeyType));
                predicate = predicate is null ? equality : Expression.OrElse(predicate, equality);
            }

            return Expression.Lambda(predicate!, parameter);
        }

        private static bool IsSimpleMappedSelector(
            LambdaExpression selector,
            IEntityType entityType
        )
        {
            var body = StripConvert(selector.Body);
            string? propertyName = body switch
            {
                MemberExpression member when member.Expression == selector.Parameters[0] => member
                    .Member
                    .Name,
                MethodCallExpression call
                    when call.Method.DeclaringType == typeof(EF)
                        && call.Method.Name == nameof(EF.Property)
                        && call.Arguments.Count == 2
                        && StripConvert(call.Arguments[0]) == selector.Parameters[0]
                        && call.Arguments[1] is ConstantExpression { Value: string name } => name,
                _ => null,
            };
            var property = propertyName is null ? null : entityType.FindProperty(propertyName);
            return property is not null && property.ClrType == selector.ReturnType;
        }

        private static Expression CreateConstant(object value, Type type)
        {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null)
            {
                return Expression.Convert(Expression.Constant(value, nullable), type);
            }

            return Expression.Constant(value, type);
        }

        private static Expression StripConvert(Expression expression)
        {
            while (
                expression is UnaryExpression unary
                && (
                    unary.NodeType == ExpressionType.Convert
                    || unary.NodeType == ExpressionType.ConvertChecked
                )
            )
            {
                expression = unary.Operand;
            }

            return expression;
        }
    }
}
