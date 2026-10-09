namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class NullableShapeHolder
{
    public ObservableChild? MaybeChild { get; set; }

    public List<string>? MaybeTags { get; set; }

    public Dictionary<string, ObservableChild>? MaybeChildren { get; set; }

    public HashSet<string>? MaybeScores { get; set; }
}

public sealed class DescriptorNullShapeTests
{
    [Test]
    public void NullMembersDescribeShapesWithoutLiveAccessors()
    {
        var model = new NullableShapeHolder();
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeChild), out var child)
            .ShouldBeTrue();
        child.Child.ShouldBeNull();
        child.Shape.HasChild.ShouldBeTrue();
        child.Shape.ChildType.ShouldBe(typeof(ObservableChild));
        child.Shape.HasArray.ShouldBeFalse();
        child.Shape.HasDictionary.ShouldBeFalse();
        child.Shape.HasSet.ShouldBeFalse();

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeTags), out var tags)
            .ShouldBeTrue();
        tags.Array.ShouldBeNull();
        tags.Shape.HasArray.ShouldBeTrue();
        tags.Shape.ArrayItemType.ShouldBe(typeof(string));
        tags.Shape.ArrayItemNullable.ShouldBeFalse();
        tags.Shape.HasChild.ShouldBeFalse();

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeChildren), out var children)
            .ShouldBeTrue();
        children.Dictionary.ShouldBeNull();
        children.Shape.HasDictionary.ShouldBeTrue();
        children.Shape.DictionaryKeyType.ShouldBe(typeof(string));
        children.Shape.DictionaryValueType.ShouldBe(typeof(ObservableChild));
        children.Shape.DictionaryValueNullable.ShouldBeFalse();

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeScores), out var scores)
            .ShouldBeTrue();
        scores.Set.ShouldBeNull();
        scores.Shape.HasSet.ShouldBeTrue();
        scores.Shape.SetItemType.ShouldBe(typeof(string));
        scores.Shape.SetItemNullable.ShouldBeFalse();
    }

    [Test]
    public void ScalarMembersHaveNoShapes()
    {
        var session = new NullableShapeHolder().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeChild), out var child)
            .ShouldBeTrue();
        child.Shape.HasArray.ShouldBeFalse();
        child.Shape.ArrayItemType.ShouldBeNull();
        child.Shape.DictionaryKeyType.ShouldBeNull();
        child.Shape.DictionaryValueType.ShouldBeNull();
        child.Shape.SetItemType.ShouldBeNull();
    }

    [Test]
    public void GenericFormCreationStartsFromNullMembers()
    {
        var model = new NullableShapeHolder();
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeChild), out var child)
            .ShouldBeTrue();
        child.Shape.ChildType.ShouldBe(typeof(ObservableChild));
        child.TrySetValue(new ObservableChild { Name = "created" }).ShouldBeTrue();
        var childSet = child.Child.ShouldNotBeNull();
        childSet!.TryGet(nameof(ObservableChild.Name), out var name).ShouldBeTrue();
        name.GetValue().ShouldBe("created");

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeTags), out var tags)
            .ShouldBeTrue();
        tags.Shape.ArrayItemType.ShouldBe(typeof(string));
        tags.TrySetValue(new List<string> { "created" }).ShouldBeTrue();
        tags.Array.ShouldNotBeNull();
        tags.Array!.Count.ShouldBe(1);

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeChildren), out var children)
            .ShouldBeTrue();
        children.Shape.DictionaryValueType.ShouldBe(typeof(ObservableChild));
        children
            .TrySetValue(
                new Dictionary<string, ObservableChild>
                {
                    ["key"] = new ObservableChild { Name = "created" },
                }
            )
            .ShouldBeTrue();
        var dict = children.Dictionary.ShouldNotBeNull();
        dict!.Count.ShouldBe(1);

        session
            .Descriptors.TryGet(nameof(NullableShapeHolder.MaybeScores), out var scores)
            .ShouldBeTrue();
        scores.Shape.SetItemType.ShouldBe(typeof(string));
        scores.TrySetValue(new HashSet<string> { "created" }).ShouldBeTrue();
        scores.Set.ShouldNotBeNull();
        scores.Set!.Contains("created").ShouldBeTrue();

        session.HasChanges.ShouldBeTrue();
    }
}
