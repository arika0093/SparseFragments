using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

/// <summary>
/// Scale equivalence tests for issue #57: the optimized provenance implementation
/// (origins derived while building the union) must agree with the naive
/// per-element scan reference on success, origins, and failure across
/// contribution layouts, duplicate densities, comparers, and resets.
/// </summary>
public sealed class ProvenanceScaleEquivalenceTests
{
    private static readonly IEqualityComparer<string>[] Comparers =
    [
        EqualityComparer<string>.Default,
        StringComparer.OrdinalIgnoreCase,
    ];

    [Test]
    public void SequenceSetUnionMatchesNaiveReference()
    {
        var random = new Random(5759);
        foreach (var comparer in Comparers)
        {
            for (var trial = 0; trial < 60; trial++)
            {
                var contributions = RandomSequenceContributions(random, comparer);
                var effective =
                    trial % 4 == 3
                        ? MismatchedSequenceUnion(contributions, comparer, random)
                        : SequenceUnion(contributions, comparer);

                var expected = NaiveExplainSetUnion(contributions, effective, comparer);
                var actualOk = SparseFragmentRuntime.TryExplainCollectionProvenance(
                    MergeMode.SetUnion,
                    PresentAll(contributions),
                    PresentList(effective),
                    comparer,
                    out var actualOrigins,
                    out var actualReason
                );

                actualOk.ShouldBe(expected.Ok);
                if (expected.Ok)
                {
                    actualReason.ShouldBeNull();
                    actualOrigins.ShouldBe(expected.Origins);
                }
                else
                {
                    actualReason.ShouldNotBeNull();
                }
            }
        }
    }

    [Test]
    public void SetSetUnionMatchesNaiveReference()
    {
        var random = new Random(5901);
        foreach (var comparer in Comparers)
        {
            var runtimeComparer =
                comparer == Comparers[0]
                    ? StringComparer.Ordinal
                    : StringComparer.OrdinalIgnoreCase;
            for (var trial = 0; trial < 60; trial++)
            {
                var contributions = RandomSetContributions(random, runtimeComparer);
                var effective =
                    trial % 4 == 3
                        ? MismatchedSetUnion(contributions, runtimeComparer)
                        : [.. contributions.SelectMany(c => c).Distinct(runtimeComparer)];

                var expected = NaiveExplainSet(contributions, effective, runtimeComparer);
                var actualOk = SparseFragmentRuntime.TryExplainSetProvenance(
                    PresentAllSets(contributions, runtimeComparer),
                    Optional<IEnumerable<string>?>.Present(
                        (IEnumerable<string>?)new HashSet<string>(effective, runtimeComparer)
                    ),
                    out var actualOrigins,
                    out var actualReason
                );

                actualOk.ShouldBe(expected.Ok);
                if (expected.Ok)
                {
                    actualReason.ShouldBeNull();
                    actualOrigins.ShouldBe(expected.Origins);
                }
                else
                {
                    actualReason.ShouldNotBeNull();
                }
            }
        }
    }

    [Test]
    public void AppendMatchesNaiveReference()
    {
        var random = new Random(5701);
        for (var trial = 0; trial < 60; trial++)
        {
            var contributions = RandomSequenceContributions(
                random,
                EqualityComparer<string>.Default
            );
            var effective =
                trial % 4 == 3
                    ? MismatchedAppend(contributions, random)
                    : ConcatAfterReset(contributions);

            var actualOk = SparseFragmentRuntime.TryExplainCollectionProvenance(
                MergeMode.Append,
                PresentAll(contributions),
                PresentList(effective),
                null,
                out var actualOrigins,
                out var actualReason
            );

            var expected = NaiveExplainAppend(contributions, effective);
            actualOk.ShouldBe(expected.Ok);
            if (expected.Ok)
            {
                actualReason.ShouldBeNull();
                actualOrigins.ShouldBe(expected.Origins);
            }
            else
            {
                actualReason.ShouldNotBeNull();
            }
        }
    }

    [Test]
    public void LargeLowDuplicateUnionExplainsWithFirstOrigins()
    {
        // Scale smoke: 8 x 2048 distinct values. The naive reference needs
        // ~134M comparisons here; the optimized path must stay near-linear and
        // still attribute every element to its first contribution.
        const int contributions = 8;
        const int size = 2048;
        var lists = new List<List<string>>(contributions);
        for (var c = 0; c < contributions; c++)
        {
            lists.Add(Enumerable.Range(0, size).Select(i => $"c{c}-item-{i}").ToList());
        }

        var effective = SequenceUnion(lists, EqualityComparer<string>.Default);

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                PresentAll(lists),
                PresentList(effective),
                null,
                out var origins,
                out var reason
            )
            .ShouldBeTrue(reason);
        origins.Length.ShouldBe(contributions * size);
        for (var c = 0; c < contributions; c++)
        {
            for (var i = 0; i < size; i++)
            {
                origins[c * size + i].ShouldBe(c);
            }
        }

