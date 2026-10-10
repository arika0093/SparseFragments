namespace SparseFragments.Tests;

using SparseFragments.Generated;

public sealed class DescriptorIndexedStalenessTests
{
    private static ObservableHolder TwoItemModel() =>
        new()
        {
            Children =
            [
                new ObservableListChild { Id = "first", Name = "first" },
                new ObservableListChild { Id = "second", Name = "second" },
            ],
        };

    [Test]
    public void MoveStalesRetainedDescriptorsAtBothPositions()
    {
        var model = TwoItemModel();
        var first = model.Children[0];
        var second = model.Children[1];
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var children)
            .ShouldBeTrue();
        var array = children.Array.ShouldNotBeNull();
        var retained0 = array!.GetItemDescriptors(0).ShouldNotBeNull();
        retained0!.TryGet(nameof(ObservableListChild.Name), out var name0).ShouldBeTrue();
        name0.PathText.ShouldBe("Children[0].Name");

        session.Observable.Children!.Move(0, 1);

        // Index 0 now denotes the other item: the retained write must fail.
        name0.TrySetValue("wrong row").ShouldBeFalse();
        first.Name.ShouldBe("first");
        second.Name.ShouldBe("second");

        var fresh0 = array.GetItemDescriptors(0).ShouldNotBeNull();
        fresh0!.TryGet(nameof(ObservableListChild.Name), out var freshName0).ShouldBeTrue();
        freshName0.PathText.ShouldBe("Children[0].Name");
        freshName0.TrySetValue("moved").ShouldBeTrue();
        second.Name.ShouldBe("moved");
    }

    [Test]
    public void InsertStalesDescriptorsAtShiftedPositions()
    {
        var model = TwoItemModel();
        var first = model.Children[0];
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var children)
            .ShouldBeTrue();
        var array = children.Array.ShouldNotBeNull();
        var retained = array!.GetItemDescriptors(0).ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();

        array
            .TryInsert(0, new ObservableListChild { Id = "inserted", Name = "inserted" })
            .ShouldBeTrue();

        name.TrySetValue("wrong row").ShouldBeFalse();
        first.Name.ShouldBe("first");
        model.Children[1].ShouldBe(first);
    }

    [Test]
    public void RemoveStalesDescriptorsAndReplacementStalesThem()
    {
        var model = TwoItemModel();
        var first = model.Children[0];
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var children)
            .ShouldBeTrue();
        var array = children.Array.ShouldNotBeNull();
        var retained = array!.GetItemDescriptors(0).ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();

        array.TryRemoveAt(0).ShouldBeTrue();

        name.TrySetValue("orphan").ShouldBeFalse();
        first.Name.ShouldBe("first");

        var live = array.GetItemDescriptors(0).ShouldNotBeNull();
        live!.TryGet(nameof(ObservableListChild.Name), out var liveName).ShouldBeTrue();
        liveName.TrySetValue("replacement target").ShouldBeTrue();

        array
            .TrySetItem(0, new ObservableListChild { Id = "replaced", Name = "replaced" })
            .ShouldBeTrue();
        liveName.TrySetValue("orphan").ShouldBeFalse();
    }

    [Test]
    public void DuplicateReferencesStayLiveForEitherPosition()
    {
        var shared = new ObservableListChild { Id = "same", Name = "shared" };
        var other = new ObservableListChild { Id = "other", Name = "other" };
        var model = new ObservableHolder { Children = [shared, other] };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var children)
            .ShouldBeTrue();
        var array = children.Array.ShouldNotBeNull();
        var retained0 = array!.GetItemDescriptors(0).ShouldNotBeNull();
        retained0!.TryGet(nameof(ObservableListChild.Name), out var name0).ShouldBeTrue();

        // Both positions resolve to distinct instances after the move.
        session.Observable.Children!.Move(0, 1);

        name0.TrySetValue("wrong row").ShouldBeFalse();
        shared.Name.ShouldBe("shared");

        var sameTwice = new ObservableHolder { Children = [shared, shared] };
        var sameSession = sameTwice.CreateEditSession();
        sameSession
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var sameChildren)
            .ShouldBeTrue();
        var sameArray = sameChildren.Array.ShouldNotBeNull();
        var same0 = sameArray!.GetItemDescriptors(0).ShouldNotBeNull();
        same0!.TryGet(nameof(ObservableListChild.Name), out var sameName0).ShouldBeTrue();

        sameArray.TryMove(0, 1).ShouldBeTrue();
        // Both positions still denote the same instance: no wrong-row edit exists.
        sameName0.TrySetValue("edited").ShouldBeTrue();
        shared.Name.ShouldBe("edited");
    }

    [Test]
    public void FreshDescriptorsAlwaysIdentifyTheirItem()
    {
        var model = TwoItemModel();
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var children)
            .ShouldBeTrue();
        var array = children.Array.ShouldNotBeNull();

        for (var index = 0; index < array!.Count; index++)
        {
            var set = array.GetItemDescriptors(index).ShouldNotBeNull();
            set!.TryGet(nameof(ObservableListChild.Name), out var name).ShouldBeTrue();
            name.PathText.ShouldBe($"Children[{index}].Name");
            name.GetValue().ShouldBe(model.Children[index].Name);
        }
    }
}
