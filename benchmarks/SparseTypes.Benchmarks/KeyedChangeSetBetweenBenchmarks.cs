using BenchmarkDotNet.Attributes;
using SparseTypes;

public enum KeyedChangeSetBetweenShape
{
    NoOp,
    OneEdit,
    AllEdits,
    Reorder,
    RemoveFirst,
    AddLast,
    ReplaceAll,
    ReplaceLast,
}

[MemoryDiagnoser]
public class KeyedChangeSetBetweenBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public KeyedChangeSetBetweenShape Shape { get; set; }

    private Optional<BenchKeyedServerHolder.Fragment?> _before;
    private Optional<BenchKeyedServerHolder.Fragment?> _after;

    [GlobalSetup]
    public void Setup()
    {
        _before = State(false);
        _after = State(true);
        var changes = Between();
        if (
            changes.IsEmpty != (Shape == KeyedChangeSetBetweenShape.NoOp)
            || !BenchKeyedServerHolder
                .Patch.Between(changes.ToPatch().Apply(_before), _after)
                .IsEmpty
            || !BenchKeyedServerHolder
                .Patch.Between(changes.Invert().ToPatch().Apply(_after), _before)
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Keyed differences must preserve endpoints and invert correctly."
            );
    }

    private Optional<BenchKeyedServerHolder.Fragment?> State(bool after)
    {
        var items = Enumerable
            .Range(0, Size)
            .Select(index => new BenchKeyedServer
            {
                Id =
                    (
                        after && Shape == KeyedChangeSetBetweenShape.ReplaceAll
                            ? "replacement-"
                            : "server-"
                    ) + index,
                Name = "name-" + index,
                Count = index + (IsEdited(index, after) ? 1 : 0),
            })
            .ToList();
        if (after && Shape == KeyedChangeSetBetweenShape.Reorder)
            items.Reverse();
        if (after && Shape == KeyedChangeSetBetweenShape.RemoveFirst)
            items.RemoveAt(0);
        if (after && Shape == KeyedChangeSetBetweenShape.AddLast)
            items.Add(
                new BenchKeyedServer
                {
                    Id = "added",
                    Name = "added",
                    Count = Size,
                }
            );
        if (after && Shape == KeyedChangeSetBetweenShape.ReplaceLast)
            items[^1].Id = "replacement-last";
        return Optional<BenchKeyedServerHolder.Fragment?>.Present(
            BenchKeyedServerHolder.Fragment.From(new() { Items = items })
        );
    }

    private bool IsEdited(int index, bool after) =>
        after
        && (
            Shape == KeyedChangeSetBetweenShape.AllEdits
            || (Shape == KeyedChangeSetBetweenShape.OneEdit && index == 0)
        );

    [Benchmark]
    public BenchKeyedServerHolder.ChangeSet Between() =>
        BenchKeyedServerHolder.ChangeSet.Between(_before, _after);

    public static void ValidateInputs()
    {
        foreach (var size in new[] { 16, 2048 })
        foreach (var shape in Enum.GetValues<KeyedChangeSetBetweenShape>())
            new KeyedChangeSetBetweenBenchmarks { Size = size, Shape = shape }.Setup();
    }
}
