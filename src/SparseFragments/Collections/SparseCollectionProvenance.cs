using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SparseFragments;

/// <summary>Collection contribution provenance shared by generated append, set-union, and replace members.</summary>
/// <remarks>
/// Presence mirrors fragment merge: <see cref="Optional{T}.Missing"/> contributes nothing, present null resets,
/// present collections contribute elements (Append concatenates, SetUnion deduplicates, Replace takes highest).
/// </remarks>
internal static class SparseCollectionProvenance
{
    /// <summary>
    /// Explains which low-to-high contribution supplied each effective sequence element for a built-in merge mode.
    /// </summary>
    /// <param name="mode">The fragment merge mode. Only Replace, Append, and SetUnion are supported.</param>
    /// <param name="contributions">Contribution values from lowest to highest priority. Missing contributes nothing; present null resets.</param>
    /// <param name="effective">The merged effective value using the same presence convention.</param>
    /// <param name="comparer">Element equality. Defaults to <see cref="EqualityComparer{T}.Default"/>, matching <see cref="SparseCollectionMerger"/> sequence semantics.</param>
    /// <param name="origins">On success, one contribution index per effective element position.</param>
    /// <param name="reason">The failure reason on failure.</param>
    /// <returns>True when <paramref name="effective"/> matches the recomputed merge and provenance was explained.</returns>
    public static bool TryExplain<T>(
        MergeMode mode,
        IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions,
        Optional<IReadOnlyList<T>?> effective,
        IEqualityComparer<T>? comparer,
        out int[] origins,
        out string? reason
    )
    {
        return mode switch
        {
            MergeMode.Replace => TryExplainReplace(
                contributions,
                effective,
                comparer,
                out _,
                out origins,
                out reason
            ),
            MergeMode.Append => TryExplainAppend(
                contributions,
                effective,
                comparer,
                out origins,
                out reason
            ),
            MergeMode.SetUnion => TryExplainSetUnion(
                contributions,
                effective,
                comparer,
                out origins,
                out reason
            ),
            MergeMode.Deep => Fail(
                "Deep merge has no flat collection provenance; it merges nested models member by member.",
                out origins,
                out reason
            ),
            _ => Fail(
                "Custom merge strategies are unsupported by the neutral provenance primitive unless they provide an explicit provenance contract.",
                out origins,
                out reason
            ),
        };
    }

    /// <summary>
    /// Explains replace provenance: the highest present contribution wins and supplies every effective element.
    /// </summary>
    /// <param name="contributions">Contribution values from lowest to highest priority.</param>
    /// <param name="effective">The merged effective value.</param>
    /// <param name="comparer">Element equality used to validate the effective value.</param>
    /// <param name="winner">On success, the winning contribution index, or -1 when no contribution is present.</param>
    /// <param name="origins">On success, one entry per effective element, each equal to <paramref name="winner"/>.</param>
    /// <param name="reason">The failure reason on failure.</param>
    public static bool TryExplainReplace<T>(
        IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions,
        Optional<IReadOnlyList<T>?> effective,
        IEqualityComparer<T>? comparer,
        out int winner,
        out int[] origins,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(contributions);

        winner = HighestPresent(contributions);
        if (winner < 0)
        {
            if (!effective.IsPresent)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "The effective value is present but no contribution is present.",
                out origins,
                out reason
            );
        }

