extern alias Playground;

using Playground::SparseTypes.Playground.Models;

namespace SparseTypes.Tests;

/// <summary>
/// Case 4 consumer flow through the path-addressed collection facade.
/// </summary>
/// <remarks>The Blazor editors (<c>TaskListEditor</c>/<c>TaskRow</c>) are not runnable without a renderer.</remarks>
public sealed class Case4ConsumerFlowTests
{
    private static PlaygroundSettings Tasks(params (string Id, string Title)[] rows) =>
        new()
        {
            Tasks = rows.Select(row => new PlaygroundTask
                {
                    Id = row.Id,
                    Title = row.Title,
                    Points = 10,
                })
                .ToList(),
        };

    private static List<string> Ids(PlaygroundSettings model) =>
        model.Tasks.Select(static task => task.Id).ToList();

    // Mirrors TaskListEditor.OnSortableUpdate.
    private static bool ApplySortableUpdate(
        PlaygroundSettings.EditSession session,
        int oldIndex,
        int newIndex
    )
    {
        var tasks = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        var count = tasks.Count;
        if (
            oldIndex == newIndex
            || oldIndex < 0
            || oldIndex >= count
            || newIndex < 0
            || newIndex > count
        )
        {
            return false;
        }

        var target = Math.Min(newIndex, count - 1);
        if (oldIndex == target)
        {
            return false;
        }

        tasks.TryMove(oldIndex, target, out _).ShouldBeTrue();
        return true;
    }

    // The pre- TaskListEditor spelling, kept as the parity oracle.
    private static void ApplyLegacySortableUpdate(
        PlaygroundSettings.EditSession session,
        int oldIndex,
        int newIndex
    )
    {
        var tasks = session.Observable.Tasks;
        if (
            oldIndex == newIndex
            || oldIndex < 0
            || oldIndex >= tasks.Count
            || newIndex < 0
            || newIndex > tasks.Count
        )
        {
            return;
        }

        session.BatchEdit(() =>
        {
            var task = tasks[oldIndex];
            tasks.RemoveAt(oldIndex);
            tasks.Insert(Math.Min(newIndex, tasks.Count), task);
        });
    }

    [Test]
    public void SortableUpdateMapping_MatchesLegacyRemoveInsert()
    {
        // Every drag pair, including forward moves, the last position, and
        // the Count-addresses-the-end convention, lands identically.
        for (var oldIndex = 0; oldIndex < 4; oldIndex++)
        {
            for (var newIndex = 0; newIndex <= 4; newIndex++)
            {
                var facadeModel = Tasks(
                    ("t1", "One"),
                    ("t2", "Two"),
                    ("t3", "Three"),
                    ("t4", "Four")
                );
                var legacyModel = Tasks(
                    ("t1", "One"),
                    ("t2", "Two"),
                    ("t3", "Three"),
                    ("t4", "Four")
                );
                var facade = facadeModel.CreateEditSession();
                var legacy = legacyModel.CreateEditSession();

                ApplySortableUpdate(facade, oldIndex, newIndex);
                ApplyLegacySortableUpdate(legacy, oldIndex, newIndex);

                Ids(facadeModel)
                    .ShouldBe(
                        Ids(legacyModel),
                        $"facade and legacy disagree for {oldIndex}->{newIndex}"
                    );
                var facadeOrder = facade.CreateChangeSet().Tasks;
                var legacyOrder = legacy.CreateChangeSet().Tasks;
                facadeOrder.OrderChanged.ShouldBe(legacyOrder.OrderChanged);
                facadeOrder.AfterOrder.ShouldBe(legacyOrder.AfterOrder);
            }
        }
    }

    [Test]
    public void SortableUpdate_StaleAndNoOp_RaiseNoTransition()
    {
        var model = Tasks(("t1", "One"), ("t2", "Two"), ("t3", "Three"));
        var session = model.CreateEditSession();
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        // No-op, stale indices, and a drop of the last row onto the end.
        ApplySortableUpdate(session, 1, 1).ShouldBeFalse();
        ApplySortableUpdate(session, -1, 0).ShouldBeFalse();
        ApplySortableUpdate(session, 0, 9).ShouldBeFalse();
        ApplySortableUpdate(session, 0, 4).ShouldBeFalse();
        ApplySortableUpdate(session, 2, 3).ShouldBeFalse();

        Ids(model).ShouldBe(["t1", "t2", "t3"]);
        transitions.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ConsumerActions_RaiseOneTransitionEach()
    {
        var model = Tasks(("t1", "One"), ("t2", "Two"), ("t3", "Three"));
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        editor.TryAdd(new PlaygroundTask { Id = "t4", Title = "Four" }, out _).ShouldBeTrue();
        editor.TryMoveByKey("t1", 2, out _).ShouldBeTrue();
        editor
            .TryReplaceByKey(
                "t2",
                new PlaygroundTask
                {
                    Id = "t2x",
                    Title = "Two",
                    Points = 10,
                },
                out _
            )
            .ShouldBeTrue();
        editor.TryRemoveByKey("t3", out _).ShouldBeTrue();

        Ids(model).ShouldBe(["t2x", "t1", "t4"]);
        transitions.ShouldBe(4);
    }

    [Test]
    public void ConsumerAdd_SkipsBaselineIds()
    {
        // Mirrors TaskListEditor.AddTask.
        var baselineIds = new[] { "t1", "t2", "t3" };
        var model = Tasks(("t1", "One"), ("t2", "Two"));
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );

        var candidate = PlaygroundIds.Allocate(
            "t",
            session.Observable.Tasks.Select(static task => task.Id).Concat(baselineIds)
        );
        candidate.ShouldBe("t4");

        editor.TryAdd(new PlaygroundTask { Id = candidate, Title = "Four" }, out _).ShouldBeTrue();

        var changes = session.CreateChangeSet();
        changes.Tasks.Added.Select(static task => task.Id).ShouldBe(["t4"]);
        changes
            .Tasks.KeyedState(PlaygroundSettings.SparsePath.Tasks)
            .GetItemState("t4")
            .IsAdded.ShouldBeTrue();
    }

