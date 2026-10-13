using System.Collections.Generic;
using System.Linq;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>Kinds of parent scope that disambiguate nested collection correlations.</summary>
/// <remarks>
/// Index-addressed parents stay <see cref="Unscoped" />: their nested paths
/// already differ per index, and indices are unstable across server
/// round-trips, so typed correlation cannot rely on them.
/// </remarks>
internal enum CollectionScopeKind
{
    /// <summary>No typed parent scope; legacy path-string correlation applies.</summary>
    Unscoped,

    /// <summary>The parent element carries a stable assigned key.</summary>
    Assigned,

    /// <summary>The parent element carries a temporary identity.</summary>
    Temporary,
}

/// <summary>Identifies one parent collection element for nested correlation.</summary>
/// <remarks>
/// A value of this type is a lookup token, never a dictionary key: assigned
/// keys are compared with the parent collection's key comparer inside
/// <see cref="NestedScopeTable" />, because key equality follows the
/// configured comparer rather than display formatting. Index-addressed
/// parents stay <see cref="CollectionScopeKind.Unscoped" />.
/// </remarks>
internal readonly record struct CollectionScopeIdentity(
    CollectionScopeKind Kind,
    object? Key,
    Guid Temp,
    IEqualityComparer<object>? KeyComparer
)
{
    /// <summary>Gets the identity for a parent without a stable scope.</summary>
    public static CollectionScopeIdentity Unscoped => default;

    /// <summary>Creates the identity for a parent with a stable assigned key.</summary>
    /// <param name="key">The parent DTO key value.</param>
    /// <param name="comparer">The parent collection's key comparer.</param>
    public static CollectionScopeIdentity Assigned(
        object key,
        IEqualityComparer<object>? comparer
    ) => new(CollectionScopeKind.Assigned, key, Guid.Empty, comparer);

    /// <summary>Creates the identity for a parent with a temporary identity.</summary>
    /// <param name="temporary">The parent temporary identity.</param>
    public static CollectionScopeIdentity Temporary(Guid temporary) =>
        new(CollectionScopeKind.Temporary, null, temporary, null);

    /// <summary>Derives the scope for a collection element from its identities.</summary>
    /// <param name="key">The element DTO key, when known.</param>
    /// <param name="assigned">Whether the key is an assigned (non-sentinel) identity.</param>
    /// <param name="temporary">The element temporary identity, when present.</param>
    /// <param name="comparer">The owning collection's key comparer.</param>
    public static CollectionScopeIdentity ForElement(
        object? key,
        bool assigned,
        Guid? temporary,
        IEqualityComparer<object>? comparer
    )
    {
        if (key is not null && assigned)
        {
            return Assigned(key, comparer);
        }

        if (temporary is not null && temporary != Guid.Empty)
        {
            return Temporary(temporary.Value);
        }

        return Unscoped;
    }
}

/// <summary>Per-collection-path nested temporary-identity records.</summary>
/// <remarks>
/// Temporary identities are recorded per parent scope in addition to the
/// legacy path-string buckets, so two parents whose display keys collide
/// (identical <c>ToString()</c>, delimiters, or cross-type equality) keep
/// independent correlations. Assigned slots use the parent collection's key
/// comparer; temporary slots use Guid equality. The legacy string buckets
/// keep their exact contents for compatible reads.
/// </remarks>
internal sealed class NestedScopeTable
{
    /// <summary>Records of one parent scope: temperatures plus member entities.</summary>
    /// <remarks>
    /// Members include temp-less created entities so scope-membership probes
    /// stay precise even when a row carries no temporary identity.
    /// </remarks>
    private sealed class ScopeSlot
    {
        public readonly Dictionary<Guid, object> Temps = new();
        public readonly HashSet<object> Members = new(ReferenceEqualityComparer.Instance);
    }

    private readonly Dictionary<object, ScopeSlot> _byKey;
    private readonly Dictionary<Guid, ScopeSlot> _byTemporary = new();
    private readonly ScopeSlot _unscoped = new();

