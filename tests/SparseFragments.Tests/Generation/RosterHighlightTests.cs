using SparseFragments.Playground.Models;

namespace SparseFragments.Tests;

public sealed class RosterHighlightTests
{
    private static PlaygroundRoster State(params PlaygroundQuest[] quests)
    {
        return new PlaygroundRoster { Quests = quests.ToList() };
    }

    private static PlaygroundQuest Quest(
        string id,
        string title = "",
        int points = 0,
        params int[] scores
    ) =>
        new()
        {
            Id = id,
            Title = title,
            Points = points,
            Scores = scores.ToList(),
        };

    private static (
        Dictionary<PlaygroundQuest, RosterRowHighlight> Before,
        Dictionary<PlaygroundQuest, RosterRowHighlight> After
    ) Build(PlaygroundRoster before, PlaygroundRoster after)
    {
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(before, after);
        return coordinator.Highlights(before, after);
    }

    [Test]
    public void DefaultsShowAddRemoveEditAndMove()
    {
        var (before, after) = Build(RosterDefaults.Before(), RosterDefaults.After());
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
    public void FieldEdit()
    {
        var (_, after) = Build(
            State(Quest("a", "Old", 1)),
            State(Quest("a", "New", 1))
        );
        var highlight = after.Single().Value;
        highlight.TitleChanged.ShouldBeTrue();
        highlight.PointsChanged.ShouldBeFalse();
        highlight.ScoresChanged.ShouldBeFalse();
    }

    [Test]
    public void ScoresTextEditUpdatesScores()
    {
        // UI helper parses the text-field input into the real model list.
        IntListText.Parse("10, 21").ShouldBe(new List<int> { 10, 21 });
        IntListText.Parse("10, abc, 20").ShouldBe(new List<int> { 10, 20 });
        IntListText.Format(new List<int> { 10, 20 }).ShouldBe("10, 20");

        var before = State(Quest("a", scores: [10, 20]));
        var after = State(Quest("a", scores: [10, 21]));
        var (_, highlights) = Build(before, after);
        highlights.Single().Value.ScoresChanged.ShouldBeTrue();
        after.Quests.Single().Scores.ShouldBe(new List<int> { 10, 21 });
    }

    [Test]
    public void AddOnly()
    {
        var (_, after) = Build(State(Quest("a")), State(Quest("a"), Quest("b", "B")));
        after.Single(kv => kv.Key.Id == "b").Value.IsAdded.ShouldBeTrue();
        after.Single(kv => kv.Key.Id == "a").Value.IsAdded.ShouldBeFalse();
    }

    [Test]
    public void AddMutatesQuestsDirectly()
    {
        var roster = State(Quest("a"));
        roster.Quests.Add(Quest("b", "B"));
        roster.Quests.Count.ShouldBe(2);
        var (_, after) = Build(State(Quest("a")), roster);
        after.Single(kv => kv.Key.Id == "b").Value.IsAdded.ShouldBeTrue();
    }

    [Test]
    public void RemoveOnly()
    {
        var (before, _) = Build(State(Quest("a"), Quest("b")), State(Quest("a")));
        before.Single(kv => kv.Key.Id == "b").Value.IsRemoved.ShouldBeTrue();
        before.Single(kv => kv.Key.Id == "a").Value.IsRemoved.ShouldBeFalse();
    }

    [Test]
    public void RemoveMutatesQuestsDirectly()
    {
        var roster = State(Quest("a"), Quest("b"));
        roster.Quests.RemoveAt(1);
        roster.Quests.Single().Id.ShouldBe("a");
        var (before, _) = Build(State(Quest("a"), Quest("b")), roster);
        before.Single(kv => kv.Key.Id == "b").Value.IsRemoved.ShouldBeTrue();
    }

    [Test]
    public void ScalarMemberEdit()
    {
        var (_, after) = Build(
            State(Quest("a", "Old", 1)),
            State(Quest("a", "New", 1))
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
            State(Quest("a", scores: [10, 20])),
            State(Quest("a", scores: [10, 21]))
        );
        after.Single().Value.ScoresChanged.ShouldBeTrue();
    }

    [Test]
    public void FormattingOnlyScoresAreNotAnEdit()
    {
        // Same parsed list, regardless of text formatting.
        IntListText.Parse("10,20").ShouldBe(IntListText.Parse("10, 20"));
        var (_, after) = Build(
            State(Quest("a", scores: [10, 20])),
            State(Quest("a", scores: [10, 20]))
        );
        var highlight = after.Single().Value;
        highlight.ScoresChanged.ShouldBeFalse();
        highlight.TitleChanged.ShouldBeFalse();
    }

    [Test]
    public void MultipleMemberEditsOnOneRow()
    {
        var (_, after) = Build(
            State(Quest("a", "Old", 1, 1)),
            State(Quest("a", "New", 2, 2))
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
            State(Quest("a"), Quest("b")),
            State(Quest("b"), Quest("a"))
        );
        foreach (var highlight in after.Values)
        {
            highlight.Moved.ShouldBeTrue();
            highlight.TitleChanged.ShouldBeFalse();
            highlight.IsAdded.ShouldBeFalse();
        }
    }

    [Test]
    public void ReorderPreservesRowIdentity()
    {
        // Simulates the SortableList OnUpdate path: the same quest instances move.
        var first = Quest("a");
        var second = Quest("b");
        var roster = State(first, second);
        var moved = roster.Quests[0];
        roster.Quests.RemoveAt(0);
        roster.Quests.Add(moved);
        ReferenceEquals(roster.Quests[0], second).ShouldBeTrue();
        ReferenceEquals(roster.Quests[1], first).ShouldBeTrue();

        var (_, after) = Build(State(Quest("a"), Quest("b")), roster);
        foreach (var highlight in after.Values)
        {
            highlight.Moved.ShouldBeTrue();
        }
    }

    [Test]
    public void ReorderPlusEdit()
    {
        var (_, after) = Build(
            State(Quest("a", "A"), Quest("b", "B")),
            State(Quest("b", "B2"), Quest("a", "A"))
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
        var (before, after) = Build(State(Quest("a", "A")), State(Quest("b", "A")));
        before.Single().Value.IsRemoved.ShouldBeTrue();
        after.Single().Value.IsAdded.ShouldBeTrue();
    }

    [Test]
    public void KeyEditIsOrdinaryModelMutation()
    {
        var roster = State(Quest("a", "A"));
        roster.Quests[0].Id = "b";
        roster.Quests.Single().Id.ShouldBe("b");
        var (before, after) = Build(State(Quest("a", "A")), roster);
        before.Single().Value.IsRemoved.ShouldBeTrue();
        after.Single().Value.IsAdded.ShouldBeTrue();
    }

    [Test]
    public void EditThenRestoreHasNoChanges()
    {
        var (before, after) = Build(
            State(Quest("a", "A", 1, 1)),
            State(Quest("a", "A", 1, 1))
        );
        before.Values.ShouldAllBe(static h => !h.IsRemoved && !h.Moved);
        after.Values.ShouldAllBe(static h =>
            !h.IsAdded && !h.TitleChanged && !h.PointsChanged && !h.ScoresChanged && !h.Moved
        );
    }

    [Test]
    public void DuplicateKeysReturnEmptyMaps()
    {
        var (before, after) = Build(State(Quest("a"), Quest("a")), State(Quest("a")));
        before.ShouldBeEmpty();
        after.ShouldBeEmpty();
    }

    [Test]
    public void ModelJsonAndFragmentPreviewsMatchEditorState()
    {
        var before = RosterDefaults.Before();
        var after = RosterDefaults.After();

        // Previews serialize the actual models directly.
        var beforeJson = PlaygroundJson.WriteRosterModel(before);
        beforeJson.ShouldContain("\"a\"");
        beforeJson.ShouldContain("First");
        var afterJson = PlaygroundJson.WriteRosterModel(after);
        afterJson.ShouldContain("\"d\"");
        afterJson.ShouldContain("Fourth");

        var beforeFragment = PlaygroundRoster.Fragment.From(before);
        var afterFragment = PlaygroundRoster.Fragment.From(after);
        var beforeFragmentJson = PlaygroundJson.WriteRosterFragment(beforeFragment);
        var afterFragmentJson = PlaygroundJson.WriteRosterFragment(afterFragment);
        beforeFragmentJson.ShouldContain("\"a\"");
        afterFragmentJson.ShouldContain("\"d\"");

        // The shared ChangeSet derives from those same fragments exactly once.
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(before, after);
        coordinator.IsInvalid.ShouldBeFalse();
        var direct = PlaygroundRoster.ChangeSet.Between(
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(before)),
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(after))
        );
        direct.Quests.Added.Select(q => q.Id).ShouldBe(["d"]);
        direct.Quests.Removed.Select(q => q.Id).ShouldBe(["a"]);
    }

