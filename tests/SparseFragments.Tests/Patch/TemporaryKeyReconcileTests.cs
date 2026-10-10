using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>DTO-based reconcile for temporary identities (issue #207).</summary>
/// <remarks>
/// The flow under test: submit a change set, let an app-owned server assign
/// permanent keys while echoing temporary identities on the ordinary response
/// DTO, then acknowledge with <c>TryReconcile</c> while post-submit local
/// edits are preserved.
/// </remarks>
public sealed class TemporaryKeyReconcileTests
{
    private static TempOrderLine Assigned(int id, string name) => new() { Id = id, Name = name };

    private static TempOrderLine Pending(string name, Guid? temp = null) =>
        new() { Name = name, TemporaryId = temp ?? Guid.NewGuid() };

    private static TempOrderHolder Persisted(params TempOrderLine[] lines) =>
        new() { Lines = lines.ToList() };

    // App-owned entity types: the server maps DTOs to entities and back with
    // no framework dependency. Temporary correlation lives only in the mapping.
    private sealed class OrderLineEntity
    {
        public int Id;
        public string Name = "";
    }

    private static int nextFakeId = 1000;

    private static TempOrderHolder SaveToFakeStore(TempOrderHolder submittedAfter)
    {
        var tracked = new List<(Guid Temp, OrderLineEntity Entity)>();
        var entities = submittedAfter
            .Lines.Select(dto =>
            {
                var entity = new OrderLineEntity { Id = dto.Id, Name = dto.Name };
                if (dto.Id == 0 && dto.TemporaryId.HasValue)
                {
                    tracked.Add((dto.TemporaryId.Value, entity));
                }
                return entity;
            })
            .ToList();
        // SaveChanges assigns keys and normalizes untouched values.
        foreach (var entity in entities)
        {
            if (entity.Id == 0)
            {
                entity.Id = nextFakeId++;
            }
            entity.Name = entity.Name.ToUpperInvariant();
        }
        var tempByKey = tracked.ToDictionary(
            static pair => pair.Entity.Id,
            static pair => pair.Temp
        );
        // Normal GET mappings leave the temporary value null; the save
        // response restores it for newly inserted rows by permanent key.
        return new TempOrderHolder
        {
            Lines = entities
                .Select(entity => new TempOrderLine
                {
                    Id = entity.Id,
                    Name = entity.Name,
                    TemporaryId = tempByKey.TryGetValue(entity.Id, out var temp) ? temp : null,
                })
                .ToList(),
        };
    }

    private static TempOrderHolder SubmittedAfter(
        TempOrderHolder baseline,
        TempOrderHolder.ChangeSet submitted
    ) => submitted.ToPatch().Apply(TempOrderHolder.Fragment.From(baseline)).Value!.ToModel();

    [Test]
    public void ReconcileAssignsKeysAndAdoptsPersistedBaseline()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        var first = Pending("first");
        var second = Pending("second");
        baseline.Lines.Add(first);
        baseline.Lines.Add(second);

        var submitted = session.CreateChangeSet();
        var persisted = SaveToFakeStore(
            SubmittedAfter(new TempOrderHolder { Lines = [Assigned(7, "existing")] }, submitted)
        );

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();

        baseline.Lines.Count.ShouldBe(3);
        // Fake-store ids come from a process-wide counter shared with other
        // tests, so compare against the persisted response instead of
        // hard-coding values.
        baseline.Lines.Select(item => item.Id).ShouldBe(persisted.Lines.Select(item => item.Id));
        baseline.Lines.Select(item => item.Name).ShouldBe(["EXISTING", "FIRST", "SECOND"]);
    }

    [Test]
    public void ReconcilePreservesPostSubmitEditsAddsRemovalsAndReorders()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        var first = Pending("first");
        var doomed = Pending("doomed");
        baseline.Lines.Add(first);
        baseline.Lines.Add(doomed);

        var submitted = session.CreateChangeSet();
        var submittedAfter = SubmittedAfter(
            new TempOrderHolder { Lines = [Assigned(7, "existing")] },
            submitted
        );

        // Local edits continue after submission: edit a pending row, drop one,
        // add a new one, and reorder.
        baseline.Lines.Single(item => item.TemporaryId == first.TemporaryId).Name = "first v2";
        baseline.Lines.Remove(doomed);
        var late = Pending("late");
        baseline.Lines.Add(late);
        var persisted = SaveToFakeStore(submittedAfter);

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        // Assigned keys landed; the dropped row stays dropped; the late row
        // stays pending with its temporary identity.
        var pending = session.CreateChangeSet();
        pending.IsEmpty.ShouldBeFalse();
        baseline.Lines.Select(item => item.Name).ShouldBe(["existing", "first v2", "late"]);
        var assigned = baseline.Lines.Single(item => item.TemporaryId == first.TemporaryId);
        assigned.Id.ShouldNotBe(0);
        pending.Lines.GetChange(assigned.Id).IsEdited.ShouldBeTrue();
        pending.Lines.GetTemporaryChange(late.TemporaryId!.Value).IsAdded.ShouldBeTrue();
        // The server-side row for the dropped submission is recorded as removed.
        var droppedId = persisted.Lines.Single(item => item.TemporaryId == doomed.TemporaryId).Id;
        pending.Lines.GetChange(droppedId).IsRemoved.ShouldBeTrue();
    }

    [Test]
    public void ReconcileFailsWithoutMutationOnResponseMismatches()
    {
        // Missing temporary identity in the response.
        {
            var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
            var session = baseline.CreateEditSession();
            var first = Pending("first");
            baseline.Lines.Add(first);
            var submitted = session.CreateChangeSet();
            var persisted = Persisted(Assigned(7, "EXISTING"), Assigned(11, "FIRST"));

            session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
            error.ShouldNotBeNullOrEmpty();
            baseline.Lines.Count.ShouldBe(2);
            baseline.Lines[1].Id.ShouldBe(0);
            session.HasChanges.ShouldBeTrue();
            session.CreateChangeSet().Lines.Added.Count.ShouldBe(1);
        }

        // Duplicated temporary identity in the response.
        {
            var baseline = new TempOrderHolder { Lines = [] };
            var session = baseline.CreateEditSession();
            var first = Pending("first");
            baseline.Lines.Add(first);
            var submitted = session.CreateChangeSet();
            var persisted = Persisted(
                new TempOrderLine
                {
                    Id = 11,
                    Name = "a",
                    TemporaryId = first.TemporaryId,
                },
                new TempOrderLine
                {
                    Id = 12,
                    Name = "b",
                    TemporaryId = first.TemporaryId,
                }
            );

            session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
            error.ShouldNotBeNullOrEmpty();
            baseline.Lines.Single().Id.ShouldBe(0);
        }

        // Response leaves the identity unassigned.
        {
            var baseline = new TempOrderHolder { Lines = [] };
            var session = baseline.CreateEditSession();
            var first = Pending("first");
            baseline.Lines.Add(first);
            var submitted = session.CreateChangeSet();
            var persisted = Persisted(
                new TempOrderLine { Name = "still pending", TemporaryId = first.TemporaryId }
            );

            session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
            error.ShouldNotBeNullOrEmpty();
            baseline.Lines.Single().Id.ShouldBe(0);
        }
    }

    [Test]
    public void RepeatedReconcileIsIdempotent()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        var first = Pending("first");
        baseline.Lines.Add(first);

        var submitted = session.CreateChangeSet();
        var persisted = SaveToFakeStore(
            SubmittedAfter(new TempOrderHolder { Lines = [Assigned(7, "existing")] }, submitted)
        );

        session.TryReconcile(submitted, persisted, out var firstError).ShouldBeTrue();
        firstError.ShouldBeNull();
        session.TryReconcile(submitted, persisted, out var secondError).ShouldBeTrue();
        secondError.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        baseline.Lines.Count.ShouldBe(2);
    }

    [Test]
    public void StaleSubmissionFailsWithoutMutation()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        baseline.Lines.Add(Pending("first"));
        var submitted = session.CreateChangeSet();

        // An overlapping acknowledgement moves the baseline past the submission.
        baseline.Lines.Single(item => item.Id == 7).Name = "intervening";
        session.AcceptChanges(session.CreateChangeSet());

        var persisted = Persisted(
            Assigned(7, "EXISTING"),
            new TempOrderLine
            {
                Id = 11,
                Name = "FIRST",
                TemporaryId = baseline.Lines[1].TemporaryId,
            }
        );
        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        // Only the intervening edit remains pending.
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReconcileCorrelatesNestedKeyedCollections()
    {
        var lineTemp = Guid.NewGuid();
        var orderTemp = Guid.NewGuid();
        var baseline = new TempOrderBook
        {
            Customer = new TempCustomer { Id = 1, Name = "c" },
            Lines = [Assigned(7, "existing")],
        };
        var session = baseline.CreateEditSession();
        baseline.Customer.Orders.Add(new TempOrderLine { Name = "nested", TemporaryId = lineTemp });
        baseline.Lines.Add(new TempOrderLine { Name = "top", TemporaryId = orderTemp });

        var submitted = session.CreateChangeSet();
        var persisted = new TempOrderBook
        {
            Customer = new TempCustomer
            {
                Id = 1,
                Name = "c",
                Orders =
                [
                    new TempOrderLine
                    {
                        Id = 21,
                        Name = "nested",
                        TemporaryId = lineTemp,
                    },
                ],
            },
            Lines =
            [
                Assigned(7, "existing"),
                new TempOrderLine
                {
                    Id = 22,
                    Name = "top",
                    TemporaryId = orderTemp,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        baseline.Customer.Orders.Single().Id.ShouldBe(21);
        baseline.Lines.Single(item => item.TemporaryId == orderTemp).Id.ShouldBe(22);
    }

    [Test]
    public void ReconcileRetargetsUneditedSubmittedRowsInDirtyMember()
    {
        // Issue #215: when a keyed member carries other pending changes, rows
        // without their own edit must still adopt the server-assigned identity.
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        var first = Pending("first");
        var second = Pending("second");
        baseline.Lines.Add(first);
        baseline.Lines.Add(second);

        var submitted = session.CreateChangeSet();

        // A post-submit edit touches only the first row, so the member is
        // dirty while the second submitted row has no edit of its own.
        baseline.Lines.Single(item => item.TemporaryId == first.TemporaryId).Name = "first v2";

        // The response echoes the submitted values (no server normalization),
        // so adopting the persisted row keeps the live values by construction.
        var persisted = Persisted(
            Assigned(7, "existing"),
            new TempOrderLine
            {
                Id = 11,
                Name = "first",
                TemporaryId = first.TemporaryId,
            },
            new TempOrderLine
            {
                Id = 12,
                Name = "second",
                TemporaryId = second.TemporaryId,
            }
        );

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        // The edited row replays its post-submit value onto the assigned identity.
        var edited = baseline.Lines.Single(item => item.TemporaryId == first.TemporaryId);
        edited.Id.ShouldBe(11);
        edited.Name.ShouldBe("first v2");

        // The unedited submitted row adopts the server-assigned identity while
        // keeping its live values instead of staying at Id=0.
        var unedited = baseline.Lines.Single(item => item.TemporaryId == second.TemporaryId);
        unedited.Id.ShouldBe(12);
        unedited.Name.ShouldBe("second");

        // Only the post-submit edit remains pending.
        var pending = session.CreateChangeSet();
        pending.Lines.GetChange(11).IsEdited.ShouldBeTrue();
        pending.Lines.GetChange(12).IsEmpty.ShouldBeTrue();
    }
}
