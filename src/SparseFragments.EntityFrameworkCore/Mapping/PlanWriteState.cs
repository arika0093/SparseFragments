using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// The per-level inputs for wholesale writes (null to object transitions).
/// </summary>
/// <remarks>
/// Unlike <see cref="PlanApplyState" />, this carries no change set: every
/// mapped member writes from the explicit After snapshot carried by the
/// ChangeSet transition (keyed <c>Added</c> elements or a nested whole-object
/// record), never a reconstructed root DTO.
/// </remarks>
internal sealed class PlanWriteState
{
    private readonly List<SparseEntityAppliedPath>? _appliedPaths;
    private readonly Dictionary<string, Dictionary<object, string>>? _collectionScopeKeys;

    public PlanWriteState(
        DbContext context,
        object entity,
        EntityEntry entry,
        object afterSnapshot,
        string path
    )
        : this(context, entity, entry, afterSnapshot, path, null) { }

    public PlanWriteState(
        DbContext context,
        object entity,
        EntityEntry entry,
        object afterSnapshot,
        string path,
        Dictionary<string, Dictionary<Guid, object>>? sharedCorrelation,
        List<SparseEntityAppliedPath>? appliedPaths = null,
        Dictionary<string, Dictionary<object, string>>? collectionScopeKeys = null
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(afterSnapshot);
        ArgumentNullException.ThrowIfNull(path);
        Context = context;
        Entity = entity;
        Entry = entry;
        AfterSnapshot = afterSnapshot;
        Path = path;
        Correlation = sharedCorrelation;
        _appliedPaths = appliedPaths;
        _collectionScopeKeys = collectionScopeKeys;
    }

    public DbContext Context { get; }

    public object Entity { get; }

    public EntityEntry Entry { get; }

    /// <summary>
    /// Gets the explicit After snapshot this wholesale write copies from.
    /// For keyed additions this is the <c>Added</c> element; for nested adds it
    /// is the whole-object record's After value.
    /// </summary>
    public object AfterSnapshot { get; }

    /// <summary>Gets the path-scoped temporary-identity correlation when a result is requested.</summary>
    public Dictionary<string, Dictionary<Guid, object>>? Correlation { get; }

    /// <summary>Gets the shared applied-path list, when requested.</summary>
    public List<SparseEntityAppliedPath>? AppliedPaths => _appliedPaths;

    /// <summary>Gets created-entity scope keys, when result tracking is enabled.</summary>
    public Dictionary<string, Dictionary<object, string>>? CollectionScopeKeys =>
        _collectionScopeKeys;

    /// <summary>Queues a write and records its applied-path detail after success.</summary>
    public void QueueWrite(List<Action> actions, SparseEntityAppliedPath appliedPath, Action write)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(appliedPath);
        ArgumentNullException.ThrowIfNull(write);
        if (_appliedPaths is null)
        {
            actions.Add(write);
            return;
        }

        actions.Add(() =>
        {
            write();
            _appliedPaths.Add(appliedPath);
        });
    }

    /// <summary>Appends an operation after its write completes successfully.</summary>
    public void RecordAppliedPath(SparseEntityAppliedPath appliedPath)
    {
        ArgumentNullException.ThrowIfNull(appliedPath);
        _appliedPaths?.Add(appliedPath);
    }

    /// <summary>Records the exact path segment used for a created collection element.</summary>
    public void RecordCollectionScope(string path, object entity, string scopeKey)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(scopeKey);
        if (_collectionScopeKeys is null)
        {
            return;
        }

        if (!_collectionScopeKeys.TryGetValue(path, out var scopes))
        {
            scopes = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            _collectionScopeKeys.Add(path, scopes);
        }

        scopes[entity] = scopeKey;
    }

    /// <summary>Records a created child entity under its path-scoped temporary identity.</summary>
    public void RecordTemporary(string path, Guid temporaryKey, object entity)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entity);
        if (Correlation is null)
        {
            return;
        }

        if (!Correlation.TryGetValue(path, out var scope))
        {
            scope = new Dictionary<Guid, object>();
            Correlation[path] = scope;
        }

        if (scope.TryGetValue(temporaryKey, out var existing))
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

        scope[temporaryKey] = entity;
    }

    /// <summary>Gets the dotted DTO path prefix for error messages.</summary>
    public string Path { get; }

    /// <summary>Prefixes a DTO member with the current path for error messages.</summary>
    public string DisplayPath(string dtoName) =>
        string.IsNullOrEmpty(Path) ? dtoName : Path + "." + dtoName;
}
