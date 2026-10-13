using SparseTypes;

namespace SparseTypes.Tests;

[SparseModel]
public partial class SharedSequenceSettings
{
    public IReadOnlyList<int> AArrayView { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> AListView { get; set; } = Array.Empty<int>();
    public IList<int> MutableArrayView { get; set; } = Array.Empty<int>();
    public int[] ZArray { get; set; } = Array.Empty<int>();
    public List<int> ZList { get; set; } = new();
}

public sealed class SharedSequenceCloneTests
{
    [Test]
    public void ReadOnlyCollectionSnapshotsPreserveAliasesAndDetachFromBackingArray()
    {
        var values = new[] { 1, 2 };
        var view = Array.AsReadOnly(values);
        var model = new SharedSequenceSettings { AArrayView = view, AListView = view };
        var clone = model.DeepClone();
        var fragment = SharedSequenceSettings.Fragment.From(model);
        ReferenceEquals(clone.AArrayView, clone.AListView).ShouldBeTrue();
        ReferenceEquals(fragment.AArrayView.Value, fragment.AListView.Value).ShouldBeTrue();
        ReferenceEquals(clone.AArrayView, view).ShouldBeFalse();
        ReferenceEquals(fragment.AArrayView.Value, view).ShouldBeFalse();
        values[0] = 9;
        clone.AArrayView.ShouldBe([1, 2]);
        fragment.AArrayView.Value.ShouldBe([1, 2]);
    }

    [Test]
    public void ClonePreservesSharedCollectionsAcrossConcreteAndInterfaceViews()
    {
        var array = new[] { 1, 2 };
        var list = new List<int> { 3, 4 };
        var model = new SharedSequenceSettings
        {
            AArrayView = array,
            MutableArrayView = array,
            ZArray = array,
            AListView = list,
            ZList = list,
        };
        var clone = model.DeepClone();
        ReferenceEquals(clone.AArrayView, clone.ZArray).ShouldBeTrue();
        ReferenceEquals(clone.MutableArrayView, clone.ZArray).ShouldBeTrue();
        ReferenceEquals(clone.AListView, clone.ZList).ShouldBeTrue();
        ReferenceEquals(clone.ZArray, array).ShouldBeFalse();
        ReferenceEquals(clone.ZList, list).ShouldBeFalse();
        clone.ZArray[0] = 9;
        clone.ZList[0] = 8;
        array[0].ShouldBe(1);
        list[0].ShouldBe(3);
    }
}
