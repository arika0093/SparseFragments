using SparseFragments.Playground.Models;

namespace SparseFragments.Tests;

public sealed class RosterHighlightTests
{
    private static RosterEditState State(params QuestRow[] rows)
    {
        var state = new RosterEditState();
        state.Rows.AddRange(rows);
        return state;
    }

    private static QuestRow Row(string id, string title = "", int points = 0, string scores = "") =>
        new()
        {
            Id = id,
            Title = title,
            Points = points,
            ScoresText = scores,
        };

    private static (
        Dictionary<QuestRow, RosterRowHighlight> Before,
        Dictionary<QuestRow, RosterRowHighlight> After
    ) Build(RosterEditState before, RosterEditState after)
    {
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(before, after);
        return coordinator.Highlights(before, after);
    }

    [Test]
    public void DefaultsShowAddRemoveEditAndMove()
    {
        var (before, after) = Build(
            RosterEditState.BeforeDefaults(),
            RosterEditState.AfterDefaults()
        );
        after.Single(kv => kv.Key.Id == "d").Value.IsAdded.ShouldBeTrue();
        before.Single(kv => kv.Key.Id == "a").Value.IsRemoved.ShouldBeTrue();
        var edited = after.Single(kv => kv.Key.Id == "b").Value;
        edited.TitleChanged.ShouldBeTrue();
        edited.PointsChanged.ShouldBeTrue();
        edited.ScoresChanged.ShouldBeTrue();
        after.Single(kv => kv.Key.Id == "c").Value.Moved.ShouldBeTrue();
        before.Single(kv => kv.Key.Id == "c").Value.Moved.ShouldBeTrue();
    }

    [Test]
    public void AddOnly()
    {
        var (_, after) = Build(State(Row("a")), State(Row("a"), Row("b", "B")));
        after.Single(kv => kv.Key.Id == "b").Value.IsAdded.ShouldBeTrue();
        after.Single(kv => kv.Key.Id == "a").Value.IsAdded.ShouldBeFalse();
    }

    [Test]
    public void RemoveOnly()
    {
        var (before, _) = Build(State(Row("a"), Row("b")), State(Row("a")));
        before.Single(kv => kv.Key.Id == "b").Value.IsRemoved.ShouldBeTrue();
        before.Single(kv => kv.Key.Id == "a").Value.IsRemoved.ShouldBeFalse();
    }

    [Test]
    public void ScalarMemberEdit()
    {
        var (_, after) = Build(
            State(Row("a", "Old", 1)),
            State(Row("a", "New", 1))
        );
        var highlight = after.Single().Value;
        highlight.TitleChanged.ShouldBeTrue();
        highlight.PointsChanged.ShouldBeFalse();
        highlight.ScoresChanged.ShouldBeFalse();
    }

    [Test]
    public void ScalarCollectionEdit()
    {
        var (_, after) = Build(
            State(Row("a", scores: "10, 20")),
            State(Row("a", scores: "10, 21"))
        );
        after.Single().Value.ScoresChanged.ShouldBeTrue();
    }

    [Test]
    public void FormattingOnlyScoresAreNotAnEdit()
    {
        var (_, after) = Build(
            State(Row("a", scores: "10,20")),
            State(Row("a", scores: "10, 20"))
        );
        var highlight = after.Single().Value;
        highlight.ScoresChanged.ShouldBeFalse();
        highlight.TitleChanged.ShouldBeFalse();
    }

    [Test]
    public void MultipleMemberEditsOnOneRow()
    {
        var (_, after) = Build(
            State(Row("a", "Old", 1, "1")),
            State(Row("a", "New", 2, "2"))
        );
        var highlight = after.Single().Value;
        highlight.TitleChanged.ShouldBeTrue();
        highlight.PointsChanged.ShouldBeTrue();
        highlight.ScoresChanged.ShouldBeTrue();
    }

    [Test]
    public void ReorderOnly()
    {
        var (_, after) = Build(
            State(Row("a"), Row("b")),
            State(Row("b"), Row("a"))
        );
        foreach (var highlight in after.Values)
        {
            highlight.Moved.ShouldBeTrue();
            highlight.TitleChanged.ShouldBeFalse();
            highlight.IsAdded.ShouldBeFalse();
        }
    }

    [Test]
    public void ReorderPlusEdit()
    {
        var (_, after) = Build(
            State(Row("a", "A"), Row("b", "B")),
            State(Row("b", "B2"), Row("a", "A"))
        );
        var moved = after.Single(kv => kv.Key.Id == "a").Value;
        moved.Moved.ShouldBeTrue();
        moved.TitleChanged.ShouldBeFalse();
        var edited = after.Single(kv => kv.Key.Id == "b").Value;
        edited.Moved.ShouldBeTrue();
        edited.TitleChanged.ShouldBeTrue();
    }

    [Test]
    public void KeyChangeIsRemovePlusAdd()
    {
        var (before, after) = Build(State(Row("a", "A")), State(Row("b", "A")));
        before.Single().Value.IsRemoved.ShouldBeTrue();
        after.Single().Value.IsAdded.ShouldBeTrue();
    }

    [Test]
    public void EditThenRestoreHasNoChanges()
    {
        var (before, after) = Build(
            State(Row("a", "A", 1, "1")),
            State(Row("a", "A", 1, "1"))
        );
        before.Values.ShouldAllBe(static h => !h.IsRemoved && !h.Moved);
        after.Values.ShouldAllBe(static h =>
            !h.IsAdded && !h.TitleChanged && !h.PointsChanged && !h.ScoresChanged && !h.Moved
        );
    }

