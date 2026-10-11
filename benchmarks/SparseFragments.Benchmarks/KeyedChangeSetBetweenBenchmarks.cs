using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum KeyedChangeSetBetweenShape
{
    NoOp,
    OneEdit,
    AllEdits,
}

[MemoryDiagnoser]
public class KeyedChangeSetBetweenBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public KeyedChangeSetBetweenShape Shape { get; set; }

    private Optional<BenchKeyedServerHolder.Fragment?> _before;
    private Optional<BenchKeyedServerHolder.Fragment?> _after;

    [GlobalSetup]
    public void Setup()
    {
        _before = State(false);
        _after = State(true);
        var changes = Between();
        if (
            changes.IsEmpty != (Shape == KeyedChangeSetBetweenShape.NoOp)
            || !BenchKeyedServerHolder
                .Patch.Between(changes.ToPatch().Apply(_before), _after)
                .IsEmpty
            || !BenchKeyedServerHolder
                .Patch.Between(changes.Invert().ToPatch().Apply(_after), _before)
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Keyed differences must preserve endpoints and invert correctly."
            );
    }

    private Optional<BenchKeyedServerHolder.Fragment?> State(bool after) =>
        Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(
                new()
                {
                    Items = Enumerable
                        .Range(0, Size)
                        .Select(index => new BenchKeyedServer
                        {
                            Id = "server-" + index,
                            Name = "name-" + index,
                            Count = index + (IsEdited(index, after) ? 1 : 0),
                        })
                        .ToList(),
                }
            )
        );

    private bool IsEdited(int index, bool after) =>
        after
        && (
            Shape == KeyedChangeSetBetweenShape.AllEdits
            || (Shape == KeyedChangeSetBetweenShape.OneEdit && index == 0)
        );

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet Between() =>
        BenchKeyedServerHolder.ChangeSet.Between(_before, _after);
}
