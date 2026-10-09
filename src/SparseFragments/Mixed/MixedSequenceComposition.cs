using System.ComponentModel;

namespace SparseFragments;

/// <summary>The typed outcome of composing two mixed operation sequences.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class MixedSequenceComposition
{
    /// <summary>Creates a mixed sequence composition outcome.</summary>
    public MixedSequenceComposition(
        bool succeeded,
        IEnumerable<MixedMemberOperation> composed,
        IEnumerable<MixedComposeResult> failures
    )
    {
        ArgumentNullException.ThrowIfNull(composed);
        ArgumentNullException.ThrowIfNull(failures);
        Succeeded = succeeded;
        Composed = Array.AsReadOnly(composed.ToArray());
        Failures = Array.AsReadOnly(failures.ToArray());
    }

    /// <summary>Whether every overlapping path composed.</summary>
    public bool Succeeded { get; }

    /// <summary>The merged operations in deterministic order.</summary>
    public IReadOnlyList<MixedMemberOperation> Composed { get; }

    /// <summary>One entry per overlapping path that could not be composed.</summary>
    public IReadOnlyList<MixedComposeResult> Failures { get; }
}
