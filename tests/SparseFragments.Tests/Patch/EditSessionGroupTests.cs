using System.Threading;
using System.Threading.Tasks;
using SparseFragments.Generated;

namespace SparseFragments.Tests;

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
        // session. The group stays convergent for later submissions.
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

        // Acknowledging outside the group moves the session baseline without
        // moving group revisions. The next group acknowledgement surfaces the
        // session outcome instead of a stale wrapper.
        session.AcceptChanges();
        var outcome = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        outcome.ShouldNotBeOfType<EditSessionStaleSubmissionException>();

        session.Observable.Name = "next";
        var fresh = group.Prepare();
        fresh.Get(handle).BaselineRevision.ShouldBe(1);
        group.AcceptChanges(fresh, handle);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void NotificationFailurePreservesOriginalErrorAndAdvancesRevision()
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
        failNotifications = true;

        // The session commits the baseline before notifying, so the handler
        // failure must propagate untouched (not as a stale submission) while
        // the group revision follows the session.
        var failure = Should.Throw<InvalidOperationException>(() =>
            group.AcceptChanges(submission, handle)
        );
        failure.ShouldNotBeOfType<EditSessionStaleSubmissionException>();
        failure.Message.ShouldBe("Notification handler failed.");
        session.HasChanges.ShouldBeFalse();
        failNotifications = false;

        session.Observable.Name = "next";
        var fresh = group.Prepare();
        fresh.Get(handle).BaselineRevision.ShouldBe(1);
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
    public void PrepareRejectsOpenDraftsAndUnreadySections()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var hasDrafts = true;
        var isReady = false;
        var group = new EditSessionGroup();
        var untrackedSession = new UntrackedSession<
            NeutralSessionModel,
            NeutralSessionModel.ChangeSet
        >(session);
        Should.Throw<ArgumentException>(() =>
            group.Register(
                "untracked",
                new EditSessionGroupRegistration<NeutralSessionModel, NeutralSessionModel.ChangeSet>
                {
                    Session = untrackedSession,
                    CreateChangeSet = session.CreateChangeSet,
                    IsEmpty = static changes => changes.IsEmpty,
                    EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
                }
            )
        );
        group.Register(
            "profile",
            Registration(
                session,
                hasOpenDrafts: () => hasDrafts,
                validate: () => isReady ? null : "The section is not ready."
            )
        );

        var draftFailure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());
        draftFailure.Issues.ShouldHaveSingleItem().Message.ShouldContain("open edit draft");

        hasDrafts = false;
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
    public void PrepareAutomaticallyRejectsGeneratedSessionScopes()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        group.Register(
            "servers",
            new EditSessionGroupRegistration<KeyedServerHolder, KeyedServerHolder.ChangeSet>
            {
                Session = session,
                CreateChangeSet = session.CreateChangeSet,
                IsEmpty = static changes => changes.IsEmpty,
                EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            }
        );
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        failure.Issues.ShouldHaveSingleItem().Message.ShouldContain("open edit draft");
        scope.Cancel();
        group.Prepare().Sections.ShouldBeEmpty();
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
    public async Task PrepareAsyncRejectsDraftsOpenedWhileAwaiting()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var hasDrafts = false;
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
                hasOpenDrafts: () => hasDrafts,
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
        hasDrafts = true;
        releaseValidation.TrySetResult(true);

        EditSessionGroupValidationException? draftFailure = null;
        try
        {
            await prepareTask;
        }
        catch (EditSessionGroupValidationException exception)
        {
            draftFailure = exception;
        }

        draftFailure.ShouldNotBeNull();
        draftFailure!.Issues.ShouldHaveSingleItem().Message.ShouldContain("open edit draft");
    }

    [Test]
    public async Task PrepareAsyncRejectsNestedScopeOpenedWhileAwaiting()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
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
            "servers",
            new EditSessionGroupRegistration<KeyedServerHolder, KeyedServerHolder.ChangeSet>
            {
                Session = session,
                CreateChangeSet = session.CreateChangeSet,
                IsEmpty = static changes => changes.IsEmpty,
                EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
                ValidateAsync = async (_, cancellationToken) =>
                {
                    validationStarted.TrySetResult(true);
                    await releaseValidation.Task.WaitAsync(cancellationToken);
                    return null;
                },
            }
        );

        var prepareTask = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));
        releaseValidation.TrySetResult(true);

        EditSessionGroupValidationException? scopeFailure = null;
        try
        {
            await prepareTask;
        }
        catch (EditSessionGroupValidationException exception)
        {
            scopeFailure = exception;
        }

        scopeFailure.ShouldNotBeNull();
        scopeFailure!.Issues.ShouldHaveSingleItem().Message.ShouldContain("open edit draft");
        scope.Cancel();
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
    public async Task PrepareAsyncFailsWhenBaselineAdvancesWhileAwaiting()
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
        var handle = group.Register(
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
        var first = await group.PrepareAsync(CancellationToken.None);

        session.Observable.Name = "second";
        var secondPrepare = group.PrepareAsync(CancellationToken.None);
        await validationStarted.Task;
        group.AcceptChanges(first, handle);
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
        Func<bool>? hasOpenDrafts = null,
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
            HasOpenDrafts = hasOpenDrafts ?? (() => false),
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
    > Registration(ObservableHolder.EditSession session) =>
        new()
        {
            Session = session,
            CreateChangeSet = session.CreateChangeSet,
            IsEmpty = static changes => changes.IsEmpty,
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            HasOpenDrafts = static () => false,
        };

    private sealed class UntrackedSession<TModel, TChangeSet>(
        ISparseEditSession<TModel, TChangeSet> inner
    ) : ISparseEditSession<TModel, TChangeSet>
        where TModel : class
        where TChangeSet : class
    {
        public TModel Model => inner.Model;

        public bool HasChanges => inner.HasChanges;

        public void AcceptChanges() => inner.AcceptChanges();

        public void AcceptChanges(TChangeSet changes) => inner.AcceptChanges(changes);
    }
}
