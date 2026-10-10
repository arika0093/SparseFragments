using System.Text.Json;

namespace SparseFragments.Tests.Integration;

[SparseFragmentModel]
public partial class LifecycleItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class LifecycleChild
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }
}

[SparseFragmentModel]
public partial class LifecycleOrder
{
    public string Number { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public LifecycleChild? Child { get; set; }

    public List<LifecycleItem> Items { get; set; } = [];

    public Dictionary<string, int> Scores { get; set; } = [];
}

[SparseFragmentModel]
public partial class LifecycleSecret
{
    public string Label { get; set; } = string.Empty;

    [SparseRedactBefore]
    public string Password { get; set; } = string.Empty;
}

// Cross-boundary edit, ChangePayload, apply, acknowledge, and reload
// lifecycle (issue #175). Each test walks the full optimistic-concurrency
// round trip and asserts identity, ordering, revision, and notification
// state at every step instead of re-proving pairwise API behavior.
public sealed class CrossBoundaryLifecycleTests
{
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseFragments.ISparseEditSession<T> session)
        where T : class => session.Model;

    private static LifecycleOrder Baseline() =>
        new()
        {
            Number = "ORD-1",
            Note = "note",
            Child = new LifecycleChild { Host = "db.local", Port = 5432 },
            Items = [new LifecycleItem { Id = 1, Name = "first" }],
            Scores = new Dictionary<string, int> { ["a"] = 1 },
        };

    private static void CheckServerState(
        LifecycleOrder server,
        string number,
        string note,
        string host,
        int port,
        int scoreA,
        (int id, string name)[] items
    )
    {
        server.Number.ShouldBe(number);
        server.Note.ShouldBe(note);
        server.Child!.Host.ShouldBe(host);
        server.Child.Port.ShouldBe(port);
        server.Scores["a"].ShouldBe(scoreA);
        server.Items.Select(static item => (item.Id, item.Name)).ShouldBe(items);
    }

