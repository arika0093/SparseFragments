using System.Collections;
using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// Executes a validated keyed-collection sync plan.
/// </summary>
/// <remarks>
/// Runs only after <see cref="CollectionSync" /> validated keys, coverage,
/// relationships, and policies, so execution performs no further checks
/// besides the final order-coverage guard.
/// </remarks>
internal static class CollectionExecution
{
    public static void ExecuteSync<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        IList list,
        SyncPlan<TDtoChild, TEntityChild> plan,
        Dictionary<Guid, (string Path, object Entity)> correlation
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        foreach (var addition in plan.Added)
        {
            context.Add(addition.Entity);
            var entry = PlanApplyState.AttachSingle(context, addition.Entity);
            var sub = new PlanWriteState(
                context,
                addition.Entity,
                entry,
                addition.Dto,
                runtime.DtoName,
                correlation
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
        }

        ReorderList(runtime, list, plan.FinalOrder);
        if (plan.PersistOrder && runtime.OrderSetter is not null)
        {
            for (var index = 0; index < plan.FinalOrder.Count; index++)
            {
                runtime.OrderSetter(plan.FinalOrder[index], index);
            }
        }
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

    private static void ReorderList<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        IList list,
        List<TEntityChild> finalOrder
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        if (finalOrder.Count != list.Count)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                runtime.DtoName,
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
    public List<PlannedEdit<TDtoChild, TEntityChild>> Edits { get; } = [];

    /// <summary>Gets or sets the final in-memory order.</summary>
    public List<TEntityChild> FinalOrder { get; set; } = [];

    /// <summary>Gets or sets whether the order setter persists positions.</summary>
    public bool PersistOrder { get; set; }
}

/// <summary>
/// A validated child addition.
/// </summary>
internal sealed class PlannedAddition<TDtoChild, TEntityChild>
    where TDtoChild : class
    where TEntityChild : class
{
    /// <summary>Creates a planned addition.</summary>
    public PlannedAddition(TDtoChild dto, TEntityChild entity, Guid? temporaryKey)
    {
        Dto = dto;
        Entity = entity;
        TemporaryKey = temporaryKey;
    }

    /// <summary>Gets the after-state DTO element.</summary>
    public TDtoChild Dto { get; }

    /// <summary>Gets the created child entity.</summary>
    public TEntityChild Entity { get; }

    /// <summary>Gets the temporary identity, when the DTO declares one.</summary>
    public Guid? TemporaryKey { get; }
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
internal sealed class PlannedEdit<TDtoChild, TEntityChild>
    where TDtoChild : class
    where TEntityChild : class
{
    /// <summary>Creates a planned edit.</summary>
    public PlannedEdit(
        TDtoChild desired,
        TEntityChild entity,
        ISparseChangeSet changes,
        string path,
        object key
    )
    {
        Desired = desired;
        Entity = entity;
        Changes = changes;
        Path = path;
        Key = key;
    }

    /// <summary>Gets the desired DTO element.</summary>
    public TDtoChild Desired { get; }

    /// <summary>Gets the tracked child entity.</summary>
    public TEntityChild Entity { get; }

    /// <summary>Gets the nested element transition.</summary>
    public ISparseChangeSet Changes { get; }

    /// <summary>Gets the dotted DTO path for diagnostics.</summary>
    public string Path { get; }

    /// <summary>Gets the stable key.</summary>
    public object Key { get; }
}
