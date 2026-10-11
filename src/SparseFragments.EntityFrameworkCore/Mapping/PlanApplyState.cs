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
        object desired,
        ISparseChangeSet changes,
        string path,
        bool isNested = false
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(path);
        Context = context;
        Entity = entity;
        Entry = entry;
        Desired = desired;
        Changes = changes;
        Path = path;
        Groups = Group(changes, path, isNested);
    }

    public DbContext Context { get; }

    public object Entity { get; }

    public EntityEntry Entry { get; }

    public object Desired { get; }

    public ISparseChangeSet Changes { get; }

    /// <summary>Gets the dotted DTO path prefix for error messages.</summary>
    public string Path { get; }

    /// <summary>Gets changed records grouped by root member name.</summary>
    public Dictionary<string, List<SparseChangeRecord>> Groups { get; }

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
                    "Root presence transitions are outside Apply scope: pass a non-null desired DTO with member-level changes."
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
