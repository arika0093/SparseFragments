using System.Collections.ObjectModel;
using System.Collections.Specialized;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class ObservableMutationBenchmarks
{
    [Params(false, true)]
    public bool CacheReferences { get; set; }

    [Params(ObservableBacking.List, ObservableBacking.ObservableCollection)]
    public ObservableBacking Backing { get; set; }

    private Func<ObservableBenchChild, Action, ObservableBenchChildView> _wrap = null!;
    private int _wrapCalls;

    [GlobalSetup]
    public void Setup() => _wrap = Wrap;

    [Benchmark]
    public int Add()
    {
        var view = CreateView(CreateBacking([NewChild("existing")]));
        _wrapCalls = 0;
        view.AddModel(NewChild("added"));
        return _wrapCalls;
    }

    [Benchmark]
    public int Replace()
    {
        var view = CreateView(CreateBacking([NewChild("existing")]));
        _ = view[0];
        _wrapCalls = 0;
        view.SetModel(0, NewChild("replacement"));
        return _wrapCalls;
    }

    [Benchmark]
    public int Remove()
    {
        var view = CreateView(CreateBacking([NewChild("existing")]));
        _ = view[0];
        _wrapCalls = 0;
        view.RemoveAt(0);
        return _wrapCalls;
    }

    [Benchmark]
    public int Move()
    {
        var view = CreateView(CreateBacking([NewChild("first"), NewChild("second")]));
        _ = view[0];
        _ = view[1];
        _wrapCalls = 0;
        view.Move(0, 1);
        return _wrapCalls;
    }

    [GlobalCleanup]
    public void ReportWrapCalls() =>
        Console.WriteLine($"Wrap calls in final invocation: {_wrapCalls}");

    private IList<ObservableBenchChild> CreateBacking(IEnumerable<ObservableBenchChild> values) =>
        Backing == ObservableBacking.ObservableCollection
            ? new ObservableCollection<ObservableBenchChild>(values)
            : values.ToList();

    private SparseObservableList<ObservableBenchChild, ObservableBenchChildView> CreateView(
        IList<ObservableBenchChild> models
    ) => new(models, _wrap, static view => view.Target, static () => { }, CacheReferences);

    private ObservableBenchChildView Wrap(ObservableBenchChild child, Action changed)
    {
        _wrapCalls++;
        return new ObservableBenchChildView(child, changed);
    }

    private static ObservableBenchChild NewChild(string id) => new() { Id = id };
}

public enum ObservableBacking
{
    List,
    ObservableCollection,
}

[MemoryDiagnoser]
public class ObservableRangeConversionBenchmarks
{
    [Params(false, true)]
    public bool CacheReferences { get; set; }

    private Func<ObservableBenchChild, Action, ObservableBenchChildView> _wrap = null!;
    private int _wrapCalls;

    [GlobalSetup]
    public void Setup() => _wrap = Wrap;

    [Benchmark]
    public int RangeConversion()
    {
        var initial = new RangeNotifyingList([
            NewChild("first"),
            NewChild("second"),
            NewChild("third"),
        ]);
        var view = new SparseObservableList<ObservableBenchChild, ObservableBenchChildView>(
            initial,
            _wrap,
            static item => item.Target,
            static () => { },
            CacheReferences
        );
        _ = view[0];
        _ = view[1];
        _ = view[2];
        _wrapCalls = 0;
        initial.AddRange([NewChild("range-1"), NewChild("range-2"), NewChild("range-3")]);
        return _wrapCalls;
    }

    [GlobalCleanup]
    public void ReportWrapCalls() =>
        Console.WriteLine($"Wrap calls in final invocation: {_wrapCalls}");

    private ObservableBenchChildView Wrap(ObservableBenchChild child, Action changed)
    {
        _wrapCalls++;
        return new ObservableBenchChildView(child, changed);
    }

    private static ObservableBenchChild NewChild(string id) => new() { Id = id };

    private sealed class RangeNotifyingList(IEnumerable<ObservableBenchChild> items)
        : List<ObservableBenchChild>(items),
            INotifyCollectionChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public void AddRange(IList<ObservableBenchChild> items)
        {
            var startIndex = Count;
            base.AddRange(items);
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    (System.Collections.IList)items,
                    startIndex
                )
            );
        }
    }
}
