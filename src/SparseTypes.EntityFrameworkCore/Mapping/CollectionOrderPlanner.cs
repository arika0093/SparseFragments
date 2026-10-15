namespace SparseTypes.EntityFrameworkCore.Mapping;

internal static class CollectionOrderPlanner
{
    internal static List<TEntityChild> PlanOrder<TDtoChild, TEntityChild>(
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
        var survivors = new Dictionary<object, TEntityChild>(
            Math.Max(0, tracked.Count - plan.Removed.Count),
            runtime.KeyComparer
        );
        foreach (var (key, child) in tracked)
        {
            if (!removed.Contains(key))
            {
                survivors[key] = child;
            }
        }

        var assignedAdds = new Dictionary<object, TEntityChild>(
            plan.Added.Count,
            runtime.KeyComparer
        );
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

        // Assigned keys follow AfterOrder; unassigned additions fill sentinel
        // slots in AfterIndex order.
        var assignedOrder = new List<TEntityChild>(view.AfterOrder.Count);
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
        // declared sentinel in AfterOrder.
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
        var merged = new List<TEntityChild>(view.AfterOrder.Count);
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
            var retentionCandidates = plan.Removed.Count + survivors.Count;
            HashSet<TEntityChild>? retained = null;
            if (retentionCandidates > 0)
            {
                // List.Contains scans the growing output per retained row;
                // one membership set keeps retention linear instead.
                retained = new HashSet<TEntityChild>(
                    merged,
                    System.Collections.Generic.EqualityComparer<TEntityChild>.Default
                );
            }

            // Soft-deleted rows stay in the collection: removals are excluded
            // from the survivor map, so re-add them from the removal plan.
            foreach (
                var removal in plan.Removed.Where(removal =>
                    retained is null || retained.Add(removal.Entity)
                )
            )
            {
                merged.Add(removal.Entity);
            }

            // Previously flagged rows leave the DTO projection but stay
            // tracked: retain proven rows in tracked order instead of failing.
            var afterSet = new HashSet<object>(
                view.AfterOrder.Where(key => !runtime.IsUnassigned(key)),
                runtime.KeyComparer
            );
            foreach (
                var survivor in survivors.Where(candidate =>
                    !afterSet.Contains(candidate.Key)
                    && runtime.IsDeletedPredicate is not null
                    && runtime.IsDeletedPredicate(candidate.Value)
                    && (retained is null || retained.Add(candidate.Value))
                )
            )
            {
                merged.Add(survivor.Value);
            }
        }

        return merged;
    }
}
