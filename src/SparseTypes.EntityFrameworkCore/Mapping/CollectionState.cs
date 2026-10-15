using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Reads and indexes the tracked collection state for delta-based keyed sync.
/// </summary>
/// <remarks>Read-only during preflight: indexing never mutates entities, assigns no navigation, attaches nothing, and queries nothing.</remarks>
internal static class CollectionState
{
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
                $"The collection '{runtime.DtoName}' changed but its navigation is not loaded. Apply never auto-loads and never issues queries: load it with an unfiltered Include or Load before applying. IsLoaded alone never proves completeness for filtered includes."
            );
        }

        var current = collection.CurrentValue;
        if (current is null && runtime.CollectionSetter is not null)
        {
            // Read-only preflight stand-in: execution assigns the real list via
            // EnsureList.
            return new List<TEntityChild>();
        }

        return PeekList(runtime, current, state.DisplayPath(runtime.DtoName));
    }

    /// <summary>
    /// Returns the current collection list without assigning a null navigation.
    /// </summary>
    public static IList PeekList<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        IEnumerable? current,
        string displayPath
    )
        where TDtoChild : class
        where TEntityChild : class
    {
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

        throw new SparseEntityMappingException(
            SparseEntityMappingFailure.MetadataMismatch,
            displayPath,
            $"The navigation '{runtime.NavigationName}' is null. Assign the collection before applying, or make the navigation settable so execution can attach added children."
        );
    }

    /// <summary>Returns the collection list, assigning a new list for a null settable navigation.</summary>
    public static IList EnsureList<TDtoChild, TEntityChild>(
        CollectionRuntime<TDtoChild, TEntityChild> runtime,
        DbContext context,
        object parent,
        string displayPath
    )
        where TDtoChild : class
        where TEntityChild : class
    {
        var current = CollectionEntryAccess
            .Read(context, parent, runtime.NavigationName)
            .CurrentValue;
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

        var created = (IList)runtime.CollectionFactory();
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
        var tracked = new Dictionary<object, TEntityChild>(list.Count, runtime.KeyComparer);
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
}
