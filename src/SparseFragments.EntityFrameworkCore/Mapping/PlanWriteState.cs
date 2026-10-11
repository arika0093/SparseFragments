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
        Dictionary<string, Dictionary<Guid, object>>? sharedCorrelation
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
        Correlation =
            sharedCorrelation
            ?? new Dictionary<string, Dictionary<Guid, object>>(StringComparer.Ordinal);
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

    /// <summary>Gets the path-scoped temporary-identity correlation for this apply.</summary>
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

    /// <summary>Gets the dotted DTO path prefix for error messages.</summary>
    public string Path { get; }

    /// <summary>Prefixes a DTO member with the current path for error messages.</summary>
    public string DisplayPath(string dtoName) =>
        string.IsNullOrEmpty(Path) ? dtoName : Path + "." + dtoName;
}
