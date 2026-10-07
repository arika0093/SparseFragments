using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchChangeSetRebaseRecord
{
    public string Label { get; set; } = "";
    public int Counter { get; set; }
    public List<int> Values { get; set; } = [];
}

public enum ChangeSetRebaseShape
{
    Empty,
    AlreadyApplied,
    Replay,
    Conflict,
}

[MemoryDiagnoser]
public class ChangeSetRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        ChangeSetRebaseShape.Empty,
        ChangeSetRebaseShape.AlreadyApplied,
        ChangeSetRebaseShape.Replay,
        ChangeSetRebaseShape.Conflict
    )]
    public ChangeSetRebaseShape Shape { get; set; }

    private BenchChangeSetRebaseRecord.ChangeSet _change = null!;
    private Optional<BenchChangeSetRebaseRecord.Fragment?> _current;

    [GlobalSetup]
    public void Setup()
    {
        var baseline = State(0, "base");
        var desired = Shape == ChangeSetRebaseShape.Empty ? baseline : State(1, "base");
        var currentCounter = Shape switch
        {
            ChangeSetRebaseShape.AlreadyApplied => 1,
            ChangeSetRebaseShape.Conflict => 2,
            _ => 0,
        };
        _current = State(currentCounter, "remote");
        _change = BenchChangeSetRebaseRecord.ChangeSet.Between(baseline, desired);
        var result = Rebase();
        var empty = Shape is ChangeSetRebaseShape.Empty or ChangeSetRebaseShape.AlreadyApplied;
        var conflict = Shape == ChangeSetRebaseShape.Conflict;
        var actual = result.Patch.ToPatch().Apply(_current);
        var expectedCounter = Shape == ChangeSetRebaseShape.Replay ? 1 : currentCounter;
        if (
            result.HasConflicts != conflict
            || result.Conflicts.Count != (conflict ? 1 : 0)
            || result.Patch.IsEmpty != (empty || conflict)
            || !actual.IsPresent
            || actual.Value is null
            || actual.Value.Counter.Value != expectedCounter
            || actual.Value.Label.Value != "remote"
            || actual.Value.Values.GetValueOrDefault()?.SequenceEqual(Enumerable.Range(0, Size))
                != true
            || _current.Value!.Counter.Value != currentCounter
            || baseline.Value!.Counter.Value != 0
            || (conflict && result.Conflicts[0].PathText != "Counter")
        )
        {
            throw new InvalidOperationException(
                "ChangeSet rebase must preserve replay, conflict and current-state semantics."
            );
        }
        if (empty)
        {
            if (!result.Patch.IsEmpty || !result.Patch.Invert().IsEmpty)
            {
                throw new InvalidOperationException(
                    "Empty rebased change sets must remain empty on inspection and inversion."
                );
            }
            var repeated = result.Patch.RebaseOnto(State(3, "newer"));
            if (repeated.HasConflicts || !repeated.Patch.IsEmpty)
            {
                throw new InvalidOperationException(
                    "Empty rebased change sets must remain empty on later states."
                );
            }
        }
        var emptyChange = BenchChangeSetRebaseRecord.ChangeSet.Between(
            Optional<BenchChangeSetRebaseRecord.Fragment?>.Missing,
            Optional<BenchChangeSetRebaseRecord.Fragment?>.Missing
        );
        foreach (
            var state in new[]
            {
                Optional<BenchChangeSetRebaseRecord.Fragment?>.Missing,
                Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(null),
            }
        )
        {
            var rebased = emptyChange.RebaseOnto(state);
            var applied = rebased.Patch.ToPatch().Apply(state);
            if (
                rebased.HasConflicts
                || !rebased.Patch.IsEmpty
                || applied.IsPresent != state.IsPresent
                || applied.GetValueOrDefault() is not null
            )
            {
                throw new InvalidOperationException(
                    "Empty change sets must preserve absent and null root states."
                );
            }
        }
    }

    private Optional<BenchChangeSetRebaseRecord.Fragment?> State(int counter, string label) =>
        Optional<BenchChangeSetRebaseRecord.Fragment?>.Present(
            BenchChangeSetRebaseRecord.Fragment.From(
                new()
                {
                    Counter = counter,
                    Label = label,
                    Values = Enumerable.Range(0, Size).ToList(),
                }
            )
        );

    [Benchmark]
    public RebaseResult<BenchChangeSetRebaseRecord.ChangeSet> Rebase() =>
        _change.RebaseOnto(_current);
}
