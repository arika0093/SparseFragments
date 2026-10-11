using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// Reads and indexes the desired and tracked collection states for sync-by-key.
/// </summary>
/// <remarks>
/// Read-only: indexing never mutates entities, attaches nothing, and queries
/// nothing. Every inconsistency surfaces as an explicit mapping failure.
/// </remarks>
internal static class CollectionState
{
    public static List<TDtoChild> ReadDesired<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        return runtime.DesiredReader(state.Desired)?.ToList()
            ?? throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                state.DisplayPath(runtime.DtoName),
                $"The DTO member '{runtime.DtoName}' changed but the desired collection is null. Synchronizing to null is not supported: pass the full desired collection."
            );
    }

    public static IList ReadTrackedList<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        CollectionEntry collection
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        if (collection.EntityEntry.State != EntityState.Added && !collection.IsLoaded)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.CollectionNotLoaded,
                state.DisplayPath(runtime.DtoName),
                $"The collection '{runtime.DtoName}' changed but its navigation is not loaded. Apply never auto-loads: load it (Include or Load) so sync-by-key sees the full state, never an inferred empty."
            );
        }

        return ReadList(runtime, state.Context, state.Entity, state.DisplayPath(runtime.DtoName));
    }

    public static IList ReadList<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        object parent,
        string displayPath
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var current = context.Entry(parent).Collection(runtime.NavigationName).CurrentValue;
        if (current is IList list)
        {
            return list;
        }

        if (current is not null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                displayPath,
                $"The navigation '{runtime.NavigationName}' is not an IList collection, so keyed synchronization cannot reorder it in place."
            );
        }

        if (runtime.CollectionSetter is null)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                displayPath,
                $"The navigation '{runtime.NavigationName}' is null and read-only, so added children have nowhere to attach."
            );
        }

        var created = new List<TEntityChild>();
        runtime.CollectionSetter(parent, created);
        return created;
    }

    public static Dictionary<object, TEntityChild> IndexTracked<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        IList list
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var tracked = new Dictionary<object, TEntityChild>(runtime.KeyComparer);
        foreach (var entry in list)
        {
            if (entry is not TEntityChild child)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    state.DisplayPath(runtime.DtoName),
                    $"The navigation '{runtime.NavigationName}' contains an unexpected element type."
                );
            }

            var key =
                runtime.EntityKey(state.Context, child)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    state.DisplayPath(runtime.DtoName),
                    $"A tracked child under '{runtime.DtoName}' has a null identity, which cannot join keyed synchronization."
                );
            if (!tracked.TryAdd(key, child))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    state.DisplayPath(runtime.DtoName),
                    $"The tracked collection '{runtime.DtoName}' contains a duplicated child key."
                );
            }
        }

        return tracked;
    }

    public static List<(TDtoChild Element, object Key, Guid? Temporary)> IndexDesired<
        TDtoChild,
        TEntityChild
    >(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        PlanApplyState state,
        List<TDtoChild> desired
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var indexed = new List<(TDtoChild Element, object Key, Guid? Temporary)>(desired.Count);
        var seen = new HashSet<object>(runtime.KeyComparer);
        var seenTemporary = new HashSet<Guid>();
        foreach (var element in desired)
        {
            if (element is null)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    state.DisplayPath(runtime.DtoName),
                    $"The desired collection '{runtime.DtoName}' contains a null element."
                );
            }

            var key =
                runtime.DtoKey(element)
                ?? throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    state.DisplayPath(runtime.DtoName),
                    $"A desired child under '{runtime.DtoName}' has a null identity."
                );
            var temporary = runtime.TemporaryGetter?.Invoke(element);
            if (runtime.IsUnassigned(key))
            {
                if (temporary is null || temporary == Guid.Empty)
                {
                    if (runtime.TemporaryDeclared)
                    {
                        throw new SparseEntityMappingException(
                            SparseEntityMappingFailure.CollectionKeyConflict,
                            state.DisplayPath(runtime.DtoName),
                            $"A desired child under '{runtime.DtoName}' has an unassigned permanent key and no [SparseTemporaryKey], so its identity cannot correlate across SaveChanges."
                        );
                    }
                }
                else if (!seenTemporary.Add(temporary.Value))
                {
                    throw new SparseEntityMappingException(
                        SparseEntityMappingFailure.CollectionKeyConflict,
                        state.DisplayPath(runtime.DtoName),
                        $"The desired collection '{runtime.DtoName}' reuses a temporary identity."
                    );
                }
            }
            else if (!seen.Add(key))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    state.DisplayPath(runtime.DtoName),
                    $"The desired collection '{runtime.DtoName}' contains a duplicated child key."
                );
            }

            indexed.Add((element, key, temporary));
        }

        return indexed;
    }
}
