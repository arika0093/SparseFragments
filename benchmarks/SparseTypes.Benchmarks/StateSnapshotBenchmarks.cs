using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchSnapshotSmall
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }

    public string Note { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchSnapshotLarge
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }

    public List<BenchFieldStateItem> Items { get; set; } = new();
}

// One shared snapshot per refresh generation: a pass reaching F fields pays
// one root snapshot/diff instead of F.
[MemoryDiagnoser]
public class StateSnapshotBenchmarks
{
    private const int LargeSize = 1000;

    [Params(1, 10, 100)]
    public int FieldCount { get; set; }

    private BenchSnapshotSmall.EditSession _small = null!;
    private BenchSnapshotLarge.EditSession _large = null!;
    private SparsePath[] _smallQueries = [];
    private SparsePath[] _largeQueries = [];

    [GlobalSetup]
    public void Setup()
    {
        var smallModel = new BenchSnapshotSmall
        {
            Title = "before",
            Count = 1,
            Note = "n",
        };
        _small = smallModel.CreateEditSession();
        _small.Observable.Title = "after";

        var largeModel = new BenchSnapshotLarge
        {
            Title = "before",
            Count = 1,
            Items = Enumerable
                .Range(0, LargeSize)
                .Select(index => new BenchFieldStateItem
                {
                    Id = "k-" + index,
                    Name = "n-" + index,
                    Count = index,
                })
                .ToList(),
        };
        _large = largeModel.CreateEditSession();
        _large.Observable.Title = "after";

        // Large collections elsewhere in the model contribute to every root
        // diff, even for a scalar field.
        var smallPaths = new[]
        {
            SparsePath.Root<BenchSnapshotSmall>().Member("Title"),
            SparsePath.Root<BenchSnapshotSmall>().Member("Count"),
            SparsePath.Root<BenchSnapshotSmall>().Member("Note"),
        };
        var largePaths = new[]
        {
            SparsePath.Root<BenchSnapshotLarge>().Member("Title"),
            SparsePath.Root<BenchSnapshotLarge>().Member("Count"),
        };
        _smallQueries = Cycle(smallPaths);
        _largeQueries = Cycle(largePaths);

        // The shared snapshot projects once no matter how many fields query it.
        var probe = _large.CaptureStateSnapshot();
        for (var index = 0; index < 100; index++)
        {
            probe.FieldState(_largeQueries[index % _largeQueries.Length]);
        }

        if (probe.ProjectionCount != 1)
        {
            throw new InvalidOperationException(
                "A shared generation snapshot must project exactly once."
            );
        }
    }

    [Benchmark(Baseline = true, Description = "F live field queries (small model)")]
    public int Live_Small()
    {
        var hits = 0;
        for (var index = 0; index < FieldCount; index++)
        {
            if (_small.FieldState(_smallQueries[index % _smallQueries.Length]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Description = "One snapshot plus F queries (small model)")]
    public int Snapshot_Small()
    {
        var snapshot = _small.CaptureStateSnapshot();
        var hits = 0;
        for (var index = 0; index < FieldCount; index++)
        {
            if (snapshot.FieldState(_smallQueries[index % _smallQueries.Length]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Description = "F live field queries (large model)")]
    public int Live_Large()
    {
        var hits = 0;
        for (var index = 0; index < FieldCount; index++)
        {
            if (_large.FieldState(_largeQueries[index % _largeQueries.Length]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    [Benchmark(Description = "One snapshot plus F queries (large model)")]
    public int Snapshot_Large()
    {
        var snapshot = _large.CaptureStateSnapshot();
        var hits = 0;
        for (var index = 0; index < FieldCount; index++)
        {
            if (snapshot.FieldState(_largeQueries[index % _largeQueries.Length]).IsChanged)
            {
                hits++;
            }
        }

        return hits;
    }

    private static SparsePath[] Cycle(SparsePath[] paths)
    {
        var cycled = new SparsePath[100];
        for (var index = 0; index < cycled.Length; index++)
        {
            cycled[index] = paths[index % paths.Length];
        }

        return cycled;
    }
}
