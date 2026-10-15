using System.ComponentModel;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchScopeItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchScopeRoot
{
    public string Title { get; set; } = string.Empty;

    public List<BenchScopeItem> Items { get; set; } = new();
}

// Session-level refresh budgets for the Blazor session scope.
[MemoryDiagnoser]
public class SessionScopeRefreshBenchmarks
{
    private const int Size = 64;
    private const int EditCount = 8;

    [GlobalSetup]
    public void Setup()
    {
        var model = Fresh();
        var session = model.CreateEditSession();
        var notifications = Subscribe(session);
        try
        {
            session.Observable.Title = "probe";
            if (notifications.Count != 1)
            {
                throw new InvalidOperationException(
                    "A single observable edit must raise one session notification."
                );
            }

            notifications.Count = 0;
            session.BatchEdit(() =>
            {
                session.Observable.Title = "batched";
                session.Observable.Items[1].Name = "batched";
                session.Observable.Items[2].Name = "batched";
            });
            if (notifications.Count != 1)
            {
                throw new InvalidOperationException(
                    "One outer batch must raise one session notification."
                );
            }

            session.RevertChanges();
            if (session.HasChanges)
            {
                throw new InvalidOperationException("The fixture must revert clean.");
            }
        }
        finally
        {
            notifications.Dispose();
        }
    }

    [Benchmark(Baseline = true, Description = "8 individual edits (8 scope refreshes)")]
    public int IndividualEdits()
    {
        var session = Fresh().CreateEditSession();
        using var notifications = Subscribe(session);
        for (var index = 0; index < EditCount; index++)
        {
            session.Observable.Items[index].Name = "edit-" + index;
        }

        return notifications.Count;
    }

    [Benchmark(Description = "8 edits in one BatchEdit (1 scope refresh)")]
    public int BatchedEdits()
    {
        var session = Fresh().CreateEditSession();
        using var notifications = Subscribe(session);
        session.BatchEdit(() =>
        {
            for (var index = 0; index < EditCount; index++)
            {
                session.Observable.Items[index].Name = "edit-" + index;
            }
        });

        return notifications.Count;
    }

    private static BenchScopeRoot Fresh() =>
        new()
        {
            Title = "before",
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new BenchScopeItem { Id = "id-" + index, Name = "n-" + index })
                .ToList(),
        };

    private sealed class Counter : IDisposable
    {
        private readonly INotifyPropertyChanged _source;
        private readonly PropertyChangedEventHandler _handler;

        public Counter(INotifyPropertyChanged source)
        {
            _source = source;
            _handler = (_, _) => Count++;
            _source.PropertyChanged += _handler;
        }

        public int Count { get; set; }

        public void Dispose() => _source.PropertyChanged -= _handler;
    }

    private static Counter Subscribe(BenchScopeRoot.EditSession session) => new(session);
}
