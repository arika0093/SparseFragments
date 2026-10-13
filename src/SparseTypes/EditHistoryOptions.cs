using System;

namespace SparseTypes;

/// <summary>Options controlling an opt-in edit history.</summary>
/// <remarks>
/// History is opt-in only. Without <c>CreateHistory</c> the session retains
/// nothing and performs no history bookkeeping. The existing
/// <c>TransitionObserved</c> snapshot and diff work runs whether or not a
/// history exists; opting in adds retention, not the underlying diff.
/// <c>MaxEntries</c> bounds the number of retained entries. It is not a byte
/// cap: change sets vary in size because they retain strings, collections,
/// and nested transitions, so two histories with the same entry count can use
/// very different amounts of memory. No per-entry serialization is required.
/// </remarks>
public sealed class EditHistoryOptions
{
    private int _maxEntries = 50;

    /// <summary>Gets or sets the maximum retained undo entries.</summary>
    /// <remarks>
    /// Must be at least 1. When a new entry would exceed the limit, the
    /// oldest undo entry is evicted. The redo stack only holds entries moved
    /// out of undo, so the combined total stays bounded.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxEntries
    {
        get => _maxEntries;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "MaxEntries must be at least 1."
                );
            }

            _maxEntries = value;
        }
    }
}
