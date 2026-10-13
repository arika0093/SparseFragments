using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class ChangeSetEnumerationComparisonBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    private readonly ChangeSetEnumerationBenchmarks _inner = new();

    [GlobalSetup]
    public void Setup()
    {
        _inner.Size = Size;
        _inner.Shape = ChangeSetEnumerationShape.AllEdits;
        _inner.Setup();
    }

    [Benchmark]
    public int Scalar() => _inner.ScalarEnumerate();

    [Benchmark]
    public int Structural() => _inner.StructuralEnumerate();
}
