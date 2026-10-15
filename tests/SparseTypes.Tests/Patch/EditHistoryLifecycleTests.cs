namespace SparseTypes.Tests;

/// <summary>History lifecycle and failure paths.</summary>
public sealed class EditHistoryLifecycleTests
{
    [Test]
    public void AcceptChangesClearsHistoryOnSuccess()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "pending";
        history.CanUndo.ShouldBeTrue();

        session.AcceptChanges();
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void AcceptChangesWithChangeSetClearsHistoryOnSuccess()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "pending";
        var changes = session.CreateChangeSet();
        session.Observable.Name = "pending-plus";
        history.UndoCount.ShouldBe(2);

        session.AcceptChanges(changes);
        history.CanUndo.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void FailedAcceptChangesKeepsHistory()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "pending";
        history.UndoCount.ShouldBe(1);

        var foreign = new HistoryScalarModel { Name = "other" }.CreateChangeSet(
            new HistoryScalarModel { Name = "foreign" }
        );
        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(foreign));

        history.UndoCount.ShouldBe(1);
        history.Undo();
        model.Name.ShouldBe("base");
    }

    [Test]
    public void ReloadClearsHistoryOnSuccessAndKeepsItOnConflict()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "local";
        var disjointServer = new HistoryScalarModel { Name = "base" };
        var disjoint = session.Reload(disjointServer);
        disjoint.HasConflicts.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();

        session.Observable.Name = "local-two";
        history.CanUndo.ShouldBeTrue();
        var conflictingServer = new HistoryScalarModel { Name = "remote" };
        var conflicted = session.Reload(conflictingServer);
        conflicted.HasConflicts.ShouldBeTrue();
        history.CanUndo.ShouldBeTrue();
    }

    [Test]
    public void RevertChangesClearsHistoryOnSuccess()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "pending";
        history.CanUndo.ShouldBeTrue();

        session.RevertChanges();
        model.Name.ShouldBe("base");
        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
    }

    [Test]
    public void FailedUndoLeavesStacksUntouched()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "after";
        history.UndoCount.ShouldBe(1);

        // Injects a transition whose before-state never matched the live
        // model, so its inverse cannot apply to the current state.
        var foreign = new HistoryScalarModel { Name = "other" }.CreateChangeSet(
            new HistoryScalarModel { Name = "foreign" }
        );
        history.OnTransition(foreign);
        history.UndoCount.ShouldBe(2);

        history.TryUndo(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.ShouldNotBeEmpty();

        // Nothing was popped or moved: the model and both stacks are intact.
        model.Name.ShouldBe("after");
        history.UndoCount.ShouldBe(2);
        history.CanRedo.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => history.Undo());
        history.UndoCount.ShouldBe(2);
    }

    [Test]
    public void NewEditAfterUndoClearsRedo()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "one";
        history.Undo();
        model.Name.ShouldBe("base");
        history.CanRedo.ShouldBeTrue();

        session.Observable.Name = "branched";
        history.CanRedo.ShouldBeFalse();
        history.UndoCount.ShouldBe(1);
    }

    [Test]
    public void ThrowingApplyLeavesStacksUntouched()
    {
        var applied = 0;
        var hooks = new SparseEditHistoryHooks<string>
        {
            IsEmpty = static changes => changes.Length == 0,
            Invert = static changes => changes,
            Compose = static (first, second) => first + second,
            TryApply = (string changes, out IReadOnlyList<SparseConflict>? conflicts) =>
            {
                conflicts = null;
                applied++;
                throw new InvalidOperationException("setter failed");
            },
            Subscribe = static handler => { },
            Unsubscribe = static handler => { },
        };
        using var history = new SparseEditHistory<string>(
            new EditHistoryOptions { MaxEntries = 4 },
            hooks
        );

        history.OnTransition("one");
        history.UndoCount.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => history.Undo());
        history.UndoCount.ShouldBe(1);
        history.CanRedo.ShouldBeFalse();
        applied.ShouldBe(1);
    }

    [Test]
    public void ConflictingApplyReturnsFalseWithoutPopping()
    {
        var hooks = new SparseEditHistoryHooks<string>
        {
            IsEmpty = static changes => changes.Length == 0,
            Invert = static changes => changes,
            Compose = static (first, second) => first + second,
            TryApply = (string changes, out IReadOnlyList<SparseConflict>? conflicts) =>
            {
                conflicts = new[]
                {
                    new SparseConflict(
                        SparsePath.Root<HistoryScalarModel>(),
                        SparseConflictKind.Scalar,
                        Optional<object?>.Missing,
                        Optional<object?>.Missing,
                        Optional<object?>.Missing,
                        "conflict"
                    ),
                };
                return false;
            },
            Subscribe = static handler => { },
            Unsubscribe = static handler => { },
        };
        using var history = new SparseEditHistory<string>(
            new EditHistoryOptions { MaxEntries = 4 },
            hooks
        );

        history.OnTransition("one");
        history.TryUndo(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        history.UndoCount.ShouldBe(1);
        history.CanRedo.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => history.Undo());
        history.UndoCount.ShouldBe(1);
    }

    [Test]
    public void ImmutableDriftReportsConflictsWithoutPoppingHistory()
    {
        var baseline = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var current = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "changed init value",
        };
        var session = baseline.CreateEditSession(current);
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 8 });

        // The immutable drift is injected as an observed transition so the
        // test exercises the real apply path for init-only members.
        history.OnTransition(session.CreateChangeSet());
        history.UndoCount.ShouldBe(1);

        history.TryUndo(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.ShouldNotBeEmpty();
        history.UndoCount.ShouldBe(1);
        history.CanRedo.ShouldBeFalse();
        current.Initialized.ShouldBe("changed init value");
    }

    [Test]
    public void FailedReconcileKeepsHistory()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "pending";
        var foreign = new HistoryScalarModel { Name = "other" }.CreateChangeSet(
            new HistoryScalarModel { Name = "foreign" }
        );
        var persisted = new HistoryScalarModel { Name = "persisted" };

        session.TryReconcile(foreign, persisted, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        history.UndoCount.ShouldBe(1);
    }

    [Test]
    public void SuccessfulReconcileClearsHistory()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "pending";
        var submitted = session.CreateChangeSet();
        var persisted = model.DeepClone();

        session.TryReconcile(submitted, persisted, out var error).ShouldBeTrue();
        error.ShouldBeNull();
        history.CanUndo.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void OutOfBandMutationFoldsIntoNextObservedTransition()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        // Direct model writes bypass the observable proxy.
        model.Name = "raw";
        history.CanUndo.ShouldBeFalse();

        session.Observable.Count = 2;
        history.UndoCount.ShouldBe(1);

        history.Undo();
        model.Name.ShouldBe("base");
        model.Count.ShouldBe(1);
    }
}
