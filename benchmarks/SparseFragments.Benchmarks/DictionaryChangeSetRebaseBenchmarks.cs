using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum DictionaryChangeSetRebaseOperation
{
    Add,
    Remove,
    Edit,
}

public enum DictionaryChangeSetRebaseState
{
    Replay,
    AlreadyApplied,
    Conflict,
}

[MemoryDiagnoser]
public class DictionaryChangeSetRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetRebaseOperation Operation { get; set; }

    [ParamsAllValues]
    public DictionaryChangeSetRebaseState State { get; set; }

    private BenchScalarDictHolder.ChangeSet _change = null!;
    private Optional<BenchScalarDictHolder.Fragment?> _current;

    [GlobalSetup]
    public void Setup()
    {
        var before = Fragment(0);
        var after = Fragment(1);
        var currentStep = State switch
        {
            DictionaryChangeSetRebaseState.AlreadyApplied => 1,
            DictionaryChangeSetRebaseState.Conflict => 2,
            _ => 0,
        };
        _current = Fragment(currentStep);
        var original = _current.Value!.Scores.Value!.ToArray();
        _change = BenchScalarDictHolder.ChangeSet.Between(before, after);
        var result = Rebase();
        var conflict = State == DictionaryChangeSetRebaseState.Conflict;
        var replay = State == DictionaryChangeSetRebaseState.Replay;
        var expected = replay ? after : _current;
        var applied = result.Patch.ToPatch().Apply(_current);
        if (
            _change.IsEmpty
            || result.HasConflicts != conflict
            || result.Conflicts.Count != (conflict ? Size : 0)
            || result.Patch.IsEmpty == replay
            || !BenchScalarDictHolder.Patch.Between(applied, expected).IsEmpty
            || !BenchScalarDictHolder
                .Patch.Between(result.Patch.Invert().ToPatch().Apply(applied), _current)
                .IsEmpty
            || !_current.Value.Scores.Value!.SequenceEqual(original)
            || !BenchScalarDictHolder.Patch.Between(before, Fragment(0)).IsEmpty
            || !BenchScalarDictHolder.Patch.Between(after, Fragment(1)).IsEmpty
        )
        {
            throw new InvalidOperationException(
                "Dictionary rebase must preserve replay, conflicts, inversion, and source maps."
            );
        }
        if (conflict)
        {
            var expectedPaths = Enumerable
                .Range(0, Size)
                .Select(index => "Scores.key-" + index)
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedPaths.SetEquals(result.Conflicts.Select(item => item.PathText)))
            {
                throw new InvalidOperationException("Every conflicting key must retain its path.");
            }
        }
    }

    private Optional<BenchScalarDictHolder.Fragment?> Fragment(int step)
    {
        var empty =
            (Operation == DictionaryChangeSetRebaseOperation.Add && step == 0)
            || (Operation == DictionaryChangeSetRebaseOperation.Remove && step == 1);
        var values = new Dictionary<string, int>(empty ? 0 : Size, StringComparer.Ordinal);
        if (!empty)
        {
            for (var index = 0; index < Size; index++)
            {
                values.Add("key-" + index, index + step);
            }
        }
        return Optional<BenchScalarDictHolder.Fragment?>.Present(
            new() { Scores = Optional<Dictionary<string, int>>.Present(values) }
        );
    }

    [Benchmark]
    public RebaseResult<BenchScalarDictHolder.ChangeSet> Rebase() => _change.RebaseOnto(_current);
}
