using System.ComponentModel;

namespace SparseFragments;

/// <summary>The typed outcome of planning a rollback over mixed operations.</summary>
/// <remarks>
/// Write-only operations have no prior value to restore, so rollback excludes
/// them and reports their paths instead. A plan with no skipped paths is a
/// complete inverse; any skipped path makes incompleteness explicit.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class MixedRollbackPlan
{
    /// <summary>Creates a mixed rollback plan.</summary>
    public MixedRollbackPlan(IEnumerable<string> reversiblePaths, IEnumerable<string> skippedPaths)
    {
        ArgumentNullException.ThrowIfNull(reversiblePaths);
        ArgumentNullException.ThrowIfNull(skippedPaths);
        ReversiblePaths = Array.AsReadOnly(reversiblePaths.ToArray());
        SkippedPaths = Array.AsReadOnly(skippedPaths.ToArray());
    }

    /// <summary>Paths whose transitions invert normally.</summary>
    public IReadOnlyList<string> ReversiblePaths { get; }

    /// <summary>Write-only paths with no prior value to restore.</summary>
    public IReadOnlyList<string> SkippedPaths { get; }

    /// <summary>Whether the plan inverts every operation.</summary>
    public bool IsComplete => SkippedPaths.Count == 0;
}
