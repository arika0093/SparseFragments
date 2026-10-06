using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchListAppend
{
    [SparseMerge(MergeMode.Append)]
    public List<string> Values { get; set; } = [];
}

[SparseFragmentModel]
public partial class BenchArrayAppend
{
    [SparseMerge(MergeMode.Append)]
    public string[] Values { get; set; } = [];
}

/// <summary>Measures Append across concrete lists, arrays, and read-only sequence members.</summary>
[MemoryDiagnoser]
public class SequenceAppendMergeBenchmarks
{
    [Params(0, 16, 256, 2048)]
    public int Size { get; set; }

    private BenchListAppend.Fragment _listLower = null!;
    private BenchListAppend.Fragment _listHigher = null!;
    private BenchArrayAppend.Fragment _arrayLower = null!;
    private BenchArrayAppend.Fragment _arrayHigher = null!;
    private BenchAppendRecord.Fragment _readOnlyLower = null!;
    private BenchAppendRecord.Fragment _readOnlyHigher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var lower = Enumerable.Range(0, Size).Select(index => "item-" + index).ToList();
        List<string> higher = Size == 0 ? [] : ["item-0", "item-0", "extra-a", "extra-b"];
        _listLower = new BenchListAppend.Fragment
        {
            Values = Optional<List<string>>.Present(lower),
        };
        _listHigher = new BenchListAppend.Fragment
        {
            Values = Optional<List<string>>.Present(higher),
        };
        _arrayLower = new BenchArrayAppend.Fragment
        {
            Values = Optional<string[]>.Present(lower.ToArray()),
        };
        _arrayHigher = new BenchArrayAppend.Fragment
        {
            Values = Optional<string[]>.Present(higher.ToArray()),
        };
        _readOnlyLower = new BenchAppendRecord.Fragment
        {
            Items = Optional<IReadOnlyList<string>>.Present(lower),
        };
        _readOnlyHigher = new BenchAppendRecord.Fragment
        {
            Items = Optional<IReadOnlyList<string>>.Present(higher),
        };
        var expected = lower.Concat(higher).ToArray();
        if (
            !MergeList().Values.Value!.SequenceEqual(expected)
            || !MergeArray().Values.Value!.SequenceEqual(expected)
            || !MergeReadOnlyList().Items.Value!.SequenceEqual(expected)
            || lower.Count != Size
            || higher.Count != (Size == 0 ? 0 : 4)
        )
        {
            throw new InvalidOperationException(
                "Append must preserve order, duplicates, and both inputs."
            );
        }
    }

    [Benchmark]
    public BenchListAppend.Fragment MergeList() => _listLower.Merge(_listHigher);

    [Benchmark]
    public BenchArrayAppend.Fragment MergeArray() => _arrayLower.Merge(_arrayHigher);

    [Benchmark]
    public BenchAppendRecord.Fragment MergeReadOnlyList() => _readOnlyLower.Merge(_readOnlyHigher);
}
