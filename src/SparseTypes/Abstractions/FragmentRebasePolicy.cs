using System.ComponentModel;

namespace SparseTypes;

/// <summary>Reconciles one member during rebase without owning merge or equality.</summary>
/// <remarks>Rebase-specific policy SPI.</remarks>
/// <typeparam name="T">The model member type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public abstract class FragmentRebasePolicy<T>
{
    /// <summary>Compares two member values according to this policy.</summary>
    public abstract bool AreEqual(T? left, T? right);

    /// <summary>Reapplies an edit based on an earlier sparse state to the current sparse state.</summary>
    /// <remarks>Presence is significant: <see cref="Optional{T}.Missing"/> never equals a present value.</remarks>
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
