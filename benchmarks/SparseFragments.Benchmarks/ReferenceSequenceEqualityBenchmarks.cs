using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

/// <summary>Compares typed and object sequence traversal for reference elements.</summary>
[MemoryDiagnoser]
public class ReferenceSequenceEqualityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    private string[] _arrayLeft = null!;
    private string[] _arrayRight = null!;
    private List<string> _listLeft = null!;
    private List<string> _listRight = null!;

    [GlobalSetup]
    public void Setup()
    {
        _arrayLeft = Enumerable.Range(0, Size).Select(value => value.ToString()).ToArray();
        _arrayRight = _arrayLeft.Select(value => new string(value.ToCharArray())).ToArray();
        _listLeft = _arrayLeft.ToList();
        _listRight = _arrayRight.ToList();
        if (!ObjectArray() || !TypedArray() || !ObjectList() || !TypedList())
        {
            throw new InvalidOperationException("Reference sequences must compare by value.");
        }

        CheckEquivalent<int?>([null, 1], [null, 1]);
        CheckEquivalent<int?>([null, 1], [1, null]);
        CheckEquivalent<double>([double.NaN, -0.0], [double.NaN, 0.0]);
        CheckEquivalent<BenchCustomScalar>([new(1)], [new(11)]);
        CheckEquivalent<BenchEnumerableScalar>([new(1)], [new(1)]);
        CheckEquivalent<int[]>(
            [
                [1, 2],
            ],
            [
                [1, 2],
            ]
        );
        CheckEquivalent<int>([1], [1, 2]);
        CheckEquivalent<int>(null, []);
        CheckEquivalent<int>(null, null);
    }

    private static void CheckEquivalent<T>(T[]? left, T[]? right)
    {
        if (
            SparseFragmentRuntime.AreSequenceEqual(left, right)
            != SparseFragmentRuntime.AreEqual((object?)left, (object?)right)
        )
        {
            throw new InvalidOperationException(
                "Typed sequences must retain object comparison semantics."
            );
        }
    }

    [Benchmark]
    public bool ObjectArray() => SparseFragmentRuntime.AreEqual(_arrayLeft, _arrayRight);

    [Benchmark]
    public bool TypedArray() => SparseFragmentRuntime.AreSequenceEqual(_arrayLeft, _arrayRight);

    [Benchmark]
    public bool ObjectList() => SparseFragmentRuntime.AreEqual(_listLeft, _listRight);

    [Benchmark]
    public bool TypedList() => SparseFragmentRuntime.AreSequenceEqual(_listLeft, _listRight);
}
