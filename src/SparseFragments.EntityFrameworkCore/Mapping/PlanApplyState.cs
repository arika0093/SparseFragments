using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SparseFragments.Generated;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// The per-level inputs for preparing and executing queued writes.
/// </summary>
internal sealed class PlanApplyState
{
    private const string RootMarker = "$root";

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
        Dictionary<string, Dictionary<Guid, object>>? sharedCorrelation
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
        Correlation =
            sharedCorrelation
            ?? new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal);
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
    /// independently there; duplicates within one path fail.
    /// </summary>
    public Dictionary<string, Dictionary<Guid, object>> Correlation { get; }

    /// <summary>Records a created child entity under its path-scoped temporary identity.</summary>
    public void RecordTemporary(string path, Guid temporaryKey, object entity)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entity);
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
