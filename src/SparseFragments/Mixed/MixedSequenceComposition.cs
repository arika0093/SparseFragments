using System.ComponentModel;

namespace SparseFragments;

/// <summary>The typed outcome of composing two mixed operation sequences.</summary>
/// <remarks>
/// <see cref="Succeeded"/> reports descriptor-shape validity only: every
/// overlapping path had a defined composition rule. It is not proof of
/// value-level composability. Overlaps that still need a before/after
/// continuity check appear in <see cref="PendingContinuityChecks"/>; callers
/// must resolve those through the typed ChangeSet API before fabricating a
/// completed ChangeSet or claiming rollback safety (issue #173).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class MixedSequenceComposition
{
    /// <summary>Creates a mixed sequence composition outcome.</summary>
    public MixedSequenceComposition(
        bool succeeded,
        IEnumerable<MixedMemberOperation> composed,
        IEnumerable<MixedComposeResult> failures
    )
        : this(succeeded, composed, failures, []) { }

    /// <summary>Creates a mixed sequence composition outcome with continuity obligations.</summary>
    public MixedSequenceComposition(
        bool succeeded,
        IEnumerable<MixedMemberOperation> composed,
        IEnumerable<MixedComposeResult> failures,
        IEnumerable<MixedComposeResult> pendingContinuityChecks
    )
    {
        ArgumentNullException.ThrowIfNull(composed);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(pendingContinuityChecks);
        Succeeded = succeeded;
        Composed = Array.AsReadOnly(composed.ToArray());
        Failures = Array.AsReadOnly(failures.ToArray());
        PendingContinuityChecks = Array.AsReadOnly(pendingContinuityChecks.ToArray());
    }

    /// <summary>Whether every overlapping path had a defined composition shape.</summary>
    /// <remarks>
    /// True while <see cref="PendingContinuityChecks"/> is non-empty means the
    /// shape is valid but value-level continuity is still unverified.
    /// </remarks>
    public bool Succeeded { get; }

    /// <summary>The merged operations in deterministic order.</summary>
    public IReadOnlyList<MixedMemberOperation> Composed { get; }

    /// <summary>One entry per overlapping path that could not be composed.</summary>
    public IReadOnlyList<MixedComposeResult> Failures { get; }

    /// <summary>Overlaps that composed by shape but still require value-continuity verification.</summary>
    /// <remarks>
    /// Each entry carries the composed path and shape; the first after-state
    /// must equal the second before-state before the merge is semantically safe.
    /// </remarks>
    public IReadOnlyList<MixedComposeResult> PendingContinuityChecks { get; }

    /// <summary>Whether the composition is both shaped and continuity-free.</summary>
    public bool IsFullyComposable => Succeeded && PendingContinuityChecks.Count == 0;
}
