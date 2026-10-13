using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SparseTypes.Generated;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// The per-level inputs for preparing and executing queued writes.
/// </summary>
internal sealed class PlanApplyState
{
    private const string RootMarker = "$root";
    private readonly List<SparseEntityMappingError> _validationErrors;
    private readonly List<SparseEntityAppliedPath>? _appliedPaths;
    private readonly Dictionary<string, Dictionary<object, string>>? _collectionScopeKeys;

    public PlanApplyState(
        DbContext context,
        object entity,
        EntityEntry entry,
        ISparseChangeSet changes,
        string path,
        bool isNested = false
    )
        : this(context, entity, entry, changes, path, isNested, null) { }

    public PlanApplyState(
        DbContext context,
        object entity,
        EntityEntry entry,
        ISparseChangeSet changes,
        string path,
        bool isNested,
        Dictionary<string, Dictionary<Guid, object>>? sharedCorrelation,
        List<SparseEntityMappingError>? sharedValidationErrors = null,
        List<SparseEntityAppliedPath>? appliedPaths = null,
        Dictionary<string, Dictionary<object, string>>? collectionScopeKeys = null,
        Dictionary<string, NestedScopeTable>? nestedScopes = null,
        CollectionScopeIdentity parentScope = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(path);
        Context = context;
        Entity = entity;
        Entry = entry;
        Changes = changes;
        Path = path;
        Correlation = sharedCorrelation;
        _validationErrors = sharedValidationErrors ?? [];
        _appliedPaths = appliedPaths;
        _collectionScopeKeys = collectionScopeKeys;
        NestedScopes = nestedScopes;
        ParentScope = parentScope;
        Groups = Group(changes, path, isNested);
    }

    public DbContext Context { get; }

    public object Entity { get; }

    public EntityEntry Entry { get; }

    public ISparseChangeSet Changes { get; }

    /// <summary>Gets the dotted DTO path prefix for error messages.</summary>
    public string Path { get; }

    /// <summary>Gets changed records grouped by root member name.</summary>
    public Dictionary<string, List<SparseChangeRecord>> Groups { get; }

    /// <summary>
    /// Gets the shared temporary-identity correlation for this apply, scoped by
    /// collection path. The same Guid reused under a different path resolves
    /// independently there; duplicates within one path fail. It is null when
    /// Apply does not need a result.
    /// </summary>
    public Dictionary<string, Dictionary<Guid, object>>? Correlation { get; }

    /// <summary>Gets whether this apply is collecting execution-path details.</summary>
    public bool TracksAppliedPaths => _appliedPaths is not null;

    /// <summary>Gets the shared validation errors collected during preflight.</summary>
    public List<SparseEntityMappingError> ValidationErrors => _validationErrors;

    /// <summary>Gets the shared applied-path list, when requested.</summary>
    public List<SparseEntityAppliedPath>? AppliedPaths => _appliedPaths;

    /// <summary>Gets created-entity scope keys, when result tracking is enabled.</summary>
    public Dictionary<string, Dictionary<object, string>>? CollectionScopeKeys =>
        _collectionScopeKeys;

    /// <summary>Gets nested temporary-identity records keyed by parent scope.</summary>
    public Dictionary<string, NestedScopeTable>? NestedScopes { get; }

    /// <summary>Gets the parent element scope for nested correlation records.</summary>
    public CollectionScopeIdentity ParentScope { get; }

    /// <summary>Collects a validation failure without executing queued writes.</summary>
    public bool CollectValidation(Action validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        try
        {
            validate();
            return true;
        }
        catch (SparseEntityMappingException exception)
        {
            _validationErrors.AddRange(exception.Errors);
            return false;
        }
    }

    /// <summary>Throws the collected preflight errors without running queued writes.</summary>
    public void ThrowIfValidationErrors()
    {
        if (_validationErrors.Count > 0)
        {
            throw SparseEntityMappingException.FromErrors(_validationErrors);
        }
    }

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

