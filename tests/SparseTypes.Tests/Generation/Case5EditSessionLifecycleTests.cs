using SparseTypes.Playground.Models;

namespace SparseTypes.Tests.Generation;

/// <summary>Case 5 draft/baseline lifecycle through the generated EditSession (issue #266).</summary>
/// <remarks>
/// <para>
/// Replaces the manual <c>PlaygroundLineIdentity.RetargetNewLineIds</c> correlation proof with the
/// session contract: capture the exact immutable submitted <c>ChangeSet</c> before an awaited save,
/// keep editing the live model, then acknowledge with <c>AcceptChanges(submitted)</c> when the
/// server returned no key/value changes or <c>TryReconcile(submitted, persisted)</c> otherwise.
/// </para>
/// <para>
/// The persisted DTOs below stand in for the post-save projection that production builds with
/// <c>ApplyWithResult</c> plus <c>AssignTemporaryKeys</c> (see the EF Core end-to-end proof in
/// <c>Case5EfSessionLifecycleTests</c>). All identities are fixed so the tests are deterministic.
/// </para>
/// </remarks>
public sealed class Case5EditSessionLifecycleTests
{
    private static readonly Guid TempA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TempB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TempGone = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TempLate = new("44444444-4444-4444-4444-444444444444");

    private static PlaygroundOrderLineDto Existing(int id, string product = "Espresso") =>
        new()
        {
            Id = id,
            Product = product,
            Qty = 1,
        };

    private static PlaygroundOrderLineDto Added(Guid temp, string product = "Latte") =>
        new()
        {
            Product = product,
            Qty = 2,
            TemporaryId = temp,
        };

    private static PlaygroundOrderLineDto Assigned(Guid temp, int id, string product = "Latte") =>
        new()
        {
            Id = id,
            Product = product,
            Qty = 2,
            TemporaryId = temp,
        };

    private static PlaygroundOrderDto Draft(params PlaygroundOrderLineDto[] lines) =>
        new()
        {
            Customer = "Cafe Vienna",
            Notes = "first order",
            Total = 13.75m,
            Lines = lines.ToList(),
        };

    [Test]
    public void ScalarOnlySaveAcknowledgesWithAcceptChanges()
    {
        var live = Draft(Existing(1), Existing(2, "Croissant"));
        var session = live.CreateEditSession();
        live.Customer = "Cafe Central";
        live.Lines[0].Qty = 5;

        var submitted = session.CreateChangeSet();
        submitted.IsChanged.ShouldBeTrue();

        // No authoritative key/value changes: advance the baseline without touching the live model.
        var before = live.Lines[0].Qty;
        session.AcceptChanges(submitted);
        session.HasChanges.ShouldBeFalse();
        live.Customer.ShouldBe("Cafe Central");
        live.Lines[0].Qty.ShouldBe(before);

        // Later edits stay pending against the advanced baseline.
        live.Notes = "edited after save";
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void AcceptChangesAdvancesOverTemporaryAddsButLeavesIdsUnassigned()
    {
        // Temporary-keyed additions may serve as an edit baseline, so AcceptChanges advances
        // over them. It must still not be used as the save acknowledgement when the server
        // assigns keys: the live rows keep Id null and the assigned identities never land.
        // That boundary is why saves with new rows reconcile instead.
        var live = Draft(Existing(1));
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA));

