using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Temporary-identity behavior for keyed collections (issue #207).</summary>
public sealed class TemporaryKeyIdentityTests
{
    private static Optional<TempOrderHolder.Fragment?> F(params TempOrderLine[] items) =>
        Optional<TempOrderHolder.Fragment?>.Present(
            TempOrderHolder.Fragment.From(new TempOrderHolder { Lines = items.ToList() })
        );

    private static TempOrderLine Assigned(int id, string name) =>
        new() { Id = id, Name = name };

    private static TempOrderLine Pending(string name)
    {
        var item = new TempOrderLine { Name = name, TemporaryId = Guid.NewGuid() };
        return item;
    }

    private static TempOrderHolder.ChangeSet PayloadRoundTrip(TempOrderHolder.ChangeSet changes)
    {
        var json = JsonSerializer.Serialize(changes.ToPayload());
        return JsonSerializer.Deserialize<TempOrderHolder.ChangePayload>(json)!.ToChangeSet();
    }

    [Test]
    public void AssignedKeyWinsOverTemporaryValue()
    {
        var before = F(Assigned(7, "a"));
        var after = F(
            new TempOrderLine
            {
                Id = 7,
                Name = "b",
                TemporaryId = Guid.NewGuid(),
            }
        );

        var changes = TempOrderHolder.ChangeSet.Between(before, after);
        // Same permanent identity: a plain edit, and the temporary value is
        // not exposed as identity on the transition item.
        var edited = changes.Lines.Edited;
        edited.Count.ShouldBe(1);
        var item = changes.Lines.GetChange(7);
        item.IsEdited.ShouldBeTrue();
        item.TemporaryKey.ShouldBeNull();
    }

    [Test]
    public void TwoPendingAddsStayDistinctThroughBetweenApplyAndPayload()
    {
        var first = Pending("first");
        var second = Pending("second");
        var before = F(Assigned(7, "existing"));
        var after = F(first, Assigned(7, "existing"), second);

        var changes = TempOrderHolder.ChangeSet.Between(before, after);
        changes.Lines.Added.Count.ShouldBe(2);
        changes.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded.ShouldBeTrue();
        changes.Lines.GetTemporaryChange(second.TemporaryId!.Value).IsAdded.ShouldBeTrue();
        changes.Lines.GetTemporaryChange(Guid.NewGuid()).IsEmpty.ShouldBeTrue();

        var applied = changes.ToPatch().Apply(before).Value!.Lines.Value!;
        applied.Select(item => item.Name).ShouldBe(["first", "existing", "second"]);
        applied[0].TemporaryId.ShouldBe(first.TemporaryId);
        applied[2].TemporaryId.ShouldBe(second.TemporaryId);

        var restored = PayloadRoundTrip(changes);
        restored
            .ToPatch()
            .Apply(before)
            .Value!.Lines.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);
        restored.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded.ShouldBeTrue();
    }

    [Test]
    public void PendingAddsAreEditableRemovableAndReorderable()
    {
        var first = Pending("first");
        var second = Pending("second");
        var baseline = F(Assigned(7, "existing"), first, second);

        // Edit a pending addition by temporary identity.
        var edited = F(Assigned(7, "existing"), first, new TempOrderLine
        {
            Name = "second v2",
            TemporaryId = second.TemporaryId,
        });
        var edit = TempOrderHolder.ChangeSet.Between(baseline, edited);
        var editItem = edit.Lines.GetTemporaryChange(second.TemporaryId!.Value);
        editItem.IsEdited.ShouldBeTrue();

        // Remove a pending addition.
        var removed = F(Assigned(7, "existing"), second);
        var removal = TempOrderHolder.ChangeSet.Between(baseline, removed);
        removal.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsRemoved.ShouldBeTrue();
        removal.ToPatch().Apply(baseline).Value!.Lines.Value!.Count.ShouldBe(2);

        // Reorder pending additions.
        var reordered = F(second, Assigned(7, "existing"), first);
        var reorder = TempOrderHolder.ChangeSet.Between(baseline, reordered);
        reorder.Lines.OrderChanged.ShouldBeTrue();
        reorder
            .ToPatch()
            .Apply(baseline)
            .Value!.Lines.Value!.Select(item => item.Name)
            .ShouldBe(["second", "existing", "first"]);
    }

    [Test]
    public void BetweenRejectsMissingEmptyAndDuplicateTemporaryIdentities()
    {
        var baseline = F(Assigned(7, "existing"));

        // Missing temporary identity.
        Should.Throw<InvalidOperationException>(() =>
            TempOrderHolder.ChangeSet.Between(baseline, F(Assigned(7, "existing"), new TempOrderLine { Name = "x" }))
        );

        // Empty GUID is reserved.
        Should.Throw<InvalidOperationException>(() =>
            TempOrderHolder.ChangeSet.Between(
                baseline,
                F(Assigned(7, "existing"), new TempOrderLine { Name = "x", TemporaryId = Guid.Empty })
            )
        );

        // Duplicate GUIDs in one collection.
        var dup = Guid.NewGuid();
        Should.Throw<InvalidOperationException>(() =>
            TempOrderHolder.ChangeSet.Between(
                baseline,
                F(
                    new TempOrderLine { Name = "a", TemporaryId = dup },
                    new TempOrderLine { Name = "b", TemporaryId = dup }
                )
            )
        );

        // Duplicate GUIDs in the baseline are rejected as well.
        var badBaseline = F(
            new TempOrderLine { Name = "a", TemporaryId = dup },
            new TempOrderLine { Name = "b", TemporaryId = dup }
        );
        Should.Throw<InvalidOperationException>(() =>
            TempOrderHolder.ChangeSet.Between(badBaseline, F(Assigned(1, "ok")))
        );
    }

    [Test]
    public void TemporaryBaselineSupportsLaterEditsAndFork()
    {
        var model = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = model.CreateEditSession();
        var added = Pending("first");
        model.Lines.Add(added);

        // Accepting a temporary-keyed addition advances the baseline.
        var outgoing = session.CreateChangeSet();
        outgoing.IsEmpty.ShouldBeFalse();
        session.AcceptChanges(outgoing);
        session.HasChanges.ShouldBeFalse();

        // Later edits against the temporary baseline diff by Guid.
        model.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name = "first v2";
        var pending = session.CreateChangeSet();
        pending.Lines.GetTemporaryChange(added.TemporaryId!.Value).IsEdited.ShouldBeTrue();

        // A session forked after the addition starts clean but retains the
        // temporary baseline for later edits.
        var fork = session.Fork();
        fork.CreateChangeSet().IsEmpty.ShouldBeTrue();
        var forkModel = ((ISparseEditSession<TempOrderHolder>)fork).Model;
        forkModel.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name =
            "forked edit";
        fork
            .CreateChangeSet()
            .Lines.GetTemporaryChange(added.TemporaryId!.Value)
            .IsEdited.ShouldBeTrue();
    }

    [Test]
    public void TemporaryChangesSurviveComposeInvertRebaseAndClone()
    {
        var first = Pending("first");
        var second = Pending("second");
        var b0 = F(Assigned(7, "existing"));
        var b1 = F(Assigned(7, "existing"), first);
        var b2 = F(Assigned(7, "existing"), first, second);

        var addFirst = TempOrderHolder.ChangeSet.Between(b0, b1);
        var addSecond = TempOrderHolder.ChangeSet.Between(b1, b2);
        var composed = addFirst.Compose(addSecond);
        composed.Lines.Added.Count.ShouldBe(2);
        composed
            .ToPatch()
            .Apply(b0)
            .Value!.Lines.Value!.Select(item => item.Name)
            .ShouldBe(["existing", "first", "second"]);

        // Editing a pending addition composes through the add.
        var b3 = F(
            Assigned(7, "existing"),
            new TempOrderLine { Name = "first v2", TemporaryId = first.TemporaryId },
            second
        );
        var editPending = TempOrderHolder.ChangeSet.Between(b2, b3);
        var updateFlow = addFirst.Compose(TempOrderHolder.ChangeSet.Between(b1, b3));
        updateFlow
            .ToPatch()
            .Apply(b0)
            .Value!.Lines.Value!.Select(item => item.Name)
            .ShouldBe(["existing", "first v2", "second"]);

        // Invert + rebase round-trip keeps identities.
        var inverted = addSecond.Invert();
        var rebased = addSecond.RebaseOnto(b1);
        rebased.HasConflicts.ShouldBeFalse();

        // Fragment clone retains temporary identities.
        var clone = TempOrderHolder.Fragment.From(
            new TempOrderHolder { Lines = [Assigned(7, "existing"), first] }
        );
        var roundTripped = PayloadRoundTrip(
            TempOrderHolder.ChangeSet.Between(b0, F(Assigned(7, "existing"), first))
        );
        roundTripped.Lines.GetTemporaryChange(first.TemporaryId!.Value).IsAdded.ShouldBeTrue();
    }

    [Test]
    public void TemporaryRemnantDoesNotCreateChanges()
    {
        var temp = Guid.NewGuid();
        var withTemp = F(new TempOrderLine
        {
            Id = 11,
            Name = "saved",
            TemporaryId = temp,
        });
        var withoutTemp = F(Assigned(11, "saved"));

        // A response echoing the temporary value compares identically with a
        // later GET that drops it when identity and values are unchanged.
        TempOrderHolder.ChangeSet.Between(withTemp, withoutTemp).IsEmpty.ShouldBeTrue();
        TempOrderHolder.ChangeSet.Between(withoutTemp, withTemp).IsEmpty.ShouldBeTrue();
        TempOrderHolder.Patch.Between(withTemp, withoutTemp).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void TypedPathAddressesTemporaryElements()
    {
        var temp = Guid.NewGuid();
        SparsePath<TempOrderHolder, string> byTemp = TempOrderHolder
            .SparsePath.Lines.TemporaryKey(temp)
            .Name;
        SparsePath<TempOrderHolder, string> byKey = TempOrderHolder.SparsePath.Lines.Key(7).Name;

        byTemp.ToString().ShouldBe("Lines[temp:\"" + temp.ToString("D") + "\"].Name");
        byTemp.ShouldNotBe(byKey);

        // Key and temporary segments never compare equal, even for Guid keys.
        SparsePath tempPath = TempOrderHolder.SparsePath.Lines.TemporaryKey(temp);
        SparsePath keyPath = TempOrderHolder.SparsePath.Lines.Key(7);
        tempPath.Equals(keyPath).ShouldBeFalse();
        tempPath.ShouldBe(
            SparsePath.Root<TempOrderHolder>().Member("Lines").TemporaryKey(temp)
        );

        // Path text round-trips temporary segments explicitly.
        var parsed = SparsePath.Parse<TempOrderHolder>(byTemp.ToString());
        parsed.ShouldBe((SparsePath)byTemp);
    }

    [Test]
    public void CompositeTupleKeysSupportTemporaryIdentity()
    {
        var temp = Guid.NewGuid();
        var before = Optional<TempCompositeHolder.Fragment?>.Present(
            TempCompositeHolder.Fragment.From(
                new TempCompositeHolder
                {
                    Lines =
                    [
                        new TempCompositeLine
                        {
                            TenantId = "t",
                            Number = 1,
                            Name = "a",
                            Unassigned = false,
                        },
                    ],
                }
            )
        );
        var after = Optional<TempCompositeHolder.Fragment?>.Present(
            TempCompositeHolder.Fragment.From(
                new TempCompositeHolder
                {
                    Lines =
                    [
                        new TempCompositeLine
                        {
                            TenantId = "t",
                            Number = 1,
                            Name = "a",
                            Unassigned = false,
                        },
                        new TempCompositeLine { Name = "new", TemporaryId = temp },
                    ],
                }
            )
        );

        var changes = TempCompositeHolder.ChangeSet.Between(before, after);
        changes.Lines.Added.Count.ShouldBe(1);
        changes.Lines.GetTemporaryChange(temp).IsAdded.ShouldBeTrue();
        changes
            .ToPatch()
            .Apply(before)
            .Value!.Lines.Value!.Count.ShouldBe(2);
    }

    [Test]
    public void NestedKeyedCollectionsCorrelateByTemporaryIdentity()
    {
        var lineTemp = Guid.NewGuid();
        var baseline = Optional<TempOrderBook.Fragment?>.Present(
            TempOrderBook.Fragment.From(
                new TempOrderBook
                {
                    Customer = new TempCustomer { Id = 1, Name = "c" },
                    Lines = [Assigned(7, "existing")],
                }
            )
        );
        var after = Optional<TempOrderBook.Fragment?>.Present(
            TempOrderBook.Fragment.From(
                new TempOrderBook
                {
                    Customer = new TempCustomer
                    {
                        Id = 1,
                        Name = "c",
                        Orders = [new TempOrderLine { Name = "nested", TemporaryId = lineTemp }],
                    },
                    Lines = [Assigned(7, "existing")],
                }
            )
        );

        var changes = TempOrderBook.ChangeSet.Between(baseline, after);
        changes.IsEmpty.ShouldBeFalse();
        changes
            .ToPatch()
            .Apply(baseline)
            .Value!.Customer.Value!.Orders.Value!.Count.ShouldBe(1);
    }
}
