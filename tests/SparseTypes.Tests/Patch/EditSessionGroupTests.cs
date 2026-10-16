using System.Threading;
using System.Threading.Tasks;
using SparseTypes.Generated;

namespace SparseTypes.Tests;

public sealed class EditSessionGroupTests
{
    [Test]
    public void PreparedChangeSetIsStableAndAcceptLeavesAdditionalEditsPending()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));

        session.Observable.Name = "submitted";
        var submission = group.Prepare();
        var captured = submission.Get(handle);
        captured.BaselineRevision.ShouldBe(0);
        session.Observable.Version = 2;

        captured.Changes.Name.After.Value.ShouldBe("submitted");
        captured.ChangedPaths.ShouldHaveSingleItem().ToString().ShouldBe("Name");
        group.AcceptChanges(submission, handle);

        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.IsChanged.ShouldBeFalse();
        pending.Version.Before.Value.ShouldBe(1);
        pending.Version.After.Value.ShouldBe(2);
        submission.ChangedPaths.ShouldHaveSingleItem().PathText.ShouldBe("Name");
        group.Prepare().Get(handle).BaselineRevision.ShouldBe(1);
    }

    [Test]
    public void PartialAcknowledgementsAdvanceOnlyTheirOwnSectionBaseline()
    {
        var profile = new NeutralSessionModel { Name = "before" };
        var profileSession = profile.CreateEditSession();
        var settings = new ObservableHolder { Title = "title" };
        var settingsSession = settings.CreateEditSession();
        var group = new EditSessionGroup();
        var profileHandle = group.Register("profile", Registration(profileSession));
        var settingsHandle = group.Register("settings", Registration(settingsSession));

        profileSession.Observable.Name = "after";
        settingsSession.Observable.Title = "updated";
        var submission = group.Prepare();

        submission.Sections.Count.ShouldBe(2);
        submission.ChangedPaths.Count.ShouldBe(2);
        submission.ChangedPaths.Select(path => path.SectionId).Distinct().Count().ShouldBe(2);
        submission.Get(profileHandle).Changes.Name.After.Value.ShouldBe("after");
        submission.Get(settingsHandle).Changes.Title.After.Value.ShouldBe("updated");

        group.AcceptChanges(submission, profileHandle);

        profileSession.HasChanges.ShouldBeFalse();
        settingsSession.HasChanges.ShouldBeTrue();
        group.AcceptChanges(submission, settingsHandle);
        settingsSession.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void StaleAndUnrelatedSubmissionsAreRejected()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));
        session.Observable.Name = "first";
        var older = group.Prepare();
        session.Observable.Name = "second";
        var newer = group.Prepare();

        group.AcceptChanges(newer, handle);

        Should.Throw<EditSessionStaleSubmissionException>(() => group.AcceptChanges(older, handle));
        group.TryReconcile(older, handle, model, out var staleError).ShouldBeFalse();
        staleError.ShouldNotBeNull();
        staleError!.ShouldContain("older baseline revision");

        session.Observable.Name = "third";
        group.Prepare().Get(handle).BaselineRevision.ShouldBe(1);

        var unrelatedGroup = new EditSessionGroup();
        var unrelated = unrelatedGroup.Prepare();
        group.TryReconcile(unrelated, handle, model, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        error!.ShouldContain("another group");
    }

    [Test]
    public void ReconcileUsesSubmittedTransitionAndPreservesLaterEdits()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session, reconcile: true));
        session.Observable.Name = "submitted";
        var submission = group.Prepare();
        session.Observable.Version = 2;
        var persisted = new NeutralSessionModel { Name = "submitted", Version = 1 };

        group.TryReconcile(submission, handle, persisted, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        var pending = session.CreateChangeSet();
        pending.Name.IsChanged.ShouldBeFalse();
        pending.Version.Before.Value.ShouldBe(1);
        pending.Version.After.Value.ShouldBe(2);
    }

    [Test]
    public void DuplicateSessionRegistrationIsRejected()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));

        var failure = Should.Throw<ArgumentException>(() =>
            group.Register("backup", Registration(session))
        );
        failure.Message.ShouldContain("profile");
        failure.ParamName.ShouldBe("registration");

        // The rejected registration leaves the group usable.
        var other = new NeutralSessionModel { Name = "other" }.CreateEditSession();
        var otherHandle = group.Register("backup", Registration(other));
        otherHandle.SectionId.ShouldBe("backup");
        handle.SectionId.ShouldBe("profile");
    }

    [Test]
    public void EqualContentSessionsRegisterIndependently()
    {
        var first = new NeutralSessionModel { Name = "same" }.CreateEditSession();
        var second = new NeutralSessionModel { Name = "same" }.CreateEditSession();
        var group = new EditSessionGroup();

        var firstHandle = group.Register("first", Registration(first));
        var secondHandle = group.Register("second", Registration(second));

        firstHandle.Session.ShouldNotBeSameAs(secondHandle.Session);
        first.Observable.Name = "one";
        second.Observable.Name = "two";
        var submission = group.Prepare();
        submission.Get(firstHandle).Changes.Name.After.Value.ShouldBe("one");
        submission.Get(secondHandle).Changes.Name.After.Value.ShouldBe("two");
    }

    [Test]
    public void ReplaceWithLiveSessionIsRejectedAndOwnSessionIsAllowed()
    {
        var first = new NeutralSessionModel { Name = "first" }.CreateEditSession();
        var second = new NeutralSessionModel { Name = "second" }.CreateEditSession();
        var group = new EditSessionGroup();
        var firstHandle = group.Register("first", Registration(first));
        group.Register("second", Registration(second));

        var collision = Should.Throw<ArgumentException>(() =>
            group.Replace(
                firstHandle,
                Registration(second),
                EditSessionGroupRemovalPolicy.DetachWithPendingChanges
            )
        );
        collision.Message.ShouldContain("second");

        // Replacing a section with its own session stays valid.
        var renewed = group.Replace(
            firstHandle,
            Registration(first),
            EditSessionGroupRemovalPolicy.DetachWithPendingChanges
        );
        renewed.SectionId.ShouldBe("first");
        renewed.Session.ShouldBeSameAs(first);
    }

    [Test]
    public void UnregisterReleasesSessionIdentity()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));

        group.Unregister(handle, EditSessionGroupRemovalPolicy.DetachWithPendingChanges);

        var again = group.Register("backup", Registration(session));
        again.SectionId.ShouldBe("backup");
        again.Session.ShouldBeSameAs(session);
    }

    [Test]
    public void SameSubmissionCannotBeAcknowledgedTwice()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));
        session.Observable.Name = "submitted";
        var submission = group.Prepare();

        group.AcceptChanges(submission, handle);

        // The consumed submission no longer matches the advanced group
        // revision, so the repeat is rejected as stale before reaching the
        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );

        session.Observable.Name = "next";
        var fresh = group.Prepare();
        fresh.Get(handle).BaselineRevision.ShouldBe(1);
        group.AcceptChanges(fresh, handle);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ExternalAcceptChangesResynchronizesRevisions()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));
        session.Observable.Name = "submitted";
        var submission = group.Prepare();

        // The next group operation observes the session's external baseline move.
        session.AcceptChanges();
        var outcome = Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );
        outcome.Message.ShouldContain("older baseline revision");

        session.Observable.Name = "next";
        var fresh = group.Prepare();
        fresh.Get(handle).BaselineRevision.ShouldBe(1);
        group.AcceptChanges(fresh, handle);
        session.HasChanges.ShouldBeFalse();
        session.Observable.Name = "after-next";
        group.Prepare().Get(handle).BaselineRevision.ShouldBe(2);
    }

    [Test]
    public void NotificationFailurePreservesOriginalErrorWithLaterSamePathEdits()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var failNotifications = false;
        var session = model.CreateEditSession();
        session.PropertyChanged += (_, _) =>
        {
            if (failNotifications)
                throw new InvalidOperationException("Notification handler failed.");
        };
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));
        session.Observable.Name = "submitted";
        var submission = group.Prepare();
        session.Observable.Name = "later";
        failNotifications = true;

        // The session commits the baseline before notifying.
        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.ShouldNotBeOfType<EditSessionStaleSubmissionException>();
        failure.Message.ShouldBe("Notification handler failed.");
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.Before.Value.ShouldBe("submitted");
        pending.Name.After.Value.ShouldBe("later");
        failNotifications = false;

        var fresh = group.Prepare();
        fresh.Get(handle).BaselineRevision.ShouldBe(1);
        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );
    }

    [Test]
    public void RejectedPreconditionDoesNotAdvanceTheBaselineRevision()
    {
        var session = new NeutralSessionModel { Name = "before" }.CreateEditSession();
        var rejectedChanges = new NeutralSessionModel { Name = "other" }.CreateChangeSet(
            new NeutralSessionModel { Name = "submitted" }
        );
        var group = new EditSessionGroup();
        var handle = group.Register(
            "profile",
            Registration(session, createChangeSet: () => rejectedChanges)
        );
        var submission = group.Prepare();

        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(submission, handle)
        );

        session.BaselineRevision.ShouldBe(0);
        group.Prepare().Get(handle).BaselineRevision.ShouldBe(0);
    }

    [Test]
    public void OtherNotificationExceptionsArePreservedAfterCommit()
    {
        var session = new NeutralSessionModel { Name = "before" }.CreateEditSession();
        var failure = new ApplicationException("Observer failed.");
        var failNotifications = false;
        session.PropertyChanged += (_, _) =>
        {
            if (failNotifications)
                throw failure;
        };
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));
        session.Observable.Name = "submitted";
        var submission = group.Prepare();
        failNotifications = true;

        var thrown = Should.Throw<ApplicationException>(() =>
            group.AcceptChanges(submission, handle)
        );

        thrown.ShouldBeSameAs(failure);
        session.BaselineRevision.ShouldBe(1);
        failNotifications = false;
        session.Observable.Name = "next";
        group.Prepare().Get(handle).BaselineRevision.ShouldBe(1);
    }

    [Test]
    public void TryReconcileAfterNotificationFailureAdvancesTheSessionRevision()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var failNotifications = false;
        var session = model.CreateEditSession();
        session.PropertyChanged += (_, _) =>
        {
            if (failNotifications)
                throw new InvalidOperationException("Notification handler failed.");
        };
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session, reconcile: true));
        session.Observable.Name = "submitted";
        var submission = group.Prepare();
        failNotifications = true;

        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.Message.ShouldBe("Notification handler failed.");

        failNotifications = false;
        session.Observable.Version = 2;
        var reconciliation = group.Prepare();
        reconciliation.Get(handle).BaselineRevision.ShouldBe(1);
        var persisted = new NeutralSessionModel { Name = "submitted", Version = 2 };

        group.TryReconcile(reconciliation, handle, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        session.Observable.Name = "after-reconcile";
        var next = group.Prepare();
        next.Get(handle).BaselineRevision.ShouldBe(2);
        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(reconciliation, handle)
        );
    }

    [Test]
    public void DuplicateAndDirtyReplacementRequireExplicitPolicy()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", Registration(session));

        Should.Throw<ArgumentException>(() => group.Register("profile", Registration(session)));
        session.Observable.Name = "pending";
        Should.Throw<InvalidOperationException>(() =>
            group.Unregister(handle, EditSessionGroupRemovalPolicy.RequireClean)
        );
        var oldSubmission = group.Prepare();

        var replacementSession = new NeutralSessionModel
        {
            Name = "replacement",
        }.CreateEditSession();
        var replacement = group.Replace(
            handle,
            Registration(replacementSession),
            EditSessionGroupRemovalPolicy.DetachWithPendingChanges
        );

        replacement.SectionId.ShouldBe("profile");
        replacement.Session.ShouldBeSameAs(replacementSession);
        Should.Throw<EditSessionStaleSubmissionException>(() =>
            group.AcceptChanges(oldSubmission, handle)
        );
        group.Unregister(replacement, EditSessionGroupRemovalPolicy.RequireClean);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void PrepareChecksReadinessWithoutRequiringDraftTracking()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var isReady = false;
        var group = new EditSessionGroup();
        group.Register(
            "profile",
            Registration(session, validate: () => isReady ? null : "The section is not ready.")
        );

        var readinessFailure = Should.Throw<EditSessionGroupValidationException>(() =>
            group.Prepare()
        );
        readinessFailure
            .Issues.ShouldHaveSingleItem()
            .Message.ShouldBe("The section is not ready.");

        isReady = true;
        group.Prepare().Sections.ShouldBeEmpty();
    }

    [Test]
    public void PrepareCapturesPendingChildSessionsWithoutAccepting()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("child", Registration(session));
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "updated";

        var submission = group.Prepare();

        submission.Get(handle).Changes.Child.IsChanged.ShouldBeTrue();
        submission.Get(handle).Changes.Child.Name.After.Value.ShouldBe("updated");
        // Preparation is observational: the draft stays pending and the model is untouched.
        child.HasChanges.ShouldBeTrue();
        model.Child.Name.ShouldBe("before");
        session.HasChanges.ShouldBeTrue();

        group.AcceptChanges(submission, handle);

        child.HasChanges.ShouldBeFalse();
        model.Child.Name.ShouldBe("updated");
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void FailedValidationLeavesChildSessionsUntouched()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        var childHistory = child.Session.EnableHistory();
        child.Session.Observable.Name = "draft";
        var group = new EditSessionGroup();
        group.Register(
            "child",
            new EditSessionGroupRegistration<ObservableHolder, ObservableHolder.ChangeSet>
            {
                Session = session,
                CreateChangeSet = session.CreateChangeSet,
                IsEmpty = static changes => changes.IsEmpty,
                EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
                ValidateChanges = static _ => "Validation failed.",
            }
        );

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        failure.Issues.ShouldHaveSingleItem().Message.ShouldBe("Validation failed.");
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("draft");
        model.Child.Name.ShouldBe("before");
        session.HasChanges.ShouldBeTrue();
        childHistory.CanUndo.ShouldBeTrue();
    }

    [Test]
    public void ConflictingChildrenFailPrepareWithoutMutation()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        group.Register("child", Registration(session));
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "child edit";
        session.Observable.Child!.Name = "parent edit";

        Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("child edit");
        session.Observable.Child.Name.ShouldBe("parent edit");
        model.Child.Name.ShouldBe("parent edit");
    }

    [Test]
    public void AcceptChangesMergesChildrenAndPreservesLaterEdits()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("child", Registration(session));
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "submitted";

        var submission = group.Prepare();
        child.HasChanges.ShouldBeTrue();

        // Edits made after capture stay pending through acknowledgement.
        session.Observable.Title = "later parent";
        child.Session.Observable.Count = 2;

        group.AcceptChanges(submission, handle);

        model.Child.Name.ShouldBe("submitted");
        model.Child.Count.ShouldBe(2);
        model.Title.ShouldBe("later parent");
        session.HasPendingChildSessions.ShouldBeFalse();
        submission.Get(handle).Changes.Child.Count.IsChanged.ShouldBeFalse();
        var pending = session.CreateChangeSet();
        pending.Title.IsChanged.ShouldBeTrue();
        pending.Child.Count.IsChanged.ShouldBeTrue();
        pending.Child.Name.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void TryReconcileWithPendingChildrenUsesCapturedSubmission()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register(
            "child",
            new EditSessionGroupRegistration<ObservableHolder, ObservableHolder.ChangeSet>
            {
                Session = session,
                CreateChangeSet = session.CreateChangeSet,
                IsEmpty = static changes => changes.IsEmpty,
                EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
                TryReconcile = (changes, persisted) =>
                {
                    var success = session.TryReconcile(changes, persisted, out var error);
                    return (success, error);
                },
            }
        );
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "submitted";

        var submission = group.Prepare();
        child.HasChanges.ShouldBeTrue();

        session.Observable.Title = "later parent";
        var persisted = new ObservableHolder
        {
            Title = "before",
            Child = new ObservableChild { Name = "submitted", Count = 1 },
        };

        group.TryReconcile(submission, handle, persisted, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        session.Observable.Title.ShouldBe("later parent");
        session.HasPendingChildSessions.ShouldBeFalse();
        var pending = session.CreateChangeSet();
        pending.Title.IsChanged.ShouldBeTrue();
        pending.Child.Name.IsChanged.ShouldBeFalse();
    }

    [Test]
    public async Task FailedPrepareAsyncLeavesChildSessionsUntouched()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        group.Register(
            "child",
            Registration(
                session,
                validateAsync: static (_, _) => Task.FromResult<string?>("Async validation failed.")
            )
        );
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "submitted";

        EditSessionGroupValidationException? failure = null;
        try
        {
            await group.PrepareAsync(CancellationToken.None);
        }
        catch (EditSessionGroupValidationException exception)
        {
            failure = exception;
        }

        failure.ShouldNotBeNull();
        failure!.Issues.ShouldHaveSingleItem().Message.ShouldBe("Async validation failed.");
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("submitted");
        model.Child.Name.ShouldBe("before");
    }

    [Test]
    public async Task CanceledPrepareAsyncPreservesChildDrafts()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        group.Register(
            "child",
            Registration(
                session,
                validateAsync: async (_, cancellationToken) =>
                {
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "submitted";

        using var cancellation = new CancellationTokenSource();
        var prepareTask = group.PrepareAsync(cancellation.Token);
        await validationStarted.Task;
        child.Session.Observable.Count = 2;
        cancellation.Cancel();

        var wasCanceled = false;
        try
        {
            await prepareTask;
        }
        catch (OperationCanceledException)
        {
            wasCanceled = true;
        }

        wasCanceled.ShouldBeTrue();
        child.HasChanges.ShouldBeTrue();
        child.Session.Observable.Name.ShouldBe("submitted");
        child.Session.Observable.Count.ShouldBe(2);
        model.Child.Name.ShouldBe("before");
        model.Child.Count.ShouldBe(1);
    }

    [Test]
    public async Task PrepareAsyncValidatesTheCapturedChangeSetAfterSynchronousCapture()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string? validatedName = null;
        var handle = group.Register(
            "profile",
            Registration(
                session,
                validateAsync: async (changes, cancellationToken) =>
                {
                    validatedName = changes.Name.After.Value;
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        session.Observable.Name = "submitted";

        Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());
        var prepareTask = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        session.Observable.Name = "edited while validating";
        releaseValidation.TrySetResult(true);

        var submission = await prepareTask;

        validatedName.ShouldBe("submitted");
        submission.Get(handle).Changes.Name.After.Value.ShouldBe("submitted");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task PrepareAsyncReportsValidationFailureAndObservesCancellation()
    {
        var rejectedSession = new NeutralSessionModel { Name = "rejected" }.CreateEditSession();
        var rejectedGroup = new EditSessionGroup();
        rejectedGroup.Register(
            "rejected",
            Registration(
                rejectedSession,
                validateAsync: static (_, _) => Task.FromResult<string?>("Async validation failed.")
            )
        );

        EditSessionGroupValidationException? validationFailure = null;
        try
        {
            await rejectedGroup.PrepareAsync(CancellationToken.None);
        }
        catch (EditSessionGroupValidationException exception)
        {
            validationFailure = exception;
        }

        validationFailure.ShouldNotBeNull();
        validationFailure!
            .Issues.ShouldHaveSingleItem()
            .Message.ShouldBe("Async validation failed.");

        var pendingSession = new NeutralSessionModel { Name = "pending" }.CreateEditSession();
        var pendingGroup = new EditSessionGroup();
        pendingGroup.Register(
            "pending",
            Registration(
                pendingSession,
                validateAsync: static async (_, cancellationToken) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return null;
                }
            )
        );
        using var cancellation = new CancellationTokenSource();
        var pendingValidation = pendingGroup.PrepareAsync(cancellation.Token);
        cancellation.Cancel();

        var wasCanceled = false;
        try
        {
            await pendingValidation;
        }
        catch (OperationCanceledException)
        {
            wasCanceled = true;
        }

        wasCanceled.ShouldBeTrue();
    }

    [Test]
    public void SyncValidateChangesSeesOnlyTheCapturedChangeSet()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        string? readinessObserved = null;
        string? changesObserved = null;
        group.Register(
            "profile",
            Registration(
                session,
                validate: () =>
                {
                    readinessObserved = session.Observable.Name;
                    return null;
                },
                validateChanges: changes =>
                {
                    changesObserved = changes.Name.After.Value;
                    return null;
                },
                createChangeSet: () =>
                {
                    var captured = session.CreateChangeSet();
                    session.Observable.Name = "mutated after capture";
                    return captured;
                }
            )
        );
        session.Observable.Name = "submitted";

        var submission = group.Prepare();

        changesObserved.ShouldBe("submitted");
        readinessObserved.ShouldBe("mutated after capture");
        submission.ChangedPaths.ShouldHaveSingleItem().PathText.ShouldBe("Name");
    }

    [Test]
    public async Task PrepareAsyncRunsSyncAndAsyncValidatorsAgainstTheCapturedChangeSet()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string? syncObserved = null;
        string? asyncObserved = null;
        var handle = group.Register(
            "profile",
            Registration(
                session,
                validateChanges: changes =>
                {
                    syncObserved = changes.Name.After.Value;
                    return null;
                },
                validateAsync: async (changes, cancellationToken) =>
                {
                    asyncObserved = changes.Name.After.Value;
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        session.Observable.Name = "submitted";

        var prepareTask = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        session.Observable.Name = "edited while validating";
        releaseValidation.TrySetResult(true);

        var submission = await prepareTask;

        syncObserved.ShouldBe("submitted");
        asyncObserved.ShouldBe("submitted");
        submission.Get(handle).Changes.Name.After.Value.ShouldBe("submitted");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task PrepareAsyncKeepsChildEditsAfterCapturePending()
    {
        var model = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before", Count = 1 },
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var handle = group.Register(
            "profile",
            Registration(
                session,
                validateAsync: async (_, cancellationToken) =>
                {
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        var child = session.OpenChildSession(ObservableHolder.SparsePath.Child);
        child.Session.Observable.Name = "submitted";

        var prepareTask = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        child.Session.Observable.Count = 2;
        releaseValidation.TrySetResult(true);

        var submission = await prepareTask;
        var captured = submission.Get(handle);
        captured.Changes.Child.Name.After.Value.ShouldBe("submitted");
        captured.Changes.Child.Count.IsChanged.ShouldBeFalse();
        child.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task PrepareAsyncFailsWhenSectionUnregisteredDuringValidation()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var handle = group.Register(
            "profile",
            Registration(
                session,
                validateAsync: async (_, cancellationToken) =>
                {
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        session.Observable.Name = "submitted";

        var prepareTask = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        group.Unregister(handle, EditSessionGroupRemovalPolicy.DetachWithPendingChanges);
        releaseValidation.TrySetResult(true);

        EditSessionGroupValidationException? unregisterFailure = null;
        try
        {
            await prepareTask;
        }
        catch (EditSessionGroupValidationException exception)
        {
            unregisterFailure = exception;
        }

        unregisterFailure.ShouldNotBeNull();
        unregisterFailure!
            .Issues.ShouldHaveSingleItem()
            .Message.ShouldContain("unregistered or replaced");
        group.Prepare().Sections.ShouldBeEmpty();
    }

    [Test]
    public async Task PrepareAsyncFailsWhenSectionReplacedDuringValidation()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var handle = group.Register(
            "profile",
            Registration(
                session,
                validateAsync: async (_, cancellationToken) =>
                {
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        session.Observable.Name = "submitted";

        var prepareTask = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        var replacementSession = new NeutralSessionModel
        {
            Name = "replacement",
        }.CreateEditSession();
        var replacement = group.Replace(
            handle,
            Registration(replacementSession),
            EditSessionGroupRemovalPolicy.DetachWithPendingChanges
        );
        releaseValidation.TrySetResult(true);

        EditSessionGroupValidationException? replaceFailure = null;
        try
        {
            await prepareTask;
        }
        catch (EditSessionGroupValidationException exception)
        {
            replaceFailure = exception;
        }

        replaceFailure.ShouldNotBeNull();
        replaceFailure!
            .Issues.ShouldHaveSingleItem()
            .Message.ShouldContain("unregistered or replaced");
        replacement.SectionId.ShouldBe("profile");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task PrepareAsyncFailsWhenExternalBaselineAdvancesWhileAwaiting()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var callCount = 0;
        var validationStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseValidation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        group.Register(
            "profile",
            Registration(
                session,
                validateAsync: async (_, cancellationToken) =>
                {
                    if (Interlocked.Increment(ref callCount) == 1)
                        return null;
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                }
            )
        );
        session.Observable.Name = "first";
        await group.PrepareAsync(CancellationToken.None);

        session.Observable.Name = "second";
        var secondPrepare = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        session.AcceptChanges();
        releaseValidation.TrySetResult(true);

        EditSessionGroupValidationException? baselineFailure = null;
        try
        {
            await secondPrepare;
        }
        catch (EditSessionGroupValidationException exception)
        {
            baselineFailure = exception;
        }

        baselineFailure.ShouldNotBeNull();
        baselineFailure!.Issues.ShouldHaveSingleItem().Message.ShouldContain("baseline changed");
    }

    [Test]
    public void EmptySectionStillRunsCapturedValidators()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var observedEmpty = false;
        var rejectEmpty = true;
        group.Register(
            "profile",
            Registration(
                session,
                validateChanges: changes =>
                {
                    observedEmpty = changes.IsEmpty;
                    return rejectEmpty ? "Empty sections cannot be submitted." : null;
                }
            )
        );

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());
        failure
            .Issues.ShouldHaveSingleItem()
            .Message.ShouldBe("Empty sections cannot be submitted.");
        observedEmpty.ShouldBeTrue();

        rejectEmpty = false;
        group.Prepare().Sections.ShouldBeEmpty();
    }

    [Test]
    public void SyncValidationExceptionsBecomeIssuesWhileCancellationPropagates()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        group.Register(
            "profile",
            Registration(
                session,
                validateChanges: static _ =>
                    throw new InvalidOperationException("Captured check failed.")
            )
        );
        session.Observable.Name = "submitted";

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());
        failure.Issues.ShouldHaveSingleItem().Message.ShouldBe("Captured check failed.");

        var cancelGroup = new EditSessionGroup();
        var cancelSession = new NeutralSessionModel { Name = "before" }.CreateEditSession();
        cancelGroup.Register(
            "profile",
            Registration(
                cancelSession,
                validateChanges: static _ => throw new OperationCanceledException()
            )
        );
        cancelSession.Observable.Name = "submitted";
        Should.Throw<OperationCanceledException>(() => cancelGroup.Prepare());
    }

    private static EditSessionGroupRegistration<
        NeutralSessionModel,
        NeutralSessionModel.ChangeSet
    > Registration(
        NeutralSessionModel.EditSession session,
        Func<string?>? validate = null,
        Func<NeutralSessionModel.ChangeSet, CancellationToken, Task<string?>>? validateAsync = null,
        bool reconcile = false,
        Func<NeutralSessionModel.ChangeSet, string?>? validateChanges = null,
        Func<NeutralSessionModel.ChangeSet>? createChangeSet = null
    ) =>
        new()
        {
            Session = session,
            CreateChangeSet = createChangeSet ?? session.CreateChangeSet,
            IsEmpty = static changes => changes.IsEmpty,
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            Validate = validate,
            ValidateChanges = validateChanges,
            ValidateAsync = validateAsync,
            TryReconcile = reconcile
                ? (changes, persisted) =>
                {
                    var success = session.TryReconcile(changes, persisted, out var error);
                    return (success, error);
                }
                : null,
        };

    private static EditSessionGroupRegistration<
        ObservableHolder,
        ObservableHolder.ChangeSet
    > Registration(
        ObservableHolder.EditSession session,
        Func<ObservableHolder.ChangeSet, CancellationToken, Task<string?>>? validateAsync = null
    ) =>
        new()
        {
            Session = session,
            CreateChangeSet = session.CreateChangeSet,
            IsEmpty = static changes => changes.IsEmpty,
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            ValidateAsync = validateAsync,
        };
}
