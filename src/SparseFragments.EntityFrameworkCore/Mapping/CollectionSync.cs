using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// Validates keyed transitions before mutating and executes the queued sync.
/// </summary>
/// <remarks>
/// Every failure below throws during <c>Prepare</c>, before any queued write
/// runs, except member-level child writes for added elements, which execute
/// with the collection action after relationship, key, and policy validation.
/// </remarks>
internal static class CollectionSync
{
    public static void Prepare<TDtoChild, TEntityChild>(
        this CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        List<Action> actions
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        var view = runtime.Fetch(state.Changes, runtime.DtoName);
        if (view is null)
        {
            if (state.HasRecords(runtime.DtoName))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    state.DisplayPath(runtime.DtoName),
                    $"The DTO member '{runtime.DtoName}' changed but its keyed transition is unreadable. The change set does not match the mapped DTO type."
                );
            }

            return;
        }

        if (!view.IsChanged)
        {
            return;
        }

        var desired = CollectionState.ReadDesired(runtime, state);
        var collection = state.Context.Entry(state.Entity).Collection(runtime.NavigationName);
        var list = CollectionState.ReadTrackedList(runtime, state, collection);
        var tracked = CollectionState.IndexTracked(runtime, state, list);
        var indexedDesired = CollectionState.IndexDesired(runtime, state, desired);
        var plan = PlanSync(runtime, state, view, list, tracked, indexedDesired);
        foreach (var edit in plan.Edits)
        {
            var sub = new PlanApplyState(
                state.Context,
                edit.Entity,
                PlanApplyState.AttachSingle(state.Context, edit.Entity),
                edit.Desired,
                edit.Changes,
                edit.Path,
                isNested: true,
                sharedCorrelation: state.Correlation
            );
            runtime.ChildPlan.Prepare(sub, actions);
        }

        actions.Add(() =>
            CollectionExecution.ExecuteSync(runtime, state.Context, list, plan, state.Correlation)
        );
    }

    public static void WriteAll<TDtoChild, TEntityChild>(
        this CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanWriteState state,
        List<Action> actions
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actions);
        var desired = runtime.DesiredReader(state.Desired)?.ToList();
        if (desired is null || desired.Count == 0)
        {
            return;
        }

        var seen = new HashSet<object>(runtime.KeyComparer);
        var creations = new List<(TDtoChild Dto, TEntityChild Entity)>(desired.Count);
        foreach (var element in desired)
        {
            if (element is null)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    state.DisplayPath(runtime.DtoName),
                    $"The DTO member '{runtime.DtoName}' contains a null element, which cannot materialize a child entity."
                );
            }

            var key =
                runtime.DtoKey(element)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    state.DisplayPath(runtime.DtoName),
                    $"A desired child under '{runtime.DtoName}' has a null identity."
                );
            if (!seen.Add(key))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    state.DisplayPath(runtime.DtoName),
                    $"The DTO member '{runtime.DtoName}' contains a duplicated child key."
                );
            }

            var created =
                runtime.CreateFactory(element)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.NestedNotCreatable,
                    state.DisplayPath(runtime.DtoName),
                    $"The Create() factory for '{runtime.DtoName}' returned null."
                );
            creations.Add((element, created));
            RecordTemporary(
                state.Correlation,
                runtime.TemporaryGetter?.Invoke(element),
                created,
                state.DisplayPath(runtime.DtoName)
            );
        }

        actions.Add(() =>
        {
            var list = CollectionState.ReadList(
                runtime,
                state.Context,
                state.Entity,
                state.DisplayPath(runtime.DtoName)
            );
            foreach (var (dto, entity) in creations)
            {
                state.Context.Add(entity);
                var entry = PlanApplyState.AttachSingle(state.Context, entity);
                var sub = new PlanWriteState(
                    state.Context,
                    entity,
                    entry,
                    dto,
                    state.DisplayPath(runtime.DtoName),
                    state.Correlation
                );
                var subs = new List<Action>();
                runtime.ChildPlan.WriteAll(sub, subs);
                foreach (var subAction in subs)
                {
                    subAction();
                }

                list.Add(entity);
            }

            if (runtime.OrderSetter is not null)
            {
                for (var index = 0; index < list.Count; index++)
                {
                    runtime.OrderSetter((TEntityChild)list[index]!, index);
                }
            }
        });
    }

    private static SyncPlan<TDtoChild, TEntityChild> PlanSync<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        KeyedCollectionView<TDtoChild> view,
        IList list,
        Dictionary<object, TEntityChild> tracked,
        List<(TDtoChild Element, object Key, Guid? Temporary)> desired
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var path = state.DisplayPath(runtime.DtoName);
        var plan = new SyncPlan<TDtoChild, TEntityChild>();
        foreach (var item in view.Items)
        {
            if (item.IsAdded && item.IsRemoved)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    path,
                    $"The keyed transition for '{runtime.DtoName}' marks one entry added and removed."
                );
            }

            if (item.IsAdded)
            {
                plan.Added.Add(PlanAddition(runtime, state, tracked, item));
            }
            else if (item.IsRemoved)
            {
                plan.Removed.Add(PlanRemoval(runtime, state, tracked, item));
            }
            else if (item.IsEdited)
            {
                plan.Edits.Add(PlanEdit(runtime, state, tracked, desired, item));
            }
        }

        ValidateCoverage(runtime, state, view, tracked, desired, plan);
        plan.FinalOrder = PlanOrder(runtime, state, list, tracked, desired, plan);
        if (
            view.OrderChanged
            && plan.Added.Count == 0
            && plan.Removed.Count == 0
            && plan.Edits.Count == 0
            && runtime.OrderSetter is null
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionNotSupported,
                path,
                $"The collection '{runtime.DtoName}' only reordered, but no Order() setter is configured. In-memory enumeration order is never assumed persisted: configure Order() to persist positions."
            );
        }

        plan.PersistOrder = view.OrderChanged && runtime.OrderSetter is not null;
        return plan;
    }

    private static PlannedAddition<TDtoChild, TEntityChild> PlanAddition<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        Dictionary<object, TEntityChild> tracked,
        KeyedItemView<TDtoChild> item
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var path = state.DisplayPath(runtime.DtoName);
        var after =
            item.After.IsPresent && item.After.Value is not null
                ? item.After.Value
                : throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    path,
                    $"The collection '{runtime.DtoName}' added an entry with no after-state."
                );
        var key =
            runtime.DtoKey(after)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyConflict,
                path,
                $"An added child under '{runtime.DtoName}' has a null identity."
            );
        if (runtime.IsUnassigned(key))
        {
            if (
                runtime.TemporaryDeclared
                && (item.TemporaryKey is null || item.TemporaryKey == Guid.Empty)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"An added child under '{runtime.DtoName}' has an unassigned key but no temporary identity, so it cannot correlate across SaveChanges."
                );
            }
        }
        else if (tracked.ContainsKey(key))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyConflict,
                path,
                $"The collection '{runtime.DtoName}' adds a key that already has a tracked child. Added rows must not collide with sync-by-key identities."
            );
        }

        var created =
            runtime.CreateFactory(after)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                path,
                $"The Create() factory for '{runtime.DtoName}' returned null."
            );
        RecordTemporary(state.Correlation, item.TemporaryKey, created, path);
        return new PlannedAddition<TDtoChild, TEntityChild>(after, created, item.TemporaryKey);
    }

    private static PlannedRemoval<TEntityChild> PlanRemoval<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        Dictionary<object, TEntityChild> tracked,
        KeyedItemView<TDtoChild> item
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var path = state.DisplayPath(runtime.DtoName);
        var before =
            item.Before.IsPresent && item.Before.Value is not null
                ? item.Before.Value
                : throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    path,
                    $"The collection '{runtime.DtoName}' removed an entry with no before-state."
                );
        var key = runtime.DtoKey(before);
        if (key is null || !tracked.TryGetValue(key, out var child))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyNotFound,
                path,
                $"The collection '{runtime.DtoName}' removes a child with no matching tracked identity. Sync-by-key never invents the missing row."
            );
        }

        return new PlannedRemoval<TEntityChild>(child, key);
    }

    private static PlannedEdit<TDtoChild, TEntityChild> PlanEdit<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        Dictionary<object, TEntityChild> tracked,
        List<(TDtoChild Element, object Key, Guid? Temporary)> desired,
        KeyedItemView<TDtoChild> item
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var path = state.DisplayPath(runtime.DtoName);
        if (runtime.IsUnassigned(item.Key))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyConflict,
                path,
                $"The collection '{runtime.DtoName}' edits an unassigned (temporary) identity, which the server cannot resolve to a persisted row."
            );
        }

        if (!tracked.TryGetValue(item.Key, out var child))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyNotFound,
                $"{path}[{item.Key}]",
                $"The collection '{runtime.DtoName}' edits a child with no matching tracked identity."
            );
        }

        if (item.Edit is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                $"{path}[{item.Key}]",
                $"The collection '{runtime.DtoName}' edits a child with no nested transition."
            );
        }

        var match = desired.FirstOrDefault(candidate =>
            !runtime.IsUnassigned(candidate.Key)
            && runtime.KeyComparer.Equals(candidate.Key, item.Key)
        );
        if (match.Element is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                $"{path}[{item.Key}]",
                $"The collection '{runtime.DtoName}' edits a child missing from the desired collection. Pass the full desired state."
            );
        }

        return new PlannedEdit<TDtoChild, TEntityChild>(
            match.Element,
            child,
            item.Edit,
            $"{path}[{item.Key}]",
            item.Key
        );
    }

    private static void ValidateCoverage<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        KeyedCollectionView<TDtoChild> view,
        Dictionary<object, TEntityChild> tracked,
        List<(TDtoChild Element, object Key, Guid? Temporary)> desired,
        SyncPlan<TDtoChild, TEntityChild> plan
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var path = state.DisplayPath(runtime.DtoName);
        var removedKeys = new HashSet<object>(
            plan.Removed.Select(static removal => removal.Key),
            runtime.KeyComparer
        );
        if (plan.Edits.Any(edit => removedKeys.Contains(edit.Key)))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The keyed transition for '{runtime.DtoName}' edits and removes the same child."
            );
        }

        var desiredAssigned = new HashSet<object>(
            desired
                .Where(candidate => !runtime.IsUnassigned(candidate.Key))
                .Select(static candidate => candidate.Key),
            runtime.KeyComparer
        );
        if (removedKeys.Overlaps(desiredAssigned))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The desired collection '{runtime.DtoName}' still contains a removed child. Pass the full desired after-state."
            );
        }

        if (tracked.Keys.Any(key => !removedKeys.Contains(key) && !desiredAssigned.Contains(key)))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The tracked collection '{runtime.DtoName}' holds a child missing from the desired state. Sync-by-key validates the full collection instead of rewriting partial navigations."
            );
        }

        if (desired.Count != tracked.Count - plan.Removed.Count + plan.Added.Count)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The desired collection '{runtime.DtoName}' does not cover the full tracked state (tracked {tracked.Count}, removed {plan.Removed.Count}, added {plan.Added.Count}, desired {desired.Count})."
            );
        }

        var afterOrder = new HashSet<object>(
            view.AfterOrder.Where(key => !runtime.IsUnassigned(key)),
            runtime.KeyComparer
        );
        if (!afterOrder.SetEquals(desiredAssigned))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The keyed transition order for '{runtime.DtoName}' does not cover the desired assigned keys. Refusing a partial reorder."
            );
        }
    }

    private static List<TEntityChild> PlanOrder<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        IList list,
        Dictionary<object, TEntityChild> tracked,
        List<(TDtoChild Element, object Key, Guid? Temporary)> desired,
        SyncPlan<TDtoChild, TEntityChild> plan
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var path = state.DisplayPath(runtime.DtoName);
        var addedByTemporary = new Dictionary<Guid, TEntityChild>();
        var addedByKey = new Dictionary<object, TEntityChild>(runtime.KeyComparer);
        var unassignedAdditions = new Queue<TEntityChild>();
        foreach (var addition in plan.Added)
        {
            var addedKey = runtime.DtoKey(addition.Dto);
            if (addition.TemporaryKey is not null && addition.TemporaryKey != Guid.Empty)
            {
                if (!addedByTemporary.TryAdd(addition.TemporaryKey.Value, addition.Entity))
                {
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.CollectionKeyConflict,
                        path,
                        $"The collection '{runtime.DtoName}' adds a duplicated temporary identity."
                    );
                }
            }
            else if (addedKey is not null && !runtime.IsUnassigned(addedKey))
            {
                if (!addedByKey.TryAdd(addedKey, addition.Entity))
                {
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.CollectionKeyConflict,
                        path,
                        $"The collection '{runtime.DtoName}' adds a duplicated child key."
                    );
                }
            }
            else
            {
                unassignedAdditions.Enqueue(addition.Entity);
            }
        }

        var finalOrder = new List<TEntityChild>(desired.Count);
        foreach (var candidate in desired)
        {
            if (!runtime.IsUnassigned(candidate.Key))
            {
                if (
                    !tracked.TryGetValue(candidate.Key, out var child)
                    && !addedByKey.TryGetValue(candidate.Key, out child)
                )
                {
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.CollectionKeyNotFound,
                        path,
                        $"The desired collection '{runtime.DtoName}' references a child with no tracked identity."
                    );
                }

                finalOrder.Add(child);
                continue;
            }

            if (candidate.Temporary is not null && candidate.Temporary != Guid.Empty)
            {
                if (!addedByTemporary.TryGetValue(candidate.Temporary.Value, out var created))
                {
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.MetadataMismatch,
                        path,
                        $"The desired collection '{runtime.DtoName}' references a temporary identity with no matching addition."
                    );
                }

                finalOrder.Add(created);
                continue;
            }

            if (unassignedAdditions.Count == 0)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    path,
                    $"The desired collection '{runtime.DtoName}' holds an unassigned child with no matching addition."
                );
            }

            finalOrder.Add(unassignedAdditions.Dequeue());
        }

        if (unassignedAdditions.Count > 0)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The keyed transition for '{runtime.DtoName}' adds children missing from the desired state."
            );
        }

        if (runtime.RemoveKind == SparseEntityRemoveKind.SoftDelete)
        {
            var softDeleted = new HashSet<TEntityChild>(
                plan.Removed.Select(static removal => removal.Entity),
                ReferenceEqualityComparer.Instance
            );
            foreach (var child in list.Cast<TEntityChild>().Where(softDeleted.Contains))
            {
                finalOrder.Add(child);
            }
        }

        return finalOrder;
    }

    private static void RecordTemporary(
        Dictionary<Guid, (string Path, object Entity)> correlation,
        Guid? temporaryKey,
        object created,
        string path
    )
    {
        if (temporaryKey is null || temporaryKey == Guid.Empty)
        {
            return;
        }

        if (
            correlation.TryGetValue(temporaryKey.Value, out var existing)
            && !ReferenceEquals(existing.Entity, created)
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyConflict,
                path,
                "Two created children share one temporary identity."
            );
        }

        correlation[temporaryKey.Value] = (path, created);
    }
}
