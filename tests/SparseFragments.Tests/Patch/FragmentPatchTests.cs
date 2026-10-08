using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class PatchOperationNames
{
    public int Set { get; set; }
    public int SetNull { get; set; }
    public int Remove { get; set; }
}

public sealed class FragmentPatchTests
{
    [Test]
    public void ScalarOperationsPreserveUnchangedAndDistinguishNullFromRemoval()
    {
        var original = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 8 });
        var patch = new Settings.Patch
        {
            Label = (string?)null,
            Enabled = FragmentOperation<bool>.Remove,
        };
        var result = original.Apply(patch);
        result.Label.IsPresent.ShouldBeTrue();
        result.Label.Value.ShouldBeNull();
        result.Enabled.IsPresent.ShouldBeFalse();
        result.RetryCount.Value.ShouldBe(8);
        original.Label.Value.ShouldBe("before");
        original.Enabled.IsPresent.ShouldBeTrue();
        new Settings.Patch().IsEmpty.ShouldBeTrue();
        patch.IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void StructuralPatchKeepsUnspecifiedMembersAndSupportsNullAndRemoval()
    {
        var original = BuilderParent.Fragment.From(
            new BuilderParent
            {
                Child = new BuilderChild { Count = 7 },
                Label = "keep",
            }
        );
        var patch = new BuilderParent.Patch();
        patch.Child.Count = 9;
        original.Apply(patch).ToModel().Child!.Count.ShouldBe(9);
        original.Apply(patch).Label.Value.ShouldBe("keep");
        original.ToModel().Child!.Count.ShouldBe(7);
        patch = new BuilderParent.Patch();
        patch.Child.SetNull();
        var presentNull = original.Apply(patch);
        presentNull.Child.IsPresent.ShouldBeTrue();
        presentNull.Child.Value.ShouldBeNull();
        patch = new BuilderParent.Patch();
        patch.Child.Remove();
        original.Apply(patch).Child.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void GeneratedChildPatchCanCreateMissingChildAndReplaceIt()
    {
        var patch = new Settings.Patch();
        patch.Nested.Port = 9000;
        var result = new Settings.Fragment().Apply(patch);
        result.Nested.Value!.Port.Value.ShouldBe(9000);
        result.Nested.Value.Host.IsPresent.ShouldBeFalse();
        patch = new Settings.Patch();
        patch.Nested.Set(new Nested { Host = "replacement", Port = 42 });
        result.Apply(patch).Nested.Value!.Host.Value.ShouldBe("replacement");
        patch.Nested = FragmentOperation<Nested.Fragment?>.Remove;
        result.Apply(patch).Nested.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void FragmentToPatchPreservesMissingAndNullWhenApplied()
    {
        var contribution = new Settings.Fragment
        {
            Label = (string?)null,
            Nested = (Nested.Fragment?)null,
        };
        var basis = Settings.Fragment.From(new Settings { RetryCount = 8 });
        var result = basis.Apply(contribution.ToPatch());
        result.Label.IsPresent.ShouldBeTrue();
        result.Label.Value.ShouldBeNull();
        result.Nested.IsPresent.ShouldBeTrue();
        result.Nested.Value.ShouldBeNull();
        result.RetryCount.Value.ShouldBe(8);
    }

    [Test]
    public void WholeOperationsRemainAvailableWhenMemberNamesCollide()
    {
        var patch = new PatchOperationNames.Patch
        {
            Set = 7,
            SetNull = 8,
            Remove = 9,
        };
        var fragment = new PatchOperationNames.Fragment().Apply(patch);
        fragment.Set.Value.ShouldBe(7);
        fragment.SetNull.Value.ShouldBe(8);
        fragment.Remove.Value.ShouldBe(9);
        var whole = new PatchOperationNames.Patch();
        whole.SparseIsEmpty.ShouldBeTrue();
        whole.SparseSet(new PatchOperationNames { Set = 11 });
        whole.Apply(Optional<PatchOperationNames.Fragment?>.Missing).Value!.Set.Value.ShouldBe(11);
        whole.SparseSetNull();
        var nullResult = whole.Apply(Optional<PatchOperationNames.Fragment?>.Present(fragment));
        nullResult.IsPresent.ShouldBeTrue();
        nullResult.Value.ShouldBeNull();
        whole.SparseRemove();
        whole.Apply(nullResult).IsPresent.ShouldBeFalse();
    }

    [Test]
    public void WholeReplacementAppliesToRootFragment()
    {
        var patch = new Settings.Patch();
        patch.Set(new Settings { RetryCount = 12 });
        new Settings.Fragment { RetryCount = 7 }
            .Apply(patch)
            .RetryCount.Value.ShouldBe(12);
    }
}
