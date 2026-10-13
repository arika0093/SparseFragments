using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class HistoryBenchScalar
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }
}

// Compares session edits without history against opt-in histories (issue
// #252): the no-history path must retain nothing extra, while small and
// large histories bound retention by entry count on scalar and keyed models.
[MemoryDiagnoser]
public class EditHistoryBenchmarks
{
    private const int EditCount = 32;

    private HistoryBenchScalar _scalarPrototype = null!;
    private BenchBatchParent _keyedPrototype = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scalarPrototype = new HistoryBenchScalar { Title = "prototype", Count = 7 };
        _keyedPrototype = new BenchBatchParent
        {
            Title = "prototype",
            Items = Enumerable
                .Range(0, 64)
                .Select(index => new BenchBatchChild { Id = "id-" + index, Name = "n-" + index })
                .ToList(),
        };

        // Validates the workload before timing: edits apply, undo restores,
        // and redo reapplies on both shapes.
        var scalar = FreshScalar();
        var scalarSession = scalar.CreateEditSession();
        using (scalarSession.CreateHistory(new EditHistoryOptions { MaxEntries = 8 }))
        {
            scalarSession.Observable.Title = "edited";
            scalarSession.Observable.Count = 9;
        }

        if (scalar.Title != "edited" || scalar.Count != 9)
        {
            throw new InvalidOperationException("Scalar history setup must observe edits.");
        }

        var keyed = FreshKeyed();
        var keyedSession = keyed.CreateEditSession();
        using (var history = keyedSession.CreateHistory(new EditHistoryOptions { MaxEntries = 8 }))
        {
            keyedSession.Observable.Items[0].Name = "edited";
            history.Undo();
            if (keyed.Items[0].Name != "n-0")
            {
                throw new InvalidOperationException("Keyed history setup must undo edits.");
            }

            history.Redo();
        }

        if (keyed.Items[0].Name != "edited")
        {
            throw new InvalidOperationException("Keyed history setup must redo edits.");
        }
    }

    private HistoryBenchScalar FreshScalar() =>
        new() { Title = _scalarPrototype.Title, Count = _scalarPrototype.Count };

    private BenchBatchParent FreshKeyed() =>
        new()
        {
            Title = _keyedPrototype.Title,
            Items = _keyedPrototype
                .Items.Select(child => new BenchBatchChild { Id = child.Id, Name = child.Name })
                .ToList(),
        };

    [Benchmark(Baseline = true, Description = "32 scalar edits without history")]
    public int ScalarWithoutHistory()
    {
        var model = FreshScalar();
        var session = model.CreateEditSession();
        for (var index = 0; index < EditCount; index++)
        {
            session.Observable.Title = "edit-" + index;
        }

        return model.Title.Length + model.Count;
    }

    [Benchmark(Description = "32 scalar edits with small history (8 entries)")]
    public int ScalarWithSmallHistory()
    {
        var model = FreshScalar();
        var session = model.CreateEditSession();
        using (session.CreateHistory(new EditHistoryOptions { MaxEntries = 8 }))
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Title = "edit-" + index;
            }
        }

        return model.Title.Length + model.Count;
    }

    [Benchmark(Description = "32 scalar edits with large history (512 entries)")]
    public int ScalarWithLargeHistory()
    {
        var model = FreshScalar();
        var session = model.CreateEditSession();
        using (session.CreateHistory(new EditHistoryOptions { MaxEntries = 512 }))
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Title = "edit-" + index;
            }
        }

        return model.Title.Length + model.Count;
    }

    [Benchmark(Description = "32 keyed edits without history")]
    public int KeyedWithoutHistory()
    {
        var model = FreshKeyed();
        var session = model.CreateEditSession();
        for (var index = 0; index < EditCount; index++)
        {
            session.Observable.Items[index % 64].Name = "edit-" + index;
        }

        return model.Items.Count + model.Items[0].Name.Length;
    }

    [Benchmark(Description = "32 keyed edits with small history (8 entries)")]
    public int KeyedWithSmallHistory()
    {
        var model = FreshKeyed();
        var session = model.CreateEditSession();
        using (session.CreateHistory(new EditHistoryOptions { MaxEntries = 8 }))
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Items[index % 64].Name = "edit-" + index;
            }
        }

        return model.Items.Count + model.Items[0].Name.Length;
    }

    [Benchmark(Description = "32 keyed edits with large history (512 entries)")]
    public int KeyedWithLargeHistory()
    {
        var model = FreshKeyed();
        var session = model.CreateEditSession();
        using (session.CreateHistory(new EditHistoryOptions { MaxEntries = 512 }))
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Items[index % 64].Name = "edit-" + index;
            }
        }

        return model.Items.Count + model.Items[0].Name.Length;
    }
}
