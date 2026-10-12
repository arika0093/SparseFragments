using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class KeyedTransitionProjectionBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        KeyedChangeSetBetweenShape.NoOp,
        KeyedChangeSetBetweenShape.OneEdit,
        KeyedChangeSetBetweenShape.AllEdits
    )]
    public KeyedChangeSetBetweenShape Shape { get; set; }

    private KeyedChangeSetBetweenBenchmarks _input = null!;
    private BenchKeyedServerHolder.ChangeSet _changes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _input = new() { Size = Size, Shape = Shape };
        _input.Setup();
        _changes = _input.Between();
        var expected =
            Shape == KeyedChangeSetBetweenShape.NoOp ? 0
            : Shape == KeyedChangeSetBetweenShape.OneEdit ? 1
            : Size;
        if (
            RepeatedView() != expected
            || RepeatedLookup() != (expected > 0)
            || FirstView() != expected
        )
            throw new InvalidOperationException("Typed projections must preserve edited entries.");
    }

    [Benchmark]
    public int RepeatedView() => _changes.Items.Edited.Count;

    [Benchmark]
    public bool RepeatedLookup() => _changes.Items.GetChange("server-0").IsChanged;

    [Benchmark]
    public int FirstView() => _input.Between().Items.Edited.Count;
}
