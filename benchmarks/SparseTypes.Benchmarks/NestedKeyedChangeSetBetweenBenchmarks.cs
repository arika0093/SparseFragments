using System.Text.Json;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[MemoryDiagnoser]
public class NestedKeyedChangeSetBetweenBenchmarks
{
    [Params(16, 128)]
    public int GroupCount { get; set; }

    [Params(false, true)]
    public bool Edited { get; set; }

    private Optional<BenchKeyedGroupHolder.Fragment?> _before;
    private Optional<BenchKeyedGroupHolder.Fragment?> _after;

    [GlobalSetup]
    public void Setup()
    {
        var before = new BenchKeyedGroupHolder
        {
            Groups = Enumerable
                .Range(0, GroupCount)
                .Select(group => new BenchKeyedGroup
                {
                    Name = "group-" + group,
                    Members = Enumerable
                        .Range(0, 16)
                        .Select(item => new BenchKeyedServer
                        {
                            Id = "server-" + item,
                            Name = "name-" + item,
                            Count = item,
                        })
                        .ToList(),
                })
                .ToList(),
        };
        var after = before.DeepClone();
        after.Groups.Reverse();
        if (Edited)
            foreach (var group in after.Groups)
                group.Members[0].Count++;
        _before = Optional<BenchKeyedGroupHolder.Fragment?>.Present(
            BenchKeyedGroupHolder.Fragment.From(before)
        );
        _after = Optional<BenchKeyedGroupHolder.Fragment?>.Present(
            BenchKeyedGroupHolder.Fragment.From(after)
        );
        var changes = Between();
        if (
            changes.IsEmpty
            || JsonSerializer.Serialize(changes.ToPatch().Apply(_before).Value!.ToModel())
                != JsonSerializer.Serialize(after)
            || JsonSerializer.Serialize(changes.Invert().ToPatch().Apply(_after).Value!.ToModel())
                != JsonSerializer.Serialize(before)
        )
            throw new InvalidOperationException("Nested changes must preserve both endpoints.");
    }

    [Benchmark]
    public BenchKeyedGroupHolder.ChangeSet Between() =>
        BenchKeyedGroupHolder.ChangeSet.Between(_before, _after);
}
