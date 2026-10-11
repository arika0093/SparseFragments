namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// The outcome of one keyed-collection-aware <c>Apply</c> call.
/// </summary>
/// <remarks>
/// Scalar-only mappings never populate this result; keyed collection
/// synchronization records every newly created child entity under its DTO
/// <c>[SparseTemporaryKey]</c> value so the application can correlate
/// database-generated permanent keys across <c>SaveChanges</c> and build the
/// authoritative DTO for client <c>Reconcile</c>. Correlations are scoped by
/// DTO collection path: the same temporary value reused under a different
/// collection resolves independently there.
/// </remarks>
public sealed class SparseEntityApplyResult
{
    private readonly Dictionary<string, Dictionary<Guid, object>> _byPath;
    private readonly Dictionary<Guid, List<object>> _byTemporaryKey;

    internal SparseEntityApplyResult(Dictionary<string, Dictionary<Guid, object>> byPath)
    {
        _byPath = byPath;
        _byTemporaryKey = [];
        foreach (var path in byPath)
        {
            foreach (var entry in path.Value)
            {
                if (!_byTemporaryKey.TryGetValue(entry.Key, out var entities))
                {
                    entities = [];
                    _byTemporaryKey[entry.Key] = entities;
                }

                entities.Add(entry.Value);
            }
        }
    }

    /// <summary>Gets created child entities by DTO collection path, then temporary key.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, object>> CreatedByPath =>
        _byPath.ToDictionary(
            static path => path.Key,
            static path => (IReadOnlyDictionary<Guid, object>)path.Value
        );

    /// <summary>Gets whether any child entity was created by this apply.</summary>
    public bool HasCreatedEntities => _byTemporaryKey.Count > 0;

    /// <summary>Looks up the created entities for a temporary key across every collection path.</summary>
    /// <param name="temporaryKey">The DTO temporary identity.</param>
    /// <returns>The created child entities carrying that temporary identity.</returns>
    public IReadOnlyList<object> GetCreated(Guid temporaryKey) =>
        _byTemporaryKey.TryGetValue(temporaryKey, out var entities) ? entities : [];

    /// <summary>Looks up the single created entity for a temporary key.</summary>
    /// <param name="temporaryKey">The DTO temporary identity.</param>
    /// <param name="entity">The created child entity.</param>
    /// <typeparam name="TEntityChild">The expected child entity type.</typeparam>
    /// <returns>True when exactly one created entity of that type carries the key.</returns>
    public bool TryGetCreated<TEntityChild>(Guid temporaryKey, out TEntityChild? entity)
        where TEntityChild : class
    {
        entity = null;
        if (!_byTemporaryKey.TryGetValue(temporaryKey, out var entities) || entities.Count != 1)
        {
            return false;
        }

        entity = entities[0] as TEntityChild;
        return entity is not null;
    }
}
