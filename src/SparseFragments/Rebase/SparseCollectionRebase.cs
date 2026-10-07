using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SparseFragments;

/// <summary>Domain-neutral collection rebase rules shared by generated append and set-union members.</summary>
internal static class SparseCollectionRebase
{
    /// <summary>
    /// Reapplies an append edit (a suffix of added elements) onto a newer collection, or reports why it cannot.
    /// </summary>
    /// <param name="before">The baseline collection.</param>
    /// <param name="desired">The locally edited collection.</param>
    /// <param name="current">The newer collection.</param>
    /// <param name="equal">Element equality.</param>
    /// <param name="rebased">The rebased collection on success.</param>
    /// <param name="reason">The failure reason on failure.</param>
    public static bool TryRebaseAppend(
        IReadOnlyList<object?> before,
        IReadOnlyList<object?> desired,
        IReadOnlyList<object?> current,
        Func<object?, object?, bool> equal,
        out IReadOnlyList<object?> rebased,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(equal);

        if (SequenceEqual(current, desired, equal) || SequenceEqual(desired, before, equal))
        {
            rebased = current;
            reason = null;
            return true;
        }

        if (!HasPrefix(desired, before, equal))
        {
            if (!SequenceEqual(current, before, equal) && !SequenceEqual(current, desired, equal))
            {
                rebased = [];
                reason =
                    "The configuration edit conflicts with a concurrent change to an append-merged member.";
                return false;
            }

            rebased = desired;
            reason = null;
            return true;
        }

        if (!HasPrefix(current, before, equal))
        {
            rebased = [];
            reason =
                "The configuration edit cannot reapply its append because the existing collection prefix changed.";
            return false;
        }

        var capacity = checked(current.Count + (desired.Count - before.Count));
        var result = new List<object?>(capacity);
        result.AddRange(current);
        for (var index = before.Count; index < desired.Count; index++)
        {
            result.Add(desired[index]);
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>Reapplies a set-union edit (added and removed elements) onto a newer collection.</summary>
    /// <param name="before">The baseline collection.</param>
    /// <param name="desired">The locally edited collection.</param>
    /// <param name="current">The newer collection.</param>
    /// <param name="equal">Element equality.</param>
    /// <param name="rebased">The rebased collection on success.</param>
    /// <param name="reason">The failure reason on failure.</param>
    public static bool TryRebaseSetUnion(
        IReadOnlyList<object?> before,
        IReadOnlyList<object?> desired,
        IReadOnlyList<object?> current,
        Func<object?, object?, bool> equal,
        out IReadOnlyList<object?> rebased,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(equal);

        var removed = before
            .Where(value => !desired.Any(candidate => equal(candidate, value)))
            .ToArray();
        if (
            removed.Length > 0
            && !SequenceEqual(current, before, equal)
            && !SequenceEqual(current, desired, equal)
        )
        {
            rebased = [];
            reason =
                "The configuration edit conflicts with a concurrent change to a set-union member.";
            return false;
        }

        if (removed.Length > 0)
        {
            rebased = desired;
            reason = null;
            return true;
        }

        var result = new List<object?>(current);
        foreach (
            var value in desired
                .Where(value => !before.Any(candidate => equal(candidate, value)))
                .Where(value => !result.Any(candidate => equal(candidate, value)))
        )
        {
            result.Add(value);
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>Reapplies a set-union edit onto a newer set without boxing or quadratic scans.</summary>
    /// <remarks>
    /// The element comparer is part of the set value (issue #5): equality gates reuse the
    /// comparer-aware <see cref="SparseValueComparer.AreSetEqual{T}"/> semantics, while
    /// difference and union run as O(n) expected-time hash operations under an effective
    /// comparer discovered from the inputs. The removal-only result preserves the desired
    /// set's comparer; the union result is rooted in the current set so concurrent elements
    /// are never lost to a comparer change, preserving the current set's comparer.
    /// </remarks>
    /// <param name="before">The baseline set.</param>
    /// <param name="desired">The locally edited set.</param>
    /// <param name="current">The newer set.</param>
    /// <param name="rebased">The rebased set on success.</param>
    /// <param name="reason">The failure reason on failure.</param>
    public static bool TryRebaseSetUnion<T>(
        IEnumerable<T> before,
        IEnumerable<T> desired,
        IEnumerable<T> current,
        out HashSet<T> rebased,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);

        var localComparer =
            TryGetSetComparer(desired) ?? TryGetSetComparer(before) ?? EqualityComparer<T>.Default;
        var currentComparer =
            TryGetSetComparer(current)
            ?? TryGetSetComparer(desired)
            ?? TryGetSetComparer(before)
            ?? EqualityComparer<T>.Default;

        var desiredLookup = new HashSet<T>(desired, localComparer);
        var hasRemoved = before.Any(value => !desiredLookup.Contains(value));

        if (hasRemoved)
        {
            if (
                SparseValueComparer.AreSetEqual(current, before)
                || SparseValueComparer.AreSetEqual(current, desired)
            )
            {
                rebased = new HashSet<T>(desired, TryGetSetComparer(desired) ?? localComparer);
                reason = null;
                return true;
            }

            rebased = new HashSet<T>(currentComparer);
            reason =
                "The configuration edit conflicts with a concurrent change to a set-union member.";
            return false;
        }

        desiredLookup.ExceptWith(before);
        var result = new HashSet<T>(current, currentComparer);
        foreach (var value in desired.Where(desiredLookup.Contains))
        {
            result.Add(value);
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>
    /// Reapplies an append edit (a suffix of added elements) onto a newer sequence
    /// without boxing or delegate dispatch.
    /// </summary>
    /// <remarks>
    /// Typed counterpart of the <c>object?</c> append helper for sequence members whose
    /// element equality is the member comparer (issue #59). Prefix semantics are
    /// unchanged: the local edit must preserve the baseline as a prefix, and the
    /// concurrent state must do the same, otherwise the edit conflicts.
    /// </remarks>
    /// <param name="before">The baseline sequence.</param>
    /// <param name="desired">The locally edited sequence.</param>
    /// <param name="current">The newer sequence.</param>
    /// <param name="comparer">Element equality. Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="rebased">The rebased sequence on success.</param>
    /// <param name="reason">The failure reason on failure.</param>
    public static bool TryRebaseSequenceAppend<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out List<T> rebased,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);

        comparer ??= EqualityComparer<T>.Default;

        if (SequenceEqual(current, desired, comparer) || SequenceEqual(desired, before, comparer))
        {
            rebased = new List<T>(current);
            reason = null;
            return true;
        }

        if (!HasPrefix(desired, before, comparer))
        {
            if (
                !SequenceEqual(current, before, comparer)
                && !SequenceEqual(current, desired, comparer)
            )
            {
                rebased = [];
                reason =
                    "The configuration edit conflicts with a concurrent change to an append-merged member.";
                return false;
            }

            rebased = new List<T>(desired);
            reason = null;
            return true;
        }

        if (!HasPrefix(current, before, comparer))
        {
            rebased = [];
            reason =
                "The configuration edit cannot reapply its append because the existing collection prefix changed.";
            return false;
        }

        var capacity = checked(current.Count + (desired.Count - before.Count));
        var result = new List<T>(capacity);
        result.AddRange(current);
        for (var index = before.Count; index < desired.Count; index++)
        {
            result.Add(desired[index]);
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>
    /// Reapplies a sequence set-union edit (added and removed elements) onto a newer
    /// sequence without boxing, delegate dispatch, or quadratic scans.
    /// </summary>
    /// <remarks>
    /// Typed counterpart of the <c>object?</c> set-union helper for sequence members
    /// whose element equality is the member comparer (issue #59). Conflict behavior,
    /// removal handling, and duplicate handling are unchanged: a removal edit
    /// conflicts with any concurrent change, while a pure addition replays the
    /// locally added elements (first occurrence wins) beside the current sequence.
    /// Membership uses comparer-aware hash lookups instead of per-element scans.
    /// </remarks>
    /// <param name="before">The baseline sequence.</param>
    /// <param name="desired">The locally edited sequence.</param>
    /// <param name="current">The newer sequence.</param>
    /// <param name="comparer">Element equality. Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
    /// <param name="rebased">The rebased sequence on success.</param>
    /// <param name="reason">The failure reason on failure.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops combine comparer-aware hash membership with early exit and result-bound dedup; LINQ would reintroduce per-element delegate scans."
    )]
    public static bool TryRebaseSequenceSetUnion<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out List<T> rebased,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);

        comparer ??= EqualityComparer<T>.Default;

        var desiredLookup = new HashSet<T>(desired, comparer);
        var hasRemoved = false;
        foreach (var value in before)
        {
            if (!desiredLookup.Contains(value))
            {
                hasRemoved = true;
                break;
            }
        }

        if (hasRemoved)
        {
            if (
                !SequenceEqual(current, before, comparer)
                && !SequenceEqual(current, desired, comparer)
            )
            {
                rebased = [];
                reason =
                    "The configuration edit conflicts with a concurrent change to a set-union member.";
                return false;
            }

            rebased = new List<T>(desired);
            reason = null;
            return true;
        }

        desiredLookup.ExceptWith(before);
        desiredLookup.ExceptWith(current);
        var result = new List<T>(checked(current.Count + desiredLookup.Count));
        result.AddRange(current);
        if (desiredLookup.Count > 0)
        {
            foreach (var value in desired)
            {
                if (desiredLookup.Contains(value))
                {
                    desiredLookup.Remove(value);
                    result.Add(value);
                    if (desiredLookup.Count == 0)
                    {
                        break;
                    }
                }
            }
        }

        rebased = result;
        reason = null;
        return true;
    }

    private static IEqualityComparer<T>? TryGetSetComparer<T>(IEnumerable<T> value)
    {
        if (value is HashSet<T> hashSet)
        {
            return hashSet.Comparer;
        }

        return GetDeclaredComparer<T>(value);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Only reads an optional public Comparer property; a trimmed property is treated as an undiscoverable comparer with a symmetric bidirectional fallback."
    )]
    private static IEqualityComparer<T>? GetDeclaredComparer<T>(object value)
    {
        PropertyInfo? property;
        try
        {
            property = value
                .GetType()
                .GetProperty("Comparer", BindingFlags.Public | BindingFlags.Instance);
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }

        if (
            property is null
            || !property.CanRead
            || property.GetIndexParameters().Length != 0
            || !typeof(IEqualityComparer<T>).IsAssignableFrom(property.PropertyType)
        )
        {
            return null;
        }

        try
        {
            return (IEqualityComparer<T>?)property.GetValue(value, null);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static bool HasPrefix(
        IReadOnlyList<object?> candidate,
        IReadOnlyList<object?> prefix,
        Func<object?, object?, bool> equal
    )
    {
        if (candidate.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!equal(candidate[index], prefix[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasPrefix<T>(
        IReadOnlyList<T> candidate,
        IReadOnlyList<T> prefix,
        IEqualityComparer<T> comparer
    )
    {
        if (candidate.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!comparer.Equals(candidate[index], prefix[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        IEqualityComparer<T> comparer
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEqual(
        IReadOnlyList<object?> left,
        IReadOnlyList<object?> right,
        Func<object?, object?, bool> equal
    )
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!equal(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
