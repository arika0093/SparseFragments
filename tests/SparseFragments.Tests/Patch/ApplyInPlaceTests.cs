using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class SelectiveWriteModel
{
    public string Mutable { get; set; } = string.Empty;

    public string Initialized { get; init; } = string.Empty;

    public string ConstructorOnly { get; }

    public SelectiveWriteModel(string constructorOnly) => ConstructorOnly = constructorOnly;
}

[SparseFragmentModel]
public partial class ImmutableOnlyWriteModel
{
    public string Initialized { get; init; } = string.Empty;

    public string ConstructorOnly { get; }

    public ImmutableOnlyWriteModel(string constructorOnly) => ConstructorOnly = constructorOnly;
}

public sealed class ApplyInPlaceTests
{
    [Test]
    public void PatchAppliesWritableMembersWhenNoImmutableMemberIsIncluded()
    {
        var model = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };

        var result = new SelectiveWriteModel.Patch { Mutable = "after" }.ApplyInPlace(model);

        result.Succeeded.ShouldBeTrue();
        result.UnsupportedMembers.ShouldBeEmpty();
        model.Mutable.ShouldBe("after");
        model.Initialized.ShouldBe("init value");
        model.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void ImmutableOnlyModelReportsStructuredFailureWithoutChangingMembers()
    {
        var model = new ImmutableOnlyWriteModel("constructor value") { Initialized = "init value" };
        var patch = new ImmutableOnlyWriteModel.Patch
        {
            Initialized = "new init value",
            ConstructorOnly = "new constructor value",
        };

        var result = patch.ApplyInPlace(model);

        result.Succeeded.ShouldBeFalse();
        result.UnsupportedMembers.ShouldBe(["ConstructorOnly", "Initialized"]);
        model.Initialized.ShouldBe("init value");
        model.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void PatchReturnsImmutableMemberFailuresWithoutPartiallyApplying()
    {
        var model = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var patch = new SelectiveWriteModel.Patch
        {
            Mutable = "after",
            Initialized = "new init value",
            ConstructorOnly = "new constructor value",
        };

        var result = patch.ApplyInPlace(model);

        result.Succeeded.ShouldBeFalse();
        result.UnsupportedMembers.ShouldBe(["ConstructorOnly", "Initialized"]);
        model.Mutable.ShouldBe("before");
        model.Initialized.ShouldBe("init value");
        model.ConstructorOnly.ShouldBe("constructor value");
    }
}
