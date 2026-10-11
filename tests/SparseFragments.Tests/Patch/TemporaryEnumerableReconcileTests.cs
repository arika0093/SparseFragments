namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class EnumerableTemporaryBaselineModel
{
    public IEnumerable<TempOrderLine> Lines { get; set; } = [];
}

public sealed class TemporaryEnumerableReconcileTests
{
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
}