    /// <summary>Creates a table for one nested collection path.</summary>
    /// <param name="comparer">The parent collection's key comparer.</param>
    public NestedScopeTable(IEqualityComparer<object>? comparer)
    {
        KeyComparer = comparer ?? EqualityComparer<object>.Default;
        _byKey = new Dictionary<object, ScopeSlot>(KeyComparer);
    }

    /// <summary>Gets the parent collection's key comparer.</summary>
    public IEqualityComparer<object> KeyComparer { get; }

    /// <summary>Records a created child under its parent scope.</summary>
    /// <param name="path">The nested collection path, for diagnostics.</param>
    /// <param name="scope">The parent element scope.</param>
    /// <param name="temporaryKey">The created child's temporary identity.</param>
    /// <param name="entity">The created child entity.</param>
    /// <exception cref="SparseEntityMappingException">
    /// The same parent scope already records another entity under the identity.
    /// </exception>
    public void Record(string path, CollectionScopeIdentity scope, Guid temporaryKey, object entity)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entity);
        var slot = ResolveSlot(scope, create: true)!;
        if (slot.Temps.TryGetValue(temporaryKey, out var existing))
        {
            if (!ReferenceEquals(existing, entity))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"Two created children under '{path}' share one temporary identity. Temporary identities must be unique within one collection scope."
                );
            }

            return;
        }

        slot.Temps[temporaryKey] = entity;
        slot.Members.Add(entity);
    }

    /// <summary>Records a created entity as a scope member without a temporary identity.</summary>
    /// <param name="scope">The parent element scope.</param>
    /// <param name="entity">The created child entity.</param>
    public void RecordEntity(CollectionScopeIdentity scope, object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ResolveSlot(scope, create: true)!.Members.Add(entity);
    }

    /// <summary>Looks up created children for a parent scope.</summary>
    /// <param name="scope">The parent element scope.</param>
    /// <returns>The temporary-to-entity map, or null when nothing was recorded.</returns>
    public IReadOnlyDictionary<Guid, object>? TryGet(CollectionScopeIdentity scope)
    {
        var slot = ResolveSlot(scope, create: false);
        return slot is null || slot.Temps.Count == 0 ? null : slot.Temps;
    }

    /// <summary>Collects members recorded under scopes outside the given set.</summary>
    /// <param name="mine">The parent scopes of the current item.</param>
    /// <returns>Members known to belong to sibling scopes.</returns>
    /// <remarks>
    /// Members with no scoped record anywhere stay unattributed and are never
    /// reported here, preserving legacy behavior for unscoped correlations.
    /// </remarks>
    public HashSet<object> ForeignMembers(IReadOnlyList<CollectionScopeIdentity> mine)
    {
        var known = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var scope in mine)
        {
            var slot = ResolveSlot(scope, create: false);
            if (slot is not null)
            {
                known.UnionWith(slot.Members);
            }
        }

        var foreign = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectForeign(_byKey.Values, known, foreign);
        CollectForeign(_byTemporary.Values, known, foreign);
        return foreign;

        static void CollectForeign(
            IEnumerable<ScopeSlot> slots,
            HashSet<object> known,
            HashSet<object> foreign
        )
        {
            foreach (
                var member in slots
                    .SelectMany(static slot => slot.Members)
                    .Where(member => !known.Contains(member))
            )
            {
                foreign.Add(member);
            }
        }
    }

    private ScopeSlot? ResolveSlot(CollectionScopeIdentity scope, bool create)
    {
        switch (scope.Kind)
        {
            case CollectionScopeKind.Assigned when scope.Key is not null:
                if (_byKey.TryGetValue(scope.Key, out var keyed))
                {
                    return keyed;
                }

                if (!create)
                {
                    return null;
                }

                var fresh = new ScopeSlot();
                _byKey.Add(scope.Key, fresh);
                return fresh;
            case CollectionScopeKind.Temporary when scope.Temp != Guid.Empty:
                if (_byTemporary.TryGetValue(scope.Temp, out var tempScoped))
                {
                    return tempScoped;
                }

                if (!create)
                {
                    return null;
                }

                var freshTemp = new ScopeSlot();
                _byTemporary.Add(scope.Temp, freshTemp);
                return freshTemp;
            default:
                return _unscoped;
        }
    }
}