        var submitted = session.CreateChangeSet();
        session.AcceptChanges(submitted);
        session.HasChanges.ShouldBeFalse();
        live.Lines.Single(line => line.TemporaryId == TempA).Id.ShouldBeNull();
    }

    [Test]
    public void ReconcileAssignsMultipleKeysAcrossReorderAndKeepsPostSubmitEdits()
    {
        var live = Draft(Existing(1), Existing(2, "Croissant"));
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA, "alpha"));
        live.Lines.Add(Added(TempB, "beta"));

        var submitted = session.CreateChangeSet();

        // Overlapping edit after submit: rename a pending row, drop nothing, add a late row,
        // and reorder the live list. The submitted snapshot stays immutable.
        live.Lines.Single(line => line.TemporaryId == TempA).Product = "alpha edited after submit";
        var late = Added(TempLate, "late");
        live.Lines.Add(late);
        var moved = live.Lines[0];
        live.Lines.RemoveAt(0);
        live.Lines.Add(moved);

        // The server persists the submitted order but returns rows in store order with
        // database-assigned identities and the temporary identities echoed back.
        var persisted = Draft(
            Existing(1),
            Existing(2, "Croissant"),
            Assigned(TempB, 20, "beta"),
            Assigned(TempA, 10, "alpha")
        );

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        var first = live.Lines.Single(line => line.TemporaryId == TempA);
        first.Id.ShouldBe(10);
        first.Product.ShouldBe("alpha edited after submit");
        var second = live.Lines.Single(line => line.TemporaryId == TempB);
        second.Id.ShouldBe(20);

        // The late row stays pending under its own temporary identity.
        live.Lines.Single(line => line.TemporaryId == TempLate).Id.ShouldBeNull();
        var pending = session.CreateChangeSet();
        pending.IsChanged.ShouldBeTrue();
        pending.Lines.GetChange(10).IsEdited.ShouldBeTrue();
        pending.Lines.GetTemporaryChange(TempLate).IsAdded.ShouldBeTrue();
    }

    [Test]
    public void ReconcileAdoptsServerNormalizationForUntouchedRows()
    {
        var live = Draft(Existing(1, "espresso"));
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA, "latte"));

        var submitted = session.CreateChangeSet();

        // The server normalizes values on write (case, trimming); untouched rows adopt the
        // authoritative value while post-submit edited rows keep their local values.
        var persisted = Draft(
            new PlaygroundOrderLineDto
            {
                Id = 1,
                Product = "ESPRESSO",
                Qty = 1,
            },
            Assigned(TempA, 10, "LATTE")
        );

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        live.Lines.Single(line => line.Id == 1).Product.ShouldBe("ESPRESSO");
        live.Lines.Single(line => line.TemporaryId == TempA).Id.ShouldBe(10);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReconcileFailsClosedWhenServerFiltersARow()
    {
        var live = Draft(Existing(1));
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA));
        live.Lines.Add(Added(TempGone));

        var submitted = session.CreateChangeSet();

        // The server filtered one submitted row: its temporary identity is absent.
        var persisted = Draft(Existing(1), Assigned(TempA, 10));

        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();

        // No partial key writeback: neither row was assigned and the submission stays pending.
        live.Lines.Single(line => line.TemporaryId == TempA).Id.ShouldBeNull();
        live.Lines.Single(line => line.TemporaryId == TempGone).Id.ShouldBeNull();
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ReconcileFailsClosedOnRepeatedTemporaryIdentity()
    {
        var live = Draft();
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA));

        var submitted = session.CreateChangeSet();
        var persisted = Draft(Assigned(TempA, 10), Assigned(TempA, 11, "beta"));

        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        live.Lines.Single().Id.ShouldBeNull();
        live.Lines.Single().TemporaryId.ShouldBe(TempA);
    }

    [Test]
    public void ReconcileKeepsRowPendingWhenPersistedLeavesIdentityUnassigned()
    {
        // Unlike the retired manual helper (which threw "database identity"), the session
        // treats an echoed-but-still-unassigned row as still pending: reconciliation succeeds
        // without mutating values, so a later save can retry. The production EF path always
        // assigns database identities via AssignTemporaryKeys after SaveChanges, so this only
        // arises with synthetic responses.
        var live = Draft();
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA));

        var submitted = session.CreateChangeSet();
        var persisted = Draft(Added(TempA));

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        live.Lines.Single().Id.ShouldBeNull();
        live.Lines.Single().TemporaryId.ShouldBe(TempA);
    }

    [Test]
    public void StaleSubmissionIsRejectedWithoutMutation()
    {
        var live = Draft(Existing(1));
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA));
        var submitted = session.CreateChangeSet();

        // An overlapping acknowledgement moves the baseline past the submission.
        live.Lines.Single(line => line.Id == 1).Product = "intervening";
        session.AcceptChanges(session.CreateChangeSet());

        var persisted = Draft(Existing(1), Assigned(TempA, 10));
        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void RepeatedReconcileIsIdempotent()
    {
        var live = Draft(Existing(1));
        var session = live.CreateEditSession();
        live.Lines.Add(Added(TempA));

        var submitted = session.CreateChangeSet();
        var persisted = Draft(Existing(1), Assigned(TempA, 10));

        session.TryReconcile(submitted, persisted, out var firstError).ShouldBeTrue();
        firstError.ShouldBeNull();
        session.TryReconcile(submitted, persisted, out var secondError).ShouldBeTrue();
        secondError.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        live.Lines.Count.ShouldBe(2);
    }

    [Test]
    public void PostSubmitRowsAddedAfterSubmitStayPending()
    {
        var live = Draft(Added(TempA), Existing(1));
        var session = live.CreateEditSession();
        var submitted = session.CreateChangeSet();

        // TempLate never appeared in the submission, so reconciliation leaves it alone.
        live.Lines.Add(Added(TempLate));
        var persisted = Draft(Existing(1), Assigned(TempA, 10));

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        live.Lines.Single(line => line.TemporaryId == TempA).Id.ShouldBe(10);
        var late = live.Lines.Single(line => line.TemporaryId == TempLate);
        late.Id.ShouldBeNull();
        session.CreateChangeSet().Lines.GetTemporaryChange(TempLate).IsAdded.ShouldBeTrue();
    }
}
