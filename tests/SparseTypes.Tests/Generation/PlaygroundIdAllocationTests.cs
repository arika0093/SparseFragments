extern alias Playground;

using Playground::SparseTypes.Playground.Models;

namespace SparseTypes.Tests;

/// <summary>Deterministic ID allocation shared by the Case 3 and Case 4 editors.</summary>
/// <remarks>Every test carries a timeout so a non-terminating allocator fails loudly instead of hanging the run. The allocator itself has no retry loop: it scans candidates against the reserved set.</remarks>
public sealed class PlaygroundIdAllocationTests
{
    private static IEnumerable<string> Prefixed(string prefix, int from, int to)
    {
        for (var candidate = from; candidate <= to; candidate++)
        {
            yield return $"{prefix}{candidate}";
        }
    }

    [Test, Timeout(10_000)]
    public void EmptyReservedStartsAtOne(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaygroundIds.Allocate("q", []).ShouldBe("q1");
        PlaygroundIds.Allocate("t", []).ShouldBe("t1");
    }

    [Test, Timeout(10_000)]
    public void SparseAndNonNumericIdsKeepFirstFreeCandidate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Non-numeric and differently-cased entries never block numeric
        // candidates; comparison is ordinal.
        var reserved = new[] { "q2", "other", "Q1", "q01", "q3" };
        PlaygroundIds.Allocate("q", reserved).ShouldBe("q1");
        PlaygroundIds.Allocate("q", reserved.Append("q1")).ShouldBe("q4");
    }

    [Test, Timeout(10_000)]
    public void NearlyFullNamespaceAllocatesLastFreeCandidate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlaygroundIds.Allocate("q", Prefixed("q", 1, 98)).ShouldBe("q99");
    }

    [Test, Timeout(10_000)]
    public void FullSeededNamespaceGrowsPastTheOldLimit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // q1..q99 seeded: the old random retry loop never returned. The
        // helper removes the artificial 99/999 bounds and returns q100.
        PlaygroundIds.Allocate("q", Prefixed("q", 1, 99)).ShouldBe("q100");
        PlaygroundIds.Allocate("t", Prefixed("t", 1, 999)).ShouldBe("t1000");
    }

    [Test, Timeout(10_000)]
    public void DeletedButBaselineReservedIdsAreSkipped(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // t3 was removed from the draft but stays reserved via the submitted
        // baseline, so reuse cannot read as an edit of the removed identity.
        var current = new[] { "t1", "t2" };
        var baseline = new[] { "t1", "t2", "t3" };
        PlaygroundIds.Allocate("t", current.Concat(baseline)).ShouldBe("t4");
    }

    [Test, Timeout(10_000)]
    public void RepeatedAllocationsStayUnique(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "q1", "q3" };
        for (var count = 0; count < 10; count++)
        {
            reserved.Add(PlaygroundIds.Allocate("q", reserved)).ShouldBeTrue();
        }

        reserved.Count.ShouldBe(12);
    }

    [Test, Timeout(10_000)]
    public void InvalidInputsThrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Should.Throw<ArgumentException>(() => PlaygroundIds.Allocate(string.Empty, []));
        Should.Throw<ArgumentNullException>(() => PlaygroundIds.Allocate("q", null!));
    }

    [Test, Timeout(10_000)]
    public void RosterAddPatternTerminatesOnFullNamespace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Mirrors RosterEditor.AddAsync without the renderer.
        var roster = new PlaygroundRoster
        {
            Quests = Prefixed("q", 1, 99).Select(id => new PlaygroundQuest { Id = id }).ToList(),
        };
        var id = PlaygroundIds.Allocate("q", roster.Quests.Select(static quest => quest.Id));
        id.ShouldBe("q100");

        roster.Quests.Add(new PlaygroundQuest { Id = id });
        roster.Quests.Select(static quest => quest.Id).Distinct().Count().ShouldBe(100);
    }

    [Test, Timeout(10_000)]
    public void TaskAddPatternReservesBaselineIds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Mirrors TaskListEditor.AddTask without the renderer.
        var baselineIds = new[] { "t1", "t2", "t3" };
        var model = new PlaygroundSettings
        {
            Tasks =
            [
                new PlaygroundTask
                {
                    Id = "t1",
                    Title = "One",
                    Points = 10,
                },
                new PlaygroundTask
                {
                    Id = "t2",
                    Title = "Two",
                    Points = 20,
                },
                new PlaygroundTask
                {
                    Id = "t3",
                    Title = "Three",
                    Points = 30,
                },
            ],
        };
        var session = model.CreateEditSession();
        session
            .EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)
            .TryRemoveByKey("t3", out _)
            .ShouldBeTrue();

        var id = PlaygroundIds.Allocate(
            "t",
            session.Observable.Tasks.Select(static task => task.Id).Concat(baselineIds)
        );
        id.ShouldBe("t4");

        session
            .EditCollection<PlaygroundTask, string>(PlaygroundSettings.SparsePath.Tasks)
            .TryAdd(new PlaygroundTask { Id = id, Title = "Four" }, out _)
            .ShouldBeTrue();
        session.CreateChangeSet().Tasks.Added.Select(static task => task.Id).ShouldBe(["t4"]);
    }

    [Test, Timeout(10_000)]
    public void TaskAddPatternReusesIdsFreedBySubmission(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // After the baseline advances past a removal, the ID is free again.
        var model = new PlaygroundSettings
        {
            Tasks =
            [
                new PlaygroundTask
                {
                    Id = "t1",
                    Title = "One",
                    Points = 10,
                },
                new PlaygroundTask
                {
                    Id = "t2",
                    Title = "Two",
                    Points = 20,
                },
            ],
        };
        var session = model.CreateEditSession();
        var editor = session.EditCollection<PlaygroundTask, string>(
            PlaygroundSettings.SparsePath.Tasks
        );
        editor.TryRemoveByKey("t2", out _).ShouldBeTrue();
        session.AcceptChanges();

        var baselineIds = model.Tasks.Select(static task => task.Id).ToList();
        var id = PlaygroundIds.Allocate(
            "t",
            session.Observable.Tasks.Select(static task => task.Id).Concat(baselineIds)
        );
        id.ShouldBe("t2");
    }
}
