using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum SequenceUnionDensity
{
    Low,
    Medium,
    High,
    SingleValue,
}

[SparseFragmentModel]
public partial class BenchSequenceUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public List<string> Values { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchArrayUnion
{
    [SparseMerge(MergeMode.SetUnion)]
    public string[] Values { get; set; } = [];
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

    [Params(
        SequenceUnionDensity.Low,
        SequenceUnionDensity.Medium,
        SequenceUnionDensity.High,
        SequenceUnionDensity.SingleValue
    )]
    public SequenceUnionDensity Density { get; set; }

    private BenchSequenceUnion.Fragment _lower = null!;
    private BenchSequenceUnion.Fragment _higher = null!;
    private BenchArrayUnion.Fragment _arrayLower = null!;
    private BenchArrayUnion.Fragment _arrayHigher = null!;

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
                case SequenceUnionDensity.High:
                    lower.Add("duplicate-" + (index % 8));
                    higher.Add("duplicate-" + (index % 8));
                    break;
                default:
                    lower.Add("duplicate");
                    higher.Add("duplicate");
                    break;
            }
        }

        _lower = new BenchSequenceUnion.Fragment { Values = Optional<List<string>>.Present(lower) };
        _higher = new BenchSequenceUnion.Fragment
        {
            Values = Optional<List<string>>.Present(higher),
        };
        _arrayLower = new BenchArrayUnion.Fragment
        {
            Values = Optional<string[]>.Present(lower.ToArray()),
        };
        _arrayHigher = new BenchArrayUnion.Fragment
        {
            Values = Optional<string[]>.Present(higher.ToArray()),
        };
        var expected = lower.Concat(higher).Distinct();
        var actual = _lower.Merge(_higher).Values.Value;
        var arrayActual = _arrayLower.Merge(_arrayHigher).Values.Value;
        if (
            actual is null
            || !actual.SequenceEqual(expected)
            || arrayActual is null
            || !arrayActual.SequenceEqual(expected)
        )
        {
            throw new InvalidOperationException(
                "Sequence SetUnion must preserve the first occurrence of each value."
            );
        }
    }

    [Benchmark(Description = "Sequence SetUnion merge: controlled contribution overlap")]
    public BenchSequenceUnion.Fragment Merge() => _lower.Merge(_higher);

    [Benchmark(Description = "Array SetUnion merge: controlled contribution overlap")]
    public BenchArrayUnion.Fragment MergeArray() => _arrayLower.Merge(_arrayHigher);
}
