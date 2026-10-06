using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum SequenceUnionDensity
{
    Low,
    Medium,
    High,
}

[SparseFragmentModel]
public partial class BenchSequenceUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public List<string> Values { get; set; } = new();
}

/// <summary>
/// Measures insertion-ordered sequence SetUnion with controlled overlap
/// between the two contributions.
/// </summary>
[MemoryDiagnoser]
public class SequenceSetUnionMergeBenchmarks
{
    [Params(16, 256, 2048)]
    public int Size { get; set; }

    [Params(SequenceUnionDensity.Low, SequenceUnionDensity.Medium, SequenceUnionDensity.High)]
    public SequenceUnionDensity Density { get; set; }

    private BenchSequenceUnion.Fragment _lower = null!;
    private BenchSequenceUnion.Fragment _higher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var lower = new List<string>(Size);
        var higher = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            switch (Density)
            {
                case SequenceUnionDensity.Low:
                    lower.Add("lower-" + index);
                    higher.Add("higher-" + index);
                    break;
                case SequenceUnionDensity.Medium:
                    lower.Add("shared-" + index);
                    higher.Add(index % 2 == 0 ? "shared-" + index : "higher-" + index);
                    break;
                default:
                    lower.Add("duplicate-" + (index % 8));
                    higher.Add("duplicate-" + (index % 8));
                    break;
            }
        }

        _lower = new BenchSequenceUnion.Fragment
        {
            Values = Optional<List<string>>.Present(lower),
        };
        _higher = new BenchSequenceUnion.Fragment
        {
            Values = Optional<List<string>>.Present(higher),
        };
    }

    [Benchmark(Description = "Sequence SetUnion merge: controlled contribution overlap")]
    public BenchSequenceUnion.Fragment Merge() => _lower.Merge(_higher);
}
