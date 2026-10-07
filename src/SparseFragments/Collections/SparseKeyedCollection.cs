namespace SparseFragments;

/// <summary>Keyed structural collection primitives shared by generated patches.</summary>
/// <remarks>Reorder is a final key sequence; duplicate keys are invalid; key changes are remove-old + add-new.</remarks>
internal static class SparseKeyedCollection
{
    /// <summary>Throws when a key sequence contains duplicates.</summary>
    public static void ThrowDuplicateKey(object? key) =>
        throw new InvalidOperationException(
            "Duplicate key '"
                + (key?.ToString() ?? "<null>")
                + "' in keyed collection. "
                + "Duplicate keys are invalid."
        );

    /// <summary>Ensures keys are unique, throwing on the first duplicate.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loop throws on first duplicate key with the offending key value."
    )]
    public static void EnsureUniqueKeys<TKey>(
        IEnumerable<TKey> keys,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(keys);
        var seen = new HashSet<TKey>(comparer ?? EqualityComparer<TKey>.Default);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                ThrowDuplicateKey(key);
            }
        }
    }

    /// <summary>Compares two final key sequences for equality.</summary>
    public static bool OrderEquals<TKey>(
        IReadOnlyList<TKey> left,
        IReadOnlyList<TKey> right,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            return false;
        }

        comparer ??= EqualityComparer<TKey>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
