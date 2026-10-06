using System.ComponentModel;

namespace SparseFragments;

/// <summary>
/// Implements the generic member-specific merge algebra shared by sparse fragments. Source values are merged from
/// lowest to highest priority.
/// </summary>
/// <remarks>
/// Advanced merge SPI: derive custom member algebras from this class. <see cref="Merge"/> and
/// <see cref="AreEqual(T?, T?)"/> are required; <see cref="TryRebase(T?, T?, T?, out T?, out string?)"/>
/// is an optional capability with a stable default: rebase succeeds when one value is shared,
/// and reports a conflict otherwise.
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

    /// <summary>
    /// Reapplies an edit based on an earlier value to the current value. The default implementation treats the edit as
    /// rebaseable whenever one of the three values is shared, and reports a conflict otherwise.
    /// </summary>
    public virtual bool TryRebase(
        T? editBase,
        T? desired,
        T? current,
        out T? rebased,
        out string? reason
    )
    {
        if (AreEqual(desired, editBase))
        {
            rebased = current;
            reason = null;
            return true;
        }
        if (AreEqual(current, editBase) || AreEqual(current, desired))
        {
            rebased = desired;
            reason = null;
            return true;
        }

        rebased = default;
        reason = "The value conflicts with a concurrent change.";
        return false;
    }
}
