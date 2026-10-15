namespace SparseTypes.Tests;

[SparseModel]
public partial class EnumerableTemporaryBaselineModel
{
    public IEnumerable<TempOrderLine> Lines { get; set; } = [];
}

[SparseModel]
public partial class EnumerableTemporaryParent
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public IEnumerable<TempOrderLine> Children { get; set; } = [];
}

[SparseModel]
public partial class EnumerableTemporaryParentBaselineModel
{
    public IEnumerable<EnumerableTemporaryParent> Parents { get; set; } = [];
}

public sealed class TemporaryEnumerableReconcileTests
{
    [Test]
    public void TemporaryCorrelationIndexesPersistedEnumerableOnceForManyRows()
    {
        var identities = Enumerable
            .Range(1, 1_000)
            .Select(id => (Id: id, TemporaryId: Guid.NewGuid()))
            .ToArray();
        var submitted = new EnumerableTemporaryBaselineModel
        {
            Lines = identities.Select(static item => new TempOrderLine
            {
                TemporaryId = item.TemporaryId,
            }),
        };
        var persisted = new EnumerableTemporaryBaselineModel
        {
            Lines = new SinglePassEnumerable<TempOrderLine>(
                identities
                    .Reverse()
                    .Select(static item => new TempOrderLine
                    {
                        Id = item.Id,
                        TemporaryId = item.TemporaryId,
                    })
            ),
        };

        var (map, error) = EnumerableTemporaryBaselineModel.__SparseBuildTempMap(
            submitted,
            persisted
        );

        error.ShouldBeNull();
        map.ShouldNotBeNull();
        map.Count.ShouldBe(identities.Length);
        foreach (var item in identities)
        {
            ((TempOrderLine)map[("Lines", item.TemporaryId)]!).Id.ShouldBe(item.Id);
        }
    }

    [Test]
    public void AssignedParentCorrelationIndexesEachPersistedCollectionOnce()
    {
        var parents = Enumerable
            .Range(1, 100)
            .Select(id => (Id: id, TemporaryId: Guid.NewGuid()))
            .ToArray();
        var submitted = new EnumerableTemporaryParentBaselineModel
        {
            Parents = parents.Select(static parent => new EnumerableTemporaryParent
            {
                Id = parent.Id,
                Children = [new TempOrderLine { TemporaryId = parent.TemporaryId }],
            }),
        };
        var persisted = new EnumerableTemporaryParentBaselineModel
        {
            Parents = new SinglePassEnumerable<EnumerableTemporaryParent>(
                parents
                    .Reverse()
                    .Select(static parent => new EnumerableTemporaryParent
                    {
                        Id = parent.Id,
                        Children = new SinglePassEnumerable<TempOrderLine>([
                            new TempOrderLine
                            {
                                Id = parent.Id + 1,
                                TemporaryId = parent.TemporaryId,
                            },
                        ]),
                    })
            ),
        };

        var (map, error) = EnumerableTemporaryParentBaselineModel.__SparseBuildTempMap(
            submitted,
            persisted
        );

        error.ShouldBeNull();
        map.ShouldNotBeNull();
        map.Count.ShouldBe(parents.Length);
        foreach (var parent in parents)
        {
            (
                (TempOrderLine)map[($"Parents[{parent.Id}].Children", parent.TemporaryId)]!
            ).Id.ShouldBe(parent.Id + 1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void EnumerableReconcileRetainsLocalAdditionAfterItsPredecessor(bool listBacked)
    {
        var model = new EnumerableTemporaryBaselineModel
        {
            Lines = Storage([new() { Id = 7, Name = "existing" }], listBacked),
        };
        var session = model.CreateEditSession();
        var pending = new TempOrderLine { TemporaryId = Guid.NewGuid(), Name = "submitted" };
        model.Lines = Storage([.. model.Lines, pending], listBacked);
        var submitted = session.CreateChangeSet();
        var local = new TempOrderLine { TemporaryId = Guid.NewGuid(), Name = "local" };
        pending.Name = "edited";
        model.Lines = Storage([.. model.Lines, local], listBacked);
        var persisted = new EnumerableTemporaryBaselineModel
        {
            Lines = Storage(
                [
                    new() { Id = 7, Name = "server existing" },
                    new()
                    {
                        Id = 8,
                        TemporaryId = pending.TemporaryId,
                        Name = "server submitted",
                    },
                    new() { Id = 9, Name = "server added" },
                ],
                listBacked
            ),
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        model.Lines.Select(item => item.Id).ShouldBe([7, 8, 0, 9]);
        model
            .Lines.Select(item => item.Name)
            .ShouldBe(["server existing", "edited", "local", "server added"]);
        model.Lines.ElementAt(2).TemporaryId.ShouldBe(local.TemporaryId);
        session.HasChanges.ShouldBeTrue();
    }

    private static IEnumerable<TempOrderLine> Storage(TempOrderLine[] items, bool listBacked) =>
        listBacked ? new List<TempOrderLine>(items) : items.Where(static _ => true);

    private sealed class SinglePassEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _items;
        private bool _enumerated;

        public SinglePassEnumerable(IEnumerable<T> items)
        {
            _items = items;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (_enumerated)
            {
                throw new InvalidOperationException("The enumerable may only be enumerated once.");
            }

            _enumerated = true;
            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
