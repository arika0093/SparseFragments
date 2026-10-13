using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class KeyedEmptyChangeSetBetweenBenchmarks
{
    private Optional<BenchKeyedServerHolder.Fragment?> _before;
    private Optional<BenchKeyedServerHolder.Fragment?> _after;

    [GlobalSetup]
    public void Setup()
    {
        _before = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new())
        );
        _after = Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new())
        );
        if (!Between().IsEmpty)
            throw new InvalidOperationException(
                "Two empty keyed collections must have no changes."
            );
    }

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet Between() =>
        BenchKeyedServerHolder.ChangeSet.Between(_before, _after);
}
