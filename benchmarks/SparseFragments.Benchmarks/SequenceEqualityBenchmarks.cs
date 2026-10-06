using System.Collections;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchIntArray
{
    public int[] Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchIntList
{
    public List<int> Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchIntReadOnlyList
{
    public IReadOnlyList<int> Values { get; set; } = [];
}

public enum SequenceEqualityScenario
{
    Equal,
    FirstChange,
    LastChange,
}

// A derived list can supply its own non-generic view; its existing comparison semantics must survive.
public class BenchReversedIntList : List<int>, IList
{
    public BenchReversedIntList(IEnumerable<int> values)
        : base(values) { }

    object? IList.this[int index]
    {
        get => this[Count - 1 - index];
        set => this[Count - 1 - index] = (int)value!;
    }
}

/// <summary>Measures sequence equality without hiding per-element value-type boxing.</summary>
[MemoryDiagnoser]
public class SequenceEqualityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        SequenceEqualityScenario.Equal,
        SequenceEqualityScenario.FirstChange,
        SequenceEqualityScenario.LastChange
    )]
    public SequenceEqualityScenario Scenario { get; set; }

    private BenchIntArray.Fragment _arrayLeft = null!;
    private BenchIntArray.Fragment _arrayRight = null!;
    private BenchIntList.Fragment _listLeft = null!;
    private BenchIntList.Fragment _listRight = null!;
    private BenchIntReadOnlyList.Fragment _mixedLeft = null!;
    private BenchIntReadOnlyList.Fragment _mixedRight = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Enumerable.Range(0, Size).ToArray();
        var after = (int[])before.Clone();
        if (Scenario != SequenceEqualityScenario.Equal)
        {
            after[Scenario == SequenceEqualityScenario.FirstChange ? 0 : Size - 1] = -1;
        }

        _arrayLeft = new BenchIntArray.Fragment { Values = Optional<int[]>.Present(before) };
        _arrayRight = new BenchIntArray.Fragment { Values = Optional<int[]>.Present(after) };
        _listLeft = new BenchIntList.Fragment
        {
            Values = Optional<List<int>>.Present(before.ToList()),
        };
        _listRight = new BenchIntList.Fragment
        {
            Values = Optional<List<int>>.Present(after.ToList()),
        };
        _mixedLeft = new BenchIntReadOnlyList.Fragment
        {
            Values = Optional<IReadOnlyList<int>>.Present(before),
        };
        _mixedRight = new BenchIntReadOnlyList.Fragment
        {
            Values = Optional<IReadOnlyList<int>>.Present(after.ToList()),
        };
        var expected = Scenario == SequenceEqualityScenario.Equal;
        var derived = new BenchIntReadOnlyList.Fragment
        {
            Values = Optional<IReadOnlyList<int>>.Present(new BenchReversedIntList(before)),
        };
        if (
            Array() != expected
            || List() != expected
            || Mixed() != expected
            || BenchIntReadOnlyList.Fragment.__SparseAreEqual(
                Optional<BenchIntReadOnlyList.Fragment?>.Present(_mixedLeft),
                Optional<BenchIntReadOnlyList.Fragment?>.Present(derived)
            )
        )
        {
            throw new InvalidOperationException(
                "Sequence equality must preserve order and derived collection comparison semantics."
            );
        }
    }

    [Benchmark]
    public bool Array() =>
        BenchIntArray.Fragment.__SparseAreEqual(
            Optional<BenchIntArray.Fragment?>.Present(_arrayLeft),
            Optional<BenchIntArray.Fragment?>.Present(_arrayRight)
        );

    [Benchmark]
    public bool List() =>
        BenchIntList.Fragment.__SparseAreEqual(
            Optional<BenchIntList.Fragment?>.Present(_listLeft),
            Optional<BenchIntList.Fragment?>.Present(_listRight)
        );

    [Benchmark]
    public bool Mixed() =>
        BenchIntReadOnlyList.Fragment.__SparseAreEqual(
            Optional<BenchIntReadOnlyList.Fragment?>.Present(_mixedLeft),
            Optional<BenchIntReadOnlyList.Fragment?>.Present(_mixedRight)
        );
}
