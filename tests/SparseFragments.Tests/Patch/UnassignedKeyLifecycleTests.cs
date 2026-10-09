using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

public sealed class UnassignedKeyLifecycleTests
{
    private static Optional<AssignedServerHolder.Fragment?> F(params AssignedServer[] items) =>
        Optional<AssignedServerHolder.Fragment?>.Present(
            AssignedServerHolder.Fragment.From(new AssignedServerHolder { Items = items.ToList() })
        );

    private static AssignedServer Item(int id, string name) => new() { Id = id, Name = name };

    private static string PayloadJson(AssignedServerHolder.ChangeSet changes) =>
        JsonSerializer.Serialize(changes.ToPayload());

    private static AssignedServerHolder.ChangeSet PayloadRoundTrip(
        AssignedServerHolder.ChangeSet changes
    ) =>
        JsonSerializer
            .Deserialize<AssignedServerHolder.ChangePayload>(PayloadJson(changes))!
            .ToChangeSet();

    private static Optional<AssignedServerHolder.Fragment?> H(AssignedServerHolder model) =>
        Optional<AssignedServerHolder.Fragment?>.Present(AssignedServerHolder.Fragment.From(model));

    [Test]
    public void TwoUnassignedAddsPreserveValuesAndOrder()
    {
        var before = F(Item(7, "existing"));
        var after = F(Item(0, "first"), Item(7, "existing"), Item(0, "second"));

        var patch = AssignedServerHolder.Patch.Between(before, after);
        patch
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);

        var changes = AssignedServerHolder.ChangeSet.Between(before, after);
        changes.Items.Added.Select(item => item.Name).ShouldBe(["first", "second"]);
        changes.Items.AfterOrder.ShouldBe([0, 7, 0]);
        changes.Items.GetChange(0).IsEmpty.ShouldBeTrue();
        changes.Items.GetChange(7).IsEmpty.ShouldBeTrue();