    [Test]
    public void SharedChangeSetIsSingleSourceOfTruth()
    {
        var before = RosterDefaults.Before();
        var after = RosterDefaults.After();
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
        var invalidBefore = State(Quest("a"), Quest("a"));
        var invalidAfter = State(Quest("a"));
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
    public void Case3HasNoWrapperTypes()
    {
        // QuestRow / RosterEditState scaffolding is gone; defaults are real models.
        var playground = typeof(RosterCaseCoordinator).Assembly;
        playground.GetType("SparseFragments.Playground.Models.QuestRow").ShouldBeNull();
        playground.GetType("SparseFragments.Playground.Models.RosterEditState").ShouldBeNull();
        RosterDefaults.Before().Quests.Count.ShouldBe(3);
        RosterDefaults.After().Quests.Count.ShouldBe(3);
    }

    [Test]
    public void HighlightsFollowTypedReorderSemantics()
    {
        // Pure membership shifts are not reorders: survivors stay unmoved even
        // though absolute indexes shift. Typed IsReordered drives Moved.
        var (_, pureAddAfter) = Build(State(Quest("a"), Quest("b")), State(Quest("a"), Quest("b"), Quest("c")));
        foreach (var highlight in pureAddAfter.Values)
        {
            highlight.Moved.ShouldBeFalse();
        }

        var (_, pureRemoveAfter) = Build(
            State(Quest("a"), Quest("b"), Quest("c")),
            State(Quest("b"), Quest("c"))
        );
        foreach (var highlight in pureRemoveAfter.Values)
        {
            highlight.Moved.ShouldBeFalse();
        }

        // Summary order is the typed AfterOrder, even when OrderChanged is false.
        var before = State(Quest("a"), Quest("b"));
        var after = State(Quest("a"), Quest("b", "B2"));
        var coordinator = new RosterCaseCoordinator();
        coordinator.Update(before, after);
        coordinator.Summary.ShouldContain("order: [a→b]");
    }
}
