namespace SparseTypes.Tests;

/// <summary>Models for opt-in edit history tests (issue #252).</summary>
[SparseModel]
public partial class HistoryScalarModel
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseModel]
public partial class HistoryKeyedItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class HistoryKeyedModel
{
    public List<HistoryKeyedItem> Items { get; set; } = [];
}

/// <summary>Opt-in bounded change-set history with undo and redo.</summary>
public sealed class EditHistoryTests
{
    [Test]
    public void UndoRestoresPriorScalarStateAndRedoReapplies()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();

        session.Observable.Name = "after";
        history.CanUndo.ShouldBeTrue();
        history.UndoCount.ShouldBe(1);
        model.Name.ShouldBe("after");

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
    public void NewOrdinaryEditClearsRedoWithoutRerecordingReplays()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "one";
        session.Observable.Name = "two";
        history.UndoCount.ShouldBe(2);

        history.Undo();
        model.Name.ShouldBe("one");
        history.CanRedo.ShouldBeTrue();

        // Replays are suppressed: the undo transition itself stored nothing.
        history.UndoCount.ShouldBe(1);

        session.Observable.Name = "branched";
        history.CanRedo.ShouldBeFalse();
        model.Name.ShouldBe("branched");
        history.UndoCount.ShouldBe(2);
    }

    [Test]
    public void BatchEditStoresOneEntry()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.BatchEdit(() =>
        {
            session.Observable.Name = "batched";
            session.Observable.Count = 2;
        });

        history.UndoCount.ShouldBe(1);

        history.Undo();
        model.Name.ShouldBe("base");
        model.Count.ShouldBe(1);

        history.Redo();
        model.Name.ShouldBe("batched");
        model.Count.ShouldBe(2);
    }

    [Test]
    public void ExplicitGroupStoresOneEntryAcrossEvents()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        using (history.BeginGroup())
        {
            session.Observable.Name = "typed";
            session.Observable.Count = 2;
        }

        history.UndoCount.ShouldBe(1);

        history.Undo();
        model.Name.ShouldBe("base");
        model.Count.ShouldBe(1);
    }

    [Test]
    public void UnrelatedEditsAreNeverSilentlyCoalesced()
    {
        var model = new HistoryScalarModel { Name = "base", Count = 1 };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "first";
        session.Observable.Count = 2;

        history.UndoCount.ShouldBe(2);

        history.Undo();
        model.Count.ShouldBe(1);
        model.Name.ShouldBe("first");

        history.Undo();
        model.Name.ShouldBe("base");
    }

    [Test]
    public void BoundedHistoryEvictsOldestEntries()
    {
        var model = new HistoryScalarModel { Count = 0 };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 3 });

        for (var index = 1; index <= 5; index++)
        {
            session.Observable.Count = index;
        }

        history.UndoCount.ShouldBe(3);

        history.Undo();
        model.Count.ShouldBe(4);
        history.Undo();
        model.Count.ShouldBe(3);
        history.Undo();
        model.Count.ShouldBe(2);
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public void EmptyTransitionsStoreNothing()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.BatchEdit(() => { });
        session.Observable.Name = "base";

        history.CanUndo.ShouldBeFalse();
        history.UndoCount.ShouldBe(0);
    }

    [Test]
    public void KeyedEditsUndoAndRedo()
    {
        var model = new HistoryKeyedModel
        {
            Items = [new HistoryKeyedItem { Id = "a", Name = "a-name" }],
        };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Items[0].Name = "edited";
        model.Items[0].Name.ShouldBe("edited");

        history.Undo();
        model.Items[0].Name.ShouldBe("a-name");

        history.Redo();
        model.Items[0].Name.ShouldBe("edited");
    }

    [Test]
    public void DisposeUnsubscribesAndReleasesRetention()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        var history = session.CreateHistory(new EditHistoryOptions { MaxEntries = 50 });

        session.Observable.Name = "one";
        history.UndoCount.ShouldBe(1);

        history.Dispose();

        // Later edits run without a stale subscription and without throwing.
        session.Observable.Name = "two";
        model.Name.ShouldBe("two");

        // Retention was released: every accessor now reports disposal.
        Should.Throw<ObjectDisposedException>(() => history.Undo());
        Should.Throw<ObjectDisposedException>(() => history.Redo());
        Should.Throw<ObjectDisposedException>(() => history.Clear());
        Should.Throw<ObjectDisposedException>(() => history.BeginGroup());
    }

    [Test]
    public void DefaultOptionsApplyWhenNullIsPassed()
    {
        var model = new HistoryScalarModel { Name = "base" };
        var session = model.CreateEditSession();
        using var history = session.CreateHistory();

        history.MaxEntries.ShouldBe(50);
        session.Observable.Name = "after";
        history.UndoCount.ShouldBe(1);
    }

    [Test]
    public void InvalidMaxEntriesAreRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EditHistoryOptions { MaxEntries = 0 });
    }
}
