using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class DictionaryTransitionProjectionBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        DictionaryChangeSetBetweenShape.NoOp,
        DictionaryChangeSetBetweenShape.OneEdit,
        DictionaryChangeSetBetweenShape.AllEdits,
        DictionaryChangeSetBetweenShape.ReplaceAll,
        DictionaryChangeSetBetweenShape.Mixed
    )]
    public DictionaryChangeSetBetweenShape Shape { get; set; }

    [Params(false, true)]
    public bool Structural { get; set; }

    private DictionaryChangeSetBetweenBenchmarks _input = null!;
    private BenchScalarDictHolder.ChangeSet _scalar = null!;
    private BenchStructuralDictHolder.ChangeSet _structural = null!;

    [GlobalSetup]
    public void Setup()
    {
        _input = new() { Size = Size, Shape = Shape };
        _input.Setup();
        _scalar = _input.ScalarBetween();
        _structural = _input.StructuralBetween();
        var expected =
            Shape == DictionaryChangeSetBetweenShape.AllEdits ? Size
            : Shape
                is DictionaryChangeSetBetweenShape.OneEdit
                    or DictionaryChangeSetBetweenShape.Mixed
                ? 1
            : 0;
        var changed = Shape != DictionaryChangeSetBetweenShape.NoOp;
        if (
            RepeatedView() != expected
            || RepeatedLookup() != changed
            || FirstView() != expected
            || FirstLookup() != changed
        )
            throw new InvalidOperationException(
                "Dictionary projections must preserve edited entries."
            );
    }

    [Benchmark]
    public int RepeatedView() =>
        Structural ? _structural.Servers.Edited.Count : _scalar.Scores.Edited.Count;

    [Benchmark]
    public bool RepeatedLookup() =>
        Structural
            ? _structural.Servers.GetChange("key-0").IsChanged
            : _scalar.Scores.GetChange("key-0").IsChanged;

    [Benchmark]
    public int FirstView() =>
        Structural
            ? _input.StructuralBetween().Servers.Edited.Count
            : _input.ScalarBetween().Scores.Edited.Count;

    [Benchmark]
    public bool FirstLookup() =>
        Structural
            ? _input.StructuralBetween().Servers.GetChange("key-0").IsChanged
            : _input.ScalarBetween().Scores.GetChange("key-0").IsChanged;
}
