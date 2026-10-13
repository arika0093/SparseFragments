using System.Text.Json;

namespace SparseTypes.Tests;

public sealed class TemporaryKeyReorderProjectionTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public void TemporaryReordersWithEqualPermanentOrdersSurviveProjection(bool mixed, bool edited)
    {
        var first = new TempOrderLine { TemporaryId = Guid.NewGuid(), Name = "first" };
        var last = new TempOrderLine { TemporaryId = Guid.NewGuid(), Name = "last" };
        var fixedItem = new TempOrderLine { Id = 7, Name = "fixed" };
        var before = State(mixed ? [first, fixedItem, last] : [first, last]);
        var updated = new TempOrderLine
        {
            TemporaryId = first.TemporaryId,
            Name = edited ? "edited" : "first",
        };
        var after = State(mixed ? [last, fixedItem, updated] : [last, updated]);
        var changes = TempOrderHolder.ChangeSet.Between(before, after);

        changes.Lines.BeforeOrder.ShouldBe(changes.Lines.AfterOrder);
        changes.Lines.OrderChanged.ShouldBeTrue();
        var payload = JsonSerializer.Deserialize<TempOrderHolder.ChangePayload>(
            JsonSerializer.Serialize(changes.ToPayload())
        )!;
        AssertSame(changes.ToPatch().Apply(before), after);
        AssertSame(changes.Invert().ToPatch().Apply(after), before);
        AssertSame(payload.ToPatch().Apply(before), after);
        AssertSame(payload.ToChangeSet().ToPatch().Apply(before), after);
    }

    private static Optional<TempOrderHolder.Fragment?> State(List<TempOrderLine> lines) =>
        Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder { Lines = lines })
        );

    private static void AssertSame(
        Optional<TempOrderHolder.Fragment?> actual,
        Optional<TempOrderHolder.Fragment?> expected
    ) =>
        JsonSerializer
            .Serialize(actual.Value!.ToModel())
            .ShouldBe(JsonSerializer.Serialize(expected.Value!.ToModel()));
}
