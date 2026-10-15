using System;

namespace SparseTypes;

/// <summary>Options controlling an opt-in edit history.</summary>
/// <remarks>History is opt-in only.</remarks>
public sealed class EditHistoryOptions
{
    private int _maxEntries = 50;

    /// <summary>Gets or sets the maximum retained undo entries.</summary>
    /// <remarks>Must be at least 1.</remarks>
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
