using SparseFragments.Generated;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class StaleItem
{
    public string Name { get; set; } = string.Empty;

    public List<string> Notes { get; set; } = [];
}

[SparseFragmentModel]
public partial class StaleGroup
{
    public StaleItem Item { get; set; } = new();
}

public sealed class DescriptorChildStalenessTests
{
    [Test]
    public void ReplacedChildStalesRetainedDescriptorsWithoutOrphanEdits()
    {
        var oldChild = new ObservableChild { Name = "old" };
        var model = new ObservableHolder { Child = oldChild };
        var session = model.CreateEditSession();
        var notifications = 0;
        session.TransitionObserved += _ => notifications++;

        session.Descriptors.TryGet(nameof(ObservableHolder.Child), out var child).ShouldBeTrue();
        var retained = child.Child.ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();

        child.TrySetValue(new ObservableChild { Name = "new" }).ShouldBeTrue();

        name.TrySetValue("orphan edit").ShouldBeFalse();
        oldChild.Name.ShouldBe("old");
        model.Child.Name.ShouldBe("new");
        notifications.ShouldBe(1);

        var fresh = child.Child.ShouldNotBeNull();
        fresh!.TryGet(nameof(ObservableChild.Name), out var live).ShouldBeTrue();
        live.TrySetValue("live edit").ShouldBeTrue();
        model.Child.Name.ShouldBe("live edit");
        notifications.ShouldBe(2);
    }

    [Test]
    public void NulledNullableChildStalesRetainedDescriptors()
    {
        var oldChild = new ObservableChild { Name = "old" };
        var model = new ObservableHolder { MaybeChild = oldChild };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.MaybeChild), out var child)
            .ShouldBeTrue();
        var retained = child.Child.ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();

        session.Observable.MaybeChild = null;

        child.Child.ShouldBeNull();
        name.TrySetValue("orphan edit").ShouldBeFalse();
        oldChild.Name.ShouldBe("old");

        child.TrySetValue(new ObservableChild { Name = "replacement" }).ShouldBeTrue();
        // Still bound to the first instance, not the replacement.
        name.TrySetValue("orphan edit").ShouldBeFalse();
        model.MaybeChild!.Name.ShouldBe("replacement");

        var fresh = child.Child.ShouldNotBeNull();
        fresh!.TryGet(nameof(ObservableChild.Name), out var live).ShouldBeTrue();
        live.TrySetValue("live edit").ShouldBeTrue();
        model.MaybeChild.Name.ShouldBe("live edit");
    }

    [Test]
    public void StaleNestedCollectionDescriptorsFailSafely()
    {
        var oldItem = new StaleItem { Notes = ["a"] };
        var model = new StaleGroup { Item = oldItem };
        var session = model.CreateEditSession();
        var notifications = 0;
        session.TransitionObserved += _ => notifications++;

        session.Descriptors.TryGet(nameof(StaleGroup.Item), out var item).ShouldBeTrue();
        var retained = item.Child.ShouldNotBeNull();
        retained!.TryGet(nameof(StaleItem.Notes), out var notes).ShouldBeTrue();
        var liveArray = notes.Array.ShouldNotBeNull();

        item.TrySetValue(new StaleItem()).ShouldBeTrue();

        notes.Array.ShouldBeNull();
        liveArray!.TryAdd("orphan").ShouldBeFalse();
        liveArray.CanAdd.ShouldBeFalse();
        oldItem.Notes.ShouldBe(["a"]);
        model.Item.Notes.ShouldBeEmpty();
        notifications.ShouldBe(1);

        var fresh = item.Child.ShouldNotBeNull();
        fresh!.TryGet(nameof(StaleItem.Notes), out var liveNotes).ShouldBeTrue();
        liveNotes.Array.ShouldNotBeNull();
        liveNotes.Array!.TryAdd("live").ShouldBeTrue();
        model.Item.Notes.ShouldBe(["live"]);
    }

    [Test]
    public void RevertChangesStalesDescriptorsBoundToReplacedInstances()
    {
        var oldChild = new ObservableChild { Name = "old" };
        var model = new ObservableHolder { Child = oldChild };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(ObservableHolder.Child), out var child).ShouldBeTrue();
        var retained = child.Child.ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableChild.Name), out var value).ShouldBeTrue();

        session.Observable.Child!.Name = "edited";
        session.RevertChanges();

        model.Child.Name.ShouldBe("old");
        session.HasChanges.ShouldBeFalse();
        // Revert rebuilds the child instance, so the retained set is stale.
        value.TrySetValue("after revert").ShouldBeFalse();
        model.Child.Name.ShouldBe("old");

        var fresh = child.Child.ShouldNotBeNull();
        fresh!.TryGet(nameof(ObservableChild.Name), out var live).ShouldBeTrue();
        live.TrySetValue("after revert").ShouldBeTrue();
        model.Child.Name.ShouldBe("after revert");
        session.HasChanges.ShouldBeTrue();
    }
}