        // Typed payload JSON preserves each occurrence and final order.
        var restored = PayloadRoundTrip(changes);
        restored
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);
        using var document = JsonDocument.Parse(PayloadJson(changes));
        document
            .RootElement.GetProperty("changes")[0]
            .GetProperty("afterOrder")
            .EnumerateArray()
            .Select(key => key.GetInt32())
            .ShouldBe([0, 7, 0]);
    }

    [Test]
    public void TwoUnassignedEqualValuesStayIndependent()
    {
        var before = F(Item(7, "existing"));
        var after = F(Item(0, "same"), Item(7, "existing"), Item(0, "same"));

        var changes = AssignedServerHolder.ChangeSet.Between(before, after);
        changes.Items.Added.Count.ShouldBe(2);
        changes
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["same", "existing", "same"]);

        var restored = PayloadRoundTrip(changes);
        restored.ToPatch().Apply(before).Value!.Items.Value!.Count.ShouldBe(3);
    }

    [Test]
    public void UnassignedWithReorderRemovalAndCollision()
    {
        // Reorder stable entries around unassigned inserts.
        var before = F(Item(7, "a"), Item(8, "b"));
        var reordered = F(Item(0, "new"), Item(8, "b"), Item(7, "a"));
        var changes = AssignedServerHolder.ChangeSet.Between(before, reordered);
        changes
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["new", "b", "a"]);

        // Removal plus unassigned add.
        var removed = F(Item(0, "new"), Item(7, "a"));
        AssignedServerHolder
            .ChangeSet.Between(before, removed)
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["new", "a"]);

        // Duplicate assigned keys stay invalid even beside unassigned adds.
        var duplicate = F(Item(0, "new"), Item(7, "a"), Item(7, "b"));
        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.ChangeSet.Between(before, duplicate)
        );
        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.Patch.Between(before, duplicate)
        );
    }

    [Test]
    public void BetweenApplyRejectUnassignedBaselines()
    {
        var invalid = F(Item(0, "stale"));
        var valid = F(Item(1, "ok"));

        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.Patch.Between(invalid, valid)
        );
        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.ChangeSet.Between(invalid, valid)
        );

        // Applying any granular patch onto an unassigned baseline fails.
        var patch = AssignedServerHolder.Patch.Between(valid, F(Item(1, "ok"), Item(2, "new")));
        Should.Throw<InvalidOperationException>(() => patch.Apply(invalid));

        // Composing through an unassigned intermediate is unsupported: the second
        // diff cannot even be built because its baseline would carry sentinels.
        var first = AssignedServerHolder.ChangeSet.Between(valid, F(Item(0, "new"), Item(1, "ok")));
        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.ChangeSet.Between(F(Item(0, "new"), Item(1, "ok")), valid)
        );
        // Conflicting stable continuities also fail deterministically.
        var conflictA = AssignedServerHolder.ChangeSet.Between(F(Item(7, "a")), F(Item(7, "b")));
        var conflictB = AssignedServerHolder.ChangeSet.Between(F(Item(7, "c")), F(Item(7, "d")));
        Should.Throw<InvalidOperationException>(() => conflictA.Compose(conflictB));
    }

    [Test]
    public void ComposeRebaseInvertToPatchJsonContracts()
    {
        var b0 = F(Item(7, "existing"));
        var b1 = F(Item(7, "updated"));
        var b2 = F(Item(0, "first"), Item(7, "updated"), Item(0, "second"));

        // Stable then unassigned composes.
        var first = AssignedServerHolder.ChangeSet.Between(b0, b1);
        var second = AssignedServerHolder.ChangeSet.Between(b1, b2);
        first
            .Compose(second)
            .ToPatch()
            .Apply(b0)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "updated", "second"]);

        // Unassigned adds rebase onto a concurrent stable add.
        var concurrent = F(Item(7, "updated"), Item(9, "concurrent"));
        var rebased = second.RebaseOnto(concurrent);
        rebased.HasConflicts.ShouldBeFalse();
        rebased
            .Rebased.ToPatch()
            .Apply(concurrent)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["updated", "concurrent", "first", "second"]);

        // Rebasing onto a current state with sentinels fails deterministically.
        Should.Throw<InvalidOperationException>(() => second.RebaseOnto(b2));

        // Inverting an unassigned Add yields a before-state the keyed
        // apply path cannot consume; assert the deterministic failure.
        var inverted = second.Invert();
        Should.Throw<InvalidOperationException>(() => inverted.ToPatch().Apply(b2));

        // Stable-only invert round-trips.
        var stableInverted = first.Invert();
        AssignedServerHolder
            .Patch.Between(stableInverted.ToPatch().Apply(b1), b0)
            .IsEmpty.ShouldBeTrue();

        // JSON round-trip keeps rebase semantics for the supported case.
        var restored = PayloadRoundTrip(second);
        var restoredRebased = restored.RebaseOnto(concurrent);
        restoredRebased.HasConflicts.ShouldBeFalse();
        restoredRebased
            .Rebased.ToPatch()
            .Apply(concurrent)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["updated", "concurrent", "first", "second"]);
    }

    [Test]
    public void ServerAssignsIdsRehydrateWithFreshSession()
    {
        var pristine = new AssignedServerHolder { Items = [Item(7, "existing")] };
        var order = new AssignedServerHolder { Items = [Item(7, "existing")] };
        var session = order.CreateEditSession();
        session.Model.Items.Add(Item(0, "first"));
        session.Model.Items.Add(Item(0, "second"));

        var outgoing = session.CreateChangeSet();
        outgoing.IsEmpty.ShouldBeFalse();
        // Payload round-trip preserves both unassigned occurrences.
        PayloadRoundTrip(outgoing)
            .ToPatch()
            .Apply(H(pristine))
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["existing", "first", "second"]);

        // Server assigns distinct IDs and normalizes order/fields.
        var persisted = new AssignedServerHolder
        {
            Items = [Item(7, "EXISTING"), Item(11, "FIRST"), Item(12, "SECOND")],
        };

        // Application discards the unassigned session; no identity is inferred.
        var fresh = persisted.CreateEditSession();
        fresh.HasChanges.ShouldBeFalse();
        fresh.CreateChangeSet().IsEmpty.ShouldBeTrue();
        ReferenceEquals(fresh.Model, persisted).ShouldBeTrue();

        // The next edit from authoritative state is a valid keyed change set.
        fresh.Model.Items.Single(item => item.Id == 11).Name = "First v2";
        var next = fresh.CreateChangeSet();
        next.IsEmpty.ShouldBeFalse();
        AssignedServerHolder
            .Patch.Between(next.ToPatch().Apply(H(persisted)), H(fresh.Model))
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AcceptChangesWithUnassignedAddRejectsAtomically()
    {
        var model = new AssignedServerHolder { Items = [Item(7, "existing")] };
        var session = model.CreateEditSession();
        var observable = session.Observable;
        session.Model.Items.Add(Item(0, "first"));

        var outgoing = session.CreateChangeSet();
        outgoing.IsEmpty.ShouldBeFalse();

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(outgoing));

        // Baseline unchanged, live edit still pending, identities preserved.
        ReferenceEquals(session.Model, model).ShouldBeTrue();
        ReferenceEquals(session.Observable, observable).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Items.Added.Select(item => item.Name).ShouldBe(["first"]);
    }

    [Test]
    public void AcceptChangesOnLiveWithUnassignedFailsFast()
    {
        var model = new AssignedServerHolder { Items = [Item(7, "existing")] };
        var session = model.CreateEditSession();
        session.Model.Items.Add(Item(0, "first"));

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges());

        // Failed accept leaves baseline and pending edits intact.
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Items.Added.Count.ShouldBe(1);
    }

    [Test]
    public void AcceptChangesAdvancesBaselineAndKeepsLaterEdits()
    {
        var model = new AssignedServerHolder { Items = [Item(7, "before")] };
        var session = model.CreateEditSession();
        var observable = session.Observable;

        session.Model.Items.Single(item => item.Id == 7).Name = "sent";
        var captured = session.CreateChangeSet();

        // Intervening stable edit after capture stays pending.
        session.Model.Items.Add(Item(9, "later"));
        session.AcceptChanges(captured);

        ReferenceEquals(session.Model, model).ShouldBeTrue();
        ReferenceEquals(session.Observable, observable).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Items.Added.Select(item => item.Name).ShouldBe(["later"]);

        // Accepting the same transition twice is stale.
        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(captured));

        // Empty acknowledgement is a no-op.
        session.AcceptChanges(session.CreateChangeSet());
        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AcceptChangesRejectsStaleTransitionsWithoutPartialWrites()
    {
        var baseline = new AssignedServerHolder { Items = [Item(7, "a")] };
        var current = new AssignedServerHolder { Items = [Item(7, "b")] };
        var session = baseline.CreateEditSession(current);

        var otherBaseline = new AssignedServerHolder { Items = [Item(7, "other")] };
        var otherCurrent = new AssignedServerHolder { Items = [Item(7, "changed")] };
        var foreign = otherBaseline.CreateChangeSet(otherCurrent);

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(foreign));
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Items.Edited.Count.ShouldBe(1);
    }

    [Test]
    public void HasChangesTreatsTemporaryDuplicateKeysAsPending()
    {
        var model = new AssignedServerHolder { Items = [Item(7, "existing")] };
        var session = model.CreateEditSession();
        var duplicate = Item(7, "duplicate");
        session.Model.Items.Add(duplicate);

        session.HasChanges.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => session.CreateChangeSet());

        session.Model.Items.Remove(duplicate);
        session.HasChanges.ShouldBeFalse();
    }
}
