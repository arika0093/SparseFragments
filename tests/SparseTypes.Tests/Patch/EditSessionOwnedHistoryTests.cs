namespace SparseTypes.Tests;

/// <summary>Session-owned single undo/redo history (issue #269).</summary>
/// <remarks>
/// Pins the <c>EnableHistory</c> consumer API against the established #252
/// semantics: opt-in retention, repeat-enable rules, explicit disable,
/// non-disposable ownership, and stack-state notifications.
/// </remarks>
public sealed class EditSessionOwnedHistoryTests
{
    [Test]
    public void HistoryIsNullUntilEnabled()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();

        session.History.ShouldBeNull();

        session.Observable.Name = "after";
        session.History.ShouldBeNull();
        model.Name.ShouldBe("after");
    }

    [Test]
    public void EnableHistoryExposesUndoRedoControl()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();

        var history = session.EnableHistory(new EditHistoryOptions { MaxEntries = 50 });

        history.ShouldNotBeNull();
        ReferenceEquals(history, session.History).ShouldBeTrue();
        history.MaxEntries.ShouldBe(50);
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();

        session.Observable.Name = "after";
        history.CanUndo.ShouldBeTrue();
        history.UndoCount.ShouldBe(1);

        history.Undo();
        model.Name.ShouldBe("base");
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeTrue();

        history.Redo();
        model.Name.ShouldBe("after");
        history.CanUndo.ShouldBeTrue();
        history.CanRedo.ShouldBeFalse();
    }

    [Test]
    public void DefaultOptionsApplyWhenNullIsPassed()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();

        var history = session.EnableHistory();

        history.MaxEntries.ShouldBe(50);
        session.Observable.Name = "after";
        history.UndoCount.ShouldBe(1);

        var repeated = session.EnableHistory();

        ReferenceEquals(history, repeated).ShouldBeTrue();
        repeated.UndoCount.ShouldBe(1);
    }

    [Test]
    public void RepeatEnableWithSameOptionsIsANoOp()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory(new EditHistoryOptions { MaxEntries = 8 });

        session.Observable.Name = "one";
        history.UndoCount.ShouldBe(1);

        var repeated = session.EnableHistory(new EditHistoryOptions { MaxEntries = 8 });

        ReferenceEquals(history, repeated).ShouldBeTrue();
        ReferenceEquals(repeated, session.History).ShouldBeTrue();
        repeated.UndoCount.ShouldBe(1);
    }

    [Test]
    public void RepeatEnableWithConflictingOptionsThrowsWithoutResetting()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory(new EditHistoryOptions { MaxEntries = 8 });

        session.Observable.Name = "one";

        Should.Throw<InvalidOperationException>(() => session.EnableHistory());
        Should.Throw<InvalidOperationException>(() =>
            session.EnableHistory(new EditHistoryOptions { MaxEntries = 50 })
        );

        ReferenceEquals(history, session.History).ShouldBeTrue();
        history.UndoCount.ShouldBe(1);
        history.Undo();
        model.Name.ShouldBe("base");
    }

    [Test]
    public void SessionOwnedHistoryCannotBeDisposedByConsumers()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();

        (history is IDisposable).ShouldBeFalse();
    }

    [Test]
    public void DisableHistoryReleasesRetentionAndNotifiesAvailability()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var availabilityChanges = 0;
        session.HistoryChanged += () => availabilityChanges++;

        var history = session.EnableHistory();
        availabilityChanges.ShouldBe(1);

        session.Observable.Name = "one";
        history.UndoCount.ShouldBe(1);
        availabilityChanges.ShouldBe(1);

        session.DisableHistory();
        availabilityChanges.ShouldBe(2);
        session.History.ShouldBeNull();

        // Disabling twice is a no-op with no further notification.
        session.DisableHistory();
        availabilityChanges.ShouldBe(2);

        // Later edits run without a stale subscription.
        session.Observable.Name = "two";
        model.Name.ShouldBe("two");

        // The stale view reports disposal instead of session state.
        Should.Throw<ObjectDisposedException>(() => history.Undo());
        Should.Throw<ObjectDisposedException>(() => history.CanUndo.ShouldBeFalse());

        // Re-enabling starts fresh and notifies again.
        var fresh = session.EnableHistory();
        availabilityChanges.ShouldBe(3);
        ReferenceEquals(history, fresh).ShouldBeFalse();
        fresh.UndoCount.ShouldBe(0);
        fresh.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void ReEnableAfterDisableSeesOnlySubsequentEdits()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        session.EnableHistory();

        session.Observable.Name = "one";
        session.DisableHistory();
        session.Observable.Name = "two";

        var history = session.EnableHistory();
        history.UndoCount.ShouldBe(0);

        session.Observable.Name = "three";
        history.UndoCount.ShouldBe(1);
        history.Undo();
        model.Name.ShouldBe("two");
    }

    [Test]
    public void StateChangedCoversEditsGroupingReplayAndAccept()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var stateChanges = 0;
        history.StateChanged += () => stateChanges++;

        session.Observable.Name = "one";
        stateChanges.ShouldBe(1);

        session.BatchEdit(() => session.Observable.Count = 2);
        stateChanges.ShouldBe(2);

        history.Group(() =>
        {
            session.Observable.Name = "two";
            session.Observable.Count = 3;
        });
        stateChanges.ShouldBe(3);
        history.UndoCount.ShouldBe(3);

        history.Undo();
        stateChanges.ShouldBe(4);

        history.Redo();
        stateChanges.ShouldBe(5);

        session.AcceptChanges();
        stateChanges.ShouldBe(6);
        history.CanUndo.ShouldBeFalse();

        // Clearing an empty history and failing a replay raise nothing.
        history.Clear();
        stateChanges.ShouldBe(6);
        history.TryUndo(out var conflicts).ShouldBeFalse();
        conflicts.ShouldBeNull();
        stateChanges.ShouldBe(6);
        Should.Throw<InvalidOperationException>(() => history.Undo());
        stateChanges.ShouldBe(6);
    }

    [Test]
    public void BatchEditAndExplicitGroupStoreSingleEntries()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();

        session.BatchEdit(() =>
        {
            session.Observable.Name = "batched";
            session.Observable.Count = 2;
        });
        history.UndoCount.ShouldBe(1);

        using (history.BeginGroup())
        {
            session.Observable.Name = "typed";
            session.Observable.Count = 3;
        }

        history.UndoCount.ShouldBe(2);

        history.Undo();
        model.Name.ShouldBe("batched");
        model.Count.ShouldBe(2);

        history.Undo();
        model.Name.ShouldBe("base");
        model.Count.ShouldBe(1);
    }

    [Test]
    public void NewEditAfterUndoClearsRedo()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();

        session.Observable.Name = "one";
        history.Undo();
        model.Name.ShouldBe("base");
        history.CanRedo.ShouldBeTrue();

        session.Observable.Name = "branched";
        history.CanRedo.ShouldBeFalse();
        history.UndoCount.ShouldBe(1);
    }

    [Test]
    public void ConflictingUndoLeavesOwnedStacksUntouched()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var stateChanges = 0;
        history.StateChanged += () => stateChanges++;

        session.Observable.Name = "after";
        stateChanges.ShouldBe(1);

        // An out-of-band write breaks the recorded before-state, so the
        // replay conflicts instead of popping the entry.
        model.Name = "raw";
        history.TryUndo(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.ShouldNotBeEmpty();

        model.Name.ShouldBe("raw");
        history.UndoCount.ShouldBe(1);
        history.CanRedo.ShouldBeFalse();
        stateChanges.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => history.Undo());
        history.UndoCount.ShouldBe(1);
    }

    [Test]
    public void AcceptChangesClearsOwnedHistoryOnSuccess()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var stateChanges = 0;
        history.StateChanged += () => stateChanges++;

        session.Observable.Name = "pending";
        stateChanges.ShouldBe(1);

        session.AcceptChanges();
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
        stateChanges.ShouldBe(2);
    }

    [Test]
    public void FailedAcceptChangesKeepsOwnedHistory()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        var stateChanges = 0;
        history.StateChanged += () => stateChanges++;

        session.Observable.Name = "pending";
        var foreign = new HistoryScalarModel { Name = "other" }.CreateChangeSet(
            new HistoryScalarModel { Name = "foreign" }
        );
        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(foreign));

        history.UndoCount.ShouldBe(1);
        stateChanges.ShouldBe(1);
        history.Undo();
        model.Name.ShouldBe("base");
    }

    [Test]
    public void RevertChangesClearsOwnedHistoryOnSuccess()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();

        session.Observable.Name = "pending";
        history.CanUndo.ShouldBeTrue();

        session.RevertChanges();
        model.Name.ShouldBe("base");
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
    }

    [Test]
    public void ReloadClearsOwnedHistoryOnSuccessAndKeepsItOnConflict()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();

        session.Observable.Name = "local";
        var disjoint = session.Reload(new HistoryScalarModel { Name = "base" });
        disjoint.HasConflicts.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();

        session.Observable.Name = "local-two";
        history.CanUndo.ShouldBeTrue();
        var conflicted = session.Reload(new HistoryScalarModel { Name = "remote" });
        conflicted.HasConflicts.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
    }

    [Test]
    public void ReconcileClearsOwnedHistoryOnSuccessAndKeepsItOnFailure()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();

        session.Observable.Name = "pending";
        var foreign = new HistoryScalarModel { Name = "other" }.CreateChangeSet(
            new HistoryScalarModel { Name = "foreign" }
        );
        var persisted = new HistoryScalarModel { Name = "persisted" };
        session.TryReconcile(foreign, persisted, out _).ShouldBeFalse();
        history.UndoCount.ShouldBe(1);

        var submitted = session.CreateChangeSet();
        var persistedClean = model.DeepClone();
        session.TryReconcile(submitted, persistedClean, out _).ShouldBeTrue();
        history.CanUndo.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ForkStartsWithoutHistoryAndParentKeepsItsOwn()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "parent-edit";

        var fork = session.Fork();

        fork.History.ShouldBeNull();
        history.UndoCount.ShouldBe(1);

        fork.Observable.Name = "fork-edit";
        history.UndoCount.ShouldBe(1);
        model.Name.ShouldBe("parent-edit");

        history.Undo();
        model.Name.ShouldBe("base");
        fork.GetModelForFrameworkAccess().Name.ShouldBe("fork-edit");
    }

    [Test]
    public void NewSessionFromSameModelStartsWithoutHistory()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "one";

        var replacement = model.CreateEditSession();

        replacement.History.ShouldBeNull();
        history.UndoCount.ShouldBe(1);

        // The replacement observes its own transitions on the shared model;
        // the original history keeps only its own entry.
        replacement.Observable.Name = "two";
        history.UndoCount.ShouldBe(1);
        model.Name.ShouldBe("two");

        // The replacement edit moved the shared model past the recorded
        // before-state, so the original entry now conflicts instead of
        // popping: session histories never observe each other.
        history.TryUndo(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        history.UndoCount.ShouldBe(1);
    }
}
