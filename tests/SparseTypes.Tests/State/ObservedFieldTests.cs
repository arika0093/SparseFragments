using System.ComponentModel;
using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Opt-in observable field state with binding notifications.</summary>
public sealed class ObservedFieldTests
{
    private static KeyedServerHolder Holder(params (string Id, string Name)[] rows) =>
        new()
        {
            Items = rows.Select(row => new KeyedServer { Id = row.Id, Name = row.Name }).ToList(),
        };

    [Test]
    public void PlainConsumerBindsThroughNotifyPropertyChanged()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        INotifyPropertyChanged binding = watcher;
        var raised = new List<string>();
        binding.PropertyChanged += (_, args) => raised.Add(args.PropertyName!);

        watcher.IsChanged.ShouldBeFalse();
        watcher.Path.ShouldBe(SparsePath.Root<Settings>().Member("Label"));

        session.Observable.Label = "dirty";

        watcher.IsChanged.ShouldBeTrue();
        watcher.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        watcher.Before.ShouldBe(Optional<string?>.Present("default"));
        watcher.After.ShouldBe(Optional<string?>.Present("dirty"));
        raised.ShouldContain(nameof(SparseObservedField<string?>.IsChanged));
        raised.ShouldContain(nameof(SparseObservedField<string?>.Current));