        var winning = contributions[winner];
        if (winning.Value is null)
        {
            if (effective.IsPresent && effective.Value is null)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "The winning contribution is a present null reset but the effective value is not a present null.",
                out origins,
                out reason
            );
        }

        if (!effective.IsPresent || effective.Value is null)
        {
            return Fail(
                "The winning contribution is a present collection but the effective value is not a present collection.",
                out origins,
                out reason
            );
        }

        comparer ??= EqualityComparer<T>.Default;
        if (!SequenceEqual(winning.Value, effective.Value, comparer))
        {
            return Fail(
                "The effective value does not equal the highest present contribution for Replace.",
                out origins,
                out reason
            );
        }

        origins = new int[effective.Value.Count];
        for (var index = 0; index < origins.Length; index++)
        {
            origins[index] = winner;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Explains append provenance: the effective value concatenates the present non-null contributions
    /// after the last present-null reset, preserving duplicates and order.
    /// </summary>
    /// <param name="contributions">Contribution values from lowest to highest priority.</param>
    /// <param name="effective">The merged effective value.</param>
    /// <param name="comparer">Element equality used to validate the effective value.</param>
    /// <param name="origins">On success, one contribution index per effective element position.</param>
    /// <param name="reason">The failure reason on failure.</param>
    public static bool TryExplainAppend<T>(
        IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions,
        Optional<IReadOnlyList<T>?> effective,
        IEqualityComparer<T>? comparer,
        out int[] origins,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(contributions);

        var highest = HighestPresent(contributions);
        if (highest < 0)
        {
            if (!effective.IsPresent)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "The effective value is present but no contribution is present.",
                out origins,
                out reason
            );
        }

        if (contributions[highest].Value is null)
        {
            if (effective.IsPresent && effective.Value is null)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "A present null contribution resets the append merge but the effective value is not a present null.",
                out origins,
                out reason
            );
        }

        if (!effective.IsPresent || effective.Value is null)
        {
            return Fail(
                "The append merge has present collections but the effective value is not a present collection.",
                out origins,
                out reason
            );
        }

        comparer ??= EqualityComparer<T>.Default;
        var reset = LastReset(contributions);
        var total = 0;
        for (var index = reset + 1; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (contribution.IsPresent && contribution.Value is not null)
            {
                total = checked(total + contribution.Value.Count);
            }
        }

        if (effective.Value.Count != total)
        {
            return Fail(
                "The effective value does not concatenate the present contributions after the last reset for Append.",
                out origins,
                out reason
            );
        }

        origins = new int[total];
        var position = 0;
        for (var index = reset + 1; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (!contribution.IsPresent || contribution.Value is null)
            {
                continue;
            }

            foreach (var value in contribution.Value)
            {
                if (!comparer.Equals(value, effective.Value[position]))
                {
                    return Fail(
                        "The effective value does not concatenate the present contributions after the last reset for Append.",
                        out origins,
                        out reason
                    );
                }

                origins[position] = index;
                position++;
            }
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Explains sequence set-union provenance: the effective value is the insertion-ordered distinct
    /// union (first occurrence wins) of the present non-null contributions after the last present-null reset.
    /// </summary>
    /// <param name="contributions">Contribution values from lowest to highest priority.</param>
    /// <param name="effective">The merged effective value.</param>
    /// <param name="comparer">Element equality. Defaults to <see cref="EqualityComparer{T}.Default"/>, matching <see cref="SparseCollectionMerger.MergeDistinctList{T}"/>.</param>
    /// <param name="origins">On success, one contribution index per effective element; each maps to the first contribution holding an equal value.</param>
    /// <param name="reason">The failure reason on failure.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops track contribution indices while building the distinct union; LINQ would obscure provenance positions."
    )]
    public static bool TryExplainSetUnion<T>(
        IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions,
        Optional<IReadOnlyList<T>?> effective,
        IEqualityComparer<T>? comparer,
        out int[] origins,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(contributions);

        var highest = HighestPresent(contributions);
        if (highest < 0)
        {
            if (!effective.IsPresent)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "The effective value is present but no contribution is present.",
                out origins,
                out reason
            );
        }

        if (contributions[highest].Value is null)
        {
            if (effective.IsPresent && effective.Value is null)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "A present null contribution resets the set-union merge but the effective value is not a present null.",
                out origins,
                out reason
            );
        }

        if (!effective.IsPresent || effective.Value is null)
        {
            return Fail(
                "The set-union merge has present collections but the effective value is not a present collection.",
                out origins,
                out reason
            );
        }

        comparer ??= EqualityComparer<T>.Default;
        var reset = LastReset(contributions);
        // First-origin tracking while building the union: each distinct value records the
        // lowest contribution index that supplied it, so origins need no second scan
        // over the contributions. Total work stays O(total contributed + effective).
        var originsByValue = new Dictionary<T, int>(effective.Value.Count, comparer);
        var originBuffer = new int[effective.Value.Count];
        var distinctCount = 0;
        for (var index = reset + 1; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (!contribution.IsPresent || contribution.Value is null)
            {
                continue;
            }

            foreach (var value in contribution.Value)
            {
                if (!originsByValue.ContainsKey(value))
                {
                    if (
                        distinctCount >= effective.Value.Count
                        || !comparer.Equals(value, effective.Value[distinctCount])
                    )
                    {
                        return Fail(
                            "The effective value is not the insertion-ordered distinct union of the present contributions after the last reset.",
                            out origins,
                            out reason
                        );
                    }

                    originsByValue[value] = index;
                    originBuffer[distinctCount++] = index;
                }
            }
        }

        // Each first accepted value was checked against its effective position
        // above, so a matching distinct count completes ordered-union validation.
        if (distinctCount != effective.Value.Count)
        {
            return Fail(
                "The effective value is not the insertion-ordered distinct union of the present contributions after the last reset.",
                out origins,
                out reason
            );
        }

        origins = originBuffer;
        reason = null;
        return true;
    }

    /// <summary>Explains set-shaped set-union provenance with comparer-correct equality.</summary>
    /// <remarks>
    /// The effective comparer is discovered like <see cref="SparseCollectionMerger.MergeSet{T}"/>:
    /// the effective set's comparer wins when available, otherwise the first active
    /// <see cref="HashSet{T}"/> comparer wins, otherwise <see cref="EqualityComparer{T}.Default"/>.
    /// When both the effective set and the contributions declare comparers that differ, the values
    /// differ (the comparer is part of the value) and provenance fails instead of depending on
    /// operand order. Origins align with the effective enumeration order at call time; set order
    /// itself carries no provenance meaning.
    /// </remarks>
    /// <param name="contributions">Contribution values from lowest to highest priority. Missing contributes nothing; present null resets.</param>
    /// <param name="effective">The merged effective set using the same presence convention.</param>
    /// <param name="origins">On success, one contribution index per effective element in enumeration order; each maps to the first contribution holding an equal value.</param>
    /// <param name="reason">The failure reason on failure.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S3267",
        Justification = "Explicit loops track contribution indices while building the comparer-aware union; LINQ would obscure provenance positions."
    )]
    public static bool TryExplainSet<T>(
        IReadOnlyList<Optional<IEnumerable<T>?>> contributions,
        Optional<IEnumerable<T>?> effective,
        out int[] origins,
        out string? reason
    )
    {
        ArgumentNullException.ThrowIfNull(contributions);

        var highest = HighestPresentSet(contributions);
        if (highest < 0)
        {
            if (!effective.IsPresent)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "The effective value is present but no contribution is present.",
                out origins,
                out reason
            );
        }

        if (contributions[highest].Value is null)
        {
            if (effective.IsPresent && effective.Value is null)
            {
                origins = [];
                reason = null;
                return true;
            }

            return Fail(
                "A present null contribution resets the set-union merge but the effective value is not a present null.",
                out origins,
                out reason
            );
        }

        if (!effective.IsPresent || effective.Value is null)
        {
            return Fail(
                "The set-union merge has present collections but the effective value is not a present collection.",
                out origins,
                out reason
            );
        }

        var reset = LastResetSet(contributions);
        var effectiveComparer = TryGetSetComparer(effective.Value);
        var activeComparer = FirstActiveSetComparer(contributions, reset + 1);
        if (
            effectiveComparer is not null
            && activeComparer is not null
            && !effectiveComparer.Equals(activeComparer)
        )
        {
            return Fail(
                "The effective set comparer differs from the contributions comparer; the comparer is part of the set value.",
                out origins,
                out reason
            );
        }

        var comparer = effectiveComparer ?? activeComparer ?? EqualityComparer<T>.Default;
        var effectiveValues = effective.Value.ToArray();
        var distinctEffective = new HashSet<T>(effectiveValues, comparer);
        if (distinctEffective.Count != effectiveValues.Length)
        {
            return Fail(
                "The effective set contains duplicate elements under its comparer.",
                out origins,
                out reason
            );
        }

        // First-origin tracking while building the union: each distinct value records
        // the lowest contribution index that supplied it, so origins need no second
        // scan over the contributions. Total work stays O(total + effective).
        var originsByValue = new Dictionary<T, int>(effectiveValues.Length, comparer);
        for (var index = reset + 1; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (!contribution.IsPresent || contribution.Value is null)
            {
                continue;
            }

            foreach (var value in contribution.Value)
            {
                if (!originsByValue.ContainsKey(value))
                {
                    originsByValue[value] = index;
                }
            }
        }

        if (
            originsByValue.Count != distinctEffective.Count
            || !distinctEffective.All(value => originsByValue.ContainsKey(value))
        )
        {
            return Fail(
                "The effective set is not the comparer-aware union of the present contributions after the last reset.",
                out origins,
                out reason
            );
        }

        origins = new int[effectiveValues.Length];
        for (var position = 0; position < effectiveValues.Length; position++)
        {
            // The union check above guarantees every effective value is tracked, so
            // the lookup succeeds for comparers whose hash codes agree with
            // equality. TryGetValue keeps a pathological comparer a validation
            // failure instead of an exception.
            if (!originsByValue.TryGetValue(effectiveValues[position], out var origin))
            {
                return Fail(
                    "The effective value contains an element supplied by no contribution.",
                    out origins,
                    out reason
                );
            }

            origins[position] = origin;
        }

        reason = null;
        return true;
    }

    private static bool Fail(string message, out int[] origins, out string? reason)
    {
        origins = [];
        reason = message;
        return false;
    }

    private static int HighestPresent<T>(IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions)
    {
        for (var index = contributions.Count - 1; index >= 0; index--)
        {
            if (contributions[index].IsPresent)
            {
                return index;
            }
        }

        return -1;
    }

    private static int LastReset<T>(IReadOnlyList<Optional<IReadOnlyList<T>?>> contributions)
    {
        for (var index = contributions.Count - 1; index >= 0; index--)
        {
            var contribution = contributions[index];
            if (contribution.IsPresent && contribution.Value is null)
            {
                return index;
            }
        }

        return -1;
    }

    private static int HighestPresentSet<T>(IReadOnlyList<Optional<IEnumerable<T>?>> contributions)
    {
        for (var index = contributions.Count - 1; index >= 0; index--)
        {
            if (contributions[index].IsPresent)
            {
                return index;
            }
        }

        return -1;
    }

    private static int LastResetSet<T>(IReadOnlyList<Optional<IEnumerable<T>?>> contributions)
    {
        for (var index = contributions.Count - 1; index >= 0; index--)
        {
            var contribution = contributions[index];
            if (contribution.IsPresent && contribution.Value is null)
            {
                return index;
            }
        }

        return -1;
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

    private static IEqualityComparer<T>? FirstActiveSetComparer<T>(
        IReadOnlyList<Optional<IEnumerable<T>?>> contributions,
        int start
    )
    {
        for (var index = start; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (!contribution.IsPresent || contribution.Value is null)
            {
                continue;
            }

            var comparer = TryGetSetComparer(contribution.Value);
            if (comparer is not null)
            {
                return comparer;
            }
        }

        return null;
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
}
