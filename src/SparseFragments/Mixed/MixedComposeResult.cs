using System.ComponentModel;

namespace SparseFragments;

/// <summary>The typed outcome of composing two same-path mixed operations.</summary>
/// <remarks>
/// A successful outcome describes the merged shape and whether the caller must
/// still verify value-level continuity (first after-state equals second
/// before-state). A failed outcome carries the path and reason instead of
/// inventing history the descriptors cannot justify.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct MixedComposeResult
{
    /// <summary>Creates a mixed composition outcome.</summary>
    public MixedComposeResult(
        bool succeeded,
        string path,
        MixedHistoryKind resultHistory,
        MixedAfterKind resultAfter,
        bool requiresContinuityCheck,
        bool resultIsWholeRoot,
        string? failureReason
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        Succeeded = succeeded;
        Path = path;
        ResultHistory = resultHistory;
        ResultAfter = resultAfter;
        RequiresContinuityCheck = requiresContinuityCheck;
        ResultIsWholeRoot = resultIsWholeRoot;
        FailureReason = failureReason;
    }

    /// <summary>Whether the two operations compose.</summary>
    public bool Succeeded { get; init; }

    /// <summary>The composed path, or the offending path on failure.</summary>
    public string Path { get; init; }

    /// <summary>The history kind of the merged operation.</summary>
    public MixedHistoryKind ResultHistory { get; init; }

    /// <summary>The after-state vocabulary of the merged operation.</summary>
    public MixedAfterKind ResultAfter { get; init; }

    /// <summary>Whether value-level continuity must still be verified.</summary>
    public bool RequiresContinuityCheck { get; init; }

    /// <summary>Whether the merged operation replaces the whole root contribution.</summary>
    public bool ResultIsWholeRoot { get; init; }

    /// <summary>The reason composition failed, without secret plaintext.</summary>
    public string? FailureReason { get; init; }
}
