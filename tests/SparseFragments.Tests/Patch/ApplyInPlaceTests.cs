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

    [Test]
    public void ChangeSetAppliesWritableMembersWhenImmutableMembersAreUnchanged()
    {
        var baseline = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var edited = new SelectiveWriteModel("constructor value")
        {
            Mutable = "after",
            Initialized = "init value",
        };
        var current = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var changes = SelectiveWriteModel.ChangeSet.Between(baseline, edited);

        changes.TryApplyInPlace(current, out var conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        current.Mutable.ShouldBe("after");
        current.Initialized.ShouldBe("init value");
        current.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void ChangeSetReportsImmutableOperationsWithoutPartiallyApplyingWritableMembers()
    {
        var baseline = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var edited = new SelectiveWriteModel("new constructor value")
        {
            Mutable = "after",
            Initialized = "new init value",
        };
        var current = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var changes = SelectiveWriteModel.ChangeSet.Between(baseline, edited);

        changes.TryApplyInPlace(current, out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts
            .Select(static conflict => conflict.PathText)
            .OrderBy(static path => path)
            .ShouldBe(["ConstructorOnly", "Initialized"]);
        conflicts.ShouldAllBe(static conflict =>
            conflict.Kind == SparseConflictKind.InPlaceWriteUnavailable
        );
        var initializedConflict = conflicts.Single(static conflict =>
            conflict.PathText == "Initialized"
        );
        initializedConflict.BaseValue.IsPresent.ShouldBeTrue();
        initializedConflict.BaseValue.Value.ShouldBe("init value");
        initializedConflict.LocalValue.Value.ShouldBe("new init value");
        initializedConflict.CurrentValue.Value.ShouldBe("init value");
        current.Mutable.ShouldBe("before");
        current.Initialized.ShouldBe("init value");
        current.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void ChangeSetChecksBeforeStateBeforeWritingMixedModels()
    {
        var baseline = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var edited = new SelectiveWriteModel("constructor value")
        {
            Mutable = "after",
            Initialized = "init value",
        };
        var current = new SelectiveWriteModel("constructor value")
        {
            Mutable = "concurrent",
            Initialized = "init value",
        };
        var changes = SelectiveWriteModel.ChangeSet.Between(baseline, edited);

        changes.TryApplyInPlace(current, out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.ShouldContain(static conflict => conflict.Kind == SparseConflictKind.Scalar);
        current.Mutable.ShouldBe("concurrent");
        current.Initialized.ShouldBe("init value");
        current.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void ChangeSetKeepsRebasedWritableStateWhenAnImmutableOperationFails()
    {
        var baseline = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var edited = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "new init value",
        };
        var current = new SelectiveWriteModel("constructor value")
        {
            Mutable = "concurrent",
            Initialized = "init value",
        };
        var changes = SelectiveWriteModel.ChangeSet.Between(baseline, edited);

        changes.TryApplyInPlace(current, out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.Count.ShouldBe(1);
        conflicts[0].Kind.ShouldBe(SparseConflictKind.InPlaceWriteUnavailable);
        conflicts[0].PathText.ShouldBe("Initialized");
        current.Mutable.ShouldBe("concurrent");
        current.Initialized.ShouldBe("init value");
        current.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void ImmutableOnlyChangeSetReturnsStructuredFailuresAndChecksBeforeState()
    {
        var baseline = new ImmutableOnlyWriteModel("constructor value")
        {
            Initialized = "init value",
        };
        var edited = new ImmutableOnlyWriteModel("new constructor value")
        {
            Initialized = "new init value",
        };
        var changes = ImmutableOnlyWriteModel.ChangeSet.Between(baseline, edited);
        var current = new ImmutableOnlyWriteModel("constructor value")
        {
            Initialized = "init value",
        };

        changes.TryApplyInPlace(current, out var immutableConflicts).ShouldBeFalse();
        immutableConflicts.ShouldNotBeNull();
        immutableConflicts
            .Select(static conflict => conflict.PathText)
            .OrderBy(static path => path)
            .ShouldBe(["ConstructorOnly", "Initialized"]);
        current.Initialized.ShouldBe("init value");
        current.ConstructorOnly.ShouldBe("constructor value");

        var concurrent = new ImmutableOnlyWriteModel("constructor value")
        {
            Initialized = "concurrent",
        };
        changes.TryApplyInPlace(concurrent, out var beforeStateConflicts).ShouldBeFalse();
        beforeStateConflicts.ShouldNotBeNull();
        beforeStateConflicts.ShouldContain(static conflict =>
            conflict.Kind == SparseConflictKind.Scalar
        );
        concurrent.Initialized.ShouldBe("concurrent");
        concurrent.ConstructorOnly.ShouldBe("constructor value");
    }

    [Test]
    public void EditSessionReturnsImmutableChangeSetConflictsWithoutMutatingTheModel()
    {
        var baseline = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var edited = new SelectiveWriteModel("constructor value")
        {
            Mutable = "after",
            Initialized = "new init value",
        };
        var model = new SelectiveWriteModel("constructor value")
        {
            Mutable = "before",
            Initialized = "init value",
        };
        var changes = SelectiveWriteModel.ChangeSet.Between(baseline, edited);
        var session = model.CreateEditSession();

        session.TryApplyInPlace(changes, out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.ShouldContain(static conflict =>
            conflict.Kind == SparseConflictKind.InPlaceWriteUnavailable
        );
        model.Mutable.ShouldBe("before");
        model.Initialized.ShouldBe("init value");
        model.ConstructorOnly.ShouldBe("constructor value");
    }
}