        SparseFragmentRuntime
            .TryExplainSetProvenance(
                PresentAllSets(lists, StringComparer.Ordinal),
                Optional<IEnumerable<string>?>.Present(
                    (IEnumerable<string>?)new HashSet<string>(effective, StringComparer.Ordinal)
                ),
                out var setOrigins,
                out var setReason
            )
            .ShouldBeTrue(setReason);
        setOrigins.Length.ShouldBe(effective.Count);
    }

    [Test]
    public void ResetScaleLayoutExplainsFirstOrigins()
    {
        var contributions = new List<List<string>?>
        {
            Enumerable.Range(0, 64).Select(i => $"low-{i}").ToList(),
            null,
            null,
            Enumerable.Range(0, 64).Select(i => $"low-{i}").ToList(),
            Enumerable.Range(0, 64).Select(i => $"high-{i}").ToList(),
        };
        var effective = SequenceUnion(contributions, EqualityComparer<string>.Default);

        SparseFragmentRuntime
            .TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                PresentAll(contributions),
                PresentList(effective),
                null,
                out var origins,
                out var reason
            )
            .ShouldBeTrue(reason);
        // The last present-null reset is at index 2, so shared values map to 3.
        origins.Take(64).ToArray().ShouldBe(Enumerable.Repeat(3, 64).ToArray());
        origins.Skip(64).ToArray().ShouldBe(Enumerable.Repeat(4, 64).ToArray());
    }

    private static List<List<string>?> RandomSequenceContributions(
        Random random,
        IEqualityComparer<string> comparer
    )
    {
        var count = random.Next(1, 6);
        var domain = random.Next(1, 4) switch
        {
            1 => Enumerable.Range(0, 24).Select(i => $"v{i}").ToArray(),
            2 => new[] { "a", "b", "c" },
            _ => new[] { "x" },
        };
        var result = new List<List<string>?>(count);
        for (var c = 0; c < count; c++)
        {
            if (random.Next(0, 10) == 0)
            {
                // Present-null reset: discards all lower contributions.
                result.Add(null);
            }
            else
            {
                var size = random.Next(0, 12);
                var list = new List<string>(size);
                for (var i = 0; i < size; i++)
                {
                    var value = domain[random.Next(domain.Length)];
                    list.Add(
                        comparer == Comparers[1] && random.Next(0, 2) == 0
                            ? value.ToUpperInvariant()
                            : value
                    );
                }

                result.Add(list);
            }
        }

        return result;
    }

    private static List<List<string>> RandomSetContributions(
        Random random,
        IEqualityComparer<string> comparer
    )
    {
        // Sets cannot carry a present-null reset in this helper; resets are covered
        // by dedicated tests. Duplicates collapse under the comparer like HashSet.
        var count = random.Next(1, 6);
        var domain = new[] { "a", "b", "c", "d", "e", "A", "B" };
        var result = new List<List<string>>(count);
        for (var c = 0; c < count; c++)
        {
            var size = random.Next(0, 8);
            var set = new HashSet<string>(comparer);
            for (var i = 0; i < size; i++)
            {
                set.Add(domain[random.Next(domain.Length)]);
            }

            result.Add(set.ToList());
        }

        return result;
    }

    private static Optional<IReadOnlyList<string>?>[] PresentAll(
        IReadOnlyList<List<string>?> contributions
    ) =>
        contributions
            .Select(c =>
                c is null
                    ? Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)null)
                    : Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)c)
            )
            .ToArray();

    private static Optional<IEnumerable<string>?>[] PresentAllSets(
        List<List<string>> contributions,
        IEqualityComparer<string> comparer
    ) =>
        contributions
            .Select(c =>
                Optional<IEnumerable<string>?>.Present(
                    (IEnumerable<string>?)new HashSet<string>(c, comparer)
                )
            )
            .ToArray();

    private static Optional<IReadOnlyList<string>?> PresentList(List<string> values) =>
        Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)values);

    private static List<string> SequenceUnion(
        IReadOnlyList<List<string>?> contributions,
        IEqualityComparer<string> comparer
    )
    {
        var reset = -1;
        for (var i = contributions.Count - 1; i >= 0; i--)
        {
            if (contributions[i] is null)
            {
                reset = i;
                break;
            }
        }

        var seen = new HashSet<string>(comparer);
        var result = new List<string>();
        for (var i = reset + 1; i < contributions.Count; i++)
        {
            if (contributions[i] is not { } list)
            {
                continue;
            }

            foreach (var value in list)
            {
                if (seen.Add(value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    private static List<string> MismatchedSequenceUnion(
        List<List<string>?> contributions,
        IEqualityComparer<string> comparer,
        Random random
    )
    {
        var union = SequenceUnion(contributions, comparer);
        if (union.Count == 0 || random.Next(0, 2) == 0)
        {
            return ["__no-such-element__"];
        }

        union[^1] = "__no-such-element__";
        return union;
    }

    private static List<string> ConcatAfterReset(IReadOnlyList<List<string>?> contributions)
    {
        var reset = -1;
        for (var i = contributions.Count - 1; i >= 0; i--)
        {
            if (contributions[i] is null)
            {
                reset = i;
                break;
            }
        }

        var result = new List<string>();
        for (var i = reset + 1; i < contributions.Count; i++)
        {
            if (contributions[i] is { } list)
            {
                result.AddRange(list);
            }
        }

        return result;
    }

    private static List<string> MismatchedAppend(List<List<string>?> contributions, Random random)
    {
        var concat = ConcatAfterReset(contributions);
        if (concat.Count == 0 || random.Next(0, 2) == 0)
        {
            return ["__no-such-element__"];
        }

        concat[^1] = "__no-such-element__";
        return concat;
    }

    private static List<string> MismatchedSetUnion(
        List<List<string>> contributions,
        IEqualityComparer<string> comparer
    )
    {
        var union = contributions.SelectMany(c => c).Distinct(comparer).ToList();
        return union.Count == 0
            ? ["__no-such-element__"]
            : ["__no-such-element__", .. union.Skip(1)];
    }

    private sealed record Explanation(bool Ok, int[] Origins);

    private static Explanation NaiveExplainSetUnion(
        List<List<string>?> contributions,
        List<string> effective,
        IEqualityComparer<string> comparer
    )
    {
        // The randomized effective value is never a present null, so a trailing
        // reset (highest present) must fail like the runtime requires.
        if (contributions.Count > 0 && contributions[^1] is null)
        {
            return new Explanation(false, []);
        }

        var reset = -1;
        for (var i = contributions.Count - 1; i >= 0; i--)
        {
            if (contributions[i] is null)
            {
                reset = i;
                break;
            }
        }

        var seen = new HashSet<string>(comparer);
        var distinct = new List<string>();
        foreach (var list in contributions.Skip(reset + 1))
        {
            if (list is null)
            {
                continue;
            }

            foreach (var value in list)
            {
                if (seen.Add(value))
                {
                    distinct.Add(value);
                }
            }
        }

        if (distinct.Count != effective.Count)
        {
            return new Explanation(false, []);
        }

        for (var i = 0; i < distinct.Count; i++)
        {
            if (!comparer.Equals(distinct[i], effective[i]))
            {
                return new Explanation(false, []);
            }
        }

        var origins = new int[effective.Count];
        for (var p = 0; p < effective.Count; p++)
        {
            var found = -1;
            for (var c = reset + 1; c < contributions.Count && found < 0; c++)
            {
                if (contributions[c] is not { } list)
                {
                    continue;
                }

                foreach (var candidate in list)
                {
                    if (comparer.Equals(candidate, effective[p]))
                    {
                        found = c;
                        break;
                    }
                }
            }

            if (found < 0)
            {
                return new Explanation(false, []);
            }

            origins[p] = found;
        }

        return new Explanation(true, origins);
    }

    private static Explanation NaiveExplainSet(
        List<List<string>> contributions,
        List<string> effective,
        IEqualityComparer<string> comparer
    )
    {
        var union = new HashSet<string>(comparer);
        foreach (var list in contributions)
        {
            union.UnionWith(list);
        }

        var effectiveSet = new HashSet<string>(effective, comparer);
        if (union.Count != effectiveSet.Count || !union.IsSupersetOf(effectiveSet))
        {
            return new Explanation(false, []);
        }

        var origins = new int[effective.Count];
        for (var p = 0; p < effective.Count; p++)
        {
            var found = -1;
            for (var c = 0; c < contributions.Count && found < 0; c++)
            {
                foreach (var candidate in contributions[c])
                {
                    if (comparer.Equals(candidate, effective[p]))
                    {
                        found = c;
                        break;
                    }
                }
            }

            if (found < 0)
            {
                return new Explanation(false, []);
            }

            origins[p] = found;
        }

        return new Explanation(true, origins);
    }

    private static Explanation NaiveExplainAppend(
        List<List<string>?> contributions,
        List<string> effective
    )
    {
        // The randomized effective value is never a present null, so a trailing
        // reset (highest present) must fail like the runtime requires.
        if (contributions.Count > 0 && contributions[^1] is null)
        {
            return new Explanation(false, []);
        }

        var reset = -1;
        for (var i = contributions.Count - 1; i >= 0; i--)
        {
            if (contributions[i] is null)
            {
                reset = i;
                break;
            }
        }

        var concat = new List<string>();
        var origins = new List<int>();
        for (var c = reset + 1; c < contributions.Count; c++)
        {
            if (contributions[c] is not { } list)
            {
                continue;
            }

            foreach (var value in list)
            {
                concat.Add(value);
                origins.Add(c);
            }
        }

        if (!concat.SequenceEqual(effective))
        {
            return new Explanation(false, []);
        }

        return new Explanation(true, origins.ToArray());
    }
}