    [Test]
    public void ConsumerHistory_UndoRedoGroupsPerAction()
    {
        var model = Tasks(("t1", "One"), ("t2", "Two"), ("t3", "Three"));
        var session = model.CreateEditSession();
        using var history = session.CreateHistory();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );

        editor.TryAdd(new PlaygroundTask { Id = "t4", Title = "Four" }, out _).ShouldBeTrue();
        editor.TryMoveByKey("t1", 3, out _).ShouldBeTrue();
        editor.TryRemoveByKey("t2", out _).ShouldBeTrue();
        history.UndoCount.ShouldBe(3);
        Ids(model).ShouldBe(["t3", "t4", "t1"]);

        history.Undo();
        history.Undo();
        history.Undo();
        Ids(model).ShouldBe(["t1", "t2", "t3"]);
        session.HasChanges.ShouldBeFalse();

        history.Redo();
        history.Redo();
        history.Redo();
        Ids(model).ShouldBe(["t3", "t4", "t1"]);
    }

    [Test]
    public void ConsumerStaleRow_LeavesModelUntouched()
    {
        var model = Tasks(("t1", "One"), ("t2", "Two"));
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        // A row kept across a removal resolves to an unknown key, and stale
        // drag indices stay out of range: both fail without notifications.
        editor.TryRemoveByKey("zzz", out var removeError).ShouldBeFalse();
        removeError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryMoveByKey("zzz", 0, out var moveError).ShouldBeFalse();
        moveError.ShouldBe(SparseCollectionEditError.UnknownKey);
        editor.TryMove(0, 7, out var indexError).ShouldBeFalse();
        indexError.ShouldBe(SparseCollectionEditError.IndexOutOfRange);

        Ids(model).ShouldBe(["t1", "t2"]);
        transitions.ShouldBe(0);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ConsumerRename_KeepsPositionAndReportsAddRemove()
    {
        var model = Tasks(("t1", "One"), ("t2", "Two"));
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        var transitions = 0;
        session.TransitionObserved += _ => transitions++;

        // Mirrors TaskRow.CommitTaskId: the renamed row keeps its position,
        // and keyed identity reads it as a removal plus an addition.
        editor
            .TryReplaceByKey(
                "t1",
                new PlaygroundTask
                {
                    Id = "t1x",
                    Title = "One",
                    Points = 10,
                },
                out _
            )
            .ShouldBeTrue();
        Ids(model).ShouldBe(["t1x", "t2"]);

        var keyed = session.CreateChangeSet().Tasks.KeyedState(PlaygroundSettings.SparsePath.Tasks);
        keyed.GetItemState("t1").IsRemoved.ShouldBeTrue();
        keyed.GetItemState("t1x").IsAdded.ShouldBeTrue();

        // Renaming onto a taken key fails like the row validation message.
        editor
            .TryReplaceByKey(
                "t2",
                new PlaygroundTask
                {
                    Id = "t1x",
                    Title = "Clash",
                    Points = 10,
                },
                out var clash
            )
            .ShouldBeFalse();
        clash.ShouldBe(SparseCollectionEditError.DuplicateKey);
        Ids(model).ShouldBe(["t1x", "t2"]);
        transitions.ShouldBe(1);
    }

    [Test]
    public void ConsumerHighlights_SurviveFacadeMutations()
    {
        // Mirrors the TaskRow highlight queries over facade-driven edits.
        var model = Tasks(("t1", "One"), ("t2", "Two"));
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );

        editor.TryAdd(new PlaygroundTask { Id = "t3", Title = "Three" }, out _).ShouldBeTrue();
        editor.TryMoveByKey("t1", 2, out _).ShouldBeTrue();
        session.Observable.Tasks.First(static task => task.Id == "t2").Title = "Two v2";

        var changes = session.CreateChangeSet();
        changes
            .Tasks.KeyedState(PlaygroundSettings.SparsePath.Tasks)
            .GetItemState("t3")
            .IsAdded.ShouldBeTrue();
        changes
            .Tasks.KeyedState(PlaygroundSettings.SparsePath.Tasks)
            .GetItemState("t1")
            .IsReordered.ShouldBeTrue();
        changes
            .FieldState(PlaygroundSettings.SparsePath.Tasks.Key("t2").Title)
            .IsChanged.ShouldBeTrue();
        changes
            .FieldState(PlaygroundSettings.SparsePath.Tasks.Key("t1").Points)
            .IsChanged.ShouldBeFalse();
    }
}
