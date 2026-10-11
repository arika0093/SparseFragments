namespace SparseFragments.Tests;

public sealed class TemporaryKeyEchoApplyTests
{
    [Test]
    public void AssignedEditAndItsInverseUpdateTheTemporaryAlias()
    {
        var temporary = Guid.NewGuid();
        var before = State(7, temporary, "before");
        var after = State(7, temporary, "after");
        var changes = TempOrderHolder.ChangeSet.Between(before, after);

        changes.ToPatch().Apply(before).Value!.ToModel().Lines.Single().Name.ShouldBe("after");
        changes
            .Invert()
            .ToPatch()
            .Apply(after)
            .Value!.ToModel()
            .Lines.Single()
            .Name.ShouldBe("before");
    }

    [Test]
    public void AssignedRemovalAlsoRemovesTheTemporaryAlias()
    {
        var before = State(7, Guid.NewGuid(), "before");
        var after = Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder())
        );
        var changes = TempOrderHolder.ChangeSet.Between(before, after);

        changes.ToPatch().Apply(before).Value!.ToModel().Lines.ShouldBeEmpty();
        changes
            .Invert()
            .ToPatch()
            .Apply(after)
            .Value!.ToModel()
            .Lines.Single()
            .Name.ShouldBe("before");
    }

    [Test]
    public void TemporaryEditReplayedOnAnAssignmentUpdatesBothMaps()
    {
        var temporary = Guid.NewGuid();
        var pending = State(0, temporary, "before");
        var changes = TempOrderHolder.ChangeSet.Between(pending, State(0, temporary, "after"));
        var applied = changes.ToPatch().Apply(State(7, temporary, "before")).Value!.ToModel();

        applied.Lines.Single().Id.ShouldBe(7);
        applied.Lines.Single().Name.ShouldBe("after");
    }

    [Test]
    public void TemporaryRemovalReplayedOnAnAssignmentRemovesBothMaps()
    {
        var temporary = Guid.NewGuid();
        var pending = State(0, temporary, "before");
        var after = Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder())
        );
        var changes = TempOrderHolder.ChangeSet.Between(pending, after);

        changes
            .ToPatch()
            .Apply(State(7, temporary, "before"))
            .Value!.ToModel()
            .Lines.ShouldBeEmpty();
    }

    private static Optional<TempOrderHolder.Fragment?> State(int id, Guid temporary, string name) =>
        Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(
                new TempOrderHolder
                {
                    Lines =
                    [
                        new TempOrderLine
                        {
                            Id = id,
                            TemporaryId = temporary,
                            Name = name,
                        },
                    ],
                }
            )
        );
}
