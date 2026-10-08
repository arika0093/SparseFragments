using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;
using SparseFragments;

#pragma warning disable CA1001 // Benchmark harness owns view lifetimes; disposal is part of what is measured.

// Reproducible proxy/prune profiles for issue #114: cold acquisition, repeated
// enumeration, random indexed reads, and batched mutations with all element
// proxies populated (the PruneProxies worst case). Sizes spread 8x to expose
// quadratic scaling as complexity changes rather than single-point timings.
[MemoryDiagnoser]
public class ObservableProxyBenchmarks
{
    [Params(256, 2048)]
    public int Size { get; set; }

    private List<ObservableBenchChild> _listModels = null!;
    private List<string> _listScalars = null!;
    private Dictionary<string, ObservableBenchChild> _dictModels = null!;
    private ObservableCollection<ObservableBenchChild> _liveModels = null!;
    private int[] _strides = null!;
    private int _noop;

    private SparseObservableList<ObservableBenchChild, ObservableBenchChildView> _cachedList =
        null!;
    private SparseObservableList<string, string> _scalarList = null!;
    private SparseObservableDictionary<
        string,
        ObservableBenchChild,
        ObservableBenchChildView
    > _cachedDict = null!;
    private SparseObservableList<ObservableBenchChild, ObservableBenchChildView> _liveList = null!;

    [GlobalSetup]
    public void Setup()
    {
        _listModels = Enumerable
            .Range(0, Size)
            .Select(index => new ObservableBenchChild { Id = "id-" + index, Name = "n-" + index })
            .ToList();
        _listScalars = Enumerable.Range(0, Size).Select(index => "item-" + index).ToList();
        _dictModels = _listModels.ToDictionary(child => child.Id);
        _liveModels = new ObservableCollection<ObservableBenchChild>(_listModels.ToList());
        _strides = Enumerable
            .Range(0, 256)
            .Select(step => (step * 7919) % Math.Max(Size, 1))
            .ToArray();

        _cachedList = CreateCachedList(new List<ObservableBenchChild>(_listModels));
        _scalarList = new SparseObservableList<string, string>(
            new List<string>(_listScalars),
            static (item, _) => item,
            static item => item,
            () => _noop++
        );
        _cachedDict = CreateCachedDict(new Dictionary<string, ObservableBenchChild>(_dictModels));
        _liveList = CreateCachedList(_liveModels);
        Warm(_cachedList, _scalarList, _cachedDict, _liveList);
    }

    [Benchmark(Description = "List cold acquisition: index every element once")]
    public int ListColdAcquisition()
    {
        var view = CreateCachedList(new List<ObservableBenchChild>(_listModels));
        var count = 0;
        for (var index = 0; index < view.Count; index++)
        {
            count += view[index].Name.Length;
        }

        return count;
    }

    [Benchmark(Description = "List repeated enum with all proxies cached")]
    public int ListRepeatedEnum()
    {
        var count = 0;
        foreach (var item in _cachedList)
        {
            count += item.Name.Length;
        }

        return count;
    }

    [Benchmark(Description = "List random indexed reads with all proxies cached")]
    public int ListRandomIndex()
    {
        var count = 0;
        foreach (var index in _strides)
        {
            count += _cachedList[index].Name.Length;
        }

        return count;
    }

    [Benchmark(Description = "List batched appends with all proxies cached")]
    public int ListBatchedAppend()
    {
        var view = CreateCachedList(new List<ObservableBenchChild>(_listModels));
        Warm(view);
        for (var step = 0; step < 64; step++)
        {
            view.AddModel(new ObservableBenchChild { Id = "new-" + step });
        }

        return view.Count;
    }

    [Benchmark(Description = "List batched removals with all proxies cached")]
    public int ListBatchedRemove()
    {
        var models = new List<ObservableBenchChild>(_listModels);
        for (var step = 0; step < 64; step++)
        {
            models.Add(new ObservableBenchChild { Id = "extra-" + step });
        }

        var view = CreateCachedList(models);
        Warm(view);
        for (var step = 0; step < 64; step++)
        {
            view.RemoveAt(view.Count - 1);
        }

        return view.Count;
    }

