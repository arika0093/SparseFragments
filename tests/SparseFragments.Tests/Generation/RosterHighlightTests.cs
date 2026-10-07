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

    [Test]
    public void DefaultsShowAddRemoveEditAndMove()
    {
        var (before, after) = RosterHighlight.Build(
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
        var (_, after) = RosterHighlight.Build(State(Row("a")), State(Row("a"), Row("b", "B")));
        after.Single(kv => kv.Key.Id == "b").Value.IsAdded.ShouldBeTrue();
        after.Single(kv => kv.Key.Id == "a").Value.IsAdded.ShouldBeFalse();
    }

    [Test]
    public void RemoveOnly()
    {
        var (before, _) = RosterHighlight.Build(State(Row("a"), Row("b")), State(Row("a")));
        before.Single(kv => kv.Key.Id == "b").Value.IsRemoved.ShouldBeTrue();
        before.Single(kv => kv.Key.Id == "a").Value.IsRemoved.ShouldBeFalse();
    }

    [Test]
    public void ScalarMemberEdit()
    {
        var (_, after) = RosterHighlight.Build(
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
        var (_, after) = RosterHighlight.Build(
            State(Row("a", scores: "10, 20")),
            State(Row("a", scores: "10, 21"))
        );
        after.Single().Value.ScoresChanged.ShouldBeTrue();
    }

    [Test]
    public void FormattingOnlyScoresAreNotAnEdit()
    {
        var (_, after) = RosterHighlight.Build(
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
        var (_, after) = RosterHighlight.Build(
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
        var (_, after) = RosterHighlight.Build(
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
        var (_, after) = RosterHighlight.Build(
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
        var (before, after) = RosterHighlight.Build(State(Row("a", "A")), State(Row("b", "A")));
        before.Single().Value.IsRemoved.ShouldBeTrue();
        after.Single().Value.IsAdded.ShouldBeTrue();
    }

    [Test]
    public void EditThenRestoreHasNoChanges()
    {
        var (before, after) = RosterHighlight.Build(
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
        var (before, after) = RosterHighlight.Build(State(Row("a"), Row("a")), State(Row("a")));
        before.ShouldBeEmpty();
        after.ShouldBeEmpty();
    }
}
