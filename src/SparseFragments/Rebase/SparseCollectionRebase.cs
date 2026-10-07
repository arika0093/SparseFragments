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
    [SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "A shared predicate avoids per-element comparison closures and preserves operand order."
    )]
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

        object? valueToFind = null;
        Func<object?, bool> matches = candidate => equal(candidate, valueToFind);
        var hasRemoved = false;
        foreach (var value in before)
        {
            valueToFind = value;
            if (!desired.Any(matches))
            {
                hasRemoved = true;
            }
        }
        if (
            hasRemoved
            && !SequenceEqual(current, before, equal)
            && !SequenceEqual(current, desired, equal)
        )
        {
            rebased = [];
            reason =
                "The configuration edit conflicts with a concurrent change to a set-union member.";
            return false;
        }

        if (hasRemoved)
        {
            rebased = desired;
            reason = null;
            return true;
        }

        var result = new List<object?>(current);
        foreach (var value in desired)
        {
            valueToFind = value;
            if (!before.Any(matches) && !result.Any(matches))
            {
                result.Add(value);
            }
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>Reapplies a set-union edit onto a newer set without boxing or quadratic scans.</summary>
    /// <remarks>Comparers are part of the value; union results stay rooted in the current set.</remarks>
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

        var borrowedDesired = desired.GetType() == typeof(HashSet<T>);
        var desiredLookup = borrowedDesired
            ? (HashSet<T>)desired
            : new HashSet<T>(desired, localComparer);
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

        if (
            borrowedDesired
            && before.GetType() == typeof(HashSet<T>)
            && ReferenceEquals(((HashSet<T>)before).Comparer, localComparer)
        )
        {
            var beforeLookup = (HashSet<T>)before;
            var nativeResult = new HashSet<T>(current, currentComparer);
            if (desiredLookup.Count != beforeLookup.Count)
            {
                foreach (var value in desired.Where(value => !beforeLookup.Contains(value)))
                {
                    nativeResult.Add(value);
                }
            }
            rebased = nativeResult;
            reason = null;
            return true;
        }

        if (borrowedDesired)
        {
            desiredLookup = new HashSet<T>(desiredLookup, localComparer);
        }
        desiredLookup.ExceptWith(before);
        var result = new HashSet<T>(current, currentComparer);
        if (desiredLookup.Count > 0)
        {
            foreach (var value in desired.Where(desiredLookup.Contains))
            {
                result.Add(value);
            }
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>Reapplies an append edit onto a newer sequence without boxing or delegate dispatch.</summary>
    /// <remarks>Both local and concurrent states must preserve the baseline as a prefix, else the edit conflicts.</remarks>
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

    public static bool TryRebaseSequenceAppendArray<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out T[] rebased,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);

        comparer ??= EqualityComparer<T>.Default;

        if (SequenceEqual(current, desired, comparer) || SequenceEqual(desired, before, comparer))
        {
            rebased = CloneSequenceArray(current);
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

            rebased = CloneSequenceArray(desired);
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
        var result = new T[capacity];
        CopySequence(current, result);
        var destination = current.Count;
        for (var index = before.Count; index < desired.Count; index++)
        {
            result[destination++] = desired[index];
        }

        rebased = result;
        reason = null;
        return true;
    }

    /// <summary>Reapplies a sequence set-union edit onto a newer sequence without boxing or quadratic scans.</summary>
    /// <remarks>Removal edits conflict with any concurrent change; pure additions replay beside the current sequence.</remarks>
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
        if (
            !TryPrepareSequenceSetUnion(
                before,
                desired,
                current,
                comparer,
                out var initial,
                out var additions,
                out reason
            )
        )
        {
            rebased = [];
            return false;
        }
        var result = new List<T>(checked(initial.Count + (additions?.Count ?? 0)));
        result.AddRange(initial);
        if (additions is not null && additions.Count > 0)
        {
            foreach (var value in desired)
            {
                if (additions.Remove(value))
                {
                    result.Add(value);
                    if (additions.Count == 0)
                    {
                        break;
                    }
                }
            }
        }
        rebased = result;
        return true;
    }

    [SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "The loop consumes pending additions in first-occurrence order and stops when all additions are copied."
    )]
    public static bool TryRebaseSequenceSetUnionArray<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out T[] rebased,
        out string? reason
    )
    {
        if (
            !TryPrepareSequenceSetUnion(
                before,
                desired,
                current,
                comparer,
                out var initial,
                out var additions,
                out reason
            )
        )
        {
            rebased = [];
            return false;
        }
        var count = checked(initial.Count + (additions?.Count ?? 0));
        var result = count == 0 ? Array.Empty<T>() : new T[count];
        CopySequence(initial, result);
        var destination = initial.Count;
        if (additions is not null && additions.Count > 0)
        {
            foreach (var value in desired)
            {
                if (additions.Remove(value))
                {
                    result[destination++] = value;
                    if (additions.Count == 0)
                    {
                        break;
                    }
                }
            }
        }
        rebased = result;
        return true;
    }

    [SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit membership checks avoid allocating a predicate during replay preparation."
    )]
    private static bool TryPrepareSequenceSetUnion<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> desired,
        IReadOnlyList<T> current,
        IEqualityComparer<T>? comparer,
        out IReadOnlyList<T> initial,
        out HashSet<T>? additions,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);

        comparer ??= EqualityComparer<T>.Default;
        initial = current;
        additions = null;

        if (ReferenceEquals(before, desired) || ReferenceEquals(desired, current))
        {
            reason = null;
            return true;
        }

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
                reason =
                    "The configuration edit conflicts with a concurrent change to a set-union member.";
                return false;
            }

            initial = desired;
            reason = null;
            return true;
        }

        desiredLookup.ExceptWith(before);
        desiredLookup.ExceptWith(current);
        additions = desiredLookup;
        reason = null;
        return true;
    }

    private static T[] CloneSequenceArray<T>(IReadOnlyList<T> source)
    {
        var result = source.Count == 0 ? Array.Empty<T>() : new T[source.Count];
        CopySequence(source, result);
        return result;
    }

    private static void CopySequence<T>(IReadOnlyList<T> source, T[] destination)
    {
        if (source is ICollection<T> collection)
        {
            collection.CopyTo(destination, 0);
            return;
        }
        var index = 0;
        foreach (var value in source)
        {
            destination[index++] = value;
        }
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