    [Test]
    public void EditPayloadApplyAcknowledgePreservesLaterEdits()
    {
        var baseline = Baseline();
        var clientModel = Baseline();
        var serverModel = Baseline();
        var client = clientModel.CreateEditSession();
        var transitions = new List<LifecycleOrder.ChangeSet>();
        client.TransitionObserved += transitions.Add;

        // One user action edits scalar, nested, keyed, and dictionary members.
        client.BatchEdit(() =>
        {
            client.Observable.Number = "ORD-2";
            client.Observable.Child!.Host = "db.new";
            client.Observable.Items.AddModel(new LifecycleItem { Id = 2, Name = "second" });
            client.Observable.Scores["a"] = 2;
        });
        transitions.Count.ShouldBe(1);
        client.HasChanges.ShouldBeTrue();
        client.Current.Number.ShouldBe("ORD-2");

        var submitted = client.CreateChangeSet();
        submitted.IsEmpty.ShouldBeFalse();
        submitted.Number.After.Value.ShouldBe("ORD-2");
        submitted.Child.Host.After.Value.ShouldBe("db.new");
        submitted.Scores.Edited["a"].ShouldBe(2);

        // Composition and inversion hold for the submitted transition.
        submitted.Compose(submitted.Invert()).IsEmpty.ShouldBeTrue();

        // Versioned transport to the server.
        var payload = submitted.ToPayload();
        var json = JsonSerializer.Serialize(payload);
        json.ShouldContain("\"version\":\"0.1\"");
        var incoming = JsonSerializer.Deserialize<LifecycleOrder.ChangePayload>(json)!;
        incoming.Version.ShouldBe("0.1");

        // The server applies the change onto its copy of the baseline.
        var applied = incoming
            .ToChangeSet()
            .TryApplyTo(serverModel, out var persisted, out var conflicts);
        applied.ShouldBeTrue();
        conflicts.ShouldBeNull();
        persisted.ShouldNotBeNull();
        CheckServerState(
            persisted!,
            "ORD-2",
            "note",
            "db.new",
            5432,
            2,
            [(1, "first"), (2, "second")]
        );

        // The client keeps editing after the snapshot; acknowledgement only
        // advances the baseline by the submitted transition.
        client.Observable.Note = "local-note";
        client.AcceptChanges(submitted);
        client.HasChanges.ShouldBeTrue();
        Raw(client).Note.ShouldBe("local-note");
        client.Current.Note.ShouldBe("local-note");
        transitions.Count.ShouldBe(2);

        var stillPending = client.CreateChangeSet();
        stillPending.Number.IsChanged.ShouldBeFalse();
        stillPending.Note.Before.Value.ShouldBe("note");
        stillPending.Note.After.Value.ShouldBe("local-note");
        stillPending.Child.IsEmpty.ShouldBeTrue();
        stillPending.IsEmpty.ShouldBeFalse();

        // The acknowledged baseline matches the server state plus local edits.
        var serverView = Baseline();
        serverView.Number = "ORD-2";
        serverView.Child = new LifecycleChild { Host = "db.new", Port = 5432 };
        serverView.Items.Add(new LifecycleItem { Id = 2, Name = "second" });
        serverView.Scores["a"] = 2;
        serverView.Note = "local-note";
        LifecycleOrder
            .ChangeSet.Between(
                Optional<LifecycleOrder.Fragment?>.Present(
                    LifecycleOrder.Fragment.From(serverView)
                ),
                Optional<LifecycleOrder.Fragment?>.Present(
                    LifecycleOrder.Fragment.From(Raw(client))
                )
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ConcurrentDisjointEditsMergeAndConflictsRejectAtomically()
    {
        var clientModel = Baseline();
        var client = clientModel.CreateEditSession();
        client.Observable.Number = "ORD-2";
        client.Observable.Note = "local-note";
        var submitted = client.CreateChangeSet();

        // A disjoint server edit merges cleanly and keeps both sides.
        var disjointServer = Baseline();
        disjointServer.Child!.Port = 9999;
        var disjoint = submitted.TryApplyTo(
            disjointServer,
            out var merged,
            out var disjointConflicts
        );
        disjoint.ShouldBeTrue();
        disjointConflicts.ShouldBeNull();
        merged!.Number.ShouldBe("ORD-2");
        merged.Note.ShouldBe("local-note");
        merged.Child!.Port.ShouldBe(9999);

        // A conflicting server edit rejects without touching the server copy.
        var conflictingServer = Baseline();
        conflictingServer.Number = "ORD-9";
        var conflicted = submitted.TryApplyTo(
            conflictingServer,
            out var rejected,
            out var conflicts
        );
        conflicted.ShouldBeFalse();
        rejected.ShouldBeNull();
        conflicts!.Count.ShouldBe(1);
        conflicts[0].PathText.ShouldBe(nameof(LifecycleOrder.Number));
        conflictingServer.Number.ShouldBe("ORD-9");

        // The client reloads around the conflict; untouched state survives.
        var reload = client.Reload(conflictingServer);
        reload.HasConflicts.ShouldBeTrue();
        Raw(client).Number.ShouldBe("ORD-2");
        client.HasChanges.ShouldBeTrue();

        // Rebasing the pending change drops only the conflicting member: the
        // disjoint edit replays onto the server state.
        var rebased = submitted.RebaseOnto(
            Optional<LifecycleOrder.Fragment?>.Present(
                LifecycleOrder.Fragment.From(conflictingServer)
            )
        );
        rebased.HasConflicts.ShouldBeTrue();
        var replayed = rebased.Rebased.TryApplyTo(conflictingServer, out var resolved, out _);
        replayed.ShouldBeTrue();
        resolved!.Number.ShouldBe("ORD-9");
        resolved.Note.ShouldBe("local-note");
    }

    [Test]
    public void VersionRejectionAndRedactedStrictnessHoldBoundaries()
    {
        var baseModel = new LifecycleSecret { Label = "a", Password = "hunter2" };
        var edited = new LifecycleSecret { Label = "b", Password = "rotated" };
        var changes = baseModel.CreateChangeSet(edited);
        var payload = changes.ToPayload();

        // A rejected wire version never becomes a ChangeSet.
        var tampered = JsonSerializer.Deserialize<LifecycleSecret.ChangePayload>(
            JsonSerializer.Serialize(payload)
        )!;
        tampered.Version = "9.9";
        Should.Throw<ArgumentException>(() => tampered.ToChangeSet());

        // Strict endpoints reject redacted before-states without partial apply.
        var current = new LifecycleSecret { Label = "a", Password = "hunter2" };
        var strict = new ChangePayloadRebaseOptions
        {
            RejectChangesWithRedactedBeforeValuesDuringRebase = true,
            RedactedBeforePaths = [nameof(LifecycleSecret.Password)],
        };
        var strictApplied = changes.TryApplyTo(
            current,
            out var strictUpdated,
            out var strictConflicts,
            strict
        );
        strictApplied.ShouldBeFalse();
        strictUpdated.ShouldBeNull();
        strictConflicts!.Count.ShouldBe(1);
        strictConflicts[0].Kind.ShouldBe(SparseConflictKind.RedactedBefore);
        current.Label.ShouldBe("a");
        current.Password.ShouldBe("hunter2");

        // The pass-through mixed path applies the blind set atomically.
        var mixed = JsonSerializer.Deserialize<LifecycleSecret.ChangePayload>(
            JsonSerializer.Serialize(payload)
        )!;
        var passed = mixed.TryApplyMixedTo(current, out var mixedUpdated, out var outcome);
        passed.ShouldBeTrue();
        mixedUpdated!.Label.ShouldBe("b");
        mixedUpdated.Password.ShouldBe("rotated");
        outcome.WriteOnlyPaths.ShouldContain(nameof(LifecycleSecret.Password));
    }

    [Test]
    public void UnassignedKeyReloadPicksUpServerAssignment()
    {
        var clientModel = Baseline();
        var client = clientModel.CreateEditSession();
        // Unassigned rows enter through the live model: observable
        // notifications diff eagerly and cannot represent the sentinel.
        Raw(client).Items.Add(new LifecycleItem { Id = 0, Name = "draft" });
        Raw(client).Number = "ORD-2";
        client.HasChanges.ShouldBeTrue();

        // The server persists the row and assigns its key.
        var serverState = Baseline();
        serverState.Items.Add(new LifecycleItem { Id = 7, Name = "draft" });

        var reload = client.Reload(serverState);
        reload.HasConflicts.ShouldBeFalse();
        // The server-assigned row arrives; the local draft stays pending until
        // the client confirms the assignment and drops the sentinel row.
        Raw(client).Items.Select(static item => item.Id).ShouldBe([1, 7, 0]);
        Raw(client).Number.ShouldBe("ORD-2");
        client.HasChanges.ShouldBeTrue();

        // Server confirmation: drop the draft sentinel. Only the scalar edit
        // stays pending against the assigned baseline.
        Raw(client).Items.RemoveAll(static item => item.Id == 0);
        var pending = client.CreateChangeSet();
        pending.Items.IsEmpty.ShouldBeTrue();
        pending.Number.Before.Value.ShouldBe("ORD-1");
        pending.Number.After.Value.ShouldBe("ORD-2");

        client.AcceptChanges();
        client.HasChanges.ShouldBeFalse();
        Raw(client).Number.ShouldBe("ORD-2");
        Raw(client).Items.Select(static item => item.Id).ShouldBe([1, 7]);
    }

    [Test]
    public void BatchEditHasNoTransactionality()
    {
        var clientModel = Baseline();
        var client = clientModel.CreateEditSession();
        client.TransitionObserved += _ => { };

        // A throwing batch keeps the mutations it already ran.
        Should.Throw<InvalidOperationException>(() =>
            client.BatchEdit(() =>
            {
                client.Observable.Number = "ORD-2";
                throw new InvalidOperationException("simulated UI failure");
            })
        );
        Raw(client).Number.ShouldBe("ORD-2");
        client.HasChanges.ShouldBeTrue();
        client.CreateChangeSet().Number.After.Value.ShouldBe("ORD-2");
    }
}
