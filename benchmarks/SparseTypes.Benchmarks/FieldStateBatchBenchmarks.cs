using BenchmarkDotNet.Attributes;
using SparseTypes;

// Repeated field-query batches over one captured change set: one projection
// plus path-depth lookups versus one projection and scan per field.
[MemoryDiagnoser]
public class FieldStateBatchBenchmarks
{
    private const int ModelSize = 1000;

    [Params(0, 1, 100, 1000)]
    public int ChangeCount { get; set; }

    [Params(1, 10, 100, 1000)]
    public int QueryCount { get; set; }

    private readonly Dictionary<int, BenchFieldStateRoot.ChangeSet> _changes = new();
    private readonly Dictionary<int, SparseChangeSetQueryView> _views = new();
    private SparsePath[] _queries = [];

    [GlobalSetup]
    public void Setup()
    {
        var paths = new SparsePath[ModelSize];
        for (var index = 0; index < ModelSize; index++)
        {
            paths[index] = SparsePath
                .Root<BenchFieldStateRoot>()
                .Member("Items")
                .Key("k-" + index)
                .Member("Name");
        }

        _queries = paths;

        foreach (var changeCount in new[] { 0, 1, 100, 1000 })
        {
            var session = FreshModel().CreateEditSession();
            for (var index = 0; index < changeCount; index++)
            {
                session.Observable.Items[index].Name = "edited-" + index;
            }

            var changes = session.CreateChangeSet();
            var view = changes.CaptureQueryView();
            view.EnsureIndexed();
            for (var index = 0; index < ModelSize; index++)
            {
                view.FieldState(paths[index]);
            }

            if (view.ProjectionCount != 1)
            {
                throw new InvalidOperationException(
                    "A reused view must project its change set exactly once."
                );
            }

            _changes.Add(changeCount, changes);
            _views.Add(changeCount, view);
        }
    }

    [Benchmark(Baseline = true, Description = "Batch scan (one projection per field)")]
    public int Batch_Scan()
    {
        var changes = _changes[ChangeCount];
        var hits = 0;
        for (var index = 0; index < QueryCount; index++)
        {
            if (changes.FieldState(_queries[index]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Description = "Batch indexed (one shared projection)")]
    public int Batch_Indexed()
    {
        var view = _views[ChangeCount];
        var hits = 0;
        for (var index = 0; index < QueryCount; index++)
        {
            if (view.FieldState(_queries[index]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Description = "Index construction (capture plus ensure)")]
    public bool Index_Construction()
    {
        var session = FreshModel().CreateEditSession();
        for (var index = 0; index < ChangeCount; index++)
        {
            session.Observable.Items[index].Name = "edited-" + index;
        }

        var view = session.CreateChangeSet().CaptureQueryView();
        view.EnsureIndexed();
        return view.ProjectionCount == 1;
    }

    private static BenchFieldStateRoot FreshModel() =>
        new()
        {
            Title = "before",
            Count = 1,
            Nested = new BenchFieldStateNested { Host = "host", Port = 1 },
            Scores = Enumerable
                .Range(0, ModelSize)
                .ToDictionary(index => "k-" + index, index => index),
            Items = Enumerable
                .Range(0, ModelSize)
                .Select(index => new BenchFieldStateItem
                {
                    Id = "k-" + index,
                    Name = "n-" + index,
                    Count = index,
                })
                .ToList(),
            Lines = new List<BenchFieldStateTempLine>(),
        };
}
