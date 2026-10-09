using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchBatchChild
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class BenchBatchParent
{
    public string Title { get; set; } = string.Empty;

    public List<BenchBatchChild> Items { get; set; } = new();
}

// Quantifies BatchEdit snapshot/diff deferral (issue #133): N observable edits
// inside one batch must cost a single net diff instead of N full comparisons.
[MemoryDiagnoser]
public class EditSessionBatchBenchmarks
{
    [Params(256, 2048)]
    public int Size { get; set; }

    private const int EditCount = 64;

    private BenchBatchParent _prototype = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prototype = new BenchBatchParent
        {
            Title = "prototype",
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new BenchBatchChild { Id = "id-" + index, Name = "n-" + index })
                .ToList(),
        };
    }

    private BenchBatchParent Fresh() =>
        new()
        {
            Title = _prototype.Title,
            Items = _prototype
                .Items.Select(child => new BenchBatchChild { Id = child.Id, Name = child.Name })
                .ToList(),
        };

    [Benchmark(Baseline = true, Description = "64 nested edits without batching")]
    public int IndividualEdits()
    {
        var model = Fresh();
        var session = model.CreateEditSession();
        for (var index = 0; index < EditCount; index++)
        {
            session.Observable.Items[index % Size].Name = "edit-" + index;
        }

        return model.Items.Count + model.Title.Length;
    }

    [Benchmark(Description = "64 nested edits in one BatchEdit")]
    public int BatchedEdits()
    {
        var model = Fresh();
        var session = model.CreateEditSession();
        session.BatchEdit(() =>
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Items[index % Size].Name = "edit-" + index;
            }
        });

        return model.Items.Count + model.Title.Length;
    }
}
