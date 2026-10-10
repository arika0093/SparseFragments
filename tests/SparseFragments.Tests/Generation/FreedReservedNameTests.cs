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

[SparseFragmentModel]
public partial class FreedEnumerateModel
{
    public string EnumerateChanges { get; set; } = string.Empty;

    public string EnumerateChangedPaths { get; set; } = string.Empty;

    public string ChangeInfo { get; set; } = string.Empty;

    public string ChangeKind { get; set; } = string.Empty;

    public string Other { get; set; } = string.Empty;
}

public sealed class FreedReservedNameTests
{
    private static Optional<FreedChangesModel.Fragment?> Present(
        FreedChangesModel.Fragment fragment
    ) => Optional<FreedChangesModel.Fragment?>.Present(fragment);

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

    [Test]
    public void EnumerationApiNamesGeneratePrefixedTransitions()
    {
        // Members colliding with ChangeSet enumeration helpers share one name map,
        // so helper emitters and typed surfaces address the same property.
        var before = Optional<FreedEnumerateModel.Fragment?>.Present(
            FreedEnumerateModel.Fragment.From(
                new FreedEnumerateModel
                {
                    EnumerateChanges = "a",
                    EnumerateChangedPaths = "p1",
                    ChangeInfo = "i1",
                    ChangeKind = "k1",
                    Other = "o",
                }
            )
        );
        var after = Optional<FreedEnumerateModel.Fragment?>.Present(
            FreedEnumerateModel.Fragment.From(
                new FreedEnumerateModel
                {
                    EnumerateChanges = "b",
                    EnumerateChangedPaths = "p1",
                    ChangeInfo = "i1",
                    ChangeKind = "k1",
                    Other = "o",
                }
            )
        );
        var changes = FreedEnumerateModel.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        changes.SparseEnumerateChanges.IsChanged.ShouldBeTrue();
        changes.SparseEnumerateChanges.Before.Value.ShouldBe("a");
        changes.SparseEnumerateChanges.After.Value.ShouldBe("b");
        changes.SparseEnumerateChangedPaths.IsChanged.ShouldBeFalse();
        changes.SparseChangeInfo.IsChanged.ShouldBeFalse();
        changes.SparseChangeKind.IsChanged.ShouldBeFalse();
        changes.Other.IsChanged.ShouldBeFalse();

        // Flattened helpers still resolve on the same renamed surface.
        var flattened = new List<FreedEnumerateModel.ChangeSet.ChangeInfo>(
            changes.EnumerateChanges()
        );
        flattened.Count.ShouldBe(1);
        flattened[0].PathText.ShouldBe("EnumerateChanges");
        changes.EnumerateChangedPaths().Count.ShouldBe(1);
    }

    [Test]
    public void NonConflictingNamesRemainUnchanged()
    {
        var before = Optional<FreedEnumerateModel.Fragment?>.Present(
            FreedEnumerateModel.Fragment.From(new FreedEnumerateModel { Other = "a" })
        );
        var after = Optional<FreedEnumerateModel.Fragment?>.Present(
            FreedEnumerateModel.Fragment.From(new FreedEnumerateModel { Other = "b" })
        );
        var changes = FreedEnumerateModel.ChangeSet.Between(before, after);
        changes.Other.IsChanged.ShouldBeTrue();
        changes.Other.Before.Value.ShouldBe("a");
        changes.Other.After.Value.ShouldBe("b");
    }
}
