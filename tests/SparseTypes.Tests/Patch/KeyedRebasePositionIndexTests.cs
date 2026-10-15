namespace SparseTypes.Tests;

/// <summary>Keyed ChangeSet rebase position indexes and temporary order assembly.</summary>
/// <remarks>
/// Guards the indexed position lookups and hash-based temp order assembly:
/// bulk replay must preserve BeforeIndex/AfterIndex, order, conflicts and
/// duplicate validation without per-row linear scans.
/// </remarks>
public sealed class KeyedRebasePositionIndexTests
{
    private static TempOrderLine Assigned(int id, string? name = null) =>
        new() { Id = id, Name = name ?? "item-" + id };

    private static TempOrderLine Temporary(Guid temp, string? name = null) =>
        new() { Name = name ?? "temp-" + temp.ToString("N")[..8], TemporaryId = temp };

    private static Optional<TempOrderHolder.Fragment?> TState(params TempOrderLine[] lines) =>
        Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder { Lines = lines.ToList() })
        );

    private static List<TempOrderLine> AssignedRows(int count, int start = 1) =>
        Enumerable.Range(start, count).Select(index => Assigned(index)).ToList();

    [Test]
    public void BulkEditAllRebasePreservesPositionsAndOrder()
    {
        const int count = 200;
        var before = AssignedRows(count);
        var desired = before
            .Select(line => new TempOrderLine { Id = line.Id, Name = line.Name + "-edited" })
            .ToList();
        var local = TempOrderHolder.ChangeSet.Between(
            TState(before.ToArray()),
            TState(desired.ToArray())
        );

        var result = local.RebaseOnto(TState(before.ToArray()));

        result.HasConflicts.ShouldBeFalse();
        var rebased = result.Rebased.Lines.ToDictionary(item => item.Key);
        rebased.Count.ShouldBe(count);
        for (var index = 0; index < count; index++)
        {
            var item = rebased[index + 1];
            item.IsEdited.ShouldBeTrue();
            item.BeforeIndex.ShouldBe(index);
            item.AfterIndex.ShouldBe(index);
            item.After.Value!.Name.ShouldBe("item-" + (index + 1) + "-edited");
        }
        TempOrderHolder
            .Patch.Between(
                result.Rebased.ToPatch().Apply(TState(before.ToArray())),
                TState(desired.ToArray())
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void BulkRemoveAllRebaseReportsOriginalPositions()
    {
        const int count = 200;
        var before = AssignedRows(count);
        var local = TempOrderHolder.ChangeSet.Between(TState(before.ToArray()), TState());

        var result = local.RebaseOnto(TState(before.ToArray()));

        result.HasConflicts.ShouldBeFalse();
        var rebased = result.Rebased.Lines.ToDictionary(item => item.Key);
        rebased.Count.ShouldBe(count);
        for (var index = 0; index < count; index++)
        {
            var item = rebased[index + 1];
            item.IsRemoved.ShouldBeTrue();
            item.BeforeIndex.ShouldBe(index);
            item.AfterIndex.ShouldBe(-1);
        }
        var applied = result.Rebased.ToPatch().Apply(TState(before.ToArray()));
        applied.Value!.Lines.Value!.Count.ShouldBe(0);
    }

    [Test]
    public void ReorderOnlyRebaseKeepsPositionsWithConcurrentServerValueEdit()
    {
        var before = AssignedRows(5);
        var desired = before.AsEnumerable().Reverse().ToList();
        var local = TempOrderHolder.ChangeSet.Between(
            TState(before.ToArray()),
            TState(desired.ToArray())
        );
        // Server concurrently edits one row value; order still replays.
        var current = before
            .Select(line =>
                line.Id == 5 ? new TempOrderLine { Id = 5, Name = "server-five" } : line
            )
            .ToList();

        var result = local.RebaseOnto(TState(current.ToArray()));

        result.HasConflicts.ShouldBeFalse();
        var byKey = result.Rebased.Lines.ToDictionary(item => item.Key);
        // Rebased reorder items keep current positions; the replayed order
        // travels in the rebased after-order, not the item indexes.
        byKey[1].BeforeIndex.ShouldBe(0);
        byKey[1].AfterIndex.ShouldBe(0);
        byKey[5].BeforeIndex.ShouldBe(4);
        byKey[5].AfterIndex.ShouldBe(4);
        var applied = result.Rebased.ToPatch().Apply(TState(current.ToArray()));
        applied
            .Value!.Lines.Value!.Select(line => line.Name)
            .ShouldBe(["server-five", "item-4", "item-3", "item-2", "item-1"]);
    }

    [Test]
    public void MixedAssignedAndTemporaryRebaseKeepsBothIndexSpaces()
    {
        var tempA = Guid.NewGuid();
        var tempB = Guid.NewGuid();
        var before = new List<TempOrderLine>
        {
            Assigned(1),
            Assigned(2),
            Temporary(tempA, "temp-a"),
            Assigned(3),
            Temporary(tempB, "temp-b"),
        };
        var desired = new List<TempOrderLine>
        {
            new() { Id = 1, Name = "item-1-edited" },
            new() { Id = 2, Name = "item-2" },
            new() { Name = "temp-a-edited", TemporaryId = tempA },
            new() { Name = "temp-b", TemporaryId = tempB },
        };
        var local = TempOrderHolder.ChangeSet.Between(
            TState(before.ToArray()),
            TState(desired.ToArray())
        );

        var result = local.RebaseOnto(TState(before.ToArray()));

        result.HasConflicts.ShouldBeFalse();
        var byKey = result.Rebased.Lines.ToDictionary(item => item.Key);
        byKey[1].IsEdited.ShouldBeTrue();
        byKey[1].BeforeIndex.ShouldBe(0);
        byKey[1].AfterIndex.ShouldBe(0);
        byKey[3].IsRemoved.ShouldBeTrue();
        byKey[3].BeforeIndex.ShouldBe(3);
        var tempChange = (ISparseChangeSet)result.Rebased;
        tempChange.ShouldNotBeNull();
        tempChange
            .TryGetKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>("Lines", out var lines)
            .ShouldBeTrue();
        var tempLines =
            lines as ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>;
        tempLines.ShouldNotBeNull();
        var editedTemp = tempLines!.GetTemporaryChange(tempA);
        editedTemp.IsEdited.ShouldBeTrue();
        editedTemp.BeforeIndex.ShouldBe(0);
        editedTemp.AfterIndex.ShouldBe(0);
        var applied = result.Rebased.ToPatch().Apply(TState(before.ToArray()));
        applied
            .Value!.Lines.Value!.Select(line => line.Name)
            .ShouldBe(["item-1-edited", "item-2", "temp-a-edited", "temp-b"]);
    }

    [Test]
    public void TemporaryEditAndReorderRebaseKeepsPositions()
    {
        var tempA = Guid.NewGuid();
        var tempB = Guid.NewGuid();
        var tempC = Guid.NewGuid();
        var before = new List<TempOrderLine>
        {
            Temporary(tempA, "a"),
            Temporary(tempB, "b"),
            Temporary(tempC, "c"),
        };
        var desired = new List<TempOrderLine>
        {
            new() { Name = "c", TemporaryId = tempC },
            new() { Name = "b", TemporaryId = tempB },
            new() { Name = "a-edited", TemporaryId = tempA },
        };
        var local = TempOrderHolder.ChangeSet.Between(
            TState(before.ToArray()),
            TState(desired.ToArray())
        );

        var result = local.RebaseOnto(TState(before.ToArray()));

        result.HasConflicts.ShouldBeFalse();
        var rebased =
            (ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>)
                result.Rebased.Lines;
        var edited = rebased.GetTemporaryChange(tempA);
        edited.IsEdited.ShouldBeTrue();
        edited.BeforeIndex.ShouldBe(0);
        var kept = rebased.GetTemporaryChange(tempB);
        kept.IsEmpty.ShouldBeTrue();
        var reordered = rebased.GetTemporaryChange(tempC);
        reordered.IsReordered.ShouldBeTrue();
        reordered.BeforeIndex.ShouldBe(2);
        reordered.AfterIndex.ShouldBe(2);
        // Item-level only: rebased temp-after order replays the current temp
        // order (pre-existing order-output behavior, unchanged here), so a
        // pure temp move is not re-applied from the rebased transition the
        // way assigned moves replay through the after-order.
    }

    [Test]
    public void TemporaryRemovalRebaseReportsOriginalPosition()
    {
        var tempA = Guid.NewGuid();
        var tempB = Guid.NewGuid();
        var before = new List<TempOrderLine> { Temporary(tempA, "a"), Temporary(tempB, "b") };
        var desired = new List<TempOrderLine> { Temporary(tempB, "b") };
        var local = TempOrderHolder.ChangeSet.Between(
            TState(before.ToArray()),
            TState(desired.ToArray())
        );

        var result = local.RebaseOnto(TState(before.ToArray()));

        // Item-level positions survive; the rebased temp-after order retains
        // removed identities (pre-existing order-output behavior, unchanged
        // here), so this locks positions without reapplying the patch.
        result.HasConflicts.ShouldBeFalse();
        var rebased =
            (ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>)
                result.Rebased.Lines;
        var removed = rebased.GetTemporaryChange(tempA);
        removed.IsRemoved.ShouldBeTrue();
        removed.BeforeIndex.ShouldBe(0);
        removed.AfterIndex.ShouldBe(-1);
    }

    [Test]
    public void BulkTemporaryComposeKeepsOrderAndSentinelSlots()
    {
        const int count = 200;
        var temps = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        var first = TempOrderHolder.ChangeSet.Between(
            TState(),
            TState(temps.Select(temp => Temporary(temp)).ToArray())
        );
        var editedSecond = temps
            .Select((temp, index) => Temporary(temp, index == 0 ? "edited-first" : null))
            .ToList();
        var second = TempOrderHolder.ChangeSet.Between(
            TState(temps.Select(temp => Temporary(temp)).ToArray()),
            TState(editedSecond.ToArray())
        );

        var composed = first.Compose(second);

        var tempLines =
            (ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>)
                composed.Lines;
        // Add-then-edit composes to a single addition carrying the final value.
        var firstTemp = tempLines.GetTemporaryChange(temps[0]);
        firstTemp.IsAdded.ShouldBeTrue();
        firstTemp.After.Value!.Name.ShouldBe("edited-first");
        for (var index = 0; index < count; index++)
        {
            tempLines.GetTemporaryChange(temps[index]).AfterIndex.ShouldBe(index);
        }
        // Every after-order slot is a temp sentinel aligned with temp order.
        composed.Lines.AfterOrder.Count.ShouldBe(count);
        composed
            .Lines.Select(item => item.TemporaryKey)
            .ShouldBe(temps.Select(temp => (Guid?)temp).ToArray());
        composed.Lines.AfterOrder.Count.ShouldBe(count);
        var applied = composed.ToPatch().Apply(TState());
        applied.Value!.Lines.Value!.Count.ShouldBe(count);
        applied.Value!.Lines.Value![0].Name.ShouldBe("edited-first");
    }

    [Test]
    public void RebaseOntoEmptyCurrentAppendsTemporaryAdditionsInReplayOrder()
    {
        var tempA = Guid.NewGuid();
        var tempB = Guid.NewGuid();
        var local = TempOrderHolder.ChangeSet.Between(
            TState(),
            TState(Temporary(tempA, "a"), Temporary(tempB, "b"))
        );

        var result = local.RebaseOnto(TState());

        result.HasConflicts.ShouldBeFalse();
        var rebasedTemp =
            (ISparseTemporaryKeyedChange<int, TempOrderLine, TempOrderLine.ChangeSet>)
                result.Rebased.Lines;
        // Replayed temporary additions share absolute-order space for
        // AfterIndex (pre-existing convention: empty current, so both 0).
        rebasedTemp.GetTemporaryChange(tempA).AfterIndex.ShouldBe(0);
        rebasedTemp.GetTemporaryChange(tempB).AfterIndex.ShouldBe(0);
        result
            .Rebased.Lines.Select(item => item.TemporaryKey)
            .ShouldBe([(Guid?)tempA, (Guid?)tempB]);
        var applied = result.Rebased.ToPatch().Apply(TState());
        applied.Value!.Lines.Value!.Select(line => line.Name).ShouldBe(["a", "b"]);
    }

    [Test]
    public void DuplicateTemporaryKeyStillRejected()
    {
        var temp = Guid.NewGuid();

        // Construction already rejects an after-state reusing one identity.
        Should.Throw<InvalidOperationException>(() =>
            TempOrderHolder.ChangeSet.Between(
                TState(Temporary(temp, "a")),
                TState(Temporary(temp, "a"), Temporary(temp, "b"))
            )
        );
    }
}
