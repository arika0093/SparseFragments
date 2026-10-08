using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryRebaseScenario
{
    Clean,
    AlreadyApplied,
    Conflict,
}

/// <summary>Measures dictionary Rebase with default and custom key comparers.</summary>
[MemoryDiagnoser]
public class DictionaryRebaseComparerBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool CustomComparer { get; set; }

    [Params(
        DictionaryRebaseScenario.Clean,
        DictionaryRebaseScenario.AlreadyApplied,
        DictionaryRebaseScenario.Conflict
    )]
    public DictionaryRebaseScenario Scenario { get; set; }

    private Optional<BenchScalarDictHolder.Fragment?> _before;
    private Optional<BenchScalarDictHolder.Fragment?> _current;
    private BenchScalarDictHolder.Patch _local = null!;

    [GlobalSetup]
    public void Setup()
    {
        IEqualityComparer<string> comparer = CustomComparer
            ? StringComparer.OrdinalIgnoreCase
            : EqualityComparer<string>.Default;
        var before = new Dictionary<string, int>(Size, comparer);
        for (var index = 0; index < Size; index++)
        {
            before["key-" + index] = index;
        }

        var key = "key-" + (Size - 1);
        var desired = new Dictionary<string, int>(before, comparer) { [key] = -1 };
        var current = new Dictionary<string, int>(before, comparer);
        if (Scenario == DictionaryRebaseScenario.Clean)
        {
            current["remote"] = 99;
        }
        else
        {
            current[key] = Scenario == DictionaryRebaseScenario.AlreadyApplied ? -1 : -2;
        }

        _before = State(before);
        _current = State(current);
        _local = BenchScalarDictHolder.Patch.Between(_before, State(desired));
        var result = Rebase();
        var actual = result.Rebased.Apply(_current).Value!.Scores.Value!;
        var conflict = Scenario == DictionaryRebaseScenario.Conflict;
        if (
            result.HasConflicts != conflict
            || result.Conflicts.Count != (conflict ? 1 : 0)
            || actual[key] != (conflict ? -2 : -1)
            || actual.Count != current.Count
            || (Scenario == DictionaryRebaseScenario.Clean && actual["remote"] != 99)
            || _before.Value!.Scores.Value![key] != Size - 1
            || _current.Value!.Scores.Value![key] != current[key]
        )
        {
            throw new InvalidOperationException(
                "Rebase must preserve independent edits, detect conflicts, and keep its inputs unchanged."
            );
        }
    }

    private static Optional<BenchScalarDictHolder.Fragment?> State(
        Dictionary<string, int> values
    ) =>
        Optional<BenchScalarDictHolder.Fragment?>.Present(
            new BenchScalarDictHolder.Fragment
            {
                Scores = Optional<Dictionary<string, int>>.Present(values),
            }
        );

    [Benchmark]
    public RebaseResult<BenchScalarDictHolder.Patch> Rebase() =>
        BenchScalarDictHolder.Patch.Rebase(_before, _local, _current);
}