    [Benchmark(Description = "List reorder (move) with all proxies cached")]
    public int ListReorder()
    {
        var view = CreateCachedList(new List<ObservableBenchChild>(_listModels));
        Warm(view);
        for (var step = 0; step < 64; step++)
        {
            view.Move(view.Count - 1, 0);
        }

        return view[0].Name.Length;
    }

    [Benchmark(Description = "Dictionary repeated enum with all proxies cached")]
    public int DictRepeatedEnum()
    {
        var count = 0;
        foreach (var pair in _cachedDict)
        {
            count += pair.Value.Name.Length;
        }

        return count;
    }

    [Benchmark(Description = "Dictionary batched removals with all proxies cached")]
    public int DictBatchedRemove()
    {
        var models = new Dictionary<string, ObservableBenchChild>(_dictModels);
        for (var step = 0; step < 64; step++)
        {
            var key = "extra-" + step;
            models[key] = new ObservableBenchChild { Id = key };
        }

        var view = CreateCachedDict(models);
        Warm(view);
        for (var step = 0; step < 64; step++)
        {
            view.Remove("extra-" + step);
        }

        return view.Count;
    }

    [Benchmark(Description = "Scalar list repeated enum (no proxy cache)")]
    public int ScalarRepeatedEnum()
    {
        var count = 0;
        foreach (var item in _scalarList)
        {
            count += item.Length;
        }

        return count;
    }

    [Benchmark(Description = "Notifying list batched appends via view")]
    public int NotifyingBatchedAppend()
    {
        var fresh = new ObservableCollection<ObservableBenchChild>(_listModels.ToList());
        var view = CreateCachedList(fresh);
        Warm(view);
        for (var step = 0; step < 64; step++)
        {
            view.AddModel(new ObservableBenchChild { Id = "live-" + step });
        }

        return view.Count;
    }

    private SparseObservableList<ObservableBenchChild, ObservableBenchChildView> CreateCachedList(
        IList<ObservableBenchChild> models
    ) =>
        new(
            models,
            static (item, changed) =>
                item is null ? null! : new ObservableBenchChildView(item, changed),
            static view => view.Target,
            () => _noop++,
            cacheReferences: true
        );

    private SparseObservableDictionary<
        string,
        ObservableBenchChild,
        ObservableBenchChildView
    > CreateCachedDict(IDictionary<string, ObservableBenchChild> models) =>
        new(
            models,
            static (item, changed) =>
                item is null ? null! : new ObservableBenchChildView(item, changed),
            static view => view.Target,
            () => _noop++,
            cacheReferences: true
        );

    private static void Warm(
        SparseObservableList<ObservableBenchChild, ObservableBenchChildView> list,
        SparseObservableList<string, string> scalars,
        SparseObservableDictionary<string, ObservableBenchChild, ObservableBenchChildView> dict,
        SparseObservableList<ObservableBenchChild, ObservableBenchChildView> live
    )
    {
        Warm(list);
        foreach (var _ in scalars) { }

        foreach (var _ in dict) { }

        Warm(live);
    }

    private static void Warm(
        SparseObservableList<ObservableBenchChild, ObservableBenchChildView> view
    )
    {
        for (var index = 0; index < view.Count; index++)
        {
            _ = view[index];
        }
    }

    private static void Warm(
        SparseObservableDictionary<string, ObservableBenchChild, ObservableBenchChildView> view
    )
    {
        foreach (var _ in view) { }
    }
}

public sealed class ObservableBenchChild
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class ObservableBenchChildView
{
    private readonly Action _changed;

    public ObservableBenchChildView(ObservableBenchChild target, Action changed)
    {
        Target = target;
        _changed = changed;
    }

    public ObservableBenchChild Target { get; }

    public string Name
    {
        get => Target.Name;
        set
        {
            Target.Name = value;
            _changed();
        }
    }
}
