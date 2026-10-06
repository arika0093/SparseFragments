using System.Collections;
using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

public readonly struct BenchCustomScalar : IEquatable<BenchCustomScalar>
{
    private readonly int _value;

    public BenchCustomScalar(int value) => _value = value;

    public bool Equals(BenchCustomScalar other) => _value % 10 == other._value % 10;

    public override bool Equals(object? other) =>
        other is BenchCustomScalar scalar && _value == scalar._value;

    public override int GetHashCode() => _value % 10;
}

public readonly struct BenchEnumerableScalar : IEnumerable<int>
{
    private readonly int _value;

    public BenchEnumerableScalar(int value) => _value = value;

    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)new[] { _value }).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Measures typed scalar equality while retaining custom and sequence semantics.</summary>
[MemoryDiagnoser]
public class ScalarEqualityBenchmarks
{
    [Params(false, true)]
    public bool Equal { get; set; }

    private int _integer;
    private string _string = null!;
    private int? _nullable;
    private double _double;
    private decimal _decimal;
    private MergeMode _enum;
    private DateTime _date;
    private Guid _guid;
    private BenchCustomScalar _custom;
    private BenchEnumerableScalar _enumerable;

    [GlobalSetup]
    public void Setup()
    {
        _integer = Equal ? 17 : 18;
        _string = Equal ? "value" : "different";
        _nullable = _integer;
        _double = Equal ? double.NaN : 0;
        _decimal = Equal ? 10.00m : 11m;
        _enum = Equal ? MergeMode.Append : MergeMode.SetUnion;
        _date = new DateTime(2024, 1, Equal ? 1 : 2);
        _guid = Equal
            ? System.Guid.Empty
            : System.Guid.Parse("00000000-0000-0000-0000-000000000001");
        _custom = new BenchCustomScalar(Equal ? 1 : 11);
        _enumerable = new BenchEnumerableScalar(Equal ? 1 : 2);
        if (
            Int32() != Equal
            || String() != Equal
            || NullableInt32() != Equal
            || DoubleNaN() != Equal
            || Decimal() != Equal
            || Enum() != Equal
            || DateTime() != Equal
            || Guid() != Equal
            || CustomStruct() != Equal
            || EnumerableStruct() != Equal
            || !SparseFragmentRuntime.AreEqual<int?>(null, null)
            || SparseFragmentRuntime.AreEqual<int?>(null, 17)
        )
        {
            throw new InvalidOperationException(
                "Scalar equality must preserve NaN, nullable, custom object equality, and sequence semantics."
            );
        }
    }

    [Benchmark]
    public bool Int32() => SparseFragmentRuntime.AreEqual(17, _integer);

    [Benchmark]
    public bool String() => SparseFragmentRuntime.AreEqual("value", _string);

    [Benchmark]
    public bool NullableInt32() => SparseFragmentRuntime.AreEqual<int?>(17, _nullable);

    [Benchmark]
    public bool DoubleNaN() => SparseFragmentRuntime.AreEqual(double.NaN, _double);

    [Benchmark]
    public bool Decimal() => SparseFragmentRuntime.AreEqual(10.0m, _decimal);

    [Benchmark]
    public bool Enum() => SparseFragmentRuntime.AreEqual(MergeMode.Append, _enum);

    [Benchmark]
    public bool DateTime() => SparseFragmentRuntime.AreEqual(new DateTime(2024, 1, 1), _date);

    [Benchmark]
    public bool Guid() => SparseFragmentRuntime.AreEqual(System.Guid.Empty, _guid);

    [Benchmark]
    public bool CustomStruct() => SparseFragmentRuntime.AreEqual(new BenchCustomScalar(1), _custom);

    [Benchmark]
    public bool EnumerableStruct() =>
        SparseFragmentRuntime.AreEqual(new BenchEnumerableScalar(1), _enumerable);
}