    [Test]
    public void DuplicateKeysReturnEmptyMaps()
    {
        var (before, after) = Build(State(Row("a"), Row("a")), State(Row("a")));
        before.ShouldBeEmpty();
        after.ShouldBeEmpty();
    }

    [Test]
    public void SharedChangeSetIsSingleSourceOfTruth()
    {
        var before = RosterEditState.BeforeDefaults();
        var after = RosterEditState.AfterDefaults();
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(before, after);

        coordinator.IsInvalid.ShouldBeFalse();
        var changes = coordinator.ChangeSet;
        changes.ShouldNotBeNull();

        // All projections read the shared ChangeSet without recomputing it.
        var highlights = coordinator.Highlights(before, after);
        var summary = coordinator.Summary;
        var manual = coordinator.ManualCSharp("patch");

        ReferenceEquals(changes, coordinator.ChangeSet).ShouldBeTrue();

        // Highlights match a direct projection of the SAME ChangeSet instance.
        var expected = RosterHighlight.BuildFromChangeSet(changes!, before, after);
        highlights.Before.Count.ShouldBe(expected.Before.Count);
        highlights.After.Count.ShouldBe(expected.After.Count);
        foreach (var kv in expected.After)
        {
            var actual = highlights.After[kv.Key];
            actual.IsAdded.ShouldBe(kv.Value.IsAdded);
            actual.TitleChanged.ShouldBe(kv.Value.TitleChanged);
            actual.PointsChanged.ShouldBe(kv.Value.PointsChanged);
            actual.ScoresChanged.ShouldBe(kv.Value.ScoresChanged);
            actual.Moved.ShouldBe(kv.Value.Moved);
        }

        foreach (var kv in expected.Before)
        {
            var actual = highlights.Before[kv.Key];
            actual.IsRemoved.ShouldBe(kv.Value.IsRemoved);
            actual.Moved.ShouldBe(kv.Value.Moved);
        }

        // Summary and snippet reflect the same diff.
        summary.ShouldContain("add:");
        summary.ShouldContain("remove:");
        summary.ShouldContain("edit:");
        manual.ShouldContain("Quests.Add");
        manual.ShouldContain("Quests.Remove");
        manual.ShouldContain("Quests.Edit");
    }

    [Test]
    public void InvalidStateIsConsistentEverywhere()
    {
        var invalidBefore = State(Row("a"), Row("a"));
        var invalidAfter = State(Row("a"));
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(invalidBefore, invalidAfter);

        coordinator.IsInvalid.ShouldBeTrue();
        coordinator.Patch.ShouldBeNull();

        var (before, after) = coordinator.Highlights(invalidBefore, invalidAfter);
        before.ShouldBeEmpty();
        after.ShouldBeEmpty();

        coordinator.Summary.ShouldBe("cannot diff: duplicate keys or invalid state");
        coordinator.ManualCSharp("patch").ShouldContain("Cannot diff");

        var exception = Should.Throw<InvalidOperationException>(() => coordinator.RequirePatch());
        exception.Message.ShouldContain("Cannot diff roster state");
    }

    [Test]
    public void Case3DoesNotDependOnGenericInspection()
    {
        var playground = typeof(RosterCaseCoordinator).Assembly;
        playground.GetType("SparseFragments.Playground.Models.RosterInspection").ShouldBeNull();

        typeof(RosterCaseCoordinator).GetProperty("ModelMetadata").ShouldBeNull();

        // Summary and snippet derive from the typed Quests transition only:
        // no before/after reconstruction parameter remains on the snippet API.
        typeof(PlaygroundSnippets)
            .GetMethod("RosterManualPatchCSharp")!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ShouldBe([typeof(string), typeof(PlaygroundRoster.ChangeSet)]);

        // Generic inspection runtime types were removed by #91 and must stay gone.
        var runtime = typeof(Optional<>).Assembly;
        runtime.GetType("SparseFragments.Metadata.SparsePropertyInfo").ShouldBeNull();
        runtime.GetType("SparseFragments.Metadata.SparsePatchChange").ShouldBeNull();
        runtime.GetType("SparseFragments.Metadata.SparseChangeKind").ShouldBeNull();
    }

    [Test]
    public void HighlightsFollowTypedReorderSemantics()
    {
        // Pure membership shifts are not reorders: survivors stay unmoved even
        // though absolute indexes shift. Typed IsReordered drives Moved.
        var (_, pureAddAfter) = Build(State(Row("a"), Row("b")), State(Row("a"), Row("b"), Row("c")));
        foreach (var highlight in pureAddAfter.Values)
        {
            highlight.Moved.ShouldBeFalse();
        }

        var (_, pureRemoveAfter) = Build(
            State(Row("a"), Row("b"), Row("c")),
            State(Row("b"), Row("c"))
        );
        foreach (var highlight in pureRemoveAfter.Values)
        {
            highlight.Moved.ShouldBeFalse();
        }

        // Summary order is the typed AfterOrder, even when OrderChanged is false.
        var before = State(Row("a"), Row("b"));
        var after = State(Row("a"), Row("b", "B2"));
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(before, after);
        coordinator.Summary.ShouldContain("order: [a→b]");
    }
}
