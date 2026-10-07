using System.ComponentModel;

namespace SparseFragments;

/// <summary>Merges source values from lowest to highest priority.</summary>
/// <remarks>
/// Advanced merge SPI: derive custom member algebras from this class.
/// Presence is significant: <see cref="Optional{T}.Missing"/> never equals a present value.
/// Generated models keep one strategy instance and may call it concurrently. Implementations must be stateless or
/// thread-safe.
/// </remarks>
/// <typeparam name="T">The model member type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public abstract class FragmentMergeStrategy<T>
{
    /// <summary>Merges two presence-aware member values.</summary>
    public abstract Optional<T> Merge(Optional<T> lowerPriority, Optional<T> higherPriority);

    /// <summary>Compares two presence-aware member values according to this algebra.</summary>
    public abstract bool AreEqual(T? left, T? right);

    /// <summary>Reapplies an edit based on an earlier sparse state to the current sparse state.</summary>
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