    /// <summary>Records a created child entity under its path-scoped temporary identity.</summary>
    /// <remarks>
    /// The legacy path-string bucket keeps its exact contents for compatible
    /// reads, but duplicate detection now runs on the parent-scoped record:
    /// parents whose display keys collide keep independent correlations.
    /// </remarks>
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

        if (NestedScopes is null || ParentScope.Kind == CollectionScopeKind.Unscoped)
        {
            if (
                scope.TryGetValue(temporaryKey, out var existing)
                && !ReferenceEquals(existing, entity)
            )
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    $"Two created children under '{path}' share one temporary identity. Temporary identities must be unique within one collection scope."
                );
            }

            scope[temporaryKey] = entity;
            return;
        }

        // Legacy bucket keeps its contents for compatible reads; precise
        // duplicate detection runs on the parent-scoped record below.
        scope[temporaryKey] = entity;
        if (!NestedScopes.TryGetValue(path, out var table))
        {
            table = new NestedScopeTable(ParentScope.KeyComparer);
            NestedScopes.Add(path, table);
        }

        table.Record(path, ParentScope, temporaryKey, entity);
    }

    /// <summary>Whether any changed record starts at the DTO member.</summary>
    public bool HasRecords(string dtoName) => Groups.ContainsKey(dtoName);

    /// <summary>Whether any changed record continues below the DTO member.</summary>
    public bool HasDeeperRecords(string dtoName)
    {
        if (!Groups.TryGetValue(dtoName, out var records))
        {
            return false;
        }

        foreach (var record in records)
        {
            if (record.Path.Segments.Count > 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the whole-object record for the DTO member, if any.</summary>
    public SparseChangeRecord? SingleSegmentRecord(string dtoName)
    {
        if (!Groups.TryGetValue(dtoName, out var records))
        {
            return null;
        }

        foreach (var record in records)
        {
            if (record.Path.Segments.Count == 1)
            {
                return record;
            }
        }

        return null;
    }

    /// <summary>Prefixes a DTO member with the current path for error messages.</summary>
    public string DisplayPath(string dtoName) =>
        string.IsNullOrEmpty(Path) ? dtoName : Path + "." + dtoName;

    /// <summary>Attaches a pre-existing instance as unchanged without querying.</summary>
    public static EntityEntry AttachSingle(DbContext context, object entity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        var entry = context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            entry.State = EntityState.Unchanged;
        }

        return entry;
    }

    /// <summary>
    /// Returns a validation-safe entry without attaching, queuing the attach first.
    /// </summary>
    /// <remarks>
    /// Preflight validation must never mutate the <c>ChangeTracker</c>: the
    /// returned entry stays detached until the queued attach action runs ahead
    /// of the other queued writes at execution time.
    /// </remarks>
    public static EntityEntry ValidationEntry(
        DbContext context,
        object entity,
        List<Action> actions
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(actions);
        actions.Add(() => AttachSingle(context, entity));
        return context.Entry(entity);
    }

    private static Dictionary<string, List<SparseChangeRecord>> Group(
        ISparseChangeSet changes,
        string path,
        bool isNested
    )
    {
        var groups = new Dictionary<string, List<SparseChangeRecord>>(StringComparer.Ordinal);
        foreach (var record in changes.EnumerateChangeRecords())
        {
            var segments = record.Path.Segments;
            if (segments.Count == 0)
            {
                // A nested root entry (null to object or back) is governed by the
                // parent whole-object record; only a root plan rejects it outright.
                if (isNested)
                {
                    continue;
                }

                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    string.IsNullOrEmpty(path) ? RootMarker : path,
                    "Root presence transitions are outside Apply scope: pass member-level changes."
                );
            }

            var first = segments[0];
            if (first.Kind != SparsePathSegmentKind.Member)
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.MetadataMismatch,
                    string.IsNullOrEmpty(path) ? RootMarker : path,
                    "Unsupported change path: the first segment is not a member."
                );
            }

            if (!groups.TryGetValue(first.Name, out var records))
            {
                records = [];
                groups[first.Name] = records;
            }

            records.Add(record);
        }

        return groups;
    }
}
