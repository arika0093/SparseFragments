#nullable disable

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class ObliviousHolder
{
    public string Name { get; set; } = string.Empty;

    public ObservableChild Child { get; set; } = new ObservableChild();

    [SparseMerge(MergeMode.Replace)]
    public ObliviousChild Poco { get; set; } = new ObliviousChild();
}

public sealed class ObliviousChild
{
    public string Label { get; set; } = string.Empty;
}

public sealed class DescriptorObliviousTests
{
    [Test]
    public void ObliviousReferenceAcceptsNullThroughDescriptor()
    {
        var model = new ObliviousHolder();
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(ObliviousHolder.Name), out var name).ShouldBeTrue();
        name.IsNullable.ShouldBeFalse();
        name.IsNullableOblivious.ShouldBeTrue();
        name.TrySetValue(null).ShouldBeTrue();
        model.Name.ShouldBeNull();

        session.Descriptors.TryGet(nameof(ObliviousHolder.Child), out var child).ShouldBeTrue();
        child.IsNullable.ShouldBeFalse();
        child.IsNullableOblivious.ShouldBeTrue();
        child.TrySetValue(null).ShouldBeTrue();
        model.Child.ShouldBeNull();

        session.Descriptors.TryGet(nameof(ObliviousHolder.Poco), out var poco).ShouldBeTrue();
        poco.IsNullableOblivious.ShouldBeTrue();
        poco.TrySetValue(null).ShouldBeTrue();
        model.Poco.ShouldBeNull();
    }
}
