using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseTypes.EntityFrameworkCore.Mapping;

internal static class CollectionEntryAccess
{
    internal static CollectionEntry Read(DbContext context, object parent, string navigation)
    {
        var tracker = context.ChangeTracker;
        var detectChanges = tracker.AutoDetectChangesEnabled;
        if (detectChanges)
            tracker.DetectChanges();

        // Per-child detection revisits ancestors and rescans the parent
        // collection.
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            return context.Entry(parent).Collection(navigation);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detectChanges;
        }
    }

    /// <summary>
    /// Reads a collection entry without a whole-tracker scan.
    /// </summary>
    /// <remarks>
    /// Preflight-only: the mapper operation scope detects once before
    /// preflight, so per-collection scans would repeat that work. Automatic
    /// detection still pauses around entry access to avoid revisiting
    /// ancestors per child.
    /// </remarks>
    internal static CollectionEntry ReadNoDetect(
        DbContext context,
        object parent,
        string navigation
    )
    {
        var tracker = context.ChangeTracker;
        var detectChanges = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            return context.Entry(parent).Collection(navigation);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detectChanges;
        }
    }
}
