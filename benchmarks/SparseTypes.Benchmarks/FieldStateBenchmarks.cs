using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchFieldStateItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseModel]
public partial class BenchFieldStateTempLine
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}

[SparseModel]
public partial class BenchFieldStateNested
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }
}

[SparseModel]
public partial class BenchFieldStateRoot
{
    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }

    public BenchFieldStateNested? Nested { get; set; }

    public Dictionary<string, int> Scores { get; set; } = new();

    public List<BenchFieldStateItem> Items { get; set; } = new();

    public List<BenchFieldStateTempLine> Lines { get; set; } = new();
}

// Hot field/row lookup budgets for the session state surface (issue #250).
// Session queries pay one baseline diff per call; view row lookups are
// dictionary probes over the captured change set. GlobalSetup validates every
// benchmark expectation so regressions fail fast instead of measuring drift.
[MemoryDiagnoser]
public class FieldStateBenchmarks
{
    private const int Size = 64;

    private BenchFieldStateRoot.EditSession _session = null!;
    private BenchFieldStateRoot.ChangeSet _changes = null!;
    private SparseCollectionState _items;
    private Guid _temp = Guid.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var model = new BenchFieldStateRoot
        {
            Title = "before",
            Count = 1,
            Nested = new BenchFieldStateNested { Host = "host", Port = 1 },
            Scores = Enumerable
                .Range(0, Size)
                .ToDictionary(index => "k-" + index, index => index),
            Items = Enumerable
                .Range(0, Size)
                .Select(index => new BenchFieldStateItem
                {
                    Id = "k-" + index,
                    Name = "n-" + index,
                    Count = index,
                })
                .ToList(),
            Lines = new List<BenchFieldStateTempLine>(),
        };
        _session = model.CreateEditSession();
        _session.Observable.Title = "after";
        _session.Observable.Nested!.Host = "edited";
        _session.Observable.Scores["k-1"] = 100;
        _session.Observable.Items[1].Name = "edited";
        _temp = Guid.NewGuid();
        _session.Observable.Lines.AddModel(
            new BenchFieldStateTempLine { Name = "pending", TemporaryId = _temp }
        );
        _changes = _session.CreateChangeSet();
        _items = _session.CollectionState(BenchFieldStateRoot.SparsePath.Items);
        if (
            !_session.FieldState(BenchFieldStateRoot.SparsePath.Title).IsChanged
            || _session.FieldState(BenchFieldStateRoot.SparsePath.Count).IsChanged
            || !_changes.FieldState(BenchFieldStateRoot.SparsePath.Title).IsChanged
            || _changes.FieldState(BenchFieldStateRoot.SparsePath.Count).IsChanged
            || !_changes
                .FieldState(BenchFieldStateRoot.SparsePath.Nested.Host)
                .IsChanged
            || !_items.IsChanged
            || !_items.GetItemState("k-1").IsEdited
            || !_items.GetTemporaryState(_temp).IsAdded
            || !_items.GetItemState("missing").IsEmpty
        )
        {
            throw new InvalidOperationException(
                "Field/row fixtures must resolve through the session state surface."
            );
        }
    }

    [Benchmark(Description = "Session field hit (scalar, changed)")]
    public bool Session_Field_Hit() =>
        _session.FieldState(BenchFieldStateRoot.SparsePath.Title).IsChanged;

    [Benchmark(Description = "Session field miss (scalar, unchanged)")]
    public bool Session_Field_Miss() =>
        _session.FieldState(BenchFieldStateRoot.SparsePath.Count).IsChanged;

    [Benchmark(Description = "ChangeSet field hit (scalar, changed)")]
    public bool ChangeSet_Field_Hit() =>
        _changes.FieldState(BenchFieldStateRoot.SparsePath.Title).IsChanged;

    [Benchmark(Description = "ChangeSet field miss (scalar, unchanged)")]
    public bool ChangeSet_Field_Miss() =>
        _changes.FieldState(BenchFieldStateRoot.SparsePath.Count).IsChanged;

    [Benchmark(Description = "ChangeSet nested hit (3 segments)")]
    public bool ChangeSet_Nested_Hit() =>
        _changes.FieldState(BenchFieldStateRoot.SparsePath.Nested.Host).IsChanged;

    [Benchmark(Description = "Session collection view (keyed, 64 rows)")]
    public bool Session_Collection_View() =>
        _session.CollectionState(BenchFieldStateRoot.SparsePath.Items).IsChanged;

    [Benchmark(Description = "Row hit (edited keyed row)")]
    public bool Collection_Row_Hit() => _items.GetItemState("k-1").IsEdited;

    [Benchmark(Description = "Row miss (unknown key)")]
    public bool Collection_Row_Miss() => _items.GetItemState("missing").IsEmpty;

    [Benchmark(Description = "Row hit (temporary identity)")]
    public bool Collection_Temp_Hit() => _items.GetTemporaryState(_temp).IsAdded;

    [Benchmark(Description = "Row hit (dictionary entry)")]
    public bool Dictionary_Row_Hit() =>
        _session
            .CollectionState(BenchFieldStateRoot.SparsePath.Scores)
            .GetItemState("k-1")
            .IsEdited;
}
