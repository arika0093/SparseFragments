using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Bounds automatic EF change detection around one mapper preflight phase.
/// </summary>
/// <remarks>
/// Preflight only reads tracked state, so one upfront whole-tracker scan
/// replaces the previous per-collection scan. Execution keeps the caller's
/// policy because user callbacks may change tracked relationships. Nested
/// mapper use joins the outer policy instead of re-enabling detection.
/// </remarks>
internal sealed class ChangeTrackerDetectionScope : IDisposable
{
    private readonly ChangeTracker _tracker;
    private readonly bool _restore;
    private bool _disposed;

    private ChangeTrackerDetectionScope(ChangeTracker tracker, bool restore)
    {
        _tracker = tracker;
        _restore = restore;
    }

    /// <summary>
    /// Detects once when the caller enables automatic detection, then
    /// suppresses further scans until disposal restores the caller flag.
    /// </summary>
    internal static ChangeTrackerDetectionScope BeginPreflight(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tracker = context.ChangeTracker;
        var enabled = tracker.AutoDetectChangesEnabled;
        if (enabled)
            tracker.DetectChanges();
        tracker.AutoDetectChangesEnabled = false;
        return new ChangeTrackerDetectionScope(tracker, enabled);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _tracker.AutoDetectChangesEnabled = _restore;
    }
}
