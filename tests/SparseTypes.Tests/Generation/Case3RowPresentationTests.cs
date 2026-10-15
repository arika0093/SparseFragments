extern alias Playground;

using System.Reflection;
using Playground::SparseTypes.Playground.Models;
using Playground::SparseTypes.Playground.Pages.Case3;

namespace SparseTypes.Tests;

/// <summary>Case 3 row rendering reads one typed lookup per row with parent-computed positions.</summary>
/// <remarks>Allocation loops carry timeouts so a regression fails loudly instead of hanging the run.</remarks>
public sealed class Case3RowPresentationTests
{
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

    private static PlaygroundRoster State(params PlaygroundQuest[] quests) =>
        new() { Quests = quests.ToList() };

    private static PlaygroundRoster.ChangeSet Diff(
        PlaygroundRoster before,
        PlaygroundRoster after
    ) =>
        PlaygroundRoster.ChangeSet.Between(
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(before)),
            Optional<PlaygroundRoster.Fragment?>.Present(PlaygroundRoster.Fragment.From(after))
        );

    private static void AssertRowParity(
        PlaygroundRoster.ChangeSet changes,
        IEnumerable<string> ids,
        bool removedOnly
    )
    {
        foreach (var id in ids)
        {
            var typed = changes.Quests.GetChange(id);
            var keyed = changes
                .Quests.KeyedState(PlaygroundRoster.SparsePath.Quests)
                .GetItemState(id);

            // The removed per-row KeyedState projection must agree with the
            // single typed lookup on every flag the row renders.
            typed.IsEmpty.ShouldBe(keyed.IsEmpty, $"IsEmpty for {id}");
            typed.IsAdded.ShouldBe(keyed.IsAdded, $"IsAdded for {id}");
            typed.IsRemoved.ShouldBe(keyed.IsRemoved, $"IsRemoved for {id}");
            typed.IsReordered.ShouldBe(keyed.IsReordered, $"IsReordered for {id}");

            var typedVisible = !typed.IsEmpty && (!removedOnly || typed.IsRemoved);
            var keyedVisible = !keyed.IsEmpty && (!removedOnly || keyed.IsRemoved);
            typedVisible.ShouldBe(keyedVisible, $"visibility for {id}");
        }
    }

    private static IEnumerable<string> UnionIds(PlaygroundRoster before, PlaygroundRoster after) =>
        before
            .Quests.Select(static quest => quest.Id)
            .Concat(after.Quests.Select(static quest => quest.Id))
            .Append("zzz")
            .Distinct(StringComparer.Ordinal);

    [Test, Timeout(30_000)]
    public void TypedLookupMatchesSharedProjection_NoChanges(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = State(Quest("a", "A", 1, 1), Quest("b", "B", 2, 2));
        var after = State(Quest("a", "A", 1, 1), Quest("b", "B", 2, 2));
        var changes = Diff(before, after);
        AssertRowParity(changes, UnionIds(before, after), removedOnly: false);
        AssertRowParity(changes, UnionIds(before, after), removedOnly: true);
    }

    [Test, Timeout(30_000)]
    public void TypedLookupMatchesSharedProjection_OneEdit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = State(Quest("a", "A", 1, 1), Quest("b", "B", 2, 2));
        var after = State(Quest("a", "A2", 1, 1), Quest("b", "B", 2, 2));
        var changes = Diff(before, after);
        AssertRowParity(changes, UnionIds(before, after), removedOnly: false);

        var edited = changes.Quests.GetChange("a");
        edited.IsEdited.ShouldBeTrue();
        edited.Edit.Title.IsChanged.ShouldBeTrue();
        edited.Edit.Points.IsChanged.ShouldBeFalse();
        edited.Edit.Scores.IsChanged.ShouldBeFalse();
        changes.Quests.GetChange("b").IsEmpty.ShouldBeTrue();
    }

    [Test, Timeout(30_000)]
    public void TypedLookupMatchesSharedProjection_AllChanged(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = State(Quest("a", "A", 1, 1), Quest("b", "B", 2, 2));
        var after = State(Quest("a", "A2", 9, 3), Quest("b", "B2", 8, 4));
        var changes = Diff(before, after);
        AssertRowParity(changes, UnionIds(before, after), removedOnly: false);
    }

    [Test, Timeout(30_000)]
    public void TypedLookupMatchesSharedProjection_AddRemoveReorderKeyEdit(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = RosterDefaults.Before();
        var after = RosterDefaults.After();
        // Key edit on top of the defaults: retitle plus identity change.
        after.Quests.Add(Quest("e", "Fifth", 7, 70));
        after.Quests.RemoveAll(static quest => quest.Id == "b");
        var changes = Diff(before, after);
        AssertRowParity(changes, UnionIds(before, after), removedOnly: false);
        AssertRowParity(changes, UnionIds(before, after), removedOnly: true);

        // Key edits stay remove-plus-add on both surfaces.
        changes.Quests.GetChange("b").IsRemoved.ShouldBeTrue();
        changes
            .Quests.KeyedState(PlaygroundRoster.SparsePath.Quests)
            .GetItemState("b")
            .IsRemoved.ShouldBeTrue();
    }

    [Test, Timeout(30_000)]
    public void RemovedOnlyKeepsOnlyRemovedRows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changes = Diff(RosterDefaults.Before(), RosterDefaults.After());
        var removed = changes.Quests.GetChange("a");
        removed.IsRemoved.ShouldBeTrue();
        var edited = changes.Quests.GetChange("b");
        edited.IsEdited.ShouldBeTrue();

        foreach (var removedOnly in new[] { false, true })
        {
            AssertRowParity(
                changes,
                UnionIds(RosterDefaults.Before(), RosterDefaults.After()),
                removedOnly
            );
        }
    }

    [Test, Timeout(30_000)]
    public void PositionMapTracksRenderOrder(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var quests = new List<PlaygroundQuest> { Quest("a"), Quest("b"), Quest("c") };
        var positions = RosterPositions.Build(quests);
        RosterPositions.GetPosition(positions, quests[0]).ShouldBe(0);
        RosterPositions.GetPosition(positions, quests[1]).ShouldBe(1);
        RosterPositions.GetPosition(positions, quests[2]).ShouldBe(2);
        RosterPositions.GetPosition(positions, Quest("zzz")).ShouldBe(-1);

        // First/last positions drive the move-button boundaries.
        RosterPositions.GetPosition(positions, quests[0]).ShouldBe(0);
        RosterPositions.GetPosition(positions, quests[^1]).ShouldBe(quests.Count - 1);
    }

    [Test, Timeout(30_000)]
    public void StalePositionNeverRetargetsCallbacks(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = Quest("a");
        var victim = Quest("b");
        var third = Quest("c");
        var quests = new List<PlaygroundQuest> { first, victim, third };
        var stale = RosterPositions.GetPosition(RosterPositions.Build(quests), victim);
        stale.ShouldBe(1);

        // Mirror RosterEditor.OnSortableUpdate: drag the head to the end.
        quests.RemoveAt(0);
        quests.Add(first);
        quests.Select(static quest => quest.Id).ShouldBe(["b", "c", "a"]);

        // Mirror QuestRow.RemoveAsync: resolve the live index by reference and
        // validate it instead of trusting the render-time position.
        var live = quests.IndexOf(victim);
        live.ShouldBe(0);
        live.ShouldNotBe(stale);
        ReferenceEquals(quests[live], victim).ShouldBeTrue();
        quests.RemoveAt(live);
        quests.Select(static quest => quest.Id).ShouldBe(["c", "a"]);

        // Mirror QuestRow.MoveAsync: the target derives from the live index.
        var mover = quests[0];
        var index = quests.IndexOf(mover);
        ReferenceEquals(quests[index], mover).ShouldBeTrue();
        var target = index + 1;
        (quests[target], quests[index]) = (quests[index], quests[target]);
        quests.Select(static quest => quest.Id).ShouldBe(["a", "c"]);
    }

    [Test, Timeout(30_000)]
    public void RowsUseParentPositionsNotPerRowProjections(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var playground = typeof(RosterDefaults).Assembly;
        var row = playground.GetType("SparseTypes.Playground.Pages.Case3.QuestRow");
        row.ShouldNotBeNull();

        // The render-time position arrives from the parent map.
        row!.GetProperty("Position").ShouldNotBeNull();
        // No per-row full-state projection remains on the row.
        row.GetProperty(
                "QuestStates",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
            )
            .ShouldBeNull();
        row.GetProperty(
                "Index",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
            )
            .ShouldBeNull();
    }

    private static long Measure(Action action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return (long)(GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (PlaygroundRoster.ChangeSet Changes, List<string> Ids) AllChangedState(int count)
    {
        var before = new PlaygroundRoster
        {
            Quests = Enumerable
                .Range(0, count)
                .Select(index => Quest($"q{index}", "Before", index))
                .ToList(),
        };
        var after = new PlaygroundRoster
        {
            Quests = Enumerable
                .Range(0, count)
                .Select(index => Quest($"q{index}", "After", index))
                .ToList(),
        };
        var ids = after.Quests.Select(static quest => quest.Id).ToList();
        return (Diff(before, after), ids);
    }

    [Test, Timeout(120_000)]
    public void TypedLookupPerRowAllocatesLessThanFullProjectionPerRow(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var count in new[] { 10, 100, 1000 })
        {
            var (changes, ids) = AllChangedState(count);
            var path = PlaygroundRoster.SparsePath.Quests;
            var consumed = 0;

            var projected = Measure(() =>
            {
                foreach (var id in ids)
                {
                    if (!changes.Quests.KeyedState(path).GetItemState(id).IsEmpty)
                    {
                        consumed++;
                    }
                }
            });
            consumed.ShouldBe(count * 2);
            consumed = 0;
            var typed = Measure(() =>
            {
                foreach (var id in ids)
                {
                    if (!changes.Quests.GetChange(id).IsEmpty)
                    {
                        consumed++;
                    }
                }
            });

            consumed.ShouldBe(count * 2);
            typed.ShouldBeLessThan(
                projected,
                $"per-row typed lookup must beat per-row full projection for {count} rows"
            );
        }
    }

    [Test, Timeout(120_000)]
    public void ProjectionSizesComplete_NoChangesAndOneEdit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var count in new[] { 10, 100, 1000 })
        {
            var before = new PlaygroundRoster
            {
                Quests = Enumerable
                    .Range(0, count)
                    .Select(index => Quest($"q{index}", "Same", index))
                    .ToList(),
            };
            var same = new PlaygroundRoster
            {
                Quests = Enumerable
                    .Range(0, count)
                    .Select(index => Quest($"q{index}", "Same", index))
                    .ToList(),
            };
            var edited = new PlaygroundRoster
            {
                Quests = Enumerable
                    .Range(0, count)
                    .Select(index => Quest($"q{index}", index == 0 ? "Edited" : "Same", index))
                    .ToList(),
            };
            var noChanges = Diff(before, same);
            var oneEdit = Diff(before, edited);
            var ids = before.Quests.Select(static quest => quest.Id).ToList();

            AssertRowParity(noChanges, ids, removedOnly: false);
            AssertRowParity(oneEdit, ids, removedOnly: false);

            // One shared parent projection per refresh, probed per row.
            var shared = noChanges.Quests.KeyedState(PlaygroundRoster.SparsePath.Quests);
            foreach (var id in ids)
            {
                shared.GetItemState(id).IsEmpty.ShouldBeTrue();
            }
        }
    }
}
