namespace SparseFragments.Tests;

public sealed class DescriptorDictValueStalenessTests
{
    private static ObservableHolder EntryModel() =>
        new()
        {
            ChildrenByName = new Dictionary<string, ObservableListChild>
            {
                ["entry"] = new ObservableListChild { Id = "first", Name = "first" },
                ["other"] = new ObservableListChild { Id = "second", Name = "second" },
            },
        };

    [Test]
    public void ReplaceValueStalesRetainedDescriptors()
    {
        var model = EntryModel();
        var first = model.ChildrenByName["entry"];
        var session = model.CreateEditSession();
        var notifications = 0;
        session.TransitionObserved += _ => notifications++;

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ChildrenByName), out var byName)
            .ShouldBeTrue();
        var dict = byName.Dictionary.ShouldNotBeNull();
        var retained = dict!.GetValueDescriptors("entry").ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();

        dict.TrySetValue("entry", new ObservableListChild { Id = "replaced", Name = "replaced" })
            .ShouldBeTrue();
        notifications.ShouldBe(1);

        name.TrySetValue("orphan").ShouldBeFalse();
        first.Name.ShouldBe("first");
        model.ChildrenByName["entry"].Name.ShouldBe("replaced");
        notifications.ShouldBe(1);

        var fresh = dict.GetValueDescriptors("entry").ShouldNotBeNull();
        fresh!.TryGet(nameof(ObservableListChild.Name), out var live).ShouldBeTrue();
        live.TrySetValue("live").ShouldBeTrue();
        model.ChildrenByName["entry"].Name.ShouldBe("live");
    }

    [Test]
    public void RemoveKeyStalesRetainedDescriptors()
    {
        var model = EntryModel();
        var first = model.ChildrenByName["entry"];
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ChildrenByName), out var byName)
            .ShouldBeTrue();
        var dict = byName.Dictionary.ShouldNotBeNull();
        var retained = dict!.GetValueDescriptors("entry").ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();

        dict.TryRemove("entry").ShouldBeTrue();

        dict.GetValueDescriptors("entry").ShouldBeNull();
        name.TrySetValue("orphan").ShouldBeFalse();
        first.Name.ShouldBe("first");
        model.ChildrenByName.ContainsKey("entry").ShouldBeFalse();
    }

    [Test]
    public void ReinsertedKeyDoesNotReviveOldDescriptors()
    {
        var model = EntryModel();
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ChildrenByName), out var byName)
            .ShouldBeTrue();
        var dict = byName.Dictionary.ShouldNotBeNull();
        var retained = dict!.GetValueDescriptors("entry").ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();

        dict.TryRemove("entry").ShouldBeTrue();
        dict.TryAdd("entry", new ObservableListChild { Id = "reinserted", Name = "reinserted" })
            .ShouldBeTrue();

        name.TrySetValue("orphan").ShouldBeFalse();
        model.ChildrenByName["entry"].Name.ShouldBe("reinserted");

        var fresh = dict.GetValueDescriptors("entry").ShouldNotBeNull();
        fresh!.TryGet(nameof(ObservableListChild.Name), out var live).ShouldBeTrue();
        live.TrySetValue("live").ShouldBeTrue();
        model.ChildrenByName["entry"].Name.ShouldBe("live");
    }

    [Test]
    public void IndependentKeyChangesPreserveOtherDescriptors()
    {
        var model = EntryModel();
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ChildrenByName), out var byName)
            .ShouldBeTrue();
        var dict = byName.Dictionary.ShouldNotBeNull();
        var retained = dict!.GetValueDescriptors("other").ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();

        dict.TrySetValue("entry", new ObservableListChild { Id = "changed", Name = "changed" })
            .ShouldBeTrue();

        name.TrySetValue("still live").ShouldBeTrue();
        model.ChildrenByName["other"].Name.ShouldBe("still live");
        session.HasChanges.ShouldBeTrue();
    }
}
