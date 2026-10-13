using System.ComponentModel;

namespace SparseFragments.Tests;

/// <summary>
/// Edit-scope and validation-display certification (issue #262).
/// Covers the one-shot <c>OpenScope</c>/<c>TryCommit</c>/<c>Cancel</c>
/// lifecycle, what binding actually attaches to, the rejected
/// <see cref="IEditableObject"/> route (issue #259), validation error
/// display through <see cref="INotifyDataErrorInfo"/>, after-disposal
/// behavior, and UI-thread responsibility.
/// </summary>
public sealed class ComponentModelScopeCertTests
{
    [Test]
    public void ScopeCommitMergesDraftAndKeepsDisjointParentEdits()
    {
        var child = new ObservableChild { Name = "before" };
        var model = new ObservableHolder { Title = "before", Child = child };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(ObservableHolder.SparsePath.Child);

        scope.Observable.Name = "draft";
        session.Observable.Title = "parent edit";

        scope.TryCommit(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Title.ShouldBe("parent edit");
        model.Child.Name.ShouldBe("draft");
        ReferenceEquals(model.Child, child).ShouldBeTrue();
    }

    [Test]
    public void BoundTargetIsObservableOrModelNeverTheScope()
    {
        var model = new ObservableHolder { Child = new ObservableChild { Name = "before" } };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(ObservableHolder.SparsePath.Child);
        ISparseEditSession<ObservableChild> childSession = scope;

        // The draft is isolated: editing it touches neither the parent model
        // nor the parent session until commit.
        scope.Observable.Name = "draft";
        model.Child.Name.ShouldBe("before");
        session.HasChanges.ShouldBeFalse();
        scope.HasChanges.ShouldBeTrue();

        // Controls bind scope.Observable (or scope.Model for read paths).
        // Binding the scope object itself observes nothing useful.
        scope.GetType().GetInterfaces().ShouldNotContain(typeof(INotifyPropertyChanged));
        childSession.Model.Name.ShouldBe("draft");
        ((INotifyPropertyChanged)session).ShouldNotBeNull();

        scope.TryCommit(out _).ShouldBeTrue();
        model.Child.Name.ShouldBe("draft");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ConflictingScopeCommitLeavesParentUntouched()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        using var scope = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server"));

        scope.Observable.Name = "draft";
        session.Observable.Items[0].Name = "parent";

        scope.TryCommit(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        scope.State.ShouldBe(KeyedServerHolder.EditSession.ScopeState.Open);
        session.Observable.Items[0].Name.ShouldBe("parent");
    }

    [Test]
    public void CancelAndDisposeDiscardTheDraft()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();

        using (var canceled = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server")))
        {
            canceled.Observable.Name = "discarded";
            canceled.Cancel();
            canceled.State.ShouldBe(KeyedServerHolder.EditSession.ScopeState.Canceled);
        }

        using (var disposed = session.OpenScope(KeyedServerHolder.SparsePath.Items.Key("server")))
        {
            disposed.Observable.Name = "discarded";
        }

        model.Items[0].Name.ShouldBe("before");
        session.HasChanges.ShouldBeFalse();
        ((ISparseEditSessionOpenDraftState)session).HasOpenDrafts.ShouldBeFalse();
    }

    [Test]
    public void ScopeDoesNotImplementIEditableObject()
    {
        // Issue #259 was closed as not planned: the scope is a one-shot
        // draft, while IEditableObject callers expect repeated
        // Begin/End/Cancel cycles on the same bound row. Grids bind the row
        // model, not the scope, so the interface on the scope would not
        // enable row integration anyway. No wrapper is provided.
        var holderSession = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before" },
        }.CreateEditSession();
        using var childScope = holderSession.OpenScope(ObservableHolder.SparsePath.Child);

        childScope.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));

