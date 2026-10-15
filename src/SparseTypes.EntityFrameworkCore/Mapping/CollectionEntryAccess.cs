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
}
