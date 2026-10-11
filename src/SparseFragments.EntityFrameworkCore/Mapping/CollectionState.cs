using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// Reads and indexes the tracked collection state for delta-based keyed sync.
/// </summary>
/// <remarks>
/// Read-only during preflight: indexing never mutates entities, assigns no
/// navigation, attaches nothing, and queries nothing. Added children come from
/// the ChangeSet <c>Added</c> After snapshots, edits recurse through child
/// ChangeSets, and removals resolve from keyed before-identity. No desired or
/// full-collection DTO is required. Every inconsistency surfaces as an
/// explicit mapping failure. Navigation assignment happens only in the
/// execution phase via <see cref="EnsureList{TDtoChild, TEntityChild}" />.
/// <para>
/// A loaded navigation is not proof of completeness (a filtered
/// <c>Include</c> marks the navigation loaded while omitting elements).
/// Delta add/edit operations touch only addressed keys and stay safe on
/// partial loads; order persistence and removal validation require complete
/// evidence and fail explicitly otherwise. Callers must load collections with
/// unfiltered <c>Include</c>/<c>Load</c>; filtered includes are unsupported
/// for reordered or removal-bearing transitions.
/// </para>
/// </remarks>
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

        if (collection.CurrentValue is null && runtime.CollectionSetter is not null)
        {
            // Read-only preflight stand-in: execution assigns the real list via
            // EnsureList. Removals and edits resolve against the empty set and
            // fail as KeyNotFound; pure additions proceed.
            return new List<TEntityChild>();
        }

        return PeekList(runtime, state.Context, state.Entity, state.DisplayPath(runtime.DtoName));
    }

    /// <summary>
    /// Returns the current collection list without assigning a null navigation.
    /// </summary>
    public static IList PeekList<TDtoChild, TEntityChild>(
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

        throw new SparseEntityMappingException(
            SparseEntityMappingFailure.MetadataMismatch,
            displayPath,
            $"The navigation '{runtime.NavigationName}' is null. Assign the collection before applying, or make the navigation settable so execution can attach added children."
        );
    }

    /// <summary>
    /// Returns the collection list, assigning a new list for a null settable
    /// navigation. Execution phase only; never call during preflight
    /// validation.
    /// </summary>
    public static IList EnsureList<TDtoChild, TEntityChild>(
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
}
