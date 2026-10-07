using BenchmarkDotNet.Attributes;
using SparseFragments;

[MemoryDiagnoser]
public class KeyedChangeSetComposeBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool ManyAdds { get; set; }

    private BenchKeyedServerHolder.ChangeSet _first = null!;
    private BenchKeyedServerHolder.ChangeSet _second = null!;

    [GlobalSetup]
    public void Setup()
    {
        var added = ManyAdds ? Size : 1;
        var before = State(Size);
        var middle = State(Size + added);
        var after = State(Size + added * 2);
        _first = BenchKeyedServerHolder.ChangeSet.Between(before, middle);
        _second = BenchKeyedServerHolder.ChangeSet.Between(State(Size + added), after);
        var composed = Compose();
        if (!BenchKeyedServerHolder.Patch.Between(composed.ToPatch().Apply(before), after).IsEmpty)
            throw new InvalidOperationException(
                "Composed additions must retain every item and its order."
            );
        if (
            !BenchKeyedServerHolder
                .Patch.Between(composed.Invert().ToPatch().Apply(after), before)
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Inverting composed additions must restore the baseline."
            );
    }

    private static Optional<BenchKeyedServerHolder.Fragment?> State(int count) =>
        Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(
                new()
                {
                    Items = Enumerable
                        .Range(0, count)
                        .Select(index => new BenchKeyedServer
                        {
                            Id = "srv-" + index,
                            Name = "server-" + index,
                            Count = index,
                        })
                        .ToList(),
                }
            )
        );

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet Compose() => _first.Compose(_second);
}
