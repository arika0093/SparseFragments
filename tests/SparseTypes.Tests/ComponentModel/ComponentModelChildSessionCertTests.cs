using System.ComponentModel;

namespace SparseTypes.Tests;

/// <summary>
/// Child-session and validation-display certification.
/// Covers child-session acceptance, discard and disposal, the rejected
/// <see cref="IEditableObject"/> route (issue #259), validation error
/// display through <see cref="INotifyDataErrorInfo"/>, after-disposal
/// behavior, and UI-thread responsibility.
/// </summary>
public sealed class ComponentModelChildSessionCertTests
{
    [Test]
    public void ChildSessionAcceptMergesDraftAndKeepsDisjointParentEdits()
    {
        var child = new ObservableChild { Name = "before" };
        var model = new ObservableHolder { Title = "before", Child = child };
        var session = model.CreateEditSession();
        using var childSession = session.OpenChildSession(ObservableHolder.SparsePath.Child);

        childSession.Session.Observable.Name = "draft";
        session.Observable.Title = "parent edit";

        childSession.TryAcceptChanges(out var conflicts).ShouldBeTrue();

        conflicts.ShouldBeNull();
        model.Title.ShouldBe("parent edit");
        model.Child.Name.ShouldBe("draft");
        ReferenceEquals(model.Child, child).ShouldBeTrue();
    }

    [Test]
    public void ChildSessionEditsRemainIsolatedUntilAccepted()
    {
        var model = new ObservableHolder { Child = new ObservableChild { Name = "before" } };
        var session = model.CreateEditSession();
        using var childSession = session.OpenChildSession(ObservableHolder.SparsePath.Child);

        childSession.Session.Observable.Name = "draft";
        model.Child.Name.ShouldBe("before");
        session.HasChanges.ShouldBeTrue();
        childSession.HasChanges.ShouldBeTrue();

        ((INotifyPropertyChanged)childSession.Session).ShouldNotBeNull();

        childSession.TryAcceptChanges(out _).ShouldBeTrue();
        model.Child.Name.ShouldBe("draft");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ConflictingChildSessionAcceptLeavesParentUntouched()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        using var childSession = session.OpenChildSession(
            KeyedServerHolder.SparsePath.Items.Key("server")
        );

        childSession.Session.Observable.Name = "draft";
        session.Observable.Items[0].Name = "parent";

        childSession.TryAcceptChanges(out var conflicts).ShouldBeFalse();

        conflicts.ShouldNotBeNull();
        session.Observable.Items[0].Name.ShouldBe("parent");
    }

    [Test]
    public void DiscardAndDisposeDoNotChangeTheParent()
    {
        var model = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        };
        var session = model.CreateEditSession();
        using var childSession = session.OpenChildSession(
            KeyedServerHolder.SparsePath.Items.Key("server")
        );
        childSession.Session.Observable.Name = "discarded";
        childSession.DiscardChanges();
        childSession.HasChanges.ShouldBeFalse();

        childSession.Session.Observable.Name = "detached";
        childSession.Dispose();

        model.Items[0].Name.ShouldBe("before");
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ChildSessionDoesNotImplementIEditableObject()
    {
        // Grids bind model rows, not the generated session object, so the
        // interface would not provide the expected Begin/End/Cancel behavior.
        var holderSession = new ObservableHolder
        {
            Child = new ObservableChild { Name = "before" },
        }.CreateEditSession();
        using var childSession = holderSession.OpenChildSession(ObservableHolder.SparsePath.Child);

        childSession.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
        childSession.Session.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));

        var keyedSession = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "server", Name = "before" }],
        }.CreateEditSession();
        using var rowSession = keyedSession.OpenChildSession(
            KeyedServerHolder.SparsePath.Items.Key("server")
        );

        rowSession.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
        rowSession.Session.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
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
