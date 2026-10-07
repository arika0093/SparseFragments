namespace SparseFragments.Tests;

// Issue #91: the generic `T.Sparse.Properties` / `Patch.Changes` /
// `ChangeSet.Changes` inspection surface is removed, so ordinary model members
// and nested types named `Sparse` or `Changes` follow the normal
// generated-name rules. A member named `Changes` generates the natural typed
// transition (`changes.Changes.IsChanged/Before/After`).

[SparseFragmentModel]
public partial class FreedChangesModel
{
    public string Changes { get; set; } = string.Empty;

    public string Other { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class FreedSparseModel
{
    public string? Sparse { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class FreedSparseNestedModel
{
    public string Name { get; set; } = string.Empty;

    public sealed class Sparse
    {
        public string Note { get; set; } = string.Empty;
    }
}

public sealed class FreedReservedNameTests
{
    private static Optional<FreedChangesModel.Fragment?> Present(FreedChangesModel.Fragment fragment) =>
        Optional<FreedChangesModel.Fragment?>.Present(fragment);

    [Test]
    public void ChangesMemberGeneratesTypedPatchMember()
    {
        var patch = new FreedChangesModel.Patch { Other = "o" };
        patch.Changes = "x";
        patch.IsEmpty.ShouldBeFalse();
        patch.Changes.Value.ShouldBe("x");
    }

    [Test]
    public void ChangesMemberGeneratesTypedChangeSetTransition()
    {
        var before = Present(
            FreedChangesModel.Fragment.From(new FreedChangesModel { Changes = "a", Other = "o" })
        );
        var after = Present(
            FreedChangesModel.Fragment.From(new FreedChangesModel { Changes = "b", Other = "o" })
        );
        var changes = FreedChangesModel.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        changes.Changes.IsChanged.ShouldBeTrue();
        changes.Changes.Before.Value.ShouldBe("a");
        changes.Changes.After.Value.ShouldBe("b");
        changes.Other.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void ChangesMemberUnchangedTransitionIsEmpty()
    {
        var state = Present(
            FreedChangesModel.Fragment.From(new FreedChangesModel { Changes = "a", Other = "o" })
        );
        var changes = FreedChangesModel.ChangeSet.Between(state, state);
        changes.IsEmpty.ShouldBeTrue();
        changes.Changes.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void SparseMemberGeneratesTypedTransition()
    {
        var before = Optional<FreedSparseModel.Fragment?>.Present(
            FreedSparseModel.Fragment.From(new FreedSparseModel { Sparse = "a", Name = "n" })
        );
        var after = Optional<FreedSparseModel.Fragment?>.Present(
            FreedSparseModel.Fragment.From(new FreedSparseModel { Sparse = "b", Name = "n" })
        );
        var changes = FreedSparseModel.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        changes.Sparse.IsChanged.ShouldBeTrue();
        changes.Sparse.Before.Value.ShouldBe("a");
        changes.Sparse.After.Value.ShouldBe("b");
        changes.Name.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void SparseNestedTypeCoexistsWithGeneratedCode()
    {
        // The nested type named `Sparse` coexists with generated code.
        var nested = new FreedSparseNestedModel.Sparse { Note = "ok" };
        nested.Note.ShouldBe("ok");
        var changes = FreedSparseNestedModel.ChangeSet.Between(
            Optional<FreedSparseNestedModel.Fragment?>.Present(
                FreedSparseNestedModel.Fragment.From(new FreedSparseNestedModel { Name = "n" })
            ),
            Optional<FreedSparseNestedModel.Fragment?>.Present(
                FreedSparseNestedModel.Fragment.From(new FreedSparseNestedModel { Name = "m" })
            )
        );
        changes.Name.IsChanged.ShouldBeTrue();
    }
}
