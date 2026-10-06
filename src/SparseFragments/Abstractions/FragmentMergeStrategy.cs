using System.ComponentModel;

namespace SparseFragments;

/// <summary>
/// Implements the generic member-specific merge algebra shared by sparse fragments. Source values are merged from
/// lowest to highest priority.
/// </summary>
/// <remarks>
/// Advanced merge SPI: derive custom member algebras from this class. <see cref="Merge"/> and
/// <see cref="AreEqual(T?, T?)"/> are required; <see cref="TryRebase(Optional{T}, Optional{T}, Optional{T}, out Optional{T}, out string?)"/>
/// is an optional capability with a stable default: rebase succeeds when one sparse state is shared,
/// and reports a conflict otherwise.
/// Presence is significant: <see cref="Optional{T}.Missing"/> never equals a present value, including a present
/// null or <c>default</c>. The generated rebase passes member states through unchanged and maps a missing
/// <c>rebased</c> result back to an <c>Unset</c> patch operation (a present result maps to <c>Set</c>,
/// and a result equal to the current state stays <c>Unchanged</c>).
/// Generated models keep one strategy instance and may call it concurrently. Implementations must be stateless or
/// thread-safe.
/// Breaking-change note (issue #7): the previous <c>TryRebase(T?, T?, T?, out T?, out string?)</c> overload
/// erased missing vs. present null/default by converting missing to <c>default</c> at the call site, so custom
/// strategies could not distinguish those sparse states. The pre-1.0 SPI (version 0.x) was redesigned to take
/// <see cref="Optional{T}"/> values instead. No <c>[Obsolete]</c> shim is kept: the old overload is removed
/// because any override of it would silently keep erasing presence, and the only in-repo overrides were test
/// strategies updated in the same change.
/// </remarks>
/// <typeparam name="T">The model member type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public abstract class FragmentMergeStrategy<T>
{
    /// <summary>Merges two presence-aware member values.</summary>
    public abstract Optional<T> Merge(Optional<T> lowerPriority, Optional<T> higherPriority);

    /// <summary>Compares two presence-aware member values according to this algebra.</summary>
    public abstract bool AreEqual(T? left, T? right);

    /// <summary>
    /// Reapplies an edit based on an earlier sparse state to the current sparse state. The default implementation
    /// treats the edit as rebaseable whenever one of the three sparse states is shared, and reports a conflict
    /// otherwise. Equality is presence-aware: two states are equal only when both are missing, or both are present
    /// with equal values according to <see cref="AreEqual(T?, T?)"/>.
    /// </summary>
    public virtual bool TryRebase(
        Optional<T> editBase,
        Optional<T> desired,
        Optional<T> current,
        out Optional<T> rebased,
        out string? reason
    )
    {
        if (OptionalsEqual(desired, editBase))
        {
            rebased = current;
            reason = null;
            return true;
        }
        if (OptionalsEqual(current, editBase) || OptionalsEqual(current, desired))
        {
            rebased = desired;
            reason = null;
            return true;
        }

        rebased = Optional<T>.Missing;
        reason = "The value conflicts with a concurrent change.";
        return false;
    }

    private bool OptionalsEqual(Optional<T> left, Optional<T> right)
    {
        if (!left.IsPresent)
        {
            return !right.IsPresent;
        }

        if (!right.IsPresent)
        {
            return false;
        }

        return AreEqual(left.Value, right.Value);
    }
}
