using System.Globalization;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class ReconcileBenchmarkItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}

[SparseModel]
public partial class ReconcileBenchmarkParent
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public List<ReconcileBenchmarkItem> Children { get; set; } = [];
}

[SparseModel]
public partial class TemporaryKeyReconcileBenchmarkRoot
{
    public List<ReconcileBenchmarkItem> Items { get; set; } = [];

    public List<ReconcileBenchmarkParent> Parents { get; set; } = [];
}

[MemoryDiagnoser]
public class TemporaryKeyReconcileBenchmarks
{
    [Params(1, 10, 100, 1_000, 10_000)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool ReverseResponseOrder { get; set; }

    private TemporaryKeyReconcileBenchmarkRoot _temporaryLive = null!;
    private TemporaryKeyReconcileBenchmarkRoot.ChangeSet _temporarySubmitted = null!;
    private TemporaryKeyReconcileBenchmarkRoot _temporaryPersisted = null!;
    private TemporaryKeyReconcileBenchmarkRoot.EditSession _temporarySession = null!;

    private TemporaryKeyReconcileBenchmarkRoot _parentsLive = null!;
    private TemporaryKeyReconcileBenchmarkRoot.ChangeSet _parentsSubmitted = null!;
    private TemporaryKeyReconcileBenchmarkRoot _parentsPersisted = null!;
    private TemporaryKeyReconcileBenchmarkRoot.EditSession _parentsSession = null!;

    [IterationSetup(Target = nameof(TemporaryRows))]
    public void SetupTemporaryRows()
    {
        var identities = Enumerable
            .Range(1, Size)
            .Select(static id => (Id: id, TemporaryId: MakeTemporaryId(id)))
            .ToArray();
        _temporaryLive = new TemporaryKeyReconcileBenchmarkRoot();
        _temporarySession = _temporaryLive.CreateEditSession();
        _temporaryLive.Items = identities
            .Select(static item => new ReconcileBenchmarkItem { TemporaryId = item.TemporaryId })
            .ToList();
        _temporarySubmitted = _temporarySession.CreateChangeSet();
        var response = identities.Select(static item => new ReconcileBenchmarkItem
        {
            Id = item.Id,
            TemporaryId = item.TemporaryId,
        });
        _temporaryPersisted = new TemporaryKeyReconcileBenchmarkRoot
        {
            Items = OrderResponse(response).ToList(),
        };
    }

    [IterationSetup(Target = nameof(TemporaryChildrenUnderAssignedParents))]
    public void SetupTemporaryChildrenUnderAssignedParents()
    {
        var identities = Enumerable
            .Range(1, Size)
            .Select(static id => (Id: id, TemporaryId: MakeTemporaryId(id)))
            .ToArray();
        _parentsLive = new TemporaryKeyReconcileBenchmarkRoot
        {
            Parents = identities
                .Select(static item => new ReconcileBenchmarkParent { Id = item.Id })
                .ToList(),
        };
        _parentsSession = _parentsLive.CreateEditSession();
        for (var index = 0; index < identities.Length; index++)
        {
            _parentsLive
                .Parents[index]
                .Children.Add(
                    new ReconcileBenchmarkItem { TemporaryId = identities[index].TemporaryId }
                );
        }
        _parentsSubmitted = _parentsSession.CreateChangeSet();
        var size = identities.Length;
        var response = identities.Select(item => new ReconcileBenchmarkParent
        {
            Id = item.Id,
            Children =
            [
                new ReconcileBenchmarkItem { Id = item.Id + size, TemporaryId = item.TemporaryId },
            ],
        });
        _parentsPersisted = new TemporaryKeyReconcileBenchmarkRoot
        {
            Parents = OrderResponse(response).ToList(),
        };
    }

    [Benchmark]
    public bool TemporaryRows() =>
        _temporarySession.TryReconcile(_temporarySubmitted, _temporaryPersisted, out _);

    [Benchmark]
    public bool TemporaryChildrenUnderAssignedParents() =>
        _parentsSession.TryReconcile(_parentsSubmitted, _parentsPersisted, out _);

    private IEnumerable<T> OrderResponse<T>(IEnumerable<T> response) =>
        ReverseResponseOrder ? response.Reverse() : response;

    private static Guid MakeTemporaryId(int id) =>
        Guid.ParseExact(id.ToString("X32", CultureInfo.InvariantCulture), "N");
}
