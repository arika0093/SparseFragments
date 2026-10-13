using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Validates delta-based keyed transitions before mutating and executes the queued sync.
/// </summary>
/// <remarks>
/// Direct ChangeSet sourcing: additions come from item <c>After</c> snapshots,
/// edits recurse through item <c>Edit</c> transitions, removals resolve from
/// keyed before-identity, and order persists from the transition's
/// <c>AfterOrder</c> plus temporary-identity metadata. No desired or
/// full-collection DTO is required at any point. Every failure below throws
/// during <c>Prepare</c>, before any queued write runs; navigation assignment,
/// tracking, correlation recording, and child wholesale writes all happen in
/// the execution phase.
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

        var collection = CollectionEntryAccess.Read(
            state.Context,
            state.Entity,
            runtime.NavigationName
        );
        // Preflight is read-only: PeekList never assigns a null navigation.
        var list = CollectionState.ReadTrackedList(runtime, state, collection);
        var tracked = CollectionState.IndexTracked(runtime, state, list);
        var path = state.DisplayPath(runtime.DtoName);
        var plan = PlanSync(runtime, view, tracked, path);
        foreach (var edit in plan.Edits)
        {
            var sub = new PlanApplyState(
                state.Context,
                edit.Entity,
                PlanApplyState.ValidationEntry(state.Context, edit.Entity, actions),
                edit.Changes,
                edit.Path,
                true,
                state.Correlation,
                appliedPaths: state.AppliedPaths,
                collectionScopeKeys: state.CollectionScopeKeys,
                nestedScopes: state.NestedScopes,
                parentScope: CollectionScopeIdentity.Assigned(edit.Key, runtime.KeyComparer)
            );
            runtime.ChildPlan.Prepare(sub, actions);
        }

        // Validate added wholesale content preflight on detached prospectives
        // so a later child failure leaves the graph untouched.
        foreach (var addition in plan.Added)
        {
            ValidateAdditionWholesale(runtime, state.Context, path, addition);
        }

        var capturedEntity = state.Entity;
        var capturedScopes = state.NestedScopes;
        var capturedParentScope = state.ParentScope;
        actions.Add(() =>
            CollectionExecution.ExecuteSync(
                runtime,
                state.Context,
                capturedEntity,
                plan,
                path,
                state.Correlation,
                state.AppliedPaths,
                state.CollectionScopeKeys,
                capturedScopes,
                capturedParentScope
            )
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
        // Wholesale writes copy from the explicit After snapshot carried by the
        // parent transition (never a reconstructed root DTO). A null snapshot
        // is absent and preserves factory state; an empty snapshot means empty
        // and clears the destination below.
        var snapshot = runtime.SnapshotReader(state.AfterSnapshot)?.ToList();
        if (snapshot is null)
        {
            return;
        }

        var path = state.DisplayPath(runtime.DtoName);
        var seen = new HashSet<object>(runtime.KeyComparer);
        var seenTemporary = new HashSet<Guid>();
        var creations = new List<(TDtoChild Dto, TEntityChild Entity, Guid? Temporary)>(
            snapshot.Count
        );
        foreach (var element in snapshot)
        {
            if (element is null)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    path,
                    $"The DTO member '{runtime.DtoName}' contains a null element, which cannot materialize a child entity."
                );
            }

            var key =
                runtime.DtoKey(element)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"A desired child under '{runtime.DtoName}' has a null identity."
                );
            var temporary = runtime.TemporaryGetter?.Invoke(element);
            if (
                runtime.IsUnassigned(key)
                && temporary is not null
                && temporary != Guid.Empty
                && !seenTemporary.Add(temporary.Value)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"Two created children under '{runtime.DtoName}' share one temporary identity. Temporary identities must be unique within one collection scope."
                );
            }
            else if (!runtime.IsUnassigned(key) && !seen.Add(key))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The DTO member '{runtime.DtoName}' contains a duplicated child key."
                );
            }

            var created =
                runtime.CreateFactory(element)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.NestedNotCreatable,
                    path,
                    $"The Create() factory for '{runtime.DtoName}' returned null."
                );
            var temporaryKey = runtime.TemporaryGetter?.Invoke(element);
            creations.Add((element, created, temporaryKey));
            ValidateWholesaleProspective(runtime, state.Context, path, element, created);
        }

        actions.Add(() =>
        {
            var list = CollectionState.EnsureList<TDtoChild, TEntityChild>(
                runtime,
                state.Context,
                state.Entity,
                path
            );
            // Replace semantics: the snapshot is complete, so factory- or
            // constructor-provided children absent from it must not survive to
            // tracking. Clearing the contents (not the reference) keeps
            // read-only navigations working, and detaching Added orphans keeps
            // them out of SaveChanges entirely. Pre-existing tracked rows keep
            // their own state: relationship fixup owns them.
            var orphans = list.Cast<object>().ToList();
            if (list.Count > 0)
            {
                list.Clear();
            }

            foreach (var orphan in orphans)
            {
                var orphanEntry = state.Context.Entry(orphan);
                if (orphanEntry.State == EntityState.Added)
                {
                    orphanEntry.State = EntityState.Detached;
                }
            }

            for (var index = 0; index < creations.Count; index++)
            {
                var (dto, entity, temporaryKey) = creations[index];
                if (temporaryKey is not null && temporaryKey != Guid.Empty)
                {
                    state.RecordTemporary(path, temporaryKey.Value, entity);
                }

                // Nested scopes qualify with the added element's identity so
                // the same temporary value reused under two new siblings
                // resolves independently instead of falsely conflicting.
                var key = runtime.DtoKey(dto);
                string scopeKey;
                if (key is not null && !runtime.IsUnassigned(key))
                {
                    scopeKey = key.ToString()!;
                }
                else if (temporaryKey is not null && temporaryKey != Guid.Empty)
                {
                    scopeKey = temporaryKey.Value.ToString();
                }
                else
                {
                    scopeKey = index.ToString();
                }
                var childPath = $"{path}[{scopeKey}]";

                state.Context.Add(entity);
                var entry = PlanApplyState.AttachSingle(state.Context, entity);
                // The owning element's scope, not the element's own: records
                // partition by parent scope.
                state.RecordCollectionScope(path, entity, scopeKey, state.ParentScope);
                if (state.AppliedPaths is not null)
                {
                    state.RecordAppliedPath(
                        AppliedPathFactory.Create(
                            path,
                            entry,
                            runtime.NavigationName,
                            SparseEntityApplyOperationKind.Created
                        )
                    );
                }

                var sub = new PlanWriteState(
                    state.Context,
                    entity,
                    entry,
                    dto,
                    childPath,
                    state.Correlation,
                    state.AppliedPaths,
                    state.CollectionScopeKeys,
                    state.NestedScopes,
                    CollectionScopeIdentity.ForElement(
                        key,
                        key is not null && !runtime.IsUnassigned(key),
                        temporaryKey,
                        runtime.KeyComparer
                    )
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
        KeyedCollectionView<TDtoChild> view,
        Dictionary<object, TEntityChild> tracked,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var plan = new SyncPlan<TDtoChild, TEntityChild> { OrderChanged = view.OrderChanged };
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
                plan.Added.Add(PlanAddition(runtime, tracked, plan, item, path));
            }
            else if (item.IsRemoved)
            {
                plan.Removed.Add(PlanRemoval(runtime, tracked, item, path));
            }
            else if (item.IsEdited)
            {
                plan.Edits.Add(PlanEdit(runtime, tracked, item, path));
            }
        }

        if (plan.Added.Count == 0 && plan.Removed.Count == 0 && !view.OrderChanged)
        {
            // Edits only, order untouched: validate only the addressed keys
            // (done above) and keep the tracked order as is. Skipping the
            // AfterOrder completeness proof keeps addressed-key edits safe on
            // partial loads; membership and order changes below still require
            // complete evidence.
            plan.PersistOrder = false;
            return plan;
        }

        ValidateDelta(runtime, view, tracked, plan, path);
        plan.FinalOrder = PlanOrder(runtime, view, tracked, plan, path);
        if (
            view.OrderChanged
            && plan.Added.Count == 0
            && plan.Removed.Count == 0
            && runtime.OrderSetter is null
        )
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionNotSupported,
                path,
                $"The collection '{runtime.DtoName}' reordered without membership changes, but no Order() setter is configured. In-memory enumeration order is never assumed persisted: configure Order() to persist positions."
            );
        }

        plan.PersistOrder = view.OrderChanged && runtime.OrderSetter is not null;
        return plan;
    }

    private static PlannedAddition<TDtoChild, TEntityChild> PlanAddition<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        Dictionary<object, TEntityChild> tracked,
        SyncPlan<TDtoChild, TEntityChild> plan,
        KeyedItemView<TDtoChild> item,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
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

            if (
                item.TemporaryKey is not null
                && item.TemporaryKey != Guid.Empty
                && plan.Added.Any(existing => existing.TemporaryKey == item.TemporaryKey)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"Two created children under '{runtime.DtoName}' share one temporary identity. Temporary identities must be unique within one collection scope."
                );
            }
        }
        else
        {
            if (tracked.ContainsKey(key))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The collection '{runtime.DtoName}' adds a key that already has a tracked child. Added rows must not collide with sync-by-key identities."
                );
            }

            if (
                plan.Added.Any(existing =>
                    existing.AssignedKey is not null
                    && runtime.KeyComparer.Equals(existing.AssignedKey, key)
                )
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The collection '{runtime.DtoName}' adds a duplicated child key."
                );
            }
        }

        // Creating the instance here allocates only; linking, tracking, and
        // correlation recording all happen in the execution phase.
        var created =
            runtime.CreateFactory(after)
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.NestedNotCreatable,
                path,
                $"The Create() factory for '{runtime.DtoName}' returned null."
            );
        return new PlannedAddition<TDtoChild, TEntityChild>(
            after,
            created,
            item.TemporaryKey,
            runtime.IsUnassigned(key) ? null : key,
            item.AfterIndex
        );
    }

    private static PlannedRemoval<TEntityChild> PlanRemoval<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        Dictionary<object, TEntityChild> tracked,
        KeyedItemView<TDtoChild> item,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var before =
            item.Before.IsPresent && item.Before.Value is not null
                ? item.Before.Value
                : throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    path,
                    $"The collection '{runtime.DtoName}' removed an entry with no before-state."
                );
        var key = runtime.DtoKey(before);
        if (key is null || runtime.IsUnassigned(key) || !tracked.TryGetValue(key, out var child))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyNotFound,
                path,
                $"The collection '{runtime.DtoName}' removes a child with no matching tracked identity in this parent collection. Sync-by-key never invents the missing row and never modifies a matching key under a different parent."
            );
        }

        return new PlannedRemoval<TEntityChild>(child, key);
    }

    private static PlannedEdit<TEntityChild> PlanEdit<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        Dictionary<object, TEntityChild> tracked,
        KeyedItemView<TDtoChild> item,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
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
                $"The collection '{runtime.DtoName}' edits a child with no matching tracked identity in this parent collection."
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

        // Child edits recurse through the typed element transition only; no
        // desired element DTO is consulted.
        return new PlannedEdit<TEntityChild>(child, item.Edit, $"{path}[{item.Key}]", item.Key);
    }

    private static void ValidateDelta<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        KeyedCollectionView<TDtoChild> view,
        Dictionary<object, TEntityChild> tracked,
        SyncPlan<TDtoChild, TEntityChild> plan,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
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

        // Order and membership evidence must be complete before anything
        // mutates: every assigned AfterOrder key must resolve to a tracked
        // survivor or an assigned addition, and every surviving tracked key
        // needs an AfterOrder slot. A filtered Include (partial load) or a
        // stale caller DTO fails here instead of syncing around unseen state.
        // SoftDelete is the single exception on the survivor side: previously
        // flagged rows stay tracked but leave the DTO projection, so their
        // omission is tolerated and they are retained (PlanOrder appends
        // them); anything else missing fails.
        var survivors = tracked.Keys.Where(key => !removedKeys.Contains(key)).ToList();
        var assignedAdds = plan
            .Added.Where(addition => addition.AssignedKey is not null)
            .Select(addition => addition.AssignedKey!)
            .ToList();
        var afterAssigned = view.AfterOrder.Where(key => !runtime.IsUnassigned(key)).ToList();
        var afterSet = new HashSet<object>(afterAssigned, runtime.KeyComparer);
        var known = new HashSet<object>(survivors, runtime.KeyComparer);
        known.UnionWith(assignedAdds);

        if (afterAssigned.Any(key => !known.Contains(key)))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyNotFound,
                path,
                $"The keyed transition order for '{runtime.DtoName}' references a child with no tracked identity in this parent collection."
            );
        }

        var missing = survivors.Where(survivor => !afterSet.Contains(survivor)).ToList();
        if (missing.Count > 0)
        {
            // SoftDelete tolerates only provably flagged rows: the predicate
            // runs here during validation (it must be pure) and stale DTOs
            // that omit an active child still fail. Anything else missing
            // fails regardless of policy.
            var proven =
                runtime.RemoveKind == SparseEntityRemoveKind.SoftDelete
                && runtime.IsDeletedPredicate is not null
                && missing.All(key =>
                    tracked.TryGetValue(key, out var child) && runtime.IsDeletedPredicate(child)
                );
            if (!proven)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyNotFound,
                    path,
                    $"The keyed transition order for '{runtime.DtoName}' omits a surviving tracked child. Refusing a partial sync: load the collection completely with an unfiltered Include or Load."
                );
            }
        }
    }

    private static List<TEntityChild> PlanOrder<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        KeyedCollectionView<TDtoChild> view,
        Dictionary<object, TEntityChild> tracked,
        SyncPlan<TDtoChild, TEntityChild> plan,
        string path
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var removed = new HashSet<object>(
            plan.Removed.Select(static removal => removal.Key),
            runtime.KeyComparer
        );
        var survivors = new Dictionary<object, TEntityChild>(runtime.KeyComparer);
        foreach (var (key, child) in tracked)
        {
            if (!removed.Contains(key))
            {
                survivors[key] = child;
            }
        }

        var assignedAdds = new Dictionary<object, TEntityChild>(runtime.KeyComparer);
        var temporarySeen = new HashSet<Guid>();
        foreach (var addition in plan.Added.OrderBy(static a => a.SequenceHint))
        {
            if (
                addition.TemporaryKey is not null
                && addition.TemporaryKey != Guid.Empty
                && !temporarySeen.Add(addition.TemporaryKey.Value)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The collection '{runtime.DtoName}' adds a duplicated temporary identity."
                );
            }

            if (
                addition.AssignedKey is not null
                && !assignedAdds.TryAdd(addition.AssignedKey, addition.Entity)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"The collection '{runtime.DtoName}' adds a duplicated child key."
                );
            }
        }

        // Delta order: AfterOrder carries assigned keys positionally; each
        // unassigned sentinel slot takes the next unassigned addition in
        // AfterIndex order (temporaries included: they share the declared
        // sentinel in AfterOrder, so their relative order comes from the item
        // AfterIndex metadata).
        var assignedOrder = new List<TEntityChild>();
        var sentinelSlots = 0;
        foreach (var key in view.AfterOrder)
        {
            if (runtime.IsUnassigned(key))
            {
                sentinelSlots++;
                continue;
            }

            if (survivors.TryGetValue(key, out var child))
            {
                assignedOrder.Add(child);
                continue;
            }

            if (assignedAdds.TryGetValue(key, out var created))
            {
                assignedOrder.Add(created);
                continue;
            }

            // Assigned AfterOrder keys outside membership validation only occur
            // when order persistence was skipped; fail rather than guessing.
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionKeyNotFound,
                path,
                $"The keyed transition order for '{runtime.DtoName}' references a child with no tracked identity in this parent collection."
            );
        }

        // All unassigned additions (temporary-keyed or plain) share the
        // declared sentinel in AfterOrder; splice them into sentinel slots in
        // AfterIndex order. Correlation stays per-addition and independent of
        // slot position.
        var orderedUnassigned = plan
            .Added.Where(addition => addition.AssignedKey is null)
            .OrderBy(static addition => addition.SequenceHint)
            .Select(static addition => addition.Entity)
            .ToList();
        if (orderedUnassigned.Count != sentinelSlots)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                path,
                $"The keyed transition order for '{runtime.DtoName}' holds {sentinelSlots} unassigned slots but {orderedUnassigned.Count} unassigned additions. Refusing a partial reorder."
            );
        }

        // Rebuild positionally: walk AfterOrder once, taking assigned entries
        // and unassigned additions in slot order.
        var merged = new List<TEntityChild>();
        var assignedIndex = 0;
        var unassignedIndex = 0;
        foreach (var key in view.AfterOrder)
        {
            if (runtime.IsUnassigned(key))
            {
                merged.Add(orderedUnassigned[unassignedIndex++]);
                continue;
            }

            merged.Add(assignedOrder[assignedIndex++]);
        }

        if (runtime.RemoveKind == SparseEntityRemoveKind.SoftDelete)
        {
            // Soft-deleted rows stay in the collection: removals are excluded
            // from the survivor map, so re-add them from the removal plan.
            foreach (var removal in plan.Removed.Where(removal => !merged.Contains(removal.Entity)))
            {
                merged.Add(removal.Entity);
            }

            // Previously flagged rows leave the DTO projection but stay
            // tracked: retain proven rows in tracked order instead of failing.
            // Their positions persist through Order() like any other element.
            // Anything unproven already failed validation above.
            var afterSet = new HashSet<object>(
                view.AfterOrder.Where(key => !runtime.IsUnassigned(key)),
                runtime.KeyComparer
            );
            foreach (
                var survivor in survivors.Where(candidate =>
                    !afterSet.Contains(candidate.Key)
                    && runtime.IsDeletedPredicate is not null
                    && runtime.IsDeletedPredicate(candidate.Value)
                    && !merged.Contains(candidate.Value)
                )
            )
            {
                merged.Add(survivor.Value);
            }
        }

        return merged;
    }

    private static void ValidateAdditionWholesale<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        string path,
        PlannedAddition<TDtoChild, TEntityChild> addition
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var probeKey = addition.AssignedKey;
        var probe = new PlanWriteState(
            context,
            addition.Entity,
            context.Entry(addition.Entity),
            addition.Dto,
            path,
            new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal),
            nestedScopes: new Dictionary<string, NestedScopeTable>(StringComparer.Ordinal),
            parentScope: CollectionScopeIdentity.ForElement(
                probeKey,
                probeKey is not null && !runtime.IsUnassigned(probeKey),
                addition.TemporaryKey,
                runtime.KeyComparer
            )
        );
        runtime.ChildPlan.WriteAll(probe, new List<Action>());
    }

    private static void ValidateWholesaleProspective<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        string path,
        TDtoChild snapshot,
        TEntityChild prospective
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var probeKey = runtime.DtoKey(snapshot);
        var probe = new PlanWriteState(
            context,
            prospective,
            context.Entry(prospective),
            snapshot,
            path,
            new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal),
            nestedScopes: new Dictionary<string, NestedScopeTable>(StringComparer.Ordinal),
            parentScope: CollectionScopeIdentity.ForElement(
                probeKey,
                probeKey is not null && !runtime.IsUnassigned(probeKey),
                runtime.TemporaryGetter?.Invoke(snapshot),
                runtime.KeyComparer
            )
        );
        runtime.ChildPlan.WriteAll(probe, new List<Action>());
    }
}
