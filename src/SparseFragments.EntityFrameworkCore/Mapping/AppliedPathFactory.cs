using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseFragments.EntityFrameworkCore.Mapping;

internal static class AppliedPathFactory
{
    public static SparseEntityAppliedPath Create(
        string dtoPath,
        EntityEntry entry,
        string? member,
        SparseEntityApplyOperationKind operation,
        SparseEntityRemoveKind? removeKind = null
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = entry.Metadata.FindPrimaryKey();
        object? entityKey = key switch
        {
            null => null,
            { Properties.Count: 1 } => entry.Property(key.Properties[0].Name).CurrentValue,
            _ => key
                .Properties.Select(property => entry.Property(property.Name).CurrentValue)
                .ToArray(),
        };
        return new SparseEntityAppliedPath(
            dtoPath,
            operation,
            entry.Metadata.ClrType.FullName ?? entry.Metadata.ClrType.Name,
            member,
            entityKey,
            removeKind
        );
    }
}
