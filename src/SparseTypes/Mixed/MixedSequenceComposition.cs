using System.ComponentModel;

namespace SparseTypes;

/// <summary>The typed outcome of composing two mixed operation sequences.</summary>
/// <remarks><see cref="Succeeded"/> reports descriptor-shape validity only: every overlapping path had a defined composition rule.</remarks>
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
    /// <remarks>True when overlapping operations compose.</remarks>
    public bool Succeeded { get; }

    /// <summary>The merged operations in deterministic order.</summary>
    public IReadOnlyList<MixedMemberOperation> Composed { get; }

    /// <summary>One entry per overlapping path that could not be composed.</summary>
    public IReadOnlyList<MixedComposeResult> Failures { get; }

    /// <summary>Overlaps that composed by shape but still require value-continuity verification.</summary>
    /// <remarks>Each entry carries the composed path and shape.</remarks>
    public IReadOnlyList<MixedComposeResult> PendingContinuityChecks { get; }

    /// <summary>Whether the composition is both shaped and continuity-free.</summary>
    public bool IsFullyComposable => Succeeded && PendingContinuityChecks.Count == 0;
}
