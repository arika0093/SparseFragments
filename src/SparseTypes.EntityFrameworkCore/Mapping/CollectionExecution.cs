using System.Collections;
using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Executes a validated keyed-collection sync plan.
/// </summary>
/// <remarks>Runs only after <see cref="CollectionSync" /> validated keys.</remarks>
internal static class CollectionExecution
{
    public static void ExecuteSync<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        object parent,
        SyncPlan<TDtoChild, TEntityChild> plan,
        string path,
        Dictionary<string, Dictionary<Guid, object>>? correlation,
        List<SparseEntityAppliedPath>? appliedPaths,
        Dictionary<string, Dictionary<object, string>>? collectionScopeKeys,
        Dictionary<string, NestedScopeTable>? nestedScopes = null,
        CollectionScopeIdentity parentScope = default
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        // Child edits already have queued writes; an edits-only plan changes no
        // collection state.
        if (plan.Added.Count == 0 && plan.Removed.Count == 0 && !plan.OrderChanged)
            return;

        var list = CollectionState.EnsureList<TDtoChild, TEntityChild>(
            runtime,
            context,
            parent,
            path
        );

        foreach (var addition in plan.Added)
        {
            if (
                correlation is not null
                && addition.TemporaryKey is not null
                && addition.TemporaryKey != Guid.Empty
            )
            {
                if (!correlation.TryGetValue(path, out var scope))
                {
                    scope = new Dictionary<Guid, object>();
                    correlation[path] = scope;
                }

                if (nestedScopes is null || parentScope.Kind == CollectionScopeKind.Unscoped)
                {
                    if (
                        scope.TryGetValue(addition.TemporaryKey.Value, out var existing)
                        && !ReferenceEquals(existing, addition.Entity)
                    )
                    {
                        throw new SparseEntityMappingException(
                            SparseEntityMappingFailure.CollectionKeyConflict,
                            path,
                            $"Two created children under '{path}' share one temporary identity. Temporary identities must be unique within one collection scope."
                        );
                    }

                    scope[addition.TemporaryKey.Value] = addition.Entity;
                }
                else
                {
                    // Legacy bucket keeps its contents for compatible reads.
                    scope[addition.TemporaryKey.Value] = addition.Entity;
                    if (!nestedScopes.TryGetValue(path, out var table))
                    {
                        table = new NestedScopeTable(parentScope.KeyComparer);
                        nestedScopes.Add(path, table);
                    }

                    table.Record(path, parentScope, addition.TemporaryKey.Value, addition.Entity);
                }
            }

            context.Add(addition.Entity);
            var entry = PlanApplyState.AttachSingle(context, addition.Entity);
            var scopeKey = addition.ScopeKey;
            RecordCollectionScope(
                collectionScopeKeys,
                nestedScopes,
                path,
                addition.Entity,
                scopeKey,
                parentScope
            );
            if (appliedPaths is not null)
            {
                appliedPaths.Add(
                    AppliedPathFactory.Create(
                        path,
                        entry,
                        runtime.NavigationName,
                        SparseEntityApplyOperationKind.Created
                    )
                );
            }

            // Nested scopes qualify with the added parent's identity so the
            // Resolve a reused temporary key independently for each new parent.
            var sub = new PlanWriteState(
                context,
                addition.Entity,
                entry,
                addition.Dto,
                $"{path}[{scopeKey}]",
                correlation,
                appliedPaths,
                collectionScopeKeys,
                nestedScopes,
                CollectionScopeIdentity.ForElement(
                    addition.AssignedKey,
                    addition.AssignedKey is not null,
                    addition.TemporaryKey,
                    runtime.KeyComparer
                )
            );
            var subs = new List<Action>();
            runtime.ChildPlan.WriteAll(sub, subs);
            foreach (var subAction in subs)
            {
                subAction();
            }

            list.Add(addition.Entity);
        }

        foreach (var removal in plan.Removed)
        {
            ApplyRemoval(runtime, context, list, removal);
            if (appliedPaths is not null)
            {
                appliedPaths.Add(
                    AppliedPathFactory.Create(
                        path,
                        context.Entry(removal.Entity),
                        runtime.NavigationName,
                        SparseEntityApplyOperationKind.Removed,
                        runtime.RemoveKind
                    )
                );
            }
        }

