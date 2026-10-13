using System.Text.Json;

namespace SparseTypes.Tests;

public sealed class KeyedAlignedBetweenTests
{
    [Test]
    public void EmptyIndexedCollectionsHaveNoChanges()
    {
        var change = KeyedServerHolder.ChangeSet.Between(
            new KeyedServerHolder(),
            new KeyedServerHolder()
        );
        change.IsEmpty.ShouldBeTrue();
        change.Items.BeforeOrder.ShouldBeEmpty();
        change.Items.AfterOrder.ShouldBeEmpty();
    }

    [Test]
    public void NullKeysDoNotBecomeValidSetEntries()
    {
        var before = new KeyedServerHolder { Items = [new() { Id = null! }] };
        var after = before.DeepClone();
        Should
            .Throw<ArgumentNullException>(() => KeyedServerHolder.ChangeSet.Between(before, after))
            .ParamName.ShouldBe("key");
    }

    [Test]
    public void AlignedEditsPreserveAbsoluteOrdersIndicesAndNestedTransitions()
    {
        var before = new KeyedServerHolder
        {
            Items =
            [
                new() { Id = "a", Name = "A" },
                new() { Id = "b", Name = "B" },
                new() { Id = "c", Name = "C" },
            ],
        };
        var after = before.DeepClone();
        after.Items[1].Name = "updated";
        var change = KeyedServerHolder.ChangeSet.Between(before, after);
        var view = change.Items;
        view.BeforeOrder.ShouldBe(["a", "b", "c"]);
        view.AfterOrder.ShouldBe(["a", "b", "c"]);
        view.OrderChanged.ShouldBeFalse();
        var item = view.Single();
        item.Key.ShouldBe("b");
        item.BeforeIndex.ShouldBe(1);
        item.AfterIndex.ShouldBe(1);
        item.IsEdited.ShouldBeTrue();
        item.IsAdded.ShouldBeFalse();
        item.IsRemoved.ShouldBeFalse();
        item.IsReordered.ShouldBeFalse();
        item.Before.IsPresent.ShouldBeFalse();
        item.After.IsPresent.ShouldBeFalse();
        item.Edit.Name.Before.Value.ShouldBe("B");
        item.Edit.Name.After.Value.ShouldBe("updated");
        var restored = JsonSerializer
            .Deserialize<KeyedServerHolder.ChangePayload>(
                JsonSerializer.Serialize(change.ToPayload())
            )!
            .ToChangeSet();
        restored.Items.BeforeOrder.ShouldBe(view.BeforeOrder);
        restored.Items.AfterOrder.ShouldBe(view.AfterOrder);
        restored.ToPatch().ApplyTo(before).Items[1].Name.ShouldBe("updated");
        restored.Invert().ToPatch().ApplyTo(after).Items[1].Name.ShouldBe("B");
        var final = after.DeepClone();
        final.Items[2].Name = "final";
        var next = KeyedServerHolder.ChangeSet.Between(after, final);
        var combined = change.Compose(next).ToPatch().ApplyTo(before);
        combined.Items.Select(static value => value.Name).ShouldBe(["A", "updated", "final"]);
        view.BeforeOrder.ShouldBe(["a", "b", "c"]);
        view.AfterOrder.ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void AlignedDuplicateKeysStillFailValidation()
    {
        var before = new KeyedServerHolder
        {
            Items = [new() { Id = "duplicate" }, new() { Id = "duplicate" }],
        };
        var after = before.DeepClone();
        Should
            .Throw<InvalidOperationException>(() =>
                KeyedServerHolder.ChangeSet.Between(before, after)
            )
            .Message.ShouldBe("Duplicate key in keyed collection.");
    }
}
