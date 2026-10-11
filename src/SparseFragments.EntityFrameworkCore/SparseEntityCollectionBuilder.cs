using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore;

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
/// <remarks>
/// The configuration is scoped to the parent collection it is declared under:
/// two collections with the same element CLR types keep independent child
/// mappings, key projections, factories, and removal/order policies. A shared
/// private method accepting this builder may deduplicate repeated declarations.
/// </remarks>
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

    internal Action<TEntityChild, int>? OrderSetter { get; private set; }

    internal Type? KeyType { get; private set; }

    internal Delegate? DtoKeySelector { get; private set; }

    internal Delegate? EntityKeySelector { get; private set; }

    /// <summary>
    /// Declares the scoped child mapping for this collection path.
    /// </summary>
    /// <remarks>
    /// Same-name child members are inferred by the generated child plan;
    /// <paramref name="configure" /> registers only path-scoped overrides.
    /// Overrides apply to this parent collection only, never to the same child
    /// type pair under another path.
    /// </remarks>
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
    /// <remarks>
    /// Both selectors must project to the same key type, which must equal the
    /// DTO <c>[SparseKey]</c> type consumed by the generated keyed transition.
    /// Without this call the DTO key must equal the single-property EF primary
    /// key type; composite EF keys and alternate identities always need it.
    /// </remarks>
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
        return this;
    }

    /// <summary>
    /// Creates added child entities from their DTO after-state.
    /// </summary>
    /// <remarks>
    /// The factory is expected to be pure: it runs during validation, before
    /// any write, and the generated child plan overwrites every mapped member
    /// from the desired state afterwards. Without this call added children
    /// need a public parameterless constructor.
    /// </remarks>
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

    /// <summary>
    /// Unlinks removed children from the parent collection without deleting their rows.
    /// </summary>
    /// <remarks>
    /// Only valid for optional relationships and non-owned navigations; required
    /// or owned relationships fail plan validation with an explicit diagnostic.
    /// </remarks>
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
    /// <remarks>
    /// The child stays in the parent collection; only <paramref name="markDeleted" />
    /// distinguishes it afterwards. Not valid for owned navigations.
    /// </remarks>
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

    /// <summary>
    /// Persists collection order through a caller-supplied setter.
    /// </summary>
    /// <remarks>
    /// Opt-in: without this call a pure reorder (no membership change) fails
    /// explicitly instead of pretending the in-memory enumeration order is
    /// persisted. Membership changes still reorder the tracked in-memory list
    /// to the desired DTO order; the setter additionally writes the persisted
    /// position for every element when the key order changed.
    /// </remarks>
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
