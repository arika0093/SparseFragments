using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// How a keyed collection mapping disposes removed child entities.
/// </summary>
public enum SparseEntityRemoveKind
{
    /// <summary>Mark the child deleted through the context.</summary>
    Delete = 0,

    /// <summary>Unlink the child from the parent collection without deleting its row.</summary>
    Detach = 1,

    /// <summary>Keep the row and flag it through a caller-supplied callback.</summary>
    SoftDelete = 2,

    /// <summary>Run a caller-supplied removal callback.</summary>
    Custom = 3,
}

/// <summary>
/// Configures keyed synchronization for one parent collection path.
/// </summary>
/// <remarks>The configuration is scoped to the parent collection it is declared under.</remarks>
/// <typeparam name="TEntityChild">The collection element entity type.</typeparam>
/// <typeparam name="TDtoChild">The collection element DTO type.</typeparam>
public sealed class SparseEntityCollectionBuilder<TEntityChild, TDtoChild>
    where TEntityChild : class
    where TDtoChild : class
{
    internal Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>>? ElementConfigure
    {
        get;
        private set;
    }

    internal Func<TDtoChild, TEntityChild>? CreateFactory { get; private set; }

    internal SparseEntityRemoveKind? RemoveKind { get; private set; }

    internal Action<DbContext, TEntityChild>? RemoveHandler { get; private set; }

    internal Action<TEntityChild>? SoftDeleteMark { get; private set; }

    internal Func<TEntityChild, bool>? IsDeletedPredicate { get; private set; }

    internal Expression<Func<TEntityChild, bool>>? IsDeletedExpression { get; private set; }

    internal Action<TEntityChild, int>? OrderSetter { get; private set; }

    internal LambdaExpression? OrderSelectorExpression { get; private set; }

    internal Type? KeyType { get; private set; }

    internal Delegate? DtoKeySelector { get; private set; }

    internal Delegate? EntityKeySelector { get; private set; }

    internal object? UnassignedKeyValue { get; private set; }

    internal bool HasUnassignedKeyValue { get; private set; }

    /// <summary>
    /// Declares the scoped child mapping for this collection path.
    /// </summary>
    /// <remarks>Same-name child members are inferred by the generated child plan.</remarks>
    /// <param name="configure">Configures the child mapping.</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Element(
        Action<SparseEntityMapBuilder<TEntityChild, TDtoChild>> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (ElementConfigure is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "Element() is already configured for this collection. Each collection path declares one child mapping."
            );
        }

        ElementConfigure = configure;
        return this;
    }

    /// <summary>
    /// Maps a computed or composite DTO key onto EF identity.
    /// </summary>
    /// <remarks>Both selectors must project to the same key type.</remarks>
    /// <param name="dtoKey">Projects the DTO element to its stable key.</param>
    /// <param name="entityKey">Projects the tracked child to the same key space.</param>
    /// <typeparam name="TKey">The stable key type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Key<TKey>(
        Expression<Func<TDtoChild, TKey>> dtoKey,
        Expression<Func<TEntityChild, TKey>> entityKey
    )
    {
        ArgumentNullException.ThrowIfNull(dtoKey);
        ArgumentNullException.ThrowIfNull(entityKey);
        if (KeyType is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "Key() is already configured for this collection. Each collection path declares one key mapping."
            );
        }

        KeyType = typeof(TKey);
        DtoKeySelector = dtoKey.Compile();
        EntityKeySelector = entityKey.Compile();
        EntityKeySelectorExpressions.Register(EntityKeySelector, entityKey);
        return this;
    }

    /// <summary>
    /// Declares the explicit unassigned-key sentinel for this collection path.
    /// </summary>
    /// <remarks>Use this when keys have an unassigned sentinel.</remarks>
    /// <param name="sentinel">The DTO key value meaning unassigned.</param>
    /// <typeparam name="TKey">The stable key type.</typeparam>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> UnassignedKey<TKey>(TKey sentinel)
    {
        if (HasUnassignedKeyValue)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "UnassignedKey() is already configured for this collection."
            );
        }

        HasUnassignedKeyValue = true;
        UnassignedKeyValue = sentinel;
        return this;
    }

    /// <summary>
    /// Creates added child entities from their DTO after-state.
    /// </summary>
    /// <remarks>The factory is expected to be pure.</remarks>
    /// <param name="factory">Builds the child entity for an added DTO element.</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Create(
        Func<TDtoChild, TEntityChild> factory
    )
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (CreateFactory is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "Create() is already configured for this collection."
            );
        }

        CreateFactory = factory;
        return this;
    }

    /// <summary>
    /// Removes children through a caller-supplied callback.
    /// </summary>
    /// <param name="remove">Disposes the removed child (delete, detach, or flag).</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Remove(
        Action<DbContext, TEntityChild> remove
    )
    {
        ArgumentNullException.ThrowIfNull(remove);
        EnsureSingleRemovePolicy();
        RemoveKind = SparseEntityRemoveKind.Custom;
        RemoveHandler = remove;
        return this;
    }

    /// <summary>
    /// Marks removed children deleted through the context.
    /// </summary>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Delete()
    {
        EnsureSingleRemovePolicy();
        RemoveKind = SparseEntityRemoveKind.Delete;
        RemoveHandler = static (db, entity) => db.Remove(entity);
        return this;
    }

    /// <summary>Unlinks removed children from the parent collection without deleting their rows.</summary>
    /// <remarks>Only valid for optional relationships and non-owned navigations.</remarks>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Detach()
    {
        EnsureSingleRemovePolicy();
        RemoveKind = SparseEntityRemoveKind.Detach;
        return this;
    }

    /// <summary>
    /// Keeps removed rows and flags them through a caller-supplied callback.
    /// </summary>
    /// <remarks>The child stays in the parent collection; only
    /// <paramref name="markDeleted" /> distinguishes it afterwards.</remarks>
    /// <param name="markDeleted">Flags the removed child (for example a soft-delete column).</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> SoftDelete(
        Action<TEntityChild> markDeleted
    )
    {
        ArgumentNullException.ThrowIfNull(markDeleted);
        EnsureSingleRemovePolicy();
        RemoveKind = SparseEntityRemoveKind.SoftDelete;
        SoftDeleteMark = markDeleted;
        return this;
    }

    /// <summary>Keeps removed rows and flags them, with a predicate proving prior deletion.</summary>
    /// <remarks>Like <see cref="SoftDelete(Action{TEntityChild})"/>.</remarks>
    /// <param name="markDeleted">Flags the removed child (for example a soft-delete column).</param>
    /// <param name="isDeleted">Tests whether a tracked child was already flagged.</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> SoftDelete(
        Action<TEntityChild> markDeleted,
        Func<TEntityChild, bool> isDeleted
    )
    {
        ArgumentNullException.ThrowIfNull(markDeleted);
        ArgumentNullException.ThrowIfNull(isDeleted);
        EnsureSingleRemovePolicy();
        RemoveKind = SparseEntityRemoveKind.SoftDelete;
        SoftDeleteMark = markDeleted;
        IsDeletedPredicate = isDeleted;
        return this;
    }

    /// <summary>Supplies an expression used to exclude soft-deleted children from DTO projections.</summary>
    /// <remarks>The expression also becomes the preflight predicate used to recognize previously soft-deleted rows.</remarks>
    /// <param name="isDeleted">Tests whether a child has already been soft-deleted.</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> SoftDeleteFilter(
        Expression<Func<TEntityChild, bool>> isDeleted
    )
    {
        ArgumentNullException.ThrowIfNull(isDeleted);
        if (RemoveKind != SparseEntityRemoveKind.SoftDelete)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                "SoftDeleteFilter() requires a SoftDelete() removal policy."
            );
        }

        if (IsDeletedExpression is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "SoftDeleteFilter() is already configured for this collection."
            );
        }

        IsDeletedExpression = isDeleted;
        IsDeletedPredicate = isDeleted.Compile();
        return this;
    }

    /// <summary>
    /// Persists collection order through a caller-supplied setter.
    /// </summary>
    /// <remarks>Key-order changes require this setter; when configured, it persists positions and reorders the tracked list.</remarks>
    /// <param name="setOrder">Writes the zero-based position to the child.</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Order(
        Action<TEntityChild, int> setOrder
    )
    {
        ArgumentNullException.ThrowIfNull(setOrder);
        if (OrderSetter is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "Order() is already configured for this collection."
            );
        }

        OrderSetter = setOrder;
        return this;
    }

    /// <summary>
    /// Persists collection order through an integer entity member.
    /// </summary>
    /// <remarks>
    /// The selected member supplies the read-side sort key and receives the
    /// zero-based collection position during keyed synchronization.
    /// </remarks>
    /// <param name="order">Selects the writable integer order member.</param>
    /// <returns>This builder.</returns>
    public SparseEntityCollectionBuilder<TEntityChild, TDtoChild> Order(
        Expression<Func<TEntityChild, int>> order
    )
    {
        ArgumentNullException.ThrowIfNull(order);
        if (OrderSetter is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "Order() is already configured for this collection."
            );
        }

        var body = StripConvert(order.Body);
        if (
            body is not MemberExpression member
            || member.Expression != order.Parameters[0]
            || !CanAssignOrder(member.Member)
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                "The expression-based Order() overload requires a directly selected writable integer property or field."
            );
        }

        var index = Expression.Parameter(typeof(int), "index");
        try
        {
            var assignment = Expression.Assign(body, index);
            OrderSetter = Expression
                .Lambda<Action<TEntityChild, int>>(assignment, order.Parameters[0], index)
                .Compile();
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The selected Order() member cannot be assigned: {exception.Message}"
            );
        }

        OrderSelectorExpression = order;
        return this;
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

    private static bool CanAssignOrder(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.CanWrite,
            FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
            _ => false,
        };

    private void EnsureSingleRemovePolicy()
    {
        if (RemoveKind is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Duplicate,
                null,
                "A removal policy is already configured for this collection. Declare delete, detach, or soft-delete once."
            );
        }
    }
}

internal static class EntityKeySelectorExpressions
{
    private static readonly ConditionalWeakTable<Delegate, LambdaExpression> Expressions = new();

    public static void Register(Delegate selector, LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(expression);
        Expressions.GetValue(selector, _ => expression);
    }

    public static bool TryGet(Delegate selector, out LambdaExpression? expression)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Expressions.TryGetValue(selector, out expression);
    }
}
