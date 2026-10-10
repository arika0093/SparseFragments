using BenchmarkDotNet.Attributes;
using SparseFragments;

public enum KeyedRebaseOrderScenario
{
    Mixed,
    ConflictingEdits,
    ReorderOnly,
    OrderConflict,
}

/// <summary>Exercises keys from all three maps and order-only patches with no touched elements.</summary>
[MemoryDiagnoser]
public class KeyedRebaseOrderBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(
        KeyedRebaseOrderScenario.Mixed,
        KeyedRebaseOrderScenario.ConflictingEdits,
        KeyedRebaseOrderScenario.ReorderOnly,
        KeyedRebaseOrderScenario.OrderConflict
    )]
    public KeyedRebaseOrderScenario Scenario { get; set; }

    private Optional<BenchValueKeyedHolder.Fragment?> _before;
    private Optional<BenchValueKeyedHolder.Fragment?> _current;
    private BenchValueKeyedHolder.Patch _local = null!;

    [GlobalSetup]
    public void Setup()
    {
        var before = Enumerable
            .Range(0, Size)
            .Select(index => new BenchValueKeyedItem { Id = index, Count = index })
            .ToList();
        var current = before.ToList();
        List<BenchValueKeyedItem> desired;
        var mixed =
            Scenario is KeyedRebaseOrderScenario.Mixed or KeyedRebaseOrderScenario.ConflictingEdits;
        var conflictingEdits = Scenario == KeyedRebaseOrderScenario.ConflictingEdits;
        if (mixed)
        {
            desired = before.Skip(1).ToList();
            desired[0] = desired[0] with { Count = -1 };
            desired.Add(new BenchValueKeyedItem { Id = Size, Count = -4 });
            desired.Add(new BenchValueKeyedItem { Id = Size + 1, Count = -5 });
            current.Add(new BenchValueKeyedItem { Id = Size + 1, Count = -6 });
            current.Add(new BenchValueKeyedItem { Id = Size + 2, Count = 100 });
            if (conflictingEdits)
            {
                current[0] = current[0] with { Count = 42 };
                current[1] = current[1] with { Count = 99 };
            }
        }
        else
        {
            desired = before.AsEnumerable().Reverse().ToList();
            if (Scenario == KeyedRebaseOrderScenario.OrderConflict)
            {
                current = before.Skip(1).Append(before[0]).ToList();
            }
        }

        _before = State(before);
        _current = State(current);
        _local = BenchValueKeyedHolder.Patch.Between(_before, State(desired));
        var result = Rebase();
        var actual = result.Rebased.Apply(_current).Value!.Items.Value!;
        var expected = mixed
            ? current
                .Where(item => conflictingEdits || item.Id != 0)
                .Select(item => !conflictingEdits && item.Id == 1 ? item with { Count = -1 } : item)
                .Append(new BenchValueKeyedItem { Id = Size, Count = -4 })
                .ToList()
            : (Scenario == KeyedRebaseOrderScenario.ReorderOnly ? desired : current);
        string[] expectedConflicts = Scenario switch
        {
            KeyedRebaseOrderScenario.Mixed => [(Size + 1).ToString(), "order"],
            KeyedRebaseOrderScenario.ConflictingEdits => ["0", "1", (Size + 1).ToString(), "order"],
            KeyedRebaseOrderScenario.OrderConflict => ["order"],
            _ => [],
        };
        if (actual.Count != expected.Count || result.Conflicts.Count != expectedConflicts.Length)
        {
            throw new InvalidOperationException(
                "Keyed Rebase must retain mixed edits and order conflicts."
            );
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (
                actual[index].Id != expected[index].Id
                || actual[index].Count != expected[index].Count
            )
            {
                throw new InvalidOperationException(
                    "Keyed Rebase must preserve the result's values and order."
                );
            }
        }
        for (var index = 0; index < expectedConflicts.Length; index++)
        {
            var path = result.Conflicts[index].Path;
            if (
                path.Depth < 2
                || path.Segments[0].Name != "Items"
                || !Equals(path.Segments[1].Key, expectedConflicts[index])
            )
            {
                throw new InvalidOperationException(
                    "Keyed Rebase must preserve conflict order and key paths."
                );
            }
        }
        if (
            !_before.Value!.Items.Value!.SequenceEqual(before)
            || !_current.Value!.Items.Value!.SequenceEqual(current)
        )
        {
            throw new InvalidOperationException(
                "Keyed Rebase must keep its input states unchanged."
            );
        }
    }

    private static Optional<BenchValueKeyedHolder.Fragment?> State(
        List<BenchValueKeyedItem> items
    ) =>
        Optional<BenchValueKeyedHolder.Fragment?>.Present(
            new BenchValueKeyedHolder.Fragment
            {
                Items = Optional<List<BenchValueKeyedItem>>.Present(items),
            }
        );

    [Benchmark]
    public RebaseResult<BenchValueKeyedHolder.Patch> Rebase() =>
        BenchValueKeyedHolder.Patch.Rebase(_before, _local, _current);
}