        // The WatchField spelling observes the same state.
        using var alias = session.WatchField(Settings.SparsePath.Label);
        alias.IsChanged.ShouldBeTrue();
        alias.After.ShouldBe(Optional<string?>.Present("dirty"));
    }

    [Test]
    public void EditThenRestoreClearsWatcher()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var count = 0;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) => count++;

        session.Observable.Label = "dirty";
        watcher.IsChanged.ShouldBeTrue();
        var afterEdit = count;
        afterEdit.ShouldBeGreaterThan(0);

        session.Observable.Label = "default";

        // Restoration is semantic cleanliness, not touched tracking.
        watcher.IsChanged.ShouldBeFalse();
        watcher.HasChanges.ShouldBeFalse();
        count.ShouldBeGreaterThan(afterEdit);
    }

    [Test]
    public void BatchEditPublishesOneNetTransition()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var raised = new List<string>();
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, args) =>
            raised.Add(args.PropertyName!);

        session.BatchEdit(() =>
        {
            session.Observable.Label = "one";
            session.Observable.Label = "two";
            session.Observable.RetryCount = 9;
        });

        watcher.IsChanged.ShouldBeTrue();
        watcher.After.ShouldBe(Optional<string?>.Present("two"));
        raised.ShouldContain(nameof(SparseObservedField<string?>.After));

        // A batch that nets no change raises nothing on the watcher.
        raised.Clear();
        session.BatchEdit(() =>
        {
            session.Observable.Label = "three";
            session.Observable.Label = "two";
        });
        raised.ShouldBeEmpty();
        watcher.IsChanged.ShouldBeTrue();
        watcher.After.ShouldBe(Optional<string?>.Present("two"));
    }

    [Test]
    public void UnrelatedEditsRaiseNothing()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var count = 0;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) => count++;

        session.Observable.RetryCount = 42;
        session.Observable.Nested!.Host = "edited";

        count.ShouldBe(0);
        watcher.IsChanged.ShouldBeFalse();
        watcher.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void NestedPathsTrackEdits()
    {
        var session = new Settings().CreateEditSession();
        using var leaf = session.ObserveField(Settings.SparsePath.Nested.Host);
        using var parent = session.ObserveField(Settings.SparsePath.Nested);

        session.Observable.Nested!.Host = "replaced";
        session.Observable.Nested!.Port = 1;

        leaf.IsChanged.ShouldBeTrue();
        leaf.After.ShouldBe(Optional<string>.Present("replaced"));
        parent.IsChanged.ShouldBeFalse();
        parent.HasDescendantChange.ShouldBeTrue();
        parent.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void NestedReplaceThroughApplyTracksDescendants()
    {
        var session = new Settings().CreateEditSession();
        using var leaf = session.ObserveField(Settings.SparsePath.Nested.Host);
        using var parent = session.ObserveField(Settings.SparsePath.Nested);

        // Wholesale instance replacement arrives through a notified apply, so
        // the path-based watcher re-resolves without re-registration.
        var replacement = new Settings().CreateChangeSet(
            new Settings
            {
                Nested = new Nested { Host = "replaced", Port = 1 },
            }
        );
        session.TryApplyInPlace(replacement, out _).ShouldBeTrue();

        leaf.IsChanged.ShouldBeTrue();
        leaf.After.ShouldBe(Optional<string>.Present("replaced"));
        parent.IsChanged.ShouldBeFalse();
        parent.HasDescendantChange.ShouldBeTrue();
    }

    [Test]
    public void KeyedRowTracksEditMoveAndDelete()
    {
        var session = Holder(("a", "a"), ("b", "b")).CreateEditSession();
        using var row = session.ObserveField(KeyedServerHolder.SparsePath.Items.Key("a").Name);
        using var member = session.ObserveField(KeyedServerHolder.SparsePath.Items);
        var rowEvents = 0;
        ((INotifyPropertyChanged)row).PropertyChanged += (_, _) => rowEvents++;

        // Pure reorder keeps the keyed identity clean but marks the ancestor.
        var items = session.Observable.Items;
        (items[0], items[1]) = (items[1], items[0]);
        row.IsChanged.ShouldBeFalse();
        row.HasAncestorChange.ShouldBeTrue();
        member.IsChanged.ShouldBeTrue();
        member.Kind.ShouldBe(SparseFieldChangeKind.Reordered);

        // Editing through the moved position still addresses the same key.
        session.Observable.Items[1].Name = "a2";
        row.IsChanged.ShouldBeTrue();
        row.Before.ShouldBe(Optional<string>.Present("a"));
        row.After.ShouldBe(Optional<string>.Present("a2"));

        // Deleting the row keeps the watcher accurate: the leaf reports an
        // ancestor change for the removed entry, never a stale value.
        rowEvents = 0;
        session.Observable.Items.RemoveAt(1);
        row.HasChanges.ShouldBeTrue();
        rowEvents.ShouldBeGreaterThan(0);
    }

    [Test]
    public void MissingPathsStayClean()
    {
        var session = new Settings().CreateEditSession();
        using var missing = session.ObserveField(SparsePath.Root<Settings>().Member("Missing"));
        var count = 0;
        ((INotifyPropertyChanged)missing).PropertyChanged += (_, _) => count++;

        session.Observable.Label = "dirty";

        count.ShouldBe(0);
        missing.HasChanges.ShouldBeFalse();
        missing.Before.IsPresent.ShouldBeFalse();
        missing.After.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void TemporaryRowsStayAccurateAcrossAdd()
    {
        var temp = Guid.NewGuid();
        var session = new TempOrderHolder().CreateEditSession();
        using var row = session.ObserveField(
            TempOrderHolder.SparsePath.Lines.TemporaryKey(temp).Name
        );

        row.IsChanged.ShouldBeFalse();

        session.Observable.Lines.AddModel(new TempOrderLine { Name = "one", TemporaryId = temp });

        row.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void DisposeStopsCallbacks()
    {
        var session = new Settings().CreateEditSession();
        var watcher = session.ObserveField(Settings.SparsePath.Label);
        var count = 0;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) => count++;

        session.Observable.Label = "dirty";
        count.ShouldBeGreaterThan(0);

        var beforeDispose = count;
        watcher.Dispose();
        session.Observable.Label = "dirtier";
        count.ShouldBe(beforeDispose);

        Should.Throw<ObjectDisposedException>(() => watcher.Refresh());
        watcher.Dispose();
    }

    [Test]
    public void WatcherStaysBoundToItsOwnSessionAfterFork()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var count = 0;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) => count++;

        var fork = session.Fork();
        fork.Observable.Label = "fork-edit";

        count.ShouldBe(0);
        watcher.IsChanged.ShouldBeFalse();

        session.Observable.Label = "own-edit";
        watcher.IsChanged.ShouldBeTrue();
        watcher.After.ShouldBe(Optional<string?>.Present("own-edit"));
    }

    [Test]
    public void NotificationOrderIsDocumented()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var order = new List<string>();
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, args) =>
            order.Add(args.PropertyName!);

        session.Observable.Label = "dirty";

        order.ShouldContain(nameof(SparseObservedField<string?>.IsChanged));
        order.ShouldContain(nameof(SparseObservedField<string?>.Before));
        order.ShouldContain(nameof(SparseObservedField<string?>.After));
        order.ShouldContain(nameof(SparseObservedField<string?>.Current));
        order
            .IndexOf(nameof(SparseObservedField<string?>.IsChanged))
            .ShouldBeLessThan(order.IndexOf(nameof(SparseObservedField<string?>.Current)));
        order
            .IndexOf(nameof(SparseObservedField<string?>.After))
            .ShouldBeLessThan(order.IndexOf(nameof(SparseObservedField<string?>.Current)));
    }

    [Test]
    public void NotificationsRunSynchronouslyWithoutMarshaling()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);
        var notifyingThread = -1;
        ((INotifyPropertyChanged)watcher).PropertyChanged += (_, _) =>
            notifyingThread = Environment.CurrentManagedThreadId;

        session.Observable.Label = "dirty";

        // No UI thread dispatch: the watcher notifies on the mutating thread.
        notifyingThread.ShouldBe(Environment.CurrentManagedThreadId);
    }

    [Test]
    public void RevertChangesClearsWatcher()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);

        session.Observable.Label = "dirty";
        watcher.IsChanged.ShouldBeTrue();

        session.RevertChanges();

        watcher.IsChanged.ShouldBeFalse();
        watcher.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void AcceptChangesAdvancesBaselineWithoutModelEdits()
    {
        var session = new Settings().CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);

        session.Observable.Label = "dirty";
        watcher.IsChanged.ShouldBeTrue();

        // Baseline-only advance publishes no transition; the watcher still
        // refreshes through the session state notification.
        session.AcceptChanges();

        watcher.IsChanged.ShouldBeFalse();
        watcher.HasChanges.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void AcceptSubmittedChangeSetKeepsPostSubmitEdits()
    {
        var session = new Settings { Label = "a" }.CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);

        session.Observable.Label = "b";
        var submitted = session.CreateChangeSet();
        session.Observable.Label = "c";

        session.AcceptChanges(submitted);

        watcher.IsChanged.ShouldBeTrue();
        watcher.Before.ShouldBe(Optional<string?>.Present("b"));
        watcher.After.ShouldBe(Optional<string?>.Present("c"));
    }

    [Test]
    public void ReloadAndReconcileRefreshWatcher()
    {
        var session = new Settings { Label = "base", RetryCount = 1 }.CreateEditSession();
        using var label = session.ObserveField(Settings.SparsePath.Label);
        using var count = session.ObserveField(Settings.SparsePath.RetryCount);

        session.Observable.Label = "local";
        var server = new Settings { Label = "base", RetryCount = 2 };

        session.Reload(server).HasConflicts.ShouldBeFalse();

        label.IsChanged.ShouldBeTrue();
        label.Before.ShouldBe(Optional<string?>.Present("base"));
        label.After.ShouldBe(Optional<string?>.Present("local"));
        count.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void InPlaceApplyRefreshesWatcher()
    {
        var session = new Settings { Label = "a" }.CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);

        var changes = new Settings { Label = "a" }.CreateChangeSet(
            new Settings { Label = "b", RetryCount = 3 }
        );
        session.TryApplyInPlace(changes, out _).ShouldBeTrue();

        watcher.IsChanged.ShouldBeTrue();
        watcher.After.ShouldBe(Optional<string?>.Present("b"));
    }

    [Test]
    public void HistoryUndoAndRedoReplayWatcher()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });
        using var watcher = session.ObserveField(HistoryScalarModel.SparsePath.Name);

        session.Observable.Name = "after";
        watcher.IsChanged.ShouldBeTrue();

        history.Undo();
        watcher.IsChanged.ShouldBeFalse();

        history.Redo();
        watcher.IsChanged.ShouldBeTrue();
        watcher.After.ShouldBe(Optional<string>.Present("after"));
    }

    [Test]
    public void ReconcileAdoptsPersistedBaselineForWatcher()
    {
        var firstTemp = Guid.NewGuid();
        var model = new TempOrderHolder();
        var session = model.CreateEditSession();
        model.Lines.Add(new TempOrderLine { Name = "one", TemporaryId = firstTemp });
        var submitted = session.CreateChangeSet();
        using var member = session.ObserveField(TempOrderHolder.SparsePath.Lines);

        var persisted = new TempOrderHolder
        {
            Lines =
            [
                new TempOrderLine
                {
                    Id = 1001,
                    Name = "ONE",
                    TemporaryId = firstTemp,
                },
            ],
        };

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        // Reconciled rows adopt persisted values, so the member watcher sees
        // no remaining change for the acknowledged submission.
        member.HasChanges.ShouldBeFalse();
    }
}
