using BenchmarkDotNet.Attributes;
using SparseFragments;

// Value-type keyed elements exercise boxing during per-key equality.
[SparseFragmentModel]
public partial struct BenchValueKeyedItem
{
    [SparseKey]
    public int Id { get; set; }
    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class BenchValueKeyedHolder
{
    public List<BenchValueKeyedItem> Items { get; set; } = [];
}

public enum ValueKeyedRebaseScenario
{
    Clean,
    AlreadyApplied,
    Conflict,
}

/// <summary>Measures keyed Rebase for value-type elements with generated patches.</summary>
[MemoryDiagnoser]
public class ValueKeyedRebaseBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(1, 64)]
    public int Touched { get; set; }

    [Params(
        ValueKeyedRebaseScenario.Clean,
        ValueKeyedRebaseScenario.AlreadyApplied,
        ValueKeyedRebaseScenario.Conflict
    )]
    public ValueKeyedRebaseScenario Scenario { get; set; }

    private Optional<BenchValueKeyedHolder.Fragment?> _before;
    private Optional<BenchValueKeyedHolder.Fragment?> _current;
    private BenchValueKeyedHolder.Patch _local = null!;

    [GlobalSetup]
    public void Setup()
    {
        var touched = Math.Min(Touched, Size);
        var before = Enumerable
            .Range(0, Size)
            .Select(index => new BenchValueKeyedItem { Id = index, Count = index })
            .ToList();
        var desired = before
            .Select((item, index) => index < touched ? item with { Count = -1 } : item)
            .ToList();
        var current = before
            .Select(
                (item, index) =>
                    index < touched && Scenario != ValueKeyedRebaseScenario.Clean
                        ? item with
                        {
                            Count = Scenario == ValueKeyedRebaseScenario.Conflict ? -2 : -1,
                        }
                        : item
            )
            .ToList();
        if (Scenario == ValueKeyedRebaseScenario.Clean)
        {
            current.Add(new BenchValueKeyedItem { Id = Size, Count = 99 });
        }

        _before = State(before);
        _current = State(current);
        _local = BenchValueKeyedHolder.Patch.Between(_before, State(desired));
        var result = Rebase();
        var actual = result.Patch.Apply(_current).Value!.Items.Value!;
        var conflict = Scenario == ValueKeyedRebaseScenario.Conflict;
        if (
            result.HasConflicts != conflict
            || result.Conflicts.Count != (conflict ? touched : 0)
            || actual.Count != current.Count
            || (
                Scenario == ValueKeyedRebaseScenario.Clean
                && (actual[^1].Id != Size || actual[^1].Count != 99)
            )
        )
        {
            throw new InvalidOperationException(
                "Value keyed Rebase must preserve additions and report touched conflicts."
            );
        }

        for (var index = 0; index < Size; index++)
        {
            var expected = index < touched ? (conflict ? -2 : -1) : index;
            if (
                actual[index].Id != index
                || actual[index].Count != expected
                || _before.Value!.Items.Value![index].Count != index
                || _current.Value!.Items.Value![index].Count != current[index].Count
            )
            {
                throw new InvalidOperationException(
                    "Value keyed Rebase must preserve values, order, and source states."
                );
            }
        }
    }

    private static Optional<BenchValueKeyedHolder.Fragment?> State(
        List<BenchValueKeyedItem> values
    ) =>
        Optional<BenchValueKeyedHolder.Fragment?>.Present(
            new BenchValueKeyedHolder.Fragment
            {
                Items = Optional<List<BenchValueKeyedItem>>.Present(values),
            }
        );

    [Benchmark]
    public RebaseResult<BenchValueKeyedHolder.Patch> Rebase() =>
        BenchValueKeyedHolder.Patch.Rebase(_before, _local, _current);
}
