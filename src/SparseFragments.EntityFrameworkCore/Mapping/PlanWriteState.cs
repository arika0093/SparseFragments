using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseFragments.EntityFrameworkCore.Mapping;

/// <summary>
/// The per-level inputs for wholesale writes (null to object transitions).
/// </summary>
/// <remarks>
/// Unlike <see cref="PlanApplyState" />, this carries no change set: every
/// mapped member writes from the desired state because the whole object is new.
/// </remarks>
internal sealed class PlanWriteState
{
    public PlanWriteState(
        DbContext context,
        object entity,
        EntityEntry entry,
        object desired,
        string path
    )
        : this(context, entity, entry, desired, path, null) { }

    public PlanWriteState(
        DbContext context,
        object entity,
        EntityEntry entry,
        object desired,
        string path,
        Dictionary<Guid, (string Path, object Entity)>? sharedCorrelation
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(path);
        Context = context;
        Entity = entity;
        Entry = entry;
        Desired = desired;
        Path = path;
        Correlation = sharedCorrelation ?? [];
    }

    public DbContext Context { get; }

    public object Entity { get; }

    public EntityEntry Entry { get; }

    public object Desired { get; }

    /// <summary>Gets the shared temporary-identity correlation for this apply.</summary>
    public Dictionary<Guid, (string Path, object Entity)> Correlation { get; }

    /// <summary>Records a created child entity under its DTO temporary identity.</summary>
    public void RecordTemporary(string path, Guid temporaryKey, object entity)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entity);
        if (Correlation.TryGetValue(temporaryKey, out var existing))
        {
            if (!ReferenceEquals(existing.Entity, entity))
            {
                throw new SparseEntityMappingException(
                    SparseEntityMappingFailure.CollectionKeyConflict,
                    path,
                    "Two created children share one temporary identity."
                );
            }

            return;
        }

        Correlation[temporaryKey] = (path, entity);
    }

    /// <summary>Gets the dotted DTO path prefix for error messages.</summary>
    public string Path { get; }

    /// <summary>Prefixes a DTO member with the current path for error messages.</summary>
    public string DisplayPath(string dtoName) =>
        string.IsNullOrEmpty(Path) ? dtoName : Path + "." + dtoName;
}
