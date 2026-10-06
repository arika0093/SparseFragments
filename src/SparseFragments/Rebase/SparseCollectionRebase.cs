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

        var result = new List<object?>(current);
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

        var beforeLookup = new HashSet<T>(before, localComparer);
        var result = new HashSet<T>(current, currentComparer);
        foreach (var value in desired.Where(value => !beforeLookup.Contains(value)))
        {
            result.Add(value);
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
