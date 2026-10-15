using System.Text.Json;
using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Regression coverage for reconcile  and.</summary>
/// <remarks>Each test names the issue it guards.</remarks>
public sealed class TemporaryKeyReconcileRegressionTests
{
    private static TempOrderLine Assigned(int id, string name) => new() { Id = id, Name = name };

    private static TempOrderLine Pending(string name, Guid? temp = null) =>
        new() { Name = name, TemporaryId = temp ?? Guid.NewGuid() };

    private static TempOrderHolder SubmittedHolderAfter(
        TempOrderHolder baseline,
        TempOrderHolder.ChangeSet submitted
    ) => submitted.ToPatch().Apply(TempOrderHolder.Fragment.From(baseline)).Value!.ToModel();

    private static TempOrderBook SubmittedBookAfter(
        TempOrderBook baseline,
        TempOrderBook.ChangeSet submitted
    ) => submitted.ToPatch().Apply(TempOrderBook.Fragment.From(baseline)).Value!.ToModel();

    private static TempCustomerHolder SubmittedCustomersAfter(
        TempCustomerHolder baseline,
        TempCustomerHolder.ChangeSet submitted
    ) => submitted.ToPatch().Apply(TempCustomerHolder.Fragment.From(baseline)).Value!.ToModel();

    [Test]
    public void ReconcileAndApplyAdoptsRemoteValuesWhenSubmittedValuesArePersisted()
    {
        var live = new TempReconcileScalar { Customer = "original", Total = 10 };
        var session = live.CreateEditSession();
        live.Total = 20;
        var submitted = session.CreateChangeSet();
        var persisted = new TempReconcileScalar { Customer = "remote", Total = 20 };

        session.TryReconcileAndApply(submitted, persisted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        live.Customer.ShouldBe("remote");
        live.Total.ShouldBe(20);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ReconcileAndApplyAdoptsRemoteValuesAndKeepsPostSubmitEdits()
    {
        var live = new TempReconcileScalar { Customer = "original", Total = 10 };
        var session = live.CreateEditSession();
        live.Total = 20;
        var submitted = session.CreateChangeSet();
        live.Total = 21;
        var persisted = new TempReconcileScalar { Customer = "remote", Total = 20 };

        session.TryReconcileAndApply(submitted, persisted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        live.Customer.ShouldBe("remote");
        live.Total.ShouldBe(21);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ReconcileAndApplyReturnsTypedOverlapConflictWithoutMutatingState()
    {
        var live = new TempReconcileScalar { Customer = "original", Total = 10 };
        var session = live.CreateEditSession();
        live.Total = 20;
        var submitted = session.CreateChangeSet();
        live.Total = 21;
        var conflictingPersisted = new TempReconcileScalar { Customer = "remote", Total = 22 };

        session
            .TryReconcileAndApply(submitted, conflictingPersisted, out var conflicts)
            .ShouldBeFalse();

        conflicts.ShouldNotBeNull().ShouldHaveSingleItem();
        conflicts![0].Path.RootType.ShouldBe(typeof(TempReconcileScalar));
        conflicts[0].PathText.ShouldBe(nameof(TempReconcileScalar.Total));
        conflicts[0].Kind.ShouldBe(SparseConflictKind.Scalar);
        live.Customer.ShouldBe("original");
        live.Total.ShouldBe(21);
        session.HasChanges.ShouldBeTrue();

        var persistedSubmission = new TempReconcileScalar { Customer = "remote", Total = 20 };
        session
            .TryReconcileAndApply(submitted, persistedSubmission, out var retryConflicts)
            .ShouldBeTrue();
        retryConflicts.ShouldBeNull();
        live.Customer.ShouldBe("remote");
        live.Total.ShouldBe(21);
    }

    [Test]
    public void ReconcileAndApplyUsesAuthoritativeKeysAndPreservesPostSubmitEdits()
    {
        var temporaryKey = Guid.NewGuid();
        var baseline = new TempOrderHolder();
        var session = baseline.CreateEditSession();
        baseline.Lines.Add(new TempOrderLine { Name = "before", TemporaryId = temporaryKey });
        var submitted = session.CreateChangeSet();

        baseline.Lines[0].Name = "edited while saving";
        var persisted = new TempOrderHolder
        {
            Lines =
            [
                new TempOrderLine
                {
                    Id = 31,
                    Name = "server normalized",
                    TemporaryId = temporaryKey,
                },
            ],
        };

        session.TryReconcileAndApply(submitted, persisted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        baseline.Lines[0].Id.ShouldBe(31);
        baseline.Lines[0].Name.ShouldBe("edited while saving");
        session
            .CreateChangeSet()
            .Lines.GetChange(31)
            .Edit.Name.After.Value.ShouldBe("edited while saving");
    }

    [Test]
    public void ReconcileAndApplyReturnsTypedRootConflictWithoutMutatingOnIdentityFailure()
    {
        var temporaryKey = Guid.NewGuid();
        var baseline = new TempOrderHolder();
        var session = baseline.CreateEditSession();
        baseline.Lines.Add(new TempOrderLine { Name = "before", TemporaryId = temporaryKey });
        var submitted = session.CreateChangeSet();
        var liveRow = baseline.Lines[0];
        var persisted = new TempOrderHolder
        {
            Lines =
            [
                new TempOrderLine
                {
                    Id = 31,
                    Name = "first",
                    TemporaryId = temporaryKey,
                },
                new TempOrderLine
                {
                    Id = 32,
                    Name = "duplicate",
                    TemporaryId = temporaryKey,
                },
            ],
        };

        session.TryReconcileAndApply(submitted, persisted, out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull().ShouldHaveSingleItem();
        conflicts![0].Path.RootType.ShouldBe(typeof(TempOrderHolder));
        conflicts[0].Kind.ShouldBe(SparseConflictKind.Reconciliation);
        conflicts[0].Reason.ShouldNotBeNullOrEmpty();
        ReferenceEquals(baseline.Lines[0], liveRow).ShouldBeTrue();
        baseline.Lines[0].Id.ShouldBe(0);
        baseline.Lines[0].Name.ShouldBe("before");
        session.HasChanges.ShouldBeTrue();
    }

    // the same Guid reused across two keyed collection paths
    // reconciles independently instead of failing as a global duplicate.
    [Test]
    public void SameTemporaryGuidAcrossSiblingCollectionsReconciles()
    {
        var shared = Guid.NewGuid();
        var baseline = new TempOrderBook
        {
            Customer = new TempCustomer { Id = 1, Name = "c" },
            Lines = [Assigned(7, "existing")],
        };
        var session = baseline.CreateEditSession();
        baseline.Customer.Orders.Add(new TempOrderLine { Name = "nested", TemporaryId = shared });
        baseline.Lines.Add(new TempOrderLine { Name = "top", TemporaryId = shared });

        var submitted = session.CreateChangeSet();

        // The submitted transition survives a ChangeSet/Payload round-trip
        // with the shared Guid visible under both collection paths.
        var payload = submitted.ToPayload();
        var json = JsonSerializer.Serialize(payload);
        var revived = JsonSerializer.Deserialize<TempOrderBook.ChangePayload>(json)!.ToChangeSet();
        revived.Customer.Orders.GetTemporaryChange(shared).IsAdded.ShouldBeTrue();
        revived.Lines.GetTemporaryChange(shared).IsAdded.ShouldBeTrue();

        // Post-submit edits continue under both paths while saving.
        baseline.Customer.Orders.Single().Name = "nested v2";
        baseline.Lines.Single(item => item.TemporaryId == shared).Name = "top v2";

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
                        TemporaryId = shared,
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
                    TemporaryId = shared,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        // No cross-targeting: each path lands its own permanent key while
        // replaying its own post-submit value.
        var nested = baseline.Customer.Orders.Single();
        nested.Id.ShouldBe(21);
        nested.Name.ShouldBe("nested v2");
        var top = baseline.Lines.Single(item => item.TemporaryId == shared);
        top.Id.ShouldBe(22);
        top.Name.ShouldBe("top v2");

        // Only the two post-submit edits stay pending, addressed by key.
        var pending = session.CreateChangeSet();
        pending.Customer.Orders.GetChange(21).IsEdited.ShouldBeTrue();
        pending.Lines.GetChange(22).IsEdited.ShouldBeTrue();
        pending.Lines.GetChange(7).IsEmpty.ShouldBeTrue();
        TempOrderBook
            .SparsePath.Customer.Orders.TemporaryKey(shared)
            .ShouldNotBe(TempOrderBook.SparsePath.Lines.TemporaryKey(shared));
    }

    // the same scope still rejects a duplicated Guid without
    // touching the live session.
    [Test]
    public void DuplicateTemporaryGuidWithinOneCollectionFailsWithoutMutation()
    {
        var shared = Guid.NewGuid();
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        var first = Pending("first", shared);
        baseline.Lines.Add(first);
        var liveRow = baseline.Lines[1];
        var submitted = session.CreateChangeSet();

        var persisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(7, "EXISTING"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "a",
                    TemporaryId = shared,
                },
                new TempOrderLine
                {
                    Id = 12,
                    Name = "b",
                    TemporaryId = shared,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        baseline.Lines.Count.ShouldBe(2);
        baseline.Lines[1].Id.ShouldBe(0);
        object.ReferenceEquals(baseline.Lines[1], liveRow).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Lines.Added.Count.ShouldBe(1);
    }

    // sibling Orders under distinct assigned parents correlate
    // through scopes that include the parent key.
    [Test]
    public void SameTemporaryGuidUnderDistinctAssignedParentsReconciles()
    {
        var shared = Guid.NewGuid();
        var baseline = new TempCustomerHolder
        {
            Customers =
            [
                new TempCustomer { Id = 1, Name = "one" },
                new TempCustomer { Id = 2, Name = "two" },
            ],
        };
        var session = baseline.CreateEditSession();
        baseline.Customers[0].Orders.Add(new TempOrderLine { Name = "a", TemporaryId = shared });
        baseline.Customers[1].Orders.Add(new TempOrderLine { Name = "b", TemporaryId = shared });

        var submitted = session.CreateChangeSet();
        var persisted = new TempCustomerHolder
        {
            Customers =
            [
                new TempCustomer
                {
                    Id = 1,
                    Name = "one",
                    Orders =
                    [
                        new TempOrderLine
                        {
                            Id = 11,
                            Name = "a",
                            TemporaryId = shared,
                        },
                    ],
                },
                new TempCustomer
                {
                    Id = 2,
                    Name = "two",
                    Orders =
                    [
                        new TempOrderLine
                        {
                            Id = 12,
                            Name = "b",
                            TemporaryId = shared,
                        },
                    ],
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        baseline.Customers[0].Orders.Single().Id.ShouldBe(11);
        baseline.Customers[1].Orders.Single().Id.ShouldBe(12);
    }

    // an untouched assigned row adopts server normalization even
    // when a sibling row keeps the member dirty.
    [Test]
    public void UntouchedAssignedRowAdoptsNormalizationInDirtyMember()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "old"), Assigned(8, "other")] };
        var session = baseline.CreateEditSession();
        var added = Pending("added");
        baseline.Lines.Add(added);
        var submitted = session.CreateChangeSet();

        // Only the pending row is edited after submission.
        baseline.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name = "added v2";

        var persisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(7, "NORMALIZED"),
                Assigned(8, "other"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "added",
                    TemporaryId = added.TemporaryId,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        baseline.Lines.Single(item => item.Id == 7).Name.ShouldBe("NORMALIZED");
        var pending = session.CreateChangeSet();
        pending.Lines.GetChange(7).IsEmpty.ShouldBeTrue();
        pending.Lines.GetChange(8).IsEmpty.ShouldBeTrue();
        // The submitted row now lives under its assigned key with the
        // post-submit value replayed onto it.
        pending.Lines.GetChange(11).IsEdited.ShouldBeTrue();
    }

    // an edited row replays its patch while an untouched row
    // adopts; both arrive at the server value through different paths.
    [Test]
    public void EditedRowReplaysWhileUntouchedRowAdopts()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(7, "old"), Assigned(8, "old")] };
        var session = baseline.CreateEditSession();
        var submitted = session.CreateChangeSet();

        // A genuine post-submit edit lands exactly on the normalized value.
        baseline.Lines.Single(item => item.Id == 8).Name = "NORMALIZED";
        session.CreateChangeSet().Lines.GetChange(8).IsEdited.ShouldBeTrue();

        var persisted = new TempOrderHolder
        {
            Lines = [Assigned(7, "NORMALIZED"), Assigned(8, "NORMALIZED")],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        baseline.Lines.Single(item => item.Id == 7).Name.ShouldBe("NORMALIZED");
        baseline.Lines.Single(item => item.Id == 8).Name.ShouldBe("NORMALIZED");
        // Neither row reports a remaining change: the untouched row adopted,
        // and the edited row converged with its own edit.
        var pending = session.CreateChangeSet();
        pending.IsEmpty.ShouldBeTrue();
        session.HasChanges.ShouldBeFalse();
    }

    // normalization reaches nested members while the top member
    // carries the temporary submission.
    [Test]
    public void NestedNormalizationAdoptsAlongsideTemporarySubmission()
    {
        var baseline = new TempOrderBook
        {
            Customer = new TempCustomer { Id = 1, Name = "c" },
            Lines = [Assigned(7, "old")],
        };
        var session = baseline.CreateEditSession();
        var added = Pending("added");
        baseline.Lines.Add(added);
        var submitted = session.CreateChangeSet();

        baseline.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name = "added v2";

        var persisted = new TempOrderBook
        {
            Customer = new TempCustomer { Id = 1, Name = "NORMALIZED" },
            Lines =
            [
                Assigned(7, "NORMALIZED"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "added",
                    TemporaryId = added.TemporaryId,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        baseline.Customer.Name.ShouldBe("NORMALIZED");
        baseline.Lines.Single(item => item.Id == 7).Name.ShouldBe("NORMALIZED");
        var pending = session.CreateChangeSet();
        pending.Customer.IsEmpty.ShouldBeTrue();
        pending.Lines.GetChange(7).IsEmpty.ShouldBeTrue();
    }

    // with no post-submit edits the persisted order wins and the
    // session converges with no remaining changes.
    [Test]
    public void CleanReorderAdoptsPersistedOrderWithoutChanges()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "a"), Assigned(2, "b")] };
        var session = baseline.CreateEditSession();
        var added = Pending("new");
        baseline.Lines.Add(added);
        var submitted = session.CreateChangeSet();
        var submittedAfter = SubmittedHolderAfter(
            new TempOrderHolder { Lines = [Assigned(1, "a"), Assigned(2, "b")] },
            submitted
        );

        _ = submittedAfter;
        var persisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(2, "B"),
                Assigned(1, "A"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "NEW",
                    TemporaryId = added.TemporaryId,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        baseline.Lines.Select(item => item.Id).ShouldBe([2, 1, 11]);
        baseline.Lines.Select(item => item.Name).ShouldBe(["B", "A", "NEW"]);
        session.HasChanges.ShouldBeFalse();
    }

    // server-added rows survive a clean reconcile.
    [Test]
    public void ServerAddedRowSurvivesCleanReconcile()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "a")] };
        var session = baseline.CreateEditSession();
        var added = Pending("new");
        baseline.Lines.Add(added);
        var submitted = session.CreateChangeSet();

        var persisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(1, "A"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "NEW",
                    TemporaryId = added.TemporaryId,
                },
                Assigned(100, "SERVER"),
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        baseline.Lines.Select(item => item.Id).ShouldBe([1, 11, 100]);
        session.HasChanges.ShouldBeFalse();
    }

    // server-dropped rows disappear on a clean reconcile.
    [Test]
    public void ServerDroppedRowDisappearsOnCleanReconcile()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "a"), Assigned(2, "b")] };
        var session = baseline.CreateEditSession();
        baseline.Lines.Single(item => item.Id == 1).Name = "a2";
        var submitted = session.CreateChangeSet();

        var persisted = new TempOrderHolder { Lines = [Assigned(1, "A2")] };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        baseline.Lines.Select(item => item.Id).ShouldBe([1]);
        baseline.Lines.Single().Name.ShouldBe("A2");
        session.HasChanges.ShouldBeFalse();
    }

    // a concurrent local reorder is preserved over the server
    // order instead of being silently replaced.
    [Test]
    public void ConcurrentLocalReorderWinsOverServerOrder()
    {
        var baseline = new TempOrderHolder
        {
            Lines = [Assigned(1, "a"), Assigned(2, "b"), Assigned(3, "c")],
        };
        var session = baseline.CreateEditSession();
        baseline.Lines.Single(item => item.Id == 1).Name = "a2";
        var submitted = session.CreateChangeSet();

        // Local reorder while the save is in flight.
        var reordered = new List<TempOrderLine>
        {
            baseline.Lines[1],
            baseline.Lines[0],
            baseline.Lines[2],
        };
        baseline.Lines.Clear();
        baseline.Lines.AddRange(reordered);

        var persisted = new TempOrderHolder
        {
            Lines = [Assigned(3, "C"), Assigned(2, "B"), Assigned(1, "A2")],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        baseline.Lines.Select(item => item.Id).ShouldBe([2, 1, 3]);
        var pending = session.CreateChangeSet();
        pending.Lines.OrderChanged.ShouldBeTrue();
        pending.Lines.AfterOrder.ShouldBe([2, 1, 3]);
    }

    // a post-submit insertion keeps its live position while a
    // server addition joins the authoritative membership.
    [Test]
    public void PostSubmitInsertionKeepsPositionWithServerAddition()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "a"), Assigned(2, "b")] };
        var session = baseline.CreateEditSession();
        var first = Pending("first");
        baseline.Lines.Add(first);
        var submitted = session.CreateChangeSet();

        var late = Pending("late");
        baseline.Lines.Insert(1, late);

        var persisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(1, "A"),
                Assigned(2, "B"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "FIRST",
                    TemporaryId = first.TemporaryId,
                },
                Assigned(100, "SERVER"),
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        baseline.Lines.Select(item => item.Name).ShouldBe(["A", "late", "B", "FIRST", "SERVER"]);
        var pending = session.CreateChangeSet();
        pending.Lines.GetTemporaryChange(late.TemporaryId!.Value).IsAdded.ShouldBeTrue();
        pending.Lines.GetChange(100).IsEmpty.ShouldBeTrue();
    }

    // an init-only list rejects a dirty reconcile before any
    // mutation, keeping contents, references, baseline and observables.
    [Test]
    public void InitOnlyListFailureLeavesEverythingUntouched()
    {
        var baseline = new TempInitHolder { Lines = [Assigned(7, "existing")] };
        var session = baseline.CreateEditSession();
        var added = Pending("added");
        baseline.Lines.Add(added);
        var listReference = baseline.Lines;
        var existingRow = baseline.Lines[0];
        var addedRow = baseline.Lines[1];
        var submitted = session.CreateChangeSet();

        var persisted = new TempInitHolder
        {
            Lines =
            [
                Assigned(7, "EXISTING"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "ADDED",
                    TemporaryId = added.TemporaryId,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        object.ReferenceEquals(baseline.Lines, listReference).ShouldBeTrue();
        baseline.Lines.Count.ShouldBe(2);
        object.ReferenceEquals(baseline.Lines[0], existingRow).ShouldBeTrue();
        object.ReferenceEquals(baseline.Lines[1], addedRow).ShouldBeTrue();
        baseline.Lines[1].Id.ShouldBe(0);
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Lines.Added.Count.ShouldBe(1);

        // The writable twin of the same scenario succeeds as a control.
        var writable = new TempOrderHolder { Lines = [Assigned(7, "existing")] };
        var writableSession = writable.CreateEditSession();
        var writableAdded = Pending("added", added.TemporaryId);
        writable.Lines.Add(writableAdded);
        var writableSubmitted = writableSession.CreateChangeSet();
        var writablePersisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(7, "EXISTING"),
                new TempOrderLine
                {
                    Id = 11,
                    Name = "ADDED",
                    TemporaryId = added.TemporaryId,
                },
            ],
        };
        writableSession
            .TryReconcile(writableSubmitted, writablePersisted, out var writableError)
            .ShouldBeTrue();
        writableError.ShouldBeNull();
    }

    // a late failure restores the earlier collection instead of
    // leaving it partially reconciled.
    [Test]
    public void LateFailureRestoresEarlierCollection()
    {
        var baseline = new TempOrderBook
        {
            Customer = new TempCustomer { Id = 1, Name = "c" },
            Lines = [Assigned(7, "existing")],
        };
        var session = baseline.CreateEditSession();
        var nestedTemp = Guid.NewGuid();
        var topTemp = Guid.NewGuid();
        baseline.Customer.Orders.Add(
            new TempOrderLine { Name = "nested", TemporaryId = nestedTemp }
        );
        baseline.Lines.Add(new TempOrderLine { Name = "top", TemporaryId = topTemp });
        var submitted = session.CreateChangeSet();

        // A post-submit assigned insertion collides with a server addition.
        baseline.Lines.Add(Assigned(100, "local"));
        var ordersReference = baseline.Customer.Orders;
        var nestedRow = baseline.Customer.Orders[0];
        var linesReference = baseline.Lines;
        var existingRow = baseline.Lines[0];

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
                        TemporaryId = nestedTemp,
                    },
                ],
            },
            Lines =
            [
                Assigned(7, "EXISTING"),
                new TempOrderLine
                {
                    Id = 22,
                    Name = "TOP",
                    TemporaryId = topTemp,
                },
                Assigned(100, "SERVER"),
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        object.ReferenceEquals(baseline.Customer.Orders, ordersReference).ShouldBeTrue();
        object.ReferenceEquals(baseline.Customer.Orders.Single(), nestedRow).ShouldBeTrue();
        baseline.Customer.Orders.Single().Id.ShouldBe(0);
        object.ReferenceEquals(baseline.Lines, linesReference).ShouldBeTrue();
        object.ReferenceEquals(baseline.Lines[0], existingRow).ShouldBeTrue();
        baseline.Lines.Count.ShouldBe(3);
        session.HasChanges.ShouldBeTrue();
    }

    // a foreign submission must not succeed merely because the
    // live model happens to equal the persisted model.
    [Test]
    public void ForeignSubmissionWithMatchingLiveAndPersistedFailsWithoutMutation()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "one")] };
        var session = baseline.CreateEditSession();
        var liveReference = baseline.Lines;
        var liveRow = baseline.Lines[0];

        var foreignHolder = new TempOrderHolder { Lines = [Assigned(9, "other")] };
        var foreignSession = foreignHolder.CreateEditSession();
        foreignHolder.Lines.Add(Pending("foreign"));
        var foreign = foreignSession.CreateChangeSet();

        // Live and authority agree on B, which the foreign submission never
        // described.
        baseline.Lines[0].Name = "b-live";
        var persisted = new TempOrderHolder { Lines = [Assigned(1, "b-live")] };

        session.TryReconcile(foreign, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();

        object.ReferenceEquals(baseline.Lines, liveReference).ShouldBeTrue();
        object.ReferenceEquals(baseline.Lines[0], liveRow).ShouldBeTrue();
        baseline.Lines.Count.ShouldBe(1);
        baseline.Lines[0].Name.ShouldBe("b-live");
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Lines.GetChange(1).IsEdited.ShouldBeTrue();
    }

    // a stale submission (baseline moved past its before-state,
    // with a reorder) must not succeed when live equals persisted.
    [Test]
    public void StaleSubmissionWithMatchingLiveAndPersistedFailsWithoutMutation()
    {
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "one"), Assigned(2, "two")] };
        var session = baseline.CreateEditSession();
        baseline.Lines[0].Name = "c1";
        var stale = session.CreateChangeSet();

        baseline.Lines[0].Name = "d1";
        var reordered = baseline.Lines[0];
        baseline.Lines.RemoveAt(0);
        baseline.Lines.Add(reordered);
        session.AcceptChanges();
        var liveReference = baseline.Lines;

        // Live and authority agree on the reordered D state.
        var persisted = new TempOrderHolder { Lines = [Assigned(2, "two"), Assigned(1, "d1")] };

        session.TryReconcile(stale, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();

        object.ReferenceEquals(baseline.Lines, liveReference).ShouldBeTrue();
        baseline.Lines.Count.ShouldBe(2);
        baseline.Lines[0].Id.ShouldBe(2);
        baseline.Lines[1].Name.ShouldBe("d1");
        session.HasChanges.ShouldBeFalse();
    }

    // retrying the same verified submission reference while live agrees with
    // the authority stays a success, including after the baseline moved on.
    [Test]
    public void VerifiedRetryAfterBaselineMovementSucceeds()
    {
        var temp = Guid.NewGuid();
        var baseline = new TempOrderHolder { Lines = [Assigned(1, "one")] };
        var session = baseline.CreateEditSession();
        baseline.Lines.Add(new TempOrderLine { Name = "new", TemporaryId = temp });
        var submitted = session.CreateChangeSet();
        var persisted = new TempOrderHolder
        {
            Lines =
            [
                Assigned(1, "one"),
                new TempOrderLine
                {
                    Id = 5,
                    Name = "new",
                    TemporaryId = temp,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        baseline.Lines.Single(item => item.TemporaryId == temp).Id.ShouldBe(5);

        // Immediate retry with live agreeing with the authority succeeds.
        session.TryReconcile(submitted, persisted, out error).ShouldBeTrue();
        error.ShouldBeNull();

        // Move the baseline on, then retry the same reference with a fresh
        // authority snapshot that matches live.
        baseline.Lines[0].Name = "one-v2";
        session.AcceptChanges();
        var moved = new TempOrderHolder
        {
            Lines =
            [
                Assigned(1, "one-v2"),
                new TempOrderLine
                {
                    Id = 5,
                    Name = "new",
                    TemporaryId = temp,
                },
            ],
        };

        session.TryReconcile(submitted, moved, out error).ShouldBeTrue();
        error.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
    }
}
