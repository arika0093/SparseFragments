namespace SparseFragments.Tests;

using SparseFragments.Generated;

[SparseFragmentModel]
public partial class ArrayElementHolder
{
    [SparseMerge(MergeMode.Replace)]
    public ObservableChild[] Kids { get; set; } = [];

    public string[] Labels { get; set; } = [];

    // NOTE: nullable fragment elements (ObservableChild?[]) still warn CS8602 in
    // the fragment clone emitter (pre-existing, fragment area), so only the
    // nullable-array shape is fixture-covered here.
    [SparseMerge(MergeMode.Replace)]
    public ObservableChild[]? MaybeKids { get; set; }
}

public sealed class DescriptorArrayElementTests
{
    [Test]
    public void ArrayModelsExposeNestedDescriptorsWithChangeTracking()
    {
        var model = new ArrayElementHolder
        {
            Kids =
            [
                new ObservableChild { Name = "first" },
                new ObservableChild { Name = "second" },
            ],
        };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(ArrayElementHolder.Kids), out var kids).ShouldBeTrue();
        var array = kids.Array.ShouldNotBeNull();
        array!.ItemType.ShouldBe(typeof(ObservableChild));
        // Arrays hold models, not views.
        array.ItemViewType.ShouldBe(typeof(ObservableChild));
        array.Count.ShouldBe(2);
        array.CanAdd.ShouldBeFalse();
        array.TryAdd(new ObservableChild()).ShouldBeFalse();

        var nested = array.GetItemDescriptors(0).ShouldNotBeNull();
        nested!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();
        name.PathText.ShouldBe("Kids[0].Name");
        name.TrySetValue("renamed").ShouldBeTrue();
        model.Kids[0].Name.ShouldBe("renamed");
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ScalarArraysKeepNullItemDescriptors()
    {
        var session = new ArrayElementHolder().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ArrayElementHolder.Labels), out var labels)
            .ShouldBeTrue();
        var array = labels.Array.ShouldNotBeNull();
        array!.ItemType.ShouldBe(typeof(string));
        array.GetItemDescriptors(0).ShouldBeNull();
    }

    [Test]
    public void NullArrayHasNoLiveDescriptor()
    {
        var model = new ArrayElementHolder { MaybeKids = null };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(ArrayElementHolder.MaybeKids), out var kids)
            .ShouldBeTrue();
        kids.Array.ShouldBeNull();
    }

    [Test]
    public void ArrayReplacementStalesRetainedDescriptors()
    {
        var oldKid = new ObservableChild { Name = "old" };
        var model = new ArrayElementHolder { Kids = [oldKid] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(ArrayElementHolder.Kids), out var kids).ShouldBeTrue();
        var array = kids.Array.ShouldNotBeNull();
        var retained = array!.GetItemDescriptors(0).ShouldNotBeNull();
        retained!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();

        model.Kids = [new ObservableChild { Name = "new" }];

        name.TrySetValue("orphan").ShouldBeFalse();
        oldKid.Name.ShouldBe("old");
        model.Kids[0].Name.ShouldBe("new");

        var fresh = kids.Array.ShouldNotBeNull();
        var live = fresh!.GetItemDescriptors(0).ShouldNotBeNull();
        live!.TryGet(nameof(ObservableChild.Name), out var liveName).ShouldBeTrue();
        liveName.TrySetValue("live").ShouldBeTrue();
        model.Kids[0].Name.ShouldBe("live");
    }
}
