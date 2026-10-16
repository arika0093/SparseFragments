using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// A keyed child collection synchronized by stable DTO keys.
/// </summary>
/// <remarks>Dispatch runs through the typed keyed transition emitted by the ordinary <c>SparseTypes.Generator</c> (closed once at plan build).</remarks>
internal sealed class CollectionNode<TDto, TEntity, TDtoChild, TEntityChild> : MemberNode
    where TDto : class
    where TEntity : class
    where TDtoChild : class
    where TEntityChild : class
{
    private readonly Expression<Func<TDto, IEnumerable<TDtoChild>?>> _dtoSelector;
    private readonly Expression<Func<TEntity, IEnumerable<TEntityChild>?>> _entitySelector;
    private readonly CollectionDeclaration<TDtoChild, TEntityChild> _declaration;
    private CollectionRuntime<TDtoChild, TEntityChild>? _runtime;

    public CollectionNode(
        Expression<Func<TDto, IEnumerable<TDtoChild>?>> dto,
        Expression<Func<TEntity, IEnumerable<TEntityChild>?>> entity,
        CollectionDeclaration<TDtoChild, TEntityChild> declaration
    )
        : base(MemberPath.Parse(dto).SingleName("DTO"))
    {
        _dtoSelector = dto;
        _entitySelector = entity;
        _declaration = declaration;
    }

    public override void Resolve(PlanBuildSession session, PlanLevel level)
    {
        _runtime = CollectionRuntime<TDtoChild, TEntityChild>.Create<TDto, TEntity>(
            DtoName,
            _dtoSelector,
            _entitySelector,
            _declaration,
            session,
            level
        );
    }

    public override void Prepare(PlanApplyState state, List<Action> actions)
    {
        Runtime.Prepare(state, actions);
    }

    public override void WriteAll(PlanWriteState state, List<Action> actions)
    {
        Runtime.WriteAll(state, actions);
    }

    public override void AssignTemporaryKeys(
        DbContext context,
        object? baseline,
        object authoritative,
        SparseEntityApplyResult result,
        string path,
        TemporaryKeyAssignmentPlan assignments,
        IReadOnlyList<CollectionScopeIdentity>? parentScopes = null
    )
    {
        var baselineItems = baseline is null ? null : Runtime.SnapshotReader(baseline);
        var authoritativeItems = Runtime.SnapshotReader(authoritative);
        Runtime.AssignTemporaryKeys(
            context,
            baselineItems,
            authoritativeItems,
            result,
            path,
            assignments,
            parentScopes
        );
    }

    public override void CollectIncludes(
        ISparseChangeSet changes,
        IncludePathCollector collector,
        bool writeAll,
        object? afterSnapshot
    )
    {
        if (writeAll)
        {
            return;
        }

        var runtime = Runtime;
        var view = runtime.Fetch(changes, runtime.DtoName);
        if (view is null || !view.IsChanged)
        {
            return;
        }

        var filterEdits =
            !view.OrderChanged
            && view.Items.Count > 0
            && view.Items.All(static item =>
                item.IsEdited && !item.IsAdded && !item.IsRemoved && !item.IsReordered
            );
        var keys = filterEdits ? view.Items.Select(static item => item.Key).ToList() : null;
        collector.PushCollection(
            runtime.NavigationName,
            typeof(TEntityChild),
            runtime.Owned,
            filterEdits,
            runtime.EntityKeySelectorExpression,
            runtime.KeyType,
            runtime.ChildEntityType,
            keys
        );
        try
        {
            collector.IncludeCurrent();
            foreach (var item in view.Items)
            {
                if (item.IsEdited && item.Edit is not null)
                {
                    runtime.ChildPlan.CollectIncludes(
                        item.Edit,
                        collector,
                        writeAll: false,
                        afterSnapshot: null
                    );
                }
            }
        }
        finally
        {
            collector.Pop();
        }
    }

    public override Expression? BuildProjection(
        Expression entity,
        ReadProjectionBuildContext context,
        string dtoPath
    )
    {
        var runtime = Runtime;
        if (
            !context.TryBuildMemberPath(
                entity,
                [runtime.NavigationName],
                dtoPath,
                out var collection
            )
        )
        {
            return null;
        }

        var childParameter = Expression.Parameter(typeof(TEntityChild), "child");
        var enumerableType = typeof(IEnumerable<TEntityChild>);
        Expression sequence =
            collection.Type == enumerableType
                ? collection
                : Expression.Convert(collection, enumerableType);
        var canProject = true;

        if (runtime.RemoveKind == SparseEntityRemoveKind.SoftDelete)
        {
            if (runtime.IsDeletedExpression is null)
            {
                context.AddError(
                    dtoPath,
                    "A soft-delete collection needs an expression predicate to exclude deleted rows from DTO projections.",
                    "Configure SoftDeleteFilter() with a predicate EF Core can translate."
                );
                canProject = false;
            }
            else if (
                context.TryInlineRead(
                    runtime.IsDeletedExpression,
                    childParameter,
                    typeof(bool),
                    dtoPath,
                    out var isDeleted
                )
            )
            {
                var predicate = Expression.Lambda(
                    typeof(Func<TEntityChild, bool>),
                    Expression.Not(isDeleted),
                    childParameter
                );
                sequence = Expression.Call(
                    ReadProjectionBuildContext.ProjectionEnumerableMethods.Where.MakeGenericMethod(
                        typeof(TEntityChild)
                    ),
                    sequence,
                    predicate
                );
            }
            else
            {
                canProject = false;
            }
        }

        if (runtime.OrderSetter is not null)
        {
            if (runtime.OrderSelectorExpression is null)
            {
                context.AddError(
                    dtoPath,
                    "The collection has a write-only Order() callback and cannot be sorted in a DTO projection.",
                    "Use Order(entity => entity.SortOrder) to register a read-side sort key."
                );
                canProject = false;
            }
            else if (
                context.TryInlineRead(
                    runtime.OrderSelectorExpression,
                    childParameter,
                    runtime.OrderSelectorExpression.ReturnType,
                    dtoPath,
                    out var orderKey
                )
            )
            {
                var keyType = runtime.OrderSelectorExpression.ReturnType;
                var keySelector = Expression.Lambda(
                    typeof(Func<,>).MakeGenericType(typeof(TEntityChild), keyType),
                    orderKey,
                    childParameter
                );
                sequence = Expression.Call(
                    ReadProjectionBuildContext.ProjectionEnumerableMethods.OrderBy.MakeGenericMethod(
                        typeof(TEntityChild),
                        keyType
                    ),
                    sequence,
                    keySelector
                );
            }
            else
            {
                canProject = false;
            }
        }

        var childProjection = runtime.ChildPlan.BuildProjection(childParameter, context, dtoPath);
        var selector = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(TEntityChild), typeof(TDtoChild)),
            childProjection,
            childParameter
        );
        var selected = Expression.Call(
            ReadProjectionBuildContext.ProjectionEnumerableMethods.Select.MakeGenericMethod(
                typeof(TEntityChild),
                typeof(TDtoChild)
            ),
            sequence,
            selector
        );
        var dtoProperty = typeof(TDto).GetProperty(
            DtoName,
            BindingFlags.Public | BindingFlags.Instance
        );
        if (dtoProperty is null || !dtoProperty.CanWrite)
        {
            context.AddError(
                dtoPath,
                $"The DTO collection member '{DtoName}' has no writable property for projection."
            );
            return null;
        }

        if (
            !context.TryMaterializeCollection(
                selected,
                typeof(TDtoChild),
                dtoProperty.PropertyType,
                dtoPath,
                out var materialized,
                out var empty
            )
        )
        {
            canProject = false;
        }

        if (!canProject)
        {
            return null;
        }

        return Expression.Condition(
            Expression.Equal(collection, Expression.Constant(null, collection.Type)),
            empty,
            materialized
        );
    }

    private CollectionRuntime<TDtoChild, TEntityChild> Runtime =>
        _runtime ?? throw new InvalidOperationException("The collection plan was not resolved.");
}
