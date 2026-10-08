using System.Text.Json;
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

    private static PlaygroundRoster.ChangeSet Diff(PlaygroundRoster before, PlaygroundRoster after)
    {
        return PlaygroundRoster.ChangeSet.Between(
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(before)),
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(after))
        );
    }

    private static PlaygroundRoster.ChangeSet? TryDiff(PlaygroundRoster before, PlaygroundRoster after)
    {
        try
        {
            return Diff(before, after);
        }
        catch
        {
            return null;
        }
    }

    private static bool TitleChanged(PlaygroundRoster.ChangeSet.QuestsTransition.Item change) =>
        change.IsEdited && change.Edit.Title.IsChanged;

    private static bool PointsChanged(PlaygroundRoster.ChangeSet.QuestsTransition.Item change) =>
        change.IsEdited && change.Edit.Points.IsChanged;

    private static bool ScoresChanged(PlaygroundRoster.ChangeSet.QuestsTransition.Item change) =>
        change.IsEdited && change.Edit.Scores.IsChanged;

    [Test]
    public void DefaultsShowAddRemoveEditAndMove()
    {
        var changes = Diff(RosterDefaults.Before(), RosterDefaults.After());
        var quests = changes.Quests;
        quests.GetChange("d").IsAdded.ShouldBeTrue();
        quests.GetChange("a").IsRemoved.ShouldBeTrue();
        var edited = quests.GetChange("b");
        edited.IsEdited.ShouldBeTrue();
        edited.Edit.Title.IsChanged.ShouldBeTrue();
        edited.Edit.Points.IsChanged.ShouldBeTrue();
        edited.Edit.Scores.IsChanged.ShouldBeTrue();
        quests.GetChange("c").IsReordered.ShouldBeTrue();
    }

    [Test]
    public void FieldEdit()
    {
        var changes = Diff(
            State(Quest("a", "Old", 1)),
            State(Quest("a", "New", 1))
        );
        var change = changes.Quests.GetChange("a");
        TitleChanged(change).ShouldBeTrue();
        PointsChanged(change).ShouldBeFalse();
        ScoresChanged(change).ShouldBeFalse();
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
        var changes = Diff(before, after);
        ScoresChanged(changes.Quests.GetChange("a")).ShouldBeTrue();
        after.Quests.Single().Scores.ShouldBe(new List<int> { 10, 21 });
    }

    [Test]
    public void AddOnly()
    {
        var changes = Diff(State(Quest("a")), State(Quest("a"), Quest("b", "B")));
        changes.Quests.GetChange("b").IsAdded.ShouldBeTrue();
        changes.Quests.GetChange("a").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AddMutatesQuestsDirectly()
    {
        var roster = State(Quest("a"));
        roster.Quests.Add(Quest("b", "B"));
        roster.Quests.Count.ShouldBe(2);
        var changes = Diff(State(Quest("a")), roster);
        changes.Quests.GetChange("b").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void RemoveOnly()
    {
        var changes = Diff(State(Quest("a"), Quest("b")), State(Quest("a")));
        changes.Quests.GetChange("b").IsRemoved.ShouldBeTrue();
        changes.Quests.GetChange("a").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RemoveMutatesQuestsDirectly()
    {
        var roster = State(Quest("a"), Quest("b"));
        roster.Quests.RemoveAt(1);
        roster.Quests.Single().Id.ShouldBe("a");
        var changes = Diff(State(Quest("a"), Quest("b")), roster);
        changes.Quests.GetChange("b").IsRemoved.ShouldBeTrue();
    }

    [Test]
    public void ScalarMemberEdit()
    {
        var changes = Diff(
            State(Quest("a", "Old", 1)),
            State(Quest("a", "New", 1))
        );
        var change = changes.Quests.GetChange("a");
        TitleChanged(change).ShouldBeTrue();
        PointsChanged(change).ShouldBeFalse();
        ScoresChanged(change).ShouldBeFalse();
    }

    [Test]
    public void ScalarCollectionEdit()
    {
        var changes = Diff(
            State(Quest("a", scores: [10, 20])),
            State(Quest("a", scores: [10, 21]))
        );
        ScoresChanged(changes.Quests.GetChange("a")).ShouldBeTrue();
    }

    [Test]
    public void FormattingOnlyScoresAreNotAnEdit()
    {
        // Same parsed list, regardless of text formatting.
        IntListText.Parse("10,20").ShouldBe(IntListText.Parse("10, 20"));
        var changes = Diff(
            State(Quest("a", scores: [10, 20])),
            State(Quest("a", scores: [10, 20]))
        );
        changes.IsEmpty.ShouldBeTrue();
        var change = changes.Quests.GetChange("a");
        change.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void MultipleMemberEditsOnOneRow()
    {
        var changes = Diff(
            State(Quest("a", "Old", 1, 1)),
            State(Quest("a", "New", 2, 2))
        );
        var change = changes.Quests.GetChange("a");
        TitleChanged(change).ShouldBeTrue();
        PointsChanged(change).ShouldBeTrue();
        ScoresChanged(change).ShouldBeTrue();
    }

    [Test]
    public void ReorderOnly()
    {
        var changes = Diff(
            State(Quest("a"), Quest("b")),
            State(Quest("b"), Quest("a"))
        );
        foreach (var item in changes.Quests)
        {
            item.IsReordered.ShouldBeTrue();
            item.IsEdited.ShouldBeFalse();
            item.IsAdded.ShouldBeFalse();
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

        var changes = Diff(State(Quest("a"), Quest("b")), roster);
        foreach (var item in changes.Quests)
        {
            item.IsReordered.ShouldBeTrue();
        }
    }

    [Test]
    public void ReorderPlusEdit()
    {
        var changes = Diff(
            State(Quest("a", "A"), Quest("b", "B")),
            State(Quest("b", "B2"), Quest("a", "A"))
        );
        var moved = changes.Quests.GetChange("a");
        moved.IsReordered.ShouldBeTrue();
        moved.IsEdited.ShouldBeFalse();
        var edited = changes.Quests.GetChange("b");
        edited.IsReordered.ShouldBeTrue();
        edited.IsEdited.ShouldBeTrue();
        edited.Edit.Title.IsChanged.ShouldBeTrue();
    }

    [Test]
    public void KeyChangeIsRemovePlusAdd()
    {
        var changes = Diff(State(Quest("a", "A")), State(Quest("b", "A")));
        changes.Quests.GetChange("a").IsRemoved.ShouldBeTrue();
        changes.Quests.GetChange("b").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void KeyEditIsOrdinaryModelMutation()
    {
        var roster = State(Quest("a", "A"));
        roster.Quests[0].Id = "b";
        roster.Quests.Single().Id.ShouldBe("b");
        var changes = Diff(State(Quest("a", "A")), roster);
        changes.Quests.GetChange("a").IsRemoved.ShouldBeTrue();
        changes.Quests.GetChange("b").IsAdded.ShouldBeTrue();
    }

    [Test]
    public void EditThenRestoreHasNoChanges()
    {
        var changes = Diff(
            State(Quest("a", "A", 1, 1)),
            State(Quest("a", "A", 1, 1))
        );
        changes.IsEmpty.ShouldBeTrue();
        changes.Quests.IsEmpty.ShouldBeTrue();
        changes.Quests.GetChange("a").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DuplicateKeysAreInvalid()
    {
        var before = State(Quest("a"), Quest("a"));
        var after = State(Quest("a"));
        Should.Throw<InvalidOperationException>(() => Diff(before, after));
        TryDiff(before, after).ShouldBeNull();
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
        var changes = Diff(before, after);
        changes.Quests.Added.Select(q => q.Id).ShouldBe(["d"]);
        changes.Quests.Removed.Select(q => q.Id).ShouldBe(["a"]);
    }

    [Test]
    public void SharedChangeSetDrivesAllProjections()
    {
        var before = RosterDefaults.Before();
        var after = RosterDefaults.After();
        var changes = Diff(before, after);

        // Editor highlighting reads the typed transition directly.
        changes.Quests.GetChange("d").IsAdded.ShouldBeTrue();
        changes.Quests.GetChange("a").IsRemoved.ShouldBeTrue();
        changes.Quests.GetChange("b").IsEdited.ShouldBeTrue();
        changes.Quests.GetChange("c").IsReordered.ShouldBeTrue();

        // Summary and snippet reflect the same diff.
        var summary = PlaygroundSnippets.RosterSummary(changes);
        summary.ShouldContain("add:");
        summary.ShouldContain("remove:");
        summary.ShouldContain("edit:");
        var manual = PlaygroundSnippets.RosterManualPatchCSharp("patch", changes);
        manual.ShouldContain("Quests.Add");
        manual.ShouldContain("Quests.Remove");
        manual.ShouldContain("Quests.Edit");

        // ChangeSet JSON uses the same shared instance.
        var json = JsonSerializer.Serialize(changes, new JsonSerializerOptions { WriteIndented = true });
        json.ShouldContain("\"d\"");
        var roundTripped = JsonSerializer.Deserialize<PlaygroundRoster.ChangeSet>(json)!;
        roundTripped.Quests.GetChange("d").IsAdded.ShouldBeTrue();
        roundTripped.Quests.GetChange("a").IsRemoved.ShouldBeTrue();

        // Applied preview derives lazily from the same ChangeSet via ToPatch.
        var applied = changes.ToPatch().Apply(
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(before)));
        var appliedJson = PlaygroundJson.WriteRosterModel(applied.Value!.ToModel());
        var afterJson = PlaygroundJson.WriteRosterModel(after);
        appliedJson.ShouldBe(afterJson);
    }

    [Test]
    public void SummaryCorrectness()
    {
        var before = State(Quest("a"), Quest("b"));
        var after = State(Quest("a"), Quest("b", "B2"));
        var summary = PlaygroundSnippets.RosterSummary(Diff(before, after));
        summary.ShouldContain("order: [a→b]");
        summary.ShouldContain("edit: [b]");

        var empty = PlaygroundSnippets.RosterSummary(Diff(before, State(Quest("a"), Quest("b"))));
        empty.ShouldBe("no changes");
    }

    [Test]
    public void ManualPatchPreviewCorrectness()
    {
        var before = RosterDefaults.Before();
        var after = RosterDefaults.After();
        var changes = Diff(before, after);
        var manual = PlaygroundSnippets.RosterManualPatchCSharp("patch", changes);
        manual.ShouldContain("Quests.Add");
        manual.ShouldContain("Quests.Remove");
        manual.ShouldContain("Quests.Edit");
        manual.ShouldContain("SetOrder");

        var empty = PlaygroundSnippets.RosterManualPatchCSharp(
            "patch",
            Diff(before, RosterDefaults.Before()));
        empty.ShouldContain("No quest changes.");
    }

    [Test]
    public void RosterCodeUsesCollectionExpressions()
    {
        var roster = State(Quest("a", "A", 1, 30));
        var model = PlaygroundSnippets.RosterModelCSharp("roster", roster);
        model.ShouldContain("Quests =\n    [");
        model.ShouldContain("Scores = [30]");
        model.ShouldNotContain("new List<");

        var manual = PlaygroundSnippets.RosterManualPatchCSharp("patch", Diff(State(), roster));
        manual.ShouldContain("Scores = [30]");
        manual.ShouldContain("SetOrder([\"a\"])");
        manual.ShouldNotContain("new[]");
    }

    [Test]
    public void InvalidStateIsConsistentEverywhere()
    {
        var invalidBefore = State(Quest("a"), Quest("a"));
        var invalidAfter = State(Quest("a"));

        // Duplicate keys fail the single derivation, leaving a null ChangeSet
        // behind the page-local invalid state (same as Home.TryRefreshRosterChanges).
        Should.Throw<InvalidOperationException>(() => Diff(invalidBefore, invalidAfter));
        TryDiff(invalidBefore, invalidAfter).ShouldBeNull();
    }

    [Test]
    public void Case3DoesNotDependOnGenericInspection()
    {
        var playground = typeof(RosterDefaults).Assembly;
        playground.GetType("SparseFragments.Playground.Models.RosterInspection").ShouldBeNull();

        // Summary and snippet derive from the typed Quests transition only:
        // no before/after reconstruction parameter remains on the snippet API.
        typeof(PlaygroundSnippets)
            .GetMethod("RosterManualPatchCSharp")!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ShouldBe([typeof(string), typeof(PlaygroundRoster.ChangeSet)]);
        typeof(PlaygroundSnippets)
            .GetMethod("RosterSummary")!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ShouldBe([typeof(PlaygroundRoster.ChangeSet)]);

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
        var playground = typeof(RosterDefaults).Assembly;
        playground.GetType("SparseFragments.Playground.Models.QuestRow").ShouldBeNull();
        playground.GetType("SparseFragments.Playground.Models.RosterEditState").ShouldBeNull();
        RosterDefaults.Before().Quests.Count.ShouldBe(3);
        RosterDefaults.After().Quests.Count.ShouldBe(3);
    }

    [Test]
    public void Case3DoesNotReintroduceCopiedChangeGraph()
    {
        var playground = typeof(RosterDefaults).Assembly;
        playground.GetType("SparseFragments.Playground.Models.RosterCaseCoordinator").ShouldBeNull();
        playground.GetType("SparseFragments.Playground.Models.RosterHighlight").ShouldBeNull();
        playground.GetType("SparseFragments.Playground.Models.RosterRowHighlight").ShouldBeNull();

        var editor = playground.GetType("SparseFragments.Playground.Components.RosterEditor");
        editor.ShouldNotBeNull();
        var changesParam = editor!.GetProperty("Changes");
        changesParam.ShouldNotBeNull();
        changesParam!.PropertyType.ShouldBe(typeof(PlaygroundRoster.ChangeSet.QuestsTransition));

        // The typed item surface exposes GetChange; the editor must not keep a
        // per-row copied dictionary of presentation booleans.
        typeof(PlaygroundRoster.ChangeSet.QuestsTransition)
            .GetMethod("GetChange")!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ShouldBe([typeof(string)]);
        editor.GetProperty("Highlights").ShouldBeNull();
        foreach (var prop in editor.GetProperties())
        {
            prop.PropertyType.IsGenericType.ShouldBeFalse(
                $"RosterEditor.{prop.Name} must not reintroduce a copied change dictionary.");
        }
    }

    [Test]
    public void TypedReorderSemanticsHold()
    {
        // Pure membership shifts are not reorders: survivors stay empty even
        // though absolute indexes shift. Typed IsReordered drives highlighting.
        var pureAdd = Diff(State(Quest("a"), Quest("b")), State(Quest("a"), Quest("b"), Quest("c")));
        pureAdd.Quests.GetChange("a").IsReordered.ShouldBeFalse();
        pureAdd.Quests.GetChange("b").IsReordered.ShouldBeFalse();
        pureAdd.Quests.GetChange("c").IsAdded.ShouldBeTrue();

        var pureRemove = Diff(
            State(Quest("a"), Quest("b"), Quest("c")),
            State(Quest("b"), Quest("c"))
        );
        pureRemove.Quests.GetChange("b").IsReordered.ShouldBeFalse();
        pureRemove.Quests.GetChange("c").IsReordered.ShouldBeFalse();

        // Summary order is the typed AfterOrder, even when OrderChanged is false.
        var before = State(Quest("a"), Quest("b"));
        var after = State(Quest("a"), Quest("b", "B2"));
        PlaygroundSnippets.RosterSummary(Diff(before, after)).ShouldContain("order: [a→b]");
    }
}
