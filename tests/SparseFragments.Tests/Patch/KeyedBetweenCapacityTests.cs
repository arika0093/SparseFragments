using System.Collections;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class EnumerableKeyedCapacityModel
{
    public IEnumerable<KeyedServer> Items { get; set; } = [];
}

public sealed class KeyedBetweenCapacityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void CountHintsDoNotConsumeASinglePassInput(bool counted)
    {
        var beforeSource = new SinglePassItems([
            new KeyedServer { Id = "key", Count = 1 },
            new KeyedServer { Id = "other" },
        ]);
        var afterSource = new SinglePassItems([
            new KeyedServer { Id = "key", Count = 2 },
            new KeyedServer { Id = "other" },
        ]);
        var before = State(counted ? new CountedItems(beforeSource) : beforeSource);
        var after = State(counted ? new CountedItems(afterSource) : afterSource);

        var changes = EnumerableKeyedCapacityModel.ChangeSet.Between(before, after);

        beforeSource.Enumerations.ShouldBe(1);
        afterSource.Enumerations.ShouldBe(1);
        changes.Items.BeforeOrder.ShouldBe(["key", "other"]);
        changes.Items.AfterOrder.ShouldBe(["key", "other"]);
        changes.Items.Edited.Count.ShouldBe(1);
        var edit = changes.Items.GetChange("key").Edit.Count;
        edit.Before.Value.ShouldBe(1);
        edit.After.Value.ShouldBe(2);
    }

    private static Optional<EnumerableKeyedCapacityModel.Fragment?> State(
        IEnumerable<KeyedServer> items
    ) =>
        Optional<EnumerableKeyedCapacityModel.Fragment?>.Present(
            new EnumerableKeyedCapacityModel.Fragment
            {
                Items = Optional<IEnumerable<KeyedServer>>.Present(items),
            }
        );

    private sealed class CountedItems(SinglePassItems source) : IReadOnlyCollection<KeyedServer>
    {
        public int Count => source.Count;

        public IEnumerator<KeyedServer> GetEnumerator() => source.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SinglePassItems(KeyedServer[] items) : IEnumerable<KeyedServer>
    {
        public int Count => items.Length;
        public int Enumerations { get; private set; }

        public IEnumerator<KeyedServer> GetEnumerator()
        {
            Enumerations++;
            if (Enumerations != 1)
                throw new InvalidOperationException("Input was enumerated more than once.");
            return ((IEnumerable<KeyedServer>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
