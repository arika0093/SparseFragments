using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum KeyedComposeEditShape
{
    SparseDisjoint,
    DenseDisjoint,
    DenseOverlap,
}

[MemoryDiagnoser]
public class KeyedChangeSetEditComposeBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        KeyedComposeEditShape.SparseDisjoint,
        KeyedComposeEditShape.DenseDisjoint,
        KeyedComposeEditShape.DenseOverlap
    )]
    public KeyedComposeEditShape Shape { get; set; }

    private BenchKeyedServerHolder.ChangeSet _first = null!;
    private BenchKeyedServerHolder.ChangeSet _second = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = State(0);
        var middle = State(1);
        var after = State(2);
        _first = BenchKeyedServerHolder.ChangeSet.Between(before, middle);
        _second = BenchKeyedServerHolder.ChangeSet.Between(State(1), after);
        var combined = Compose();
        if (
            !BenchKeyedServerHolder.Patch.Between(combined.ToPatch().Apply(before), after).IsEmpty
            || !BenchKeyedServerHolder
                .Patch.Between(combined.Invert().ToPatch().Apply(after), before)
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Disjoint and overlapping edited items must compose and invert correctly."
            );
    }

    private Optional<BenchKeyedServerHolder.Fragment?> State(int step) =>
        Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(
                new()
                {
                    Items = Enumerable
                        .Range(0, Size)
                        .Select(index => new BenchKeyedServer
                        {
                            Id = "srv-" + index,
                            Name = "server-" + index,
                            Count = index + Increment(index, step),
                        })
                        .ToList(),
                }
            )
        );

    private int Increment(int index, int step)
    {
        if (step == 0)
            return 0;
        if (Shape == KeyedComposeEditShape.SparseDisjoint)
            return index == 0 || (step == 2 && index == Size - 1) ? 1 : 0;
        if (Shape == KeyedComposeEditShape.DenseOverlap)
            return index < Size / 2 ? step : 0;
        return index < Size / 2 || step == 2 ? 1 : 0;
    }

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet Compose() => _first.Compose(_second);
}