        var keyedSession = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        }.CreateEditSession();
        using var rowScope = keyedSession.OpenScope(
            KeyedServerHolder.SparsePath.Items.Key("server")
        );

        rowScope.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
        keyedSession.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
        keyedSession.Observable.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
    }

    [Test]
    public void ValidationStoreDisplaysThroughNotifyDataErrorInfo()
    {
        var session = new Settings().CreateEditSession();
        var store = new SparseValidationStore<Settings>();
        INotifyDataErrorInfo notify = store;
        var events = new List<string?>();
        notify.ErrorsChanged += (_, args) => events.Add(args.PropertyName);

        // The application runs its own validators and feeds results in.
        store.SetErrors(
            Settings.SparsePath.Label,
            SparseValidationOrigins.LocalValidation,
            ["Label is required."]
        );

        notify.HasErrors.ShouldBeTrue();
        notify.GetErrors("Label").Cast<object?>().ShouldBe(["Label is required."]);
        notify.GetErrors(null).Cast<object?>().ShouldBe(["Label is required."]);
        events.ShouldBe(["Label", string.Empty]);

        // Error state stays independent from semantic dirty state.
        session.HasChanges.ShouldBeFalse();
        session.FieldState(Settings.SparsePath.Label).HasChanges.ShouldBeFalse();

        store.ClearErrors(Settings.SparsePath.Label);
        notify.HasErrors.ShouldBeFalse();
    }

    [Test]
    public void RebaseConflictsFlowIntoValidationDisplay()
    {
        var store = new SparseValidationStore<Settings>();
        var baseModel = new Settings();
        var edited = new Settings { Label = "local" };
        var current = new Settings { Label = "server" };
        var rebase = baseModel
            .CreateChangeSet(edited)
            .RebaseOnto(Optional<Settings.Fragment?>.Present(Settings.Fragment.From(current)));
        rebase.HasConflicts.ShouldBeTrue();

        var added = store.AddConflicts(rebase.Conflicts);

        added.ShouldBe(rebase.Conflicts.Count);
        store.HasPathErrors(Settings.SparsePath.Label).ShouldBeTrue();
        INotifyDataErrorInfo notify = store;
        notify.GetErrors("Label").Cast<object?>().ToList().Count.ShouldBe(added);
    }

    [Test]
    public void AfterDisposalBehaviorIsExplicit()
    {
        var session = new Settings().CreateEditSession();
        var watcher = session.ObserveField(Settings.SparsePath.Label);
        var notifications = 0;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) => notifications++;

        session.Observable.Label = "dirty";
        notifications.ShouldBeGreaterThan(0);

        // Disposing the watcher unsubscribes: later edits stay silent.
        var observed = notifications;
        watcher.Dispose();
        session.Observable.Label = "dirtier";
        notifications.ShouldBe(observed);
        Should.Throw<ObjectDisposedException>(() => watcher.Refresh());
        watcher.Dispose();

        // Disposing the store clears errors with a final notification,
        // then rejects further use.
        var store = new SparseValidationStore<Settings>();
        var errorEvents = new List<string?>();
        ((INotifyDataErrorInfo)store).ErrorsChanged += (_, args) =>
            errorEvents.Add(args.PropertyName);
        store.SetErrors(Settings.SparsePath.Label, SparseValidationOrigins.LocalValidation, ["x"]);
        errorEvents.Clear();

        store.Dispose();

        store.IsDisposed.ShouldBeTrue();
        errorEvents.ShouldBe(["Label", string.Empty]);
        Should.Throw<ObjectDisposedException>(() => store.HasErrors.ShouldBeFalse());
        Should.Throw<ObjectDisposedException>(() => store.Clear());
        store.Dispose();
    }

    [Test]
    public void NotificationsRunOnTheMutatingThread()
    {
        // Core performs no UI-thread dispatching. Applications marshal to
        // their own synchronization context when the mutation happens off
        // the UI thread. WPF, WinForms, Avalonia, MAUI, and WinUI handlers
        // all own that step; the session and the validation store never do.
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var watcherThread = -1;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) =>
            watcherThread = Environment.CurrentManagedThreadId;

        session.Observable.Label = "dirty";

        watcherThread.ShouldBe(Environment.CurrentManagedThreadId);

        var store = new SparseValidationStore<Settings>();
        var storeThread = -1;
        ((INotifyDataErrorInfo)store).ErrorsChanged += (_, _) =>
            storeThread = Environment.CurrentManagedThreadId;
        store.SetErrors(Settings.SparsePath.Label, SparseValidationOrigins.LocalValidation, ["x"]);

        storeThread.ShouldBe(Environment.CurrentManagedThreadId);
    }
}
