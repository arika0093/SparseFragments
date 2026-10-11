using System.Collections;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class EnumerableAssignedBaselineModel
{
    public IEnumerable<AssignedServer> Items { get; set; } = [];
}

public sealed class KeyedBetweenBaselineValidationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void TemporaryBaselineIsConsumedOnce(bool assigned)
    {
        var identity = Guid.NewGuid();
        var before = new SinglePassItems<TempOrderLine>([
            new()
            {
                Id = assigned ? 7 : 0,
                TemporaryId = identity,
                Name = "before",
            },
        ]);
        var after = new SinglePassItems<TempOrderLine>([
            new()
            {
                Id = assigned ? 7 : 0,
                TemporaryId = identity,
                Name = "after",
            },
        ]);
        var changes = EnumerableTemporaryBaselineModel.ChangeSet.Between(
            Optional<EnumerableTemporaryBaselineModel.Fragment?>.Present(
                new() { Lines = Optional<IEnumerable<TempOrderLine>>.Present(before) }
            ),
            Optional<EnumerableTemporaryBaselineModel.Fragment?>.Present(
                new() { Lines = Optional<IEnumerable<TempOrderLine>>.Present(after) }
            )
        );

        before.Enumerations.ShouldBe(1);
        after.Enumerations.ShouldBe(1);
        var edit = assigned
            ? changes.Lines.GetChange(7).Edit
            : changes.Lines.GetTemporaryChange(identity).Edit;
        edit.Name.Before.Value.ShouldBe("before");
        edit.Name.After.Value.ShouldBe("after");
    }

    [Test]
    public void SentinelConfiguredBaselineIsConsumedOnce()
    {
        var before = new SinglePassItems<AssignedServer>([new() { Id = 7, Name = "before" }]);
        var after = new SinglePassItems<AssignedServer>([new() { Id = 7, Name = "after" }]);
        var changes = EnumerableAssignedBaselineModel.ChangeSet.Between(
            Optional<EnumerableAssignedBaselineModel.Fragment?>.Present(
                new() { Items = Optional<IEnumerable<AssignedServer>>.Present(before) }
            ),
            Optional<EnumerableAssignedBaselineModel.Fragment?>.Present(
                new() { Items = Optional<IEnumerable<AssignedServer>>.Present(after) }
            )
        );

        before.Enumerations.ShouldBe(1);
        after.Enumerations.ShouldBe(1);
        changes.Items.GetChange(7).Edit.Name.Before.Value.ShouldBe("before");
        changes.Items.GetChange(7).Edit.Name.After.Value.ShouldBe("after");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void WholeTransitionsStillRejectUnassignedBaselines(bool nullAfter)
    {
        var before = Optional<AssignedServerHolder.Fragment?>.Present(
            new() { Items = Optional<List<AssignedServer>>.Present([new() { Id = 0 }]) }
        );
        var after = Optional<AssignedServerHolder.Fragment?>.Present(
            new() { Items = nullAfter ? Optional<List<AssignedServer>>.Present(null!) : default }
        );

        Should
            .Throw<InvalidOperationException>(() =>
                AssignedServerHolder.ChangeSet.Between(before, after)
            )
            .Message.ShouldBe(
                "An unassigned key cannot appear in the baseline of a keyed collection transition."
            );
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public void WholeTransitionsStillValidateTemporaryBaselines(int invalidKind, bool nullAfter)
    {
        Guid? identity = invalidKind switch
        {
            0 => null,
            1 => Guid.Empty,
            _ => Guid.NewGuid(),
        };
        List<TempOrderLine> lines = [new() { TemporaryId = identity }];
        if (invalidKind == 2)
            lines.Add(new() { TemporaryId = identity });
        var before = Optional<TempOrderHolder.Fragment?>.Present(
            new() { Lines = Optional<List<TempOrderLine>>.Present(lines) }
        );
        var after = Optional<TempOrderHolder.Fragment?>.Present(
            new() { Lines = nullAfter ? Optional<List<TempOrderLine>>.Present(null!) : default }
        );

        var expected = invalidKind switch
        {
            0 => "An unassigned keyed element is missing its temporary identity.",
            1 => "An unassigned keyed element has a reserved temporary identity.",
            _ => "Duplicate temporary key in keyed collection.",
        };
        Should
            .Throw<InvalidOperationException>(() =>
                TempOrderHolder.ChangeSet.Between(before, after)
            )
            .Message.ShouldBe(expected);
    }

    private sealed class SinglePassItems<T>(T[] items) : IEnumerable<T>
    {
        public int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            if (Enumerations != 1)
                throw new InvalidOperationException("Input was enumerated more than once.");
            return ((IEnumerable<T>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
