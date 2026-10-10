namespace SparseFragments;

/// <summary>
/// Computes per-element origin attributions for built-in collection merges.
/// </summary>
/// <remarks>
/// <para>
/// Live-merge counterpart to the post-hoc <see cref="SparseCollectionProvenance"/>
/// explanation: where provenance explains which contribution index supplied each
/// effective element, these helpers map the same first-accepted semantics
/// directly onto origin strings. Set comparer discovery reuses
/// <see cref="SparseCollectionProvenance"/> so union attribution agrees with
/// merge membership.
/// </para>
/// <para>
/// All helpers run only when at least one merging side already carries origin
/// information; merges without any origin state skip them entirely. Element
/// attributions fall back to the contributing member attribution, which itself
/// falls back to the contributing fragment origin (<c>null</c> is Unknown at
/// every level).
/// </para>
/// </remarks>
internal static class SparseOriginMerger
{
    /// <summary>
    /// Concatenates lower then higher attributions for an append merge.
    /// </summary>
    public static string?[] MergeAppendOrigins<T>(
        IEnumerable<T> lower,
        string?[]? lowerOrigins,
        string? lowerFallback,
        IEnumerable<T> higher,
        string?[]? higherOrigins,
        string? higherFallback
    )
    {
        var lowerList = AsReadOnlyList(lower);
        var higherList = AsReadOnlyList(higher);
        var result = new string?[checked(lowerList.Count + higherList.Count)];
        Fill(result, 0, lowerList, lowerOrigins, lowerFallback);
        Fill(result, lowerList.Count, higherList, higherOrigins, higherFallback);
        return result;
    }

    /// <summary>
    /// Tracks first-accepted attributions for a sequence set-union merge.
    /// </summary>
    public static string?[] MergeSetUnionOrigins<T>(
        IEnumerable<T> lower,
        string?[]? lowerOrigins,
        string? lowerFallback,
        IEnumerable<T> higher,
        string?[]? higherOrigins,
        string? higherFallback,
        IEqualityComparer<T>? comparer
    )
    {
        comparer ??= EqualityComparer<T>.Default;
        var seen = new HashSet<T>(comparer);
        var result = new List<string?>();
        AddDistinct(lower, lowerOrigins, lowerFallback, seen, result);
        AddDistinct(higher, higherOrigins, higherFallback, seen, result);
        return result.ToArray();
    }

    /// <summary>
    /// Tracks first-accepted attributions for a set-union merge in effective enumeration order.
    /// </summary>
    /// <remarks>
    /// The effective comparer wins, otherwise the first active contribution
    /// comparer wins, otherwise the default comparer applies, mirroring
    /// <see cref="SparseCollectionProvenance.TryExplainSet{T}"/>. When the
    /// effective and contribution comparers disagree the attribution is
    /// Unknown rather than fabricated.
    /// </remarks>
    public static string?[] MergeSetOrigins<T>(
        IEnumerable<T> lower,
        string?[]? lowerOrigins,
        string? lowerFallback,
        IEnumerable<T> higher,
        string?[]? higherOrigins,
        string? higherFallback,
        IEnumerable<T> effective
    )
    {
        var effectiveList = AsReadOnlyList(effective);
        var comparer = DiscoverSetComparer(lower, higher, effective);
        if (comparer is null)
        {
            return new string?[effectiveList.Count];
        }

        // First-origin tracking while building the union: each distinct value
        // records the lowest contribution that supplied it, so the effective
        // enumeration below needs no second scan over the contributions.
        var firstByValue = new Dictionary<T, string?>(comparer);
        AddFirst(lower, lowerOrigins, lowerFallback, firstByValue);
        AddFirst(higher, higherOrigins, higherFallback, firstByValue);

        var result = new string?[effectiveList.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = firstByValue.TryGetValue(effectiveList[index], out var origin)
                ? origin
                : null;
        }

        return result;
    }

    private static IEqualityComparer<T>? DiscoverSetComparer<T>(
        IEnumerable<T> lower,
        IEnumerable<T> higher,
        IEnumerable<T> effective
    )
    {
        var effectiveComparer = SparseCollectionProvenance.TryGetSetComparer(effective);
        var activeComparer =
            SparseCollectionProvenance.TryGetSetComparer(lower)
            ?? SparseCollectionProvenance.TryGetSetComparer(higher);
        if (
            effectiveComparer is not null
            && activeComparer is not null
            && !effectiveComparer.Equals(activeComparer)
        )
        {
            return null;
        }

        return effectiveComparer ?? activeComparer ?? EqualityComparer<T>.Default;
    }

    private static IReadOnlyList<T> AsReadOnlyList<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values as IReadOnlyList<T> ?? values.ToArray();
    }

    private static void Fill<T>(
        string?[] result,
        int offset,
        IReadOnlyList<T> values,
        string?[]? origins,
        string? fallback
    )
    {
        for (var index = 0; index < values.Count; index++)
        {
            result[offset + index] =
                origins is null || index >= origins.Length ? fallback : origins[index] ?? fallback;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops track element positions while building the distinct union; LINQ would obscure provenance positions."
    )]
    private static void AddDistinct<T>(
        IEnumerable<T> values,
        string?[]? origins,
        string? fallback,
        HashSet<T> seen,
        List<string?> result
    )
    {
        var list = AsReadOnlyList(values);
        for (var index = 0; index < list.Count; index++)
        {
            if (seen.Add(list[index]))
            {
                result.Add(
                    origins is null || index >= origins.Length
                        ? fallback
                        : origins[index] ?? fallback
                );
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops track element positions while building the comparer-aware union; LINQ would obscure provenance positions."
    )]
    private static void AddFirst<T>(
        IEnumerable<T> values,
        string?[]? origins,
        string? fallback,
        Dictionary<T, string?> firstByValue
    )
    {
        var list = AsReadOnlyList(values);
        for (var index = 0; index < list.Count; index++)
        {
            if (!firstByValue.ContainsKey(list[index]))
            {
                firstByValue[list[index]] =
                    origins is null || index >= origins.Length
                        ? fallback
                        : origins[index] ?? fallback;
            }
        }
    }
}
