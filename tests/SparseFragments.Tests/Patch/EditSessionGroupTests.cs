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

    private static EditSessionGroupRegistration<
        NeutralSessionModel,
        NeutralSessionModel.ChangeSet
    > Registration(
        NeutralSessionModel.EditSession session,
        Func<bool>? hasOpenDrafts = null,
        Func<string?>? validate = null,
        Func<NeutralSessionModel.ChangeSet, CancellationToken, Task<string?>>? validateAsync = null,
        bool reconcile = false
    ) =>
        new()
        {
            Session = session,
            CreateChangeSet = session.CreateChangeSet,
            IsEmpty = static changes => changes.IsEmpty,
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            HasOpenDrafts = hasOpenDrafts ?? (() => false),
            Validate = validate,
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