        // Edits-only plans keep the tracked order: FinalOrder stays empty and
        // no reorder runs. Membership and order plans always carry coverage.
        if (plan.FinalOrder.Count > 0)
        {
            ReorderList(list, plan.FinalOrder, path);
        }
        if (plan.PersistOrder && runtime.OrderSetter is not null)
        {
            for (var index = 0; index < plan.FinalOrder.Count; index++)
            {
                runtime.OrderSetter(plan.FinalOrder[index], index);
            }
        }
        if (plan.OrderChanged && appliedPaths is not null)
        {
            appliedPaths.Add(
                AppliedPathFactory.Create(
                    path,
                    context.Entry(parent),
                    runtime.NavigationName,
                    SparseEntityApplyOperationKind.Reordered
                )
            );
        }
    }

    private static void RecordCollectionScope(
        Dictionary<string, Dictionary<object, string>>? collectionScopeKeys,
        Dictionary<string, NestedScopeTable>? nestedScopes,
        string path,
        object entity,
        string scopeKey,
        CollectionScopeIdentity scope
    )
    {
        if (collectionScopeKeys is null)
        {
            return;
        }

        if (!collectionScopeKeys.TryGetValue(path, out var scopes))
        {
            scopes = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            collectionScopeKeys.Add(path, scopes);
        }

        scopes[entity] = scopeKey;

        if (nestedScopes is null || scope.Kind == CollectionScopeKind.Unscoped)
        {
            return;
        }

        if (!nestedScopes.TryGetValue(path, out var table))
        {
            table = new NestedScopeTable(scope.KeyComparer);
            nestedScopes.Add(path, table);
        }

        table.RecordEntity(scope, entity);
    }

    private static void ApplyRemoval<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        IList list,
        PlannedRemoval<TEntityChild> removal
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        switch (runtime.RemoveKind)
        {
            case SparseEntityRemoveKind.Delete:
            case SparseEntityRemoveKind.Custom:
                runtime.RemoveHandler!(context, removal.Entity);
                list.Remove(removal.Entity);
                break;
            case SparseEntityRemoveKind.Detach:
                list.Remove(removal.Entity);
                break;
            case SparseEntityRemoveKind.SoftDelete:
                runtime.SoftDeleteMark!(removal.Entity);
                break;
        }
    }

    private static void ReorderList<TEntityChild>(
        IList list,
        List<TEntityChild> finalOrder,
        string path
    )
        where TEntityChild : class
    {
        if (finalOrder.Count != list.Count)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                "The planned collection order does not cover the tracked list. The navigation changed during Apply."
            );
        }

        for (var index = 0; index < finalOrder.Count; index++)
        {
            list[index] = finalOrder[index];
        }
    }
}

/// <summary>
/// The validated operations for one keyed-collection apply.
/// </summary>
internal sealed class SyncPlan<TDtoChild, TEntityChild>
    where TDtoChild : class
    where TEntityChild : class
{
    /// <summary>Gets the planned additions.</summary>
    public List<PlannedAddition<TDtoChild, TEntityChild>> Added { get; } = [];

    /// <summary>Gets the planned removals.</summary>
    public List<PlannedRemoval<TEntityChild>> Removed { get; } = [];

    /// <summary>Gets the planned edits.</summary>
    public List<PlannedEdit<TEntityChild>> Edits { get; } = [];

    /// <summary>Gets or sets the final in-memory order.</summary>
    public List<TEntityChild> FinalOrder { get; set; } = [];

    /// <summary>Gets or sets whether the order setter persists positions.</summary>
    public bool PersistOrder { get; set; }

    /// <summary>Gets or sets whether the accepted transition changed order.</summary>
    public bool OrderChanged { get; set; }
}

/// <summary>
/// A validated child addition.
/// </summary>
internal sealed class PlannedAddition<TDtoChild, TEntityChild>
    where TDtoChild : class
    where TEntityChild : class
{
    /// <summary>Creates a planned addition.</summary>
    public PlannedAddition(
        TDtoChild dto,
        TEntityChild entity,
        Guid? temporaryKey,
        object? assignedKey,
        int sequenceHint = 0
    )
    {
        Dto = dto;
        Entity = entity;
        TemporaryKey = temporaryKey;
        AssignedKey = assignedKey;
        SequenceHint = sequenceHint;
    }

    /// <summary>Gets the after-state DTO element.</summary>
    public TDtoChild Dto { get; }

    /// <summary>Gets the created child entity.</summary>
    public TEntityChild Entity { get; }

    /// <summary>Gets the path-scoped temporary identity, when the DTO declares one.</summary>
    public Guid? TemporaryKey { get; }

    /// <summary>Gets the assigned key, or null for unassigned additions.</summary>
    public object? AssignedKey { get; }

    /// <summary>Gets the item AfterIndex used to order sentinel slots.</summary>
    public int SequenceHint { get; }

    /// <summary>
    /// Gets the display key qualifying nested scopes: the assigned key, the
    /// temporary identity, or the sequence hint as a last resort.
    /// </summary>
    public string ScopeKey =>
        AssignedKey?.ToString() ?? TemporaryKey?.ToString() ?? SequenceHint.ToString();
}

/// <summary>
/// A validated child removal.
/// </summary>
internal sealed class PlannedRemoval<TEntityChild>
    where TEntityChild : class
{
    /// <summary>Creates a planned removal.</summary>
    public PlannedRemoval(TEntityChild entity, object key)
    {
        Entity = entity;
        Key = key;
    }

    /// <summary>Gets the tracked child entity.</summary>
    public TEntityChild Entity { get; }

    /// <summary>Gets the stable key.</summary>
    public object Key { get; }
}

/// <summary>
/// A validated child edit.
/// </summary>
internal sealed class PlannedEdit<TEntityChild>
    where TEntityChild : class
{
    /// <summary>Creates a planned edit.</summary>
    public PlannedEdit(TEntityChild entity, ISparseChangeSet changes, string path, object key)
    {
        Entity = entity;
        Changes = changes;
        Path = path;
        Key = key;
    }

    /// <summary>Gets the tracked child entity.</summary>
    public TEntityChild Entity { get; }

    /// <summary>Gets the nested element transition.</summary>
    public ISparseChangeSet Changes { get; }

    /// <summary>Gets the dotted DTO path for diagnostics.</summary>
    public string Path { get; }

    /// <summary>Gets the stable key.</summary>
    public object Key { get; }
}
