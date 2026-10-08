using System.ComponentModel;

namespace SparseFragments;

/// <summary>Reconciles one member during rebase without owning merge or equality.</summary>
/// <remarks>
/// Rebase-specific policy SPI: select it per member with
/// <see cref="SparseRebasePolicyAttribute"/> instead of coupling rebase
/// behavior to a <see cref="FragmentMergeStrategy{T}"/>. A member-level
/// policy wins over the merge strategy's <c>TryRebase</c>; the strategy still
/// owns <c>Merge</c>. Generated code keeps one instance per member and may
/// call it concurrently, so implementations must be stateless or thread-safe.
/// Policies apply to scalar and whole-replace members; nested, keyed,
/// dictionary, and merge-collection members always reconcile granularly.
/// </remarks>
/// <typeparam name="T">The model member type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public abstract class FragmentRebasePolicy<T>
{
    /// <summary>Compares two member values according to this policy.</summary>
    public abstract bool AreEqual(T? left, T? right);

    /// <summary>Reapplies an edit based on an earlier sparse state to the current sparse state.</summary>
    /// <remarks>
    /// Presence is significant: <see cref="Optional{T}.Missing"/> never equals
    /// a present value. A present result maps to a <c>Set</c> patch operation,
    /// a missing result maps to <c>Remove</c>, and a result equal to the
    /// current state stays <c>Keep</c>. Returning <c>false</c> reports a
    /// conflict; <c>PreferIncoming</c> and <c>PreferCurrent</c> overwrite
    /// instead and never report one.
    /// </remarks>
    public abstract bool TryRebase(
        Optional<T> editBase,
        Optional<T> desired,
        Optional<T> current,
        out Optional<T> rebased,
        out string? reason
    );

    /// <summary>Creates a policy that conflicts unless the edit replays cleanly.</summary>
    public static FragmentRebasePolicy<T> FailOnConflict(IEqualityComparer<T>? comparer = null) =>
        new FailOnConflictPolicy(comparer ?? EqualityComparer<T>.Default);

    /// <summary>Creates a policy that keeps the desired value on divergence.</summary>
    /// <remarks>An overwrite, not a reconciliation: divergence never conflicts.</remarks>
    public static FragmentRebasePolicy<T> PreferIncoming(IEqualityComparer<T>? comparer = null) =>
        new PreferIncomingPolicy(comparer ?? EqualityComparer<T>.Default);

    /// <summary>Creates a policy that keeps the current value on divergence.</summary>
    /// <remarks>An overwrite, not a reconciliation: divergence never conflicts.</remarks>
    public static FragmentRebasePolicy<T> PreferCurrent(IEqualityComparer<T>? comparer = null) =>
        new PreferCurrentPolicy(comparer ?? EqualityComparer<T>.Default);

    private static bool OptionalsEqual(
        IEqualityComparer<T> comparer,
        Optional<T> left,
        Optional<T> right
    )
    {
        if (!left.IsPresent)
        {
            return !right.IsPresent;
        }

        if (!right.IsPresent)
        {
            return false;
        }

        return comparer.Equals(left.Value!, right.Value!);
    }

    private sealed class FailOnConflictPolicy(IEqualityComparer<T> comparer)
        : FragmentRebasePolicy<T>
    {
        public override bool AreEqual(T? left, T? right) => comparer.Equals(left!, right!);

        public override bool TryRebase(
            Optional<T> editBase,
            Optional<T> desired,
            Optional<T> current,
            out Optional<T> rebased,
            out string? reason
        )
        {
            if (OptionalsEqual(comparer, desired, editBase))
            {
                rebased = current;
                reason = null;
                return true;
            }

            if (
                OptionalsEqual(comparer, current, editBase)
                || OptionalsEqual(comparer, current, desired)
            )
            {
                rebased = desired;
                reason = null;
                return true;
            }

            rebased = Optional<T>.Missing;
            reason = "The value conflicts with a concurrent change.";
            return false;
        }
    }

    private sealed class PreferIncomingPolicy(IEqualityComparer<T> comparer)
        : FragmentRebasePolicy<T>
    {
        public override bool AreEqual(T? left, T? right) => comparer.Equals(left!, right!);

        public override bool TryRebase(
            Optional<T> editBase,
            Optional<T> desired,
            Optional<T> current,
            out Optional<T> rebased,
            out string? reason
        )
        {
            _ = editBase;
            rebased = OptionalsEqual(comparer, desired, current) ? current : desired;
            reason = null;
            return true;
        }
    }

    private sealed class PreferCurrentPolicy(IEqualityComparer<T> comparer)
        : FragmentRebasePolicy<T>
    {
        public override bool AreEqual(T? left, T? right) => comparer.Equals(left!, right!);

        public override bool TryRebase(
            Optional<T> editBase,
            Optional<T> desired,
            Optional<T> current,
            out Optional<T> rebased,
            out string? reason
        )
        {
            _ = editBase;
            _ = desired;
            rebased = current;
            reason = null;
            return true;
        }
    }
}
