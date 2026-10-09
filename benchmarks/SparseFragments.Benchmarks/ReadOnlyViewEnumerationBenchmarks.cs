using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchStreamingModel
{
    public IEnumerable<string> Items { get; set; } = [];
}

/// <summary>
/// Read-only view enumeration costs over streaming vs list-backed sources (issue #172).
/// Indexed reads over a lazy source re-enumerate per access, so a full
/// indexed traversal is quadratic; list-backed sources index in O(1).
/// </summary>
[MemoryDiagnoser]
public class ReadOnlyViewEnumerationBenchmarks
{
    [Params(10, 100, 1000)]
    public int Size { get; set; }

    private BenchStreamingModel _lazy = null!;
    private BenchStreamingModel _list = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lazy = new BenchStreamingModel { Items = LazyItems(Size) };
        _list = new BenchStreamingModel { Items = LazyItems(Size).ToList() };
    }

    private static IEnumerable<string> LazyItems(int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return "item-" + i;
        }
    }

    [Benchmark(Baseline = true)]
    public int IndexedReads_List()
    {
        var view = new BenchStreamingModel.ReadOnlyView(_list);
        var sum = 0;
        for (var i = 0; i < view.Items.Count; i++)
        {
            sum += view.Items[i].Length;
        }

        return sum;
    }

    [Benchmark]
    public int IndexedReads_Lazy()
    {
        var view = new BenchStreamingModel.ReadOnlyView(_lazy);
        var sum = 0;
        for (var i = 0; i < view.Items.Count; i++)
        {
            sum += view.Items[i].Length;
        }

        return sum;
    }

    [Benchmark]
    public int Foreach_Lazy()
    {
        var view = new BenchStreamingModel.ReadOnlyView(_lazy);
        var sum = 0;
        foreach (var item in view.Items)
        {
            sum += item.Length;
        }

        return sum;
    }
}
