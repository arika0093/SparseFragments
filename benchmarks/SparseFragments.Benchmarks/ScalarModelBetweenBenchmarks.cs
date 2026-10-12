using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial struct BenchScalarBetweenValue
{
    public int Count { get; set; }
}

[MemoryDiagnoser]
public class ScalarModelBetweenBenchmarks
{
    [Params(false, true)]
    public bool Edited { get; set; }

    private BenchKeyedServer _before = null!;
    private BenchKeyedServer _after = null!;
    private BenchScalarBetweenValue _valueBefore;
    private BenchScalarBetweenValue _valueAfter;

    [GlobalSetup]
    public void Setup()
    {
        _before = new()
        {
            Id = "server",
            Name = "name",
            Count = 1,
        };
        _after = new()
        {
            Id = "server",
            Name = "name",
            Count = Edited ? 2 : 1,
        };
        _valueBefore = new() { Count = 1 };
        _valueAfter = new() { Count = Edited ? 2 : 1 };
        var change = Between();
        if (change.IsEmpty == Edited)
            throw new InvalidOperationException("Model comparison must preserve scalar changes.");
        var result = change.ToPatch().ApplyTo(_before);
        if (result.Count != _after.Count || result.Id != _after.Id || result.Name != _after.Name)
            throw new InvalidOperationException("Model changes must preserve the after state.");
        if (ValueBetween().ToPatch().ApplyTo(_valueBefore).Count != _valueAfter.Count)
            throw new InvalidOperationException(
                "Value model changes must preserve the after state."
            );
    }

    [Benchmark]
    public BenchKeyedServer.ChangeSet Between() =>
        BenchKeyedServer.ChangeSet.Between(_before, _after);

    [Benchmark]
    public BenchScalarBetweenValue.ChangeSet ValueBetween() =>
        BenchScalarBetweenValue.ChangeSet.Between(_valueBefore, _valueAfter);
}
