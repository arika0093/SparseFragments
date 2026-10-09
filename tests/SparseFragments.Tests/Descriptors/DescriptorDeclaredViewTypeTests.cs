using SparseFragments.Generated;

namespace SparseFragments.Tests;

public sealed class DescriptorDeclaredViewTypeTests
{
    [Test]
    public void DeclaredTypeAndViewTypeAgreeWithReads()
    {
        var model = new ObservableHolder
        {
            Title = "t",
            Child = new ObservableChild { Name = "n" },
            ReplacementTags = ["a"],
            Metadata = new() { ["k"] = "v" },
            Children = [new ObservableListChild { Id = "1", Name = "c" }],
            ChildrenByName = new()
            {
                ["e"] = new ObservableListChild { Id = "2", Name = "d" },
            },
        };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(ObservableHolder.Title), out var title).ShouldBeTrue();
        title.Type.ShouldBe(typeof(string));
        title.ViewType.ShouldBe(typeof(string));
        title.Type.IsInstanceOfType(title.GetValue()).ShouldBeTrue();

        session.Descriptors.TryGet(nameof(ObservableHolder.Child), out var child).ShouldBeTrue();
        child.Type.ShouldBe(typeof(ObservableChild));
        child.ViewType.IsAssignableFrom(child.GetValue()!.GetType()).ShouldBeTrue();
        child.Type.IsInstanceOfType(child.GetValue()).ShouldBeFalse();

        // Round-trip accepts both model instances and observable proxies.
        var proxyValue = child.GetValue()!;
        child.TrySetValue(new ObservableChild { Name = "model" }).ShouldBeTrue();
        child.TrySetValue(proxyValue).ShouldBeTrue();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ReplacementTags), out var tags)
            .ShouldBeTrue();
        var array = tags.Array.ShouldNotBeNull();
        array!.ItemType.ShouldBe(typeof(string));
        array.ItemViewType.ShouldBe(typeof(string));

        session
            .Descriptors.TryGet(nameof(ObservableHolder.Children), out var children)
            .ShouldBeTrue();
        var seq = children.Array.ShouldNotBeNull();
        seq!.ItemType.ShouldBe(typeof(ObservableListChild));
        seq.GetItem(0).ShouldNotBeNull();
        seq.ItemViewType.IsAssignableFrom(seq.GetItem(0)!.GetType()).ShouldBeTrue();
        seq.TrySetItem(0, new ObservableListChild { Id = "1", Name = "updated" }).ShouldBeTrue();

        session
            .Descriptors.TryGet(nameof(ObservableHolder.ChildrenByName), out var byName)
            .ShouldBeTrue();
        var dict = byName.Dictionary.ShouldNotBeNull();
        dict!.ValueType.ShouldBe(typeof(ObservableListChild));
        dict.TryGetValue("e", out var entry).ShouldBeTrue();
        dict.ValueViewType.IsAssignableFrom(entry!.GetType()).ShouldBeTrue();
        dict.TrySetValue("e", new ObservableListChild { Id = "2", Name = "updated" })
            .ShouldBeTrue();
    }
}
