using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

public enum ProvenanceDensity
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Provenance scale benchmarks (issue #57): Replace, Append, sequence SetUnion, and
/// set-shaped SetUnion provenance across contribution count, contribution size, and
/// duplicate density, with default/custom comparers, reset/no-reset, and
/// success/mismatch cases. Origins must scale near-linearly in total contributed
/// plus effective elements.
/// </summary>
[MemoryDiagnoser]
public class ProvenanceBenchmarks
{
    [Params(2, 8, 32)]
    public int ContributionCount { get; set; }

    [Params(16, 256, 2048)]
    public int ElementsPerContribution { get; set; }

    [Params(ProvenanceDensity.Low, ProvenanceDensity.Medium, ProvenanceDensity.High)]
    public ProvenanceDensity Density { get; set; }

    private Optional<IReadOnlyList<string>?>[] _sequenceContributions = null!;
    private Optional<IReadOnlyList<string>?> _sequenceAppendEffective = default;
    private Optional<IReadOnlyList<string>?> _sequenceUnionEffective = default;
    private Optional<IReadOnlyList<string>?> _sequenceUnionIgnoreCaseEffective = default;
    private Optional<IReadOnlyList<string>?> _sequenceUnionMismatch = default;
    private Optional<IReadOnlyList<string>?> _replaceEffective = default;

    private Optional<IReadOnlyList<string>?>[] _sequenceResetContributions = null!;
    private Optional<IReadOnlyList<string>?> _sequenceAppendResetEffective = default;
    private Optional<IReadOnlyList<string>?> _sequenceUnionResetEffective = default;

    private Optional<IEnumerable<string>?>[] _setContributions = null!;
    private Optional<IEnumerable<string>?> _setEffective = default;
    private Optional<IEnumerable<string>?> _setMismatch = default;

    private Optional<IEnumerable<string>?>[] _setIgnoreCaseContributions = null!;
    private Optional<IEnumerable<string>?> _setIgnoreCaseEffective = default;

    private Optional<IEnumerable<string>?>[] _setResetContributions = null!;
    private Optional<IEnumerable<string>?> _setResetEffective = default;

    [GlobalSetup]
    public void Setup()
    {
        var contributions = new List<List<string>>(ContributionCount);
        for (var c = 0; c < ContributionCount; c++)
        {
            contributions.Add(BuildContribution(c, ElementsPerContribution, Density));
        }

        _sequenceContributions = contributions
            .Select(l => Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)l))
            .ToArray();
        _sequenceAppendEffective = Optional<IReadOnlyList<string>?>.Present(
            (IReadOnlyList<string>?)contributions.SelectMany(l => l).ToList()
        );
        _sequenceUnionEffective = Optional<IReadOnlyList<string>?>.Present(
            (IReadOnlyList<string>?)SequenceUnion(contributions, StringComparer.Ordinal)
        );
        _sequenceUnionIgnoreCaseEffective = Optional<IReadOnlyList<string>?>.Present(
            (IReadOnlyList<string>?)SequenceUnion(contributions, StringComparer.OrdinalIgnoreCase)
        );
        _sequenceUnionMismatch = Optional<IReadOnlyList<string>?>.Present(
            (IReadOnlyList<string>?)Mismatched(SequenceUnion(contributions, StringComparer.Ordinal))
        );
        _replaceEffective = Optional<IReadOnlyList<string>?>.Present(
            (IReadOnlyList<string>?)new List<string>(contributions[^1])
        );

        var resetAt = ContributionCount / 2;
        var resetContributions = contributions.Select((l, i) => (List<string>?)l).ToList();
        resetContributions[resetAt] = null;
        _sequenceResetContributions = resetContributions
            .Select(l =>
                l is null
                    ? Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)null)
                    : Optional<IReadOnlyList<string>?>.Present((IReadOnlyList<string>?)l)
            )
            .ToArray();
        var afterReset = contributions.Skip(resetAt + 1).ToList();
        _sequenceAppendResetEffective = Optional<IReadOnlyList<string>?>.Present(
            afterReset.Count == 0
                ? null
                : (IReadOnlyList<string>?)afterReset.SelectMany(l => l).ToList()
        );
        _sequenceUnionResetEffective = Optional<IReadOnlyList<string>?>.Present(
            afterReset.Count == 0
                ? null
                : (IReadOnlyList<string>?)SequenceUnion(afterReset, StringComparer.Ordinal)
        );

        _setContributions = contributions
            .Select(l =>
                Optional<IEnumerable<string>?>.Present(
                    (IEnumerable<string>?)new HashSet<string>(l, StringComparer.Ordinal)
                )
            )
            .ToArray();
        _setEffective = Optional<IEnumerable<string>?>.Present(
            (IEnumerable<string>?)
                new HashSet<string>(
                    SequenceUnion(contributions, StringComparer.Ordinal),
                    StringComparer.Ordinal
                )
        );
        _setMismatch = Optional<IEnumerable<string>?>.Present(
            (IEnumerable<string>?)
                new HashSet<string>(
                    Mismatched(SequenceUnion(contributions, StringComparer.Ordinal)),
                    StringComparer.Ordinal
                )
        );

        _setIgnoreCaseContributions = contributions
            .Select(l =>
                Optional<IEnumerable<string>?>.Present(
                    (IEnumerable<string>?)new HashSet<string>(l, StringComparer.OrdinalIgnoreCase)
                )
            )
            .ToArray();
        _setIgnoreCaseEffective = Optional<IEnumerable<string>?>.Present(
            (IEnumerable<string>?)
                new HashSet<string>(
                    SequenceUnion(contributions, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
        );

        _setResetContributions = resetContributions
            .Select(l =>
                l is null
                    ? Optional<IEnumerable<string>?>.Present((IEnumerable<string>?)null)
                    : Optional<IEnumerable<string>?>.Present(
                        (IEnumerable<string>?)new HashSet<string>(l, StringComparer.Ordinal)
                    )
            )
            .ToArray();
        _setResetEffective = Optional<IEnumerable<string>?>.Present(
            afterReset.Count == 0
                ? null
                : (IEnumerable<string>?)
                    new HashSet<string>(
                        SequenceUnion(afterReset, StringComparer.Ordinal),
                        StringComparer.Ordinal
                    )
        );
        if (
            !Replace_Success()
            || !Append_Success()
            || !Append_Reset()
            || !SequenceSetUnion_Success()
            || !SequenceSetUnion_CustomComparer()
            || !SequenceSetUnion_Reset()
            || SequenceSetUnion_Mismatch()
            || !SetSetUnion_Success()
            || !SetSetUnion_CustomComparer()
            || !SetSetUnion_Reset()
            || SetSetUnion_Mismatch()
        )
            throw new InvalidOperationException(
                "Provenance must accept matching merges and reject mismatches."
            );
        ValidateSequenceOrigins(
            _sequenceContributions,
            _sequenceUnionEffective,
            StringComparer.Ordinal
        );
        ValidateSequenceOrigins(
            _sequenceContributions,
            _sequenceUnionIgnoreCaseEffective,
            StringComparer.OrdinalIgnoreCase
        );
        ValidateSequenceOrigins(
            _sequenceResetContributions,
            _sequenceUnionResetEffective,
            StringComparer.Ordinal
        );
        try
        {
            SparseFragmentRuntime.TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                new[] { Optional<IReadOnlyList<string>?>.Present(new[] { (string)null! }) },
                Optional<IReadOnlyList<string>?>.Present(new[] { (string)null! }),
                StringComparer.Ordinal,
                out _,
                out _
            );
        }
        catch (ArgumentNullException)
        {
            return;
        }
        throw new InvalidOperationException("Null provenance elements must remain rejected.");
    }

    private static void ValidateSequenceOrigins(
        IReadOnlyList<Optional<IReadOnlyList<string>?>> contributions,
        Optional<IReadOnlyList<string>?> effective,
        IEqualityComparer<string> comparer
    )
    {
        var expected = new Dictionary<string, int>(comparer);
        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            if (!contribution.IsPresent)
                continue;
            if (contribution.Value is null)
            {
                expected.Clear();
                continue;
            }
            foreach (var value in contribution.Value)
                expected.TryAdd(value, index);
        }
        if (
            !SparseFragmentRuntime.TryExplainCollectionProvenance(
                MergeMode.SetUnion,
                contributions,
                effective,
                comparer,
                out var origins,
                out _
            ) || !origins.SequenceEqual((effective.Value ?? []).Select(value => expected[value]))
        )
            throw new InvalidOperationException(
                "Sequence provenance must preserve first contribution indices after resets."
            );
    }

    [Benchmark(Description = "Provenance Replace: highest contribution wins")]
    public bool Replace_Success()
    {
        // Replace provenance covers only the winning contribution; the remaining
        // contributions stay present so reset/winner discovery still scans them.
        return SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.Replace,
            _sequenceContributions,
            _replaceEffective,
            null,
            out _,
            out _
        );
    }

    [Benchmark(Description = "Provenance Append: concatenated contributions")]
    public bool Append_Success() =>
        SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.Append,
            _sequenceContributions,
            _sequenceAppendEffective,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance Append: present-null reset discards lower contributions")]
    public bool Append_Reset() =>
        SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.Append,
            _sequenceResetContributions,
            _sequenceAppendResetEffective,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance sequence SetUnion: insertion-ordered distinct union")]
    public bool SequenceSetUnion_Success() =>
        SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.SetUnion,
            _sequenceContributions,
            _sequenceUnionEffective,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance sequence SetUnion: custom comparer (OrdinalIgnoreCase)")]
    public bool SequenceSetUnion_CustomComparer() =>
        SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.SetUnion,
            _sequenceContributions,
            _sequenceUnionIgnoreCaseEffective,
            StringComparer.OrdinalIgnoreCase,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance sequence SetUnion: present-null reset")]
    public bool SequenceSetUnion_Reset() =>
        SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.SetUnion,
            _sequenceResetContributions,
            _sequenceUnionResetEffective,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance sequence SetUnion: mismatched effective value")]
    public bool SequenceSetUnion_Mismatch() =>
        SparseFragmentRuntime.TryExplainCollectionProvenance(
            MergeMode.SetUnion,
            _sequenceContributions,
            _sequenceUnionMismatch,
            null,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance set SetUnion: comparer-aware union")]
    public bool SetSetUnion_Success() =>
        SparseFragmentRuntime.TryExplainSetProvenance(
            _setContributions,
            _setEffective,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance set SetUnion: custom comparer (OrdinalIgnoreCase)")]
    public bool SetSetUnion_CustomComparer() =>
        SparseFragmentRuntime.TryExplainSetProvenance(
            _setIgnoreCaseContributions,
            _setIgnoreCaseEffective,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance set SetUnion: present-null reset")]
    public bool SetSetUnion_Reset() =>
        SparseFragmentRuntime.TryExplainSetProvenance(
            _setResetContributions,
            _setResetEffective,
            out _,
            out _
        );

    [Benchmark(Description = "Provenance set SetUnion: mismatched effective value")]
    public bool SetSetUnion_Mismatch() =>
        SparseFragmentRuntime.TryExplainSetProvenance(
            _setContributions,
            _setMismatch,
            out _,
            out _
        );

    private static List<string> BuildContribution(int index, int size, ProvenanceDensity density) =>
        density switch
        {
            ProvenanceDensity.Low => Enumerable
                .Range(0, size)
                .Select(i => $"c{index}-item-{i}")
                .ToList(),
            ProvenanceDensity.Medium => Enumerable
                .Range(0, size)
                .Select(i => $"shared-{i % Math.Max(1, size / 2)}-from-{index % 2}")
                .ToList(),
            _ => Enumerable.Range(0, size).Select(i => $"dup-{i % 8}").ToList(),
        };

    private static List<string> SequenceUnion(
        IReadOnlyList<List<string>> contributions,
        IEqualityComparer<string> comparer
    )
    {
        var seen = new HashSet<string>(comparer);
        var result = new List<string>();
        foreach (var contribution in contributions)
        {
            foreach (var value in contribution)
            {
                if (seen.Add(value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    private static List<string> Mismatched(List<string> union)
    {
        if (union.Count == 0)
        {
            return ["__mismatch__"];
        }

        var copy = new List<string>(union) { [^1] = "__mismatch__" };
        return copy;
    }
}
