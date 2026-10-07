using BenchmarkDotNet.Attributes;
using SparseFragments;

/// <summary>Measures keyed composition when additions or edits are entirely canceled.</summary>
[MemoryDiagnoser]
public class KeyedChangeSetCancellationComposeBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Additions { get; set; }

    private BenchKeyedServerHolder.ChangeSet _first = null!;
    private BenchKeyedServerHolder.ChangeSet _second = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = State(Size, 0);
        var middle = State(Additions ? Size * 2 : Size, Additions ? 0 : 1);
        _first = BenchKeyedServerHolder.ChangeSet.Between(before, middle);
        _second = BenchKeyedServerHolder.ChangeSet.Between(middle, State(Size, 0));
        var result = Compose();
        if (
            !result.IsEmpty
            || !result.Invert().IsEmpty
            || !BenchKeyedServerHolder
                .Patch.Between(result.ToPatch().Apply(before), State(Size, 0))
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Canceled keyed changes must produce an empty composition and preserve the original state."
            );
    }

    private static Optional<BenchKeyedServerHolder.Fragment?> State(int count, int step) =>
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
                            Count = index + step,
                        })
                        .ToList(),
                }
            )
        );

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet Compose() => _first.Compose(_second);
}
