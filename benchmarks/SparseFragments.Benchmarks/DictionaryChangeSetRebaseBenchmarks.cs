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
        ValidateEqualitySemantics();
    }

    private void ValidateEqualitySemantics()
    {
        var before = EqualityFragment(0);
        var after = EqualityFragment(1);
        var change = BenchEqualityDictHolder.ChangeSet.Between(before, after);
        var current = EqualityFragment(0);
        var replay = change.RebaseOnto(current);
        var applied = replay.Patch.ToPatch().Apply(current);
        var alreadyApplied = change.RebaseOnto(EqualityFragment(1));
        if (
            change.IsEmpty
            || replay.HasConflicts
            || replay.Patch.IsEmpty
            || !BenchEqualityDictHolder.Patch.Between(applied, after).IsEmpty
            || !BenchEqualityDictHolder
                .Patch.Between(replay.Patch.Invert().ToPatch().Apply(applied), before)
                .IsEmpty
            || alreadyApplied.HasConflicts
            || !alreadyApplied.Patch.IsEmpty
        )
        {
            throw new InvalidOperationException(
                "Dictionary rebase must retain nullable, NaN, enum, custom object, and sequence equality."
            );
        }

        // This struct's typed equality treats 1 and 11 as equal; object equality does not.
        var customBefore = CustomFragment(0, 1);
        var customAfter = CustomFragment(1, 2);
        var customCurrent = CustomFragment(
            2,
            Operation == DictionaryChangeSetRebaseOperation.Add ? 12 : 11
        );
        var customChange = BenchEqualityDictHolder.ChangeSet.Between(customBefore, customAfter);
        var customResult = customChange.RebaseOnto(customCurrent);
        if (
            !customResult.HasConflicts
            || customResult.Conflicts.Count != 1
            || customResult.Conflicts[0].PathText != "CustomValues.key"
            || !customResult.Patch.IsEmpty
        )
        {
            throw new InvalidOperationException("Custom struct rebase must use object equality.");
        }
    }

    private Optional<BenchEqualityDictHolder.Fragment?> EqualityFragment(int step)
    {
        var empty = IsEmptyStep(step);
        return Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new()
                {
                    NullableValues = empty ? new() : new() { ["key"] = step == 0 ? null : 1 },
                    Text = empty ? new() : new() { ["key"] = step == 0 ? null : "next" },
                    Numbers = empty ? new() : new() { ["key"] = step == 0 ? double.NaN : 1d },
                    Modes = empty
                        ? new()
                        : new() { ["key"] = step == 0 ? MergeMode.Append : MergeMode.SetUnion },
                    CustomValues = empty
                        ? new()
                        : new() { ["key"] = new BenchCustomScalar(step + 1) },
                    Sequences = empty
                        ? new()
                        : new() { ["key"] = new BenchEnumerableScalar(step + 1) },
                }
            )
        );
    }

    private Optional<BenchEqualityDictHolder.Fragment?> CustomFragment(int step, int value) =>
        Optional<BenchEqualityDictHolder.Fragment?>.Present(
            BenchEqualityDictHolder.Fragment.From(
                new()
                {
                    CustomValues = IsEmptyStep(step)
                        ? new()
                        : new() { ["key"] = new BenchCustomScalar(value) },
                }
            )
        );

    private bool IsEmptyStep(int step) =>
        (Operation == DictionaryChangeSetRebaseOperation.Add && step == 0)
        || (Operation == DictionaryChangeSetRebaseOperation.Remove && step == 1);

    private Optional<BenchScalarDictHolder.Fragment?> Fragment(int step)
    {
        var empty = IsEmptyStep(step);
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
