using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

[SparseFragmentModel]
public partial class BenchSetUnionReplay
{
    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> Values { get; set; } = [];
}

public enum SetReplayScenario
{
    Addition,
    AlreadyApplied,
    Unchanged,
    Removal,
    RemovalConflict,
}

[MemoryDiagnoser]
public class SetUnionRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool IgnoreCase { get; set; }

    [ParamsAllValues]
    public SetReplayScenario Scenario { get; set; }

    private HashSet<string> _before = null!;
    private HashSet<string> _desired = null!;
    private HashSet<string> _current = null!;
    private Optional<BenchSetUnionReplay.Fragment?> _generatedBefore;
    private Optional<BenchSetUnionReplay.Fragment?> _generatedCurrent;
    private BenchSetUnionReplay.Patch _local = null!;

    [GlobalSetup]
    public void Setup()
    {
        var comparer = IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _before = new HashSet<string>(
            Enumerable.Range(0, Size).Select(index => "base-" + index),
            comparer
        );
        _desired = new HashSet<string>(_before, comparer);
        var removal = Scenario is SetReplayScenario.Removal or SetReplayScenario.RemovalConflict;
        if (removal)
        {
            _desired.Remove("base-0");
        }
        else if (Scenario != SetReplayScenario.Unchanged)
        {
            _desired.Add("local");
        }
        _current = new HashSet<string>(_before, comparer);
        if (Scenario != SetReplayScenario.Removal)
        {
            _current.Add("remote");
        }
        if (Scenario == SetReplayScenario.AlreadyApplied)
        {
            _current.Add(IgnoreCase ? "LOCAL" : "local");
        }
        var expected =
            Scenario == SetReplayScenario.Removal
                ? new HashSet<string>(_desired, comparer)
                : new HashSet<string>(_current, comparer);
        if (Scenario == SetReplayScenario.Addition)
        {
            expected.Add("local");
        }
        var beforeCopy = _before.ToArray();
        var desiredCopy = _desired.ToArray();
        var currentCopy = _current.ToArray();
        _generatedBefore = State(_before);
        _generatedCurrent = State(_current);
        _local = new BenchSetUnionReplay.Patch { Values = _desired };
        var success = SparseFragmentRuntime.TryRebaseSetUnion<string>(
            _before,
            _desired,
            _current,
            out var direct,
            out var reason
        );
        var generated = Generated();
        var actual = generated.Patch.Apply(_generatedCurrent).Value!.Values.Value!;
        var conflict = Scenario == SetReplayScenario.RemovalConflict;
        if (
            success == conflict
            || (reason is not null) != conflict
            || generated.Conflicts.Count != (conflict ? 1 : 0)
            || (
                !conflict
                && (!direct.SetEquals(expected) || !ReferenceEquals(direct.Comparer, comparer))
            )
            || !actual.SetEquals(expected)
            || !ReferenceEquals(actual.Comparer, comparer)
            || !_before.SequenceEqual(beforeCopy)
            || !_desired.SequenceEqual(desiredCopy)
            || !_current.SequenceEqual(currentCopy)
            || (
                Scenario == SetReplayScenario.AlreadyApplied
                && IgnoreCase
                && (
                    !direct.Contains("LOCAL")
                    || !direct.TryGetValue("local", out var spelling)
                    || spelling != "LOCAL"
                )
            )
        )
        {
            throw new InvalidOperationException(
                "Set replay must preserve comparer identity, spelling, values, conflicts and inputs."
            );
        }
        CheckMixedComparers();
    }

    private static void CheckMixedComparers()
    {
        var before = new HashSet<string>(["base"], StringComparer.OrdinalIgnoreCase);
        var desired = new HashSet<string>(["BASE", "LOCAL"], StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(["base", "local", "remote"], StringComparer.Ordinal);
        if (
            !SparseFragmentRuntime.TryRebaseSetUnion<string>(
                before,
                desired,
                current,
                out var result,
                out _
            )
            || !ReferenceEquals(result.Comparer, current.Comparer)
            || !result.SetEquals(["base", "local", "remote", "LOCAL"])
        )
        {
            throw new InvalidOperationException(
                "Current set comparer must govern the union result."
            );
        }
        var desiredRemoval = new HashSet<string>(["LOCAL"], StringComparer.OrdinalIgnoreCase);
        var cleanCurrent = new HashSet<string>(["base"], StringComparer.OrdinalIgnoreCase);
        if (
            !SparseFragmentRuntime.TryRebaseSetUnion<string>(
                before,
                desiredRemoval,
                cleanCurrent,
                out var removed,
                out _
            )
            || !ReferenceEquals(removed.Comparer, desiredRemoval.Comparer)
            || !removed.SetEquals(desiredRemoval)
        )
        {
            throw new InvalidOperationException("Removal replay must retain the desired comparer.");
        }
        string[] duplicateDesired = ["base", "LOCAL", "local", "LOCAL"];
        if (
            !SparseFragmentRuntime.TryRebaseSetUnion<string>(
                before,
                duplicateDesired,
                current,
                out var duplicate,
                out _
            ) || !duplicate.SetEquals(["base", "local", "remote", "LOCAL"])
        )
        {
            throw new InvalidOperationException(
                "Enumerable variants must replay under the current comparer."
            );
        }
    }

    private static Optional<BenchSetUnionReplay.Fragment?> State(HashSet<string> values) =>
        Optional<BenchSetUnionReplay.Fragment?>.Present(
            new BenchSetUnionReplay.Fragment { Values = Optional<HashSet<string>>.Present(values) }
        );

    [Benchmark]
    public HashSet<string> Direct()
    {
        SparseFragmentRuntime.TryRebaseSetUnion<string>(
            _before,
            _desired,
            _current,
            out var result,
            out _
        );
        return result;
    }

    [Benchmark]
    public RebaseResult<BenchSetUnionReplay.Patch> Generated() =>
        BenchSetUnionReplay.Patch.Rebase(_generatedBefore, _local, _generatedCurrent);
}
