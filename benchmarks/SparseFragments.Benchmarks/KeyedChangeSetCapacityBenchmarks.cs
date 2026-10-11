using System.Collections;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchEnumerableKeyedHolder
{
    public IEnumerable<BenchKeyedServer> Items { get; set; } = [];
}

public enum KeyedBetweenStorage
{
    List,
    ReadOnlyCollection,
    Enumerable,
}

[MemoryDiagnoser]
public class KeyedChangeSetCapacityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [ParamsAllValues]
    public KeyedBetweenStorage Storage { get; set; }

    private Optional<BenchEnumerableKeyedHolder.Fragment?> _before;
    private Optional<BenchEnumerableKeyedHolder.Fragment?> _after;

    [GlobalSetup]
    public void Setup()
    {
        _before = State(false);
        _after = State(true);
        var changes = Between();
        if (
            changes.IsEmpty
            || !BenchEnumerableKeyedHolder
                .Patch.Between(changes.ToPatch().Apply(_before), _after)
                .IsEmpty
            || !BenchEnumerableKeyedHolder
                .Patch.Between(changes.Invert().ToPatch().Apply(_after), _before)
                .IsEmpty
        )
            throw new InvalidOperationException("Keyed storage must preserve both endpoints.");
    }

    private Optional<BenchEnumerableKeyedHolder.Fragment?> State(bool after)
    {
        var items = Enumerable
            .Range(0, Size)
            .Select(index => new BenchKeyedServer
            {
                Id = "key-" + index,
                Name = "value-" + index,
                Count = index + (after && index == 0 ? 1 : 0),
            })
            .ToList();
        IEnumerable<BenchKeyedServer> source = Storage switch
        {
            KeyedBetweenStorage.List => items,
            KeyedBetweenStorage.ReadOnlyCollection => new ReadOnlyItems<BenchKeyedServer>(items),
            _ => items.Where(static _ => true),
        };
        return Optional<BenchEnumerableKeyedHolder.Fragment?>.Present(
            new BenchEnumerableKeyedHolder.Fragment
            {
                Items = Optional<IEnumerable<BenchKeyedServer>>.Present(source),
            }
        );
    }

    [Benchmark]
    public BenchEnumerableKeyedHolder.ChangeSet Between() =>
        BenchEnumerableKeyedHolder.ChangeSet.Between(_before, _after);

    private sealed class ReadOnlyItems<T>(IReadOnlyList<T> items) : IReadOnlyCollection<T>
    {
        public int Count => items.Count;

        public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

[MemoryDiagnoser]
public class TemporaryKeyChangeSetCapacityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Assigned { get; set; }

    private Optional<BenchTemporaryPayloadHolder.Fragment?> _before;
    private Optional<BenchTemporaryPayloadHolder.Fragment?> _after;

    [GlobalSetup]
    public void Setup()
    {
        var before = new BenchTemporaryPayloadHolder
        {
            Items = Enumerable
                .Range(1, Size)
                .Select(index => new BenchTemporaryPayloadItem
                {
                    Id = Assigned ? index : 0,
                    TemporaryId = new Guid(index, 0, 0, new byte[8]),
                    Name = "value-" + index,
                })
                .ToList(),
        };
        var after = before.DeepClone();
        after.Items[0].Name = "edited";
        _before = Optional<BenchTemporaryPayloadHolder.Fragment?>.Present(
            BenchTemporaryPayloadHolder.Fragment.From(before)
        );
        _after = Optional<BenchTemporaryPayloadHolder.Fragment?>.Present(
            BenchTemporaryPayloadHolder.Fragment.From(after)
        );
        var changes = Between();
        if (
            changes.IsEmpty
            || !BenchTemporaryPayloadHolder
                .Patch.Between(changes.ToPatch().Apply(_before), _after)
                .IsEmpty
            || !BenchTemporaryPayloadHolder
                .Patch.Between(changes.Invert().ToPatch().Apply(_after), _before)
                .IsEmpty
        )
            throw new InvalidOperationException(
                "Temporary identities must preserve both endpoints."
            );
    }

    [Benchmark]
    public BenchTemporaryPayloadHolder.ChangeSet Between() =>
        BenchTemporaryPayloadHolder.ChangeSet.Between(_before, _after);
}
