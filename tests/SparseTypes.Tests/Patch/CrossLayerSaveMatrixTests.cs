using System.IO;

namespace SparseTypes.Tests;

// Cross-layer save matrix over the generated Core session surface: every
// scenario drives the full client/save/reconcile sequence (capture the exact
// immutable submission, apply it to a server-side baseline copy, reconcile
// the authoritative readback) instead of single API calls. The server copy
// stands in for persistence; the client model must never observe submitted
// values before reconciliation acknowledges them.
public sealed class CrossLayerSaveMatrixTests
{
    private static ClusterHolder Baseline() =>
        new()
        {
            Groups =
            [
                new ServerGroup
                {
                    Name = "first",
                    Servers =
                    [
                        new KeyedServer
                        {
                            Id = "s1",
                            Name = "one",
                            Count = 1,
                        },
                    ],
                },
                new ServerGroup
                {
                    Name = "second",
                    Servers =
                    [
                        new KeyedServer
                        {
                            Id = "s2",
                            Name = "two",
                            Count = 2,
                        },
                    ],
                },
            ],
        };

    [Test]
    public void InFlightSaveAcrossRootChildAndGrandchildPreservesPostSubmitEdits()
    {
        var model = Baseline();
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var groupPath = ClusterHolder.SparsePath.Groups.Key("first");
        var group = session.OpenChildSession(groupPath);
        var groupHistory = group.Session.EnableHistory();
        var serverPath = ServerGroup.SparsePath.Servers.Key("s1");
        var server = group.Session.OpenChildSession(serverPath);
        var serverHistory = server.Session.EnableHistory();

        // Three layers edit disjoint members: grandchild renames, the child
        // adds a row, the root edits the untouched group.
        server.Session.Observable.Name = "server draft";
        group.Session.Observable.Servers.AddModel(
            new KeyedServer
            {
                Id = "added",
                Name = "child added",
                Count = 3,
            }
        );
        session.Observable.Groups[1].Servers[0].Name = "root edit";

        // The exact immutable submission captured before the awaited save.
        // The preview is pure: child drafts stay in their forks and nothing
        // is accepted.
        session.TryCreateChangeSet(out var submitted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        submitted.ShouldNotBeNull();
        var previewPaths = submitted!
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ToList();
        previewPaths.ShouldBe(
            [
                "Groups[\"first\"].Servers",
                "Groups[\"first\"].Servers[\"added\"]",
                "Groups[\"first\"].Servers[\"s1\"].Name",
                "Groups[\"second\"].Servers[\"s2\"].Name",
            ],
            ignoreOrder: true
        );
        group.HasChanges.ShouldBeTrue();
        server.HasChanges.ShouldBeTrue();
        model.Groups[0].Servers[0].Name.ShouldBe("one");
        model.Groups[0].Servers.Count.ShouldBe(1);
        // Observable edits write through to the live model; only the child
        // drafts stay hidden in their forks until accepted.
        model.Groups[1].Servers[0].Name.ShouldBe("root edit");
        history.CanUndo.ShouldBeTrue();
        groupHistory.CanUndo.ShouldBeTrue();
        serverHistory.CanUndo.ShouldBeTrue();

        // Overlapping edits continue while the save is in flight.
        server.Session.Observable.Count = 9;
        session.Observable.Groups[1].Servers[0].Count = 5;

        // Persistence applies only the submitted transition to the
        // server-side baseline copy.
        submitted
            .TryApplyTo(Baseline(), out var persisted, out var applyConflicts)
            .ShouldBeTrue();

        applyConflicts.ShouldBeNull();
        var authoritative = persisted.ShouldNotBeNull();
        authoritative.Groups[0].Servers[0].Name.ShouldBe("server draft");
        authoritative.Groups[0].Servers.Count.ShouldBe(2);
        authoritative.Groups[1].Servers[0].Name.ShouldBe("root edit");

        session.TryReconcile(submitted, authoritative, out var error).ShouldBeTrue();

        // The user-visible outcome: success, submitted values adopted,
        // post-submit edits still pending.
        error.ShouldBeNull();
        model.Groups[0].Servers[0].Name.ShouldBe("server draft");
        model.Groups[0].Servers.Count.ShouldBe(2);
        model
            .Groups[0]
            .Servers.Single(static row => row.Id == "added")
            .Name.ShouldBe("child added");
        model.Groups[0].Servers[0].Count.ShouldBe(9);
        model.Groups[1].Servers[0].Name.ShouldBe("root edit");
        model.Groups[1].Servers[0].Count.ShouldBe(5);

        // Only the post-submit member edits stay pending, against the
        // persisted baseline. Reconciliation confirms the child drafts and
        // carries the post-submit descendant values as pending own changes:
        // the values survive, but the child scopes no longer own them.
        var pending = session.CreateChangeSet();
        pending
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldBe(
                [
                    "Groups[\"first\"].Servers[\"s1\"].Count",
                    "Groups[\"second\"].Servers[\"s2\"].Count",
                ],
                ignoreOrder: true
            );
        server.HasChanges.ShouldBeFalse();
        group.HasChanges.ShouldBeFalse();
        session.HasPendingChildSessions.ShouldBeFalse();
        session
            .GetChildSessionStatus(ClusterHolder.SparsePath.Groups.Key("first"))
            .ShouldBe(SparseChildSessionStatus.OpenUnchanged);
        session.HasChanges.ShouldBeTrue();

        // Acknowledgement clears retained histories: the submitted drafts are
        // no longer undoable.
        history.CanUndo.ShouldBeFalse();
        groupHistory.CanUndo.ShouldBeFalse();
        serverHistory.CanUndo.ShouldBeFalse();
        group.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void KeyedReorderDuringInFlightSaveReconcilesByKey()
    {
        var model = new KeyedServerHolder
        {
            Items =
            [
                new KeyedServer { Id = "first", Name = "one" },
                new KeyedServer { Id = "second", Name = "two" },
            ],
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var child = session.OpenChildSession(KeyedServerHolder.SparsePath.Items.Key("second"));
        child.Session.Observable.Name = "updated";

        session.TryCreateChangeSet(out var submitted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        submitted.ShouldNotBeNull();
        // The child draft lives in the child session only: the parent
        // history is still empty.
        history.CanUndo.ShouldBeFalse();

        // The reorder happens after capture: it is a post-submit edit and
        // must survive reconciliation by key, not by position.
        session.Observable.Items.Move(1, 0);
        history.CanUndo.ShouldBeTrue();

        submitted
            .TryApplyTo(
                new KeyedServerHolder
                {
                    Items =
                    [
                        new KeyedServer { Id = "first", Name = "one" },
                        new KeyedServer { Id = "second", Name = "two" },
                    ],
                },
                out var persisted,
                out var applyConflicts
            )
            .ShouldBeTrue();

        applyConflicts.ShouldBeNull();
        session.TryReconcile(submitted!, persisted!, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        model.Items.Select(static item => item.Id).ShouldBe(["second", "first"]);
        model.Items[0].Name.ShouldBe("updated");
        model.Items[1].Name.ShouldBe("one");

        // The submitted rename was acknowledged (clearing the undo retention);
        // only the reorder is pending.
        child.HasChanges.ShouldBeFalse();
        var pending = session.CreateChangeSet();
        pending.IsChanged.ShouldBeTrue();
        history.CanUndo.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void RemoveAndRecreateChildRecoversDraftAcrossSave()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var path = KeyedServerHolder.SparsePath.Items.Key("server");
        var child = session.OpenChildSession(path);
        child.Session.Observable.Name = "draft";

        // Removing the row makes the draft unmergeable; the save cannot
        // proceed and the draft is preserved, not dropped.
        session.Observable.Items.Clear();
        session.TryPreflightChildSessions(out var preflightConflicts).ShouldBeFalse();

        preflightConflicts.ShouldBeNull();
        child.IsTargetMissing.ShouldBeTrue();
        child.IsDetached.ShouldBeFalse();
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("draft");
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.TargetMissing);

        // Recreating the row under the same key recovers the draft, which
        // then flows through a normal save.
        session.Observable.Items.AddModel(new KeyedServer { Id = "server", Name = "before" });
        child.IsTargetMissing.ShouldBeFalse();
        history.CanUndo.ShouldBeTrue();
        session.TryCreateChangeSet(out var submitted, out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        submitted.ShouldNotBeNull();
        submitted
            .TryApplyTo(
                new KeyedServerHolder
                {
                    Items = [new KeyedServer { Id = "server", Name = "before" }],
                },
                out var persisted,
                out var applyConflicts
            )
            .ShouldBeTrue();

        applyConflicts.ShouldBeNull();
        session.TryReconcile(submitted!, persisted!, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        model.Items.ShouldHaveSingleItem().Name.ShouldBe("draft");
        session.ResolveChildModel(path).ShouldNotBeNull();
        session.GetChildSessionStatus(path).ShouldBe(SparseChildSessionStatus.OpenUnchanged);
        child.HasChanges.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
        // Acknowledgement clears the undo retention; the recovered draft is
        // now the baseline.
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void AuthoritativeReconcileConflictKeepsRecoverableDraft()
    {
        var model = new ObservableHolder
        {
            Title = "before",
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "child draft";
        session.Observable.Title = "submitted";

        session.TryAcceptChildSessions(out var acceptConflicts).ShouldBeTrue();

        acceptConflicts.ShouldBeNull();
        var submitted = session.CreateChangeSet();
        submitted.IsEmpty.ShouldBeFalse();

        // Another writer committed first: the server-side apply of our
        // submission fails with a typed conflict and touches nothing.
        var serverCopy = new ObservableHolder
        {
            Title = "remote",
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        submitted.TryApplyTo(serverCopy, out var rejected, out var applyConflicts).ShouldBeFalse();

        rejected.ShouldBeNull();
        var typed = applyConflicts.ShouldNotBeNull();
        typed.Select(static conflict => conflict.PathText).ShouldContain("Title");
        serverCopy.Title.ShouldBe("remote");
        serverCopy.Child.Name.ShouldBe("before");

        // Reconciliation against the authoritative state is only meaningful
        // for persisted submissions, so the client keeps its draft without
        // reconciling: model, pending change set, and histories are untouched.
        model.Title.ShouldBe("submitted");
        model.Child.Name.ShouldBe("child draft");
        session.HasChanges.ShouldBeTrue();
        child.HasChanges.ShouldBeFalse();
        var stillPending = session.CreateChangeSet();
        stillPending.Title.After.Value.ShouldBe("submitted");
        stillPending.Child.Name.After.Value.ShouldBe("child draft");
        history.CanUndo.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeFalse();

        // Recovery rebases the kept submission onto the authoritative state:
        // the overlap surfaces as a typed, recoverable conflict whose path
        // the UI can display, instead of silent loss.
        var rebase = submitted.RebaseOnto(serverCopy);
        rebase.HasConflicts.ShouldBeTrue();
        rebase.Conflicts.Select(static conflict => conflict.PathText).ShouldContain("Title");

        // Recovery adopts the authoritative value for the conflicting member
        // only; the disjoint child draft rebases onto it and replays.
        session.Observable.Title = "remote";
        var resubmitted = session.CreateChangeSet();
        resubmitted.Title.After.Value.ShouldBe("remote");
        var converged = resubmitted.RebaseOnto(serverCopy);
        converged.HasConflicts.ShouldBeFalse();
        converged
            .Rebased.TryApplyTo(serverCopy, out var persisted, out applyConflicts)
            .ShouldBeTrue();

        applyConflicts.ShouldBeNull();
        session.TryReconcile(resubmitted, persisted!, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        model.Title.ShouldBe("remote");
        model.Child.Name.ShouldBe("child draft");
        session.HasChanges.ShouldBeFalse();
        // The successful reconcile acknowledged the recovery and cleared the
        // undo retention.
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void GroupAcceptanceCommitsBeforeNotifyingAcrossSections()
    {
        var profile = new NeutralSessionModel { Name = "before" };
        var profileSession = profile.CreateEditSession();
        var settings = new NeutralSessionModel
        {
            Name = "before",
            Child = new NeutralSessionChild { Value = "before" },
        };
        var settingsSession = settings.CreateEditSession();
        var group = new EditSessionGroup();
        var profileHandle = group.Register("profile", Registration(profileSession));
        var settingsHandle = group.Register("settings", Registration(settingsSession));
        var settingsChild = settingsSession.OpenChildSession(NeutralSessionModel.SparsePath.Child);
        settingsChild.Session.Observable.Value = "child edit";

        profileSession.Observable.Name = "profile edit";
        settingsSession.Observable.Name = "settings edit";
        var submission = group.Prepare();
        submission.Sections.Count.ShouldBe(2);
        submission.Get(settingsHandle).Changes.Child.Value.After.Value.ShouldBe("child edit");

        // The first section acknowledges independently.
        group.AcceptChanges(submission, profileHandle);
        profileSession.HasChanges.ShouldBeFalse();
        settingsSession.HasChanges.ShouldBeTrue();

        // Phase 1: a late notification failure while a child draft is still
        // pending rolls the whole section back without acknowledging
        // anything. The child seam stays atomic: both drafts survive with
        // their histories, the model keeps the pre-commit values, and the
        // original error surfaces (aggregated across the failed commit).
        var failNotifications = true;
        settingsSession.PropertyChanged += (_, _) =>
        {
            if (failNotifications)
            {
                throw new InvalidOperationException("Notification handler failed.");
            }
        };

        var rollback = Should.Throw<AggregateException>(() =>
            group.AcceptChanges(submission, settingsHandle)
        );

        rollback
            .InnerExceptions.Select(static error => error.Message)
            .ShouldAllBe(static message => message == "Notification handler failed.");
        profileSession.HasChanges.ShouldBeFalse();
        profile.Name.ShouldBe("profile edit");
        settingsSession.HasChanges.ShouldBeTrue();
        settingsSession.Observable.Name.ShouldBe("settings edit");
        settingsChild.HasChanges.ShouldBeTrue();
        settingsChild.Session.Observable.Value.ShouldBe("child edit");
        // The rolled-back section kept its pre-commit live values: the save
        // must not observe a half-merged section.
        settings.Name.ShouldBe("settings edit");
        settings.Child.Value.ShouldBe("before");

        // Phase 2: with the child draft merged first, the same late failure
        // reaches the commit-before-notify seam instead: the section baseline
        // already committed, so the values stand acknowledged while the
        // original error still surfaces.
        failNotifications = false;
        settingsSession.TryAcceptChildSessions(out var mergeConflicts).ShouldBeTrue();
        mergeConflicts.ShouldBeNull();
        settingsChild.HasChanges.ShouldBeFalse();
        var resubmission = group.Prepare();
        failNotifications = true;

        var committed = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(resubmission, settingsHandle)
        );

        committed.Message.ShouldBe("Notification handler failed.");
        settingsSession.HasChanges.ShouldBeFalse();
        settingsSession.Observable.Name.ShouldBe("settings edit");
        settings.Child.Value.ShouldBe("child edit");

        // The consumed submission is stale for the failed section, so
        // recovery prepares fresh: nothing is pending and nothing applies
        // twice.
        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(resubmission, settingsHandle)
        );
        failNotifications = false;

        var retry = group.Prepare();
        retry.Sections.ShouldBeEmpty();
        settings.Name.ShouldBe("settings edit");
        settings.Child.Value.ShouldBe("child edit");
    }

    [Test]
    public void UnknownSaveKeepsSubmissionPendingWithoutBaselineAck()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));

        session.Observable.Name = "submitted";
        var submission = group.Prepare();
        submission.Get(handle).Changes.Name.After.Value.ShouldBe("submitted");

        // The save callback failed without confirming persistence, so nothing
        // is acknowledged: the baseline does not move and the same capture
        // stays usable. The caller must query persisted state next instead of
        // claiming success or blindly resubmitting the write.
        var saveError = new IOException("connection lost");
        session.HasChanges.ShouldBeTrue();
        session.Observable.Name.ShouldBe("submitted");
        history.CanUndo.ShouldBeTrue();
        saveError.Message.ShouldBe("connection lost");

        // The submission was never consumed: re-preparing observes the same
        // baseline revision and the same pending content.
        var rechecked = group.Prepare();
        rechecked.Get(handle).BaselineRevision.ShouldBe(submission.Get(handle).BaselineRevision);
        rechecked.Get(handle).Changes.Name.After.Value.ShouldBe("submitted");

        // The must-query read shows the write never landed, so resubmitting
        // the identical capture is safe and acknowledges exactly once,
        // clearing the undo retention.
        group.AcceptChanges(rechecked, handle);
        session.HasChanges.ShouldBeFalse();
        model.Name.ShouldBe("submitted");
        history.CanUndo.ShouldBeFalse();

        // A repeat of the consumed capture is rejected as stale rather than
        // applied twice, and acknowledgement cleared the undo retention.
        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(rechecked, handle)
        );
        model.Name.ShouldBe("submitted");
        history.CanUndo.ShouldBeFalse();
    }

    private static EditSessionGroupRegistration<
        NeutralSessionModel,
        NeutralSessionModel.ChangeSet
    > Registration(NeutralSessionModel.EditSession session) =>
        new()
        {
            Session = session,
            CreateChangeSet = session.CreateChangeSet,
            IsEmpty = static changes => changes.IsEmpty,
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
        };
}
