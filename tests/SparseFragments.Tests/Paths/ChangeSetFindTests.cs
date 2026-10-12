namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class FindReservedMembers
{
    public int Find { get; set; }
    public int EnumerateChanges { get; set; }
}

public sealed class ChangeSetFindTests
{
    [Test]
    public void ScalarFindUsesTheReservedMemberNameMap()
    {
        var changes = FindReservedMembers.ChangeSet.Between(
            new FindReservedMembers(),
            new FindReservedMembers { Find = 1, EnumerateChanges = 2 }
        );
        changes.SparseFind.After.Value.ShouldBe(1);
        changes.SparseEnumerateChanges.After.Value.ShouldBe(2);
        changes.Find(FindReservedMembers.SparsePath.Find).ShouldNotBeNull().After.Value.ShouldBe(1);
        changes
            .Find(FindReservedMembers.SparsePath.EnumerateChanges)
            .ShouldNotBeNull()
            .After.Value.ShouldBe(2);
    }

    [Test]
    public void ScalarFindRejectsOtherRootsAndNonMemberSegments()
    {
        var changes = FindReservedMembers.ChangeSet.Between(
            new FindReservedMembers(),
            new FindReservedMembers { Find = 1 }
        );
        changes.Find(SparsePath.Root<Settings>().Member("Find")).ShouldBeNull();
        changes.Find(SparsePath.Root<FindReservedMembers>().Key("Find")).ShouldBeNull();
        changes.Find(SparsePath.Root<FindReservedMembers>().At(0)).ShouldBeNull();
        changes.Find(SparsePath.Root<FindReservedMembers>().Member("Missing")).ShouldBeNull();
        changes.Find(FindReservedMembers.SparsePath.EnumerateChanges).ShouldBeNull();
    }

    [Test]
    public void ScalarFindRetainsMissingAndPresentNullValues()
    {
        var changes = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment { Label = Optional<string?>.Present("before") }
            ),
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present(null),
                    RetryCount = Optional<int>.Present(4),
                }
            )
        );
        var label = changes.Find(Settings.SparsePath.Label).ShouldNotBeNull();
        label.Before.Value.ShouldBe("before");
        label.After.IsPresent.ShouldBeTrue();
        label.After.Value.ShouldBeNull();
        label.Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Changed);
        var retry = changes.Find(Settings.SparsePath.RetryCount).ShouldNotBeNull();
        retry.Before.IsPresent.ShouldBeFalse();
        retry.After.Value.ShouldBe(4);
        retry.Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Added);
    }

    [Test]
    public void WholeRootFindDoesNotExposeIndividualMembers()
    {
        var added = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment { Label = Optional<string?>.Present("added") }
            )
        );
        added.Find(Settings.SparsePath.Label).ShouldBeNull();
        added
            .Find(SparsePath.Root<Settings>())
            .ShouldNotBeNull()
            .Kind.ShouldBe(Settings.ChangeSet.ChangeKind.Added);
    }

    [Test]
    public void EmptyFindReturnsNoEntry()
    {
        var same = new Settings();
        var empty = Settings.ChangeSet.Between(same, same);
        empty.Find(Settings.SparsePath.Label).ShouldBeNull();
        empty.Find(SparsePath.Root<Settings>()).ShouldBeNull();
    }
}
