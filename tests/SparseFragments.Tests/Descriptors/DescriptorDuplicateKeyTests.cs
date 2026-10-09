namespace SparseFragments.Tests;

public sealed class DescriptorDuplicateKeyTests
{
    [Test]
    public void DuplicateAssignedKeysAreRejectedWithoutMutation()
    {
        var model = new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "a" }] };
        var session = model.CreateEditSession();
        var notifications = 0;
        session.TransitionObserved += _ => notifications++;

        session.Descriptors.TryGet(nameof(KeyedServerHolder.Items), out var items).ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();

        array!.TryAdd(new KeyedServer { Id = "a", Name = "duplicate" }).ShouldBeFalse();
        array.TryInsert(0, new KeyedServer { Id = "a", Name = "duplicate" }).ShouldBeFalse();
        array.TrySetItem(0, new KeyedServer { Id = "a", Name = "same key edit" }).ShouldBeTrue();

        model.Items.Count.ShouldBe(1);
        model.Items[0].Name.ShouldBe("same key edit");
        notifications.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();

        // Replacing with a duplicate of another row is rejected too.
        model.Items.Add(new KeyedServer { Id = "b", Name = "b" });
        array.TrySetItem(0, new KeyedServer { Id = "b", Name = "collision" }).ShouldBeFalse();
        model.Items[0].Id.ShouldBe("a");
        model.Items.Count.ShouldBe(2);
    }

    [Test]
    public void RejectedDuplicateLeavesTheSessionRecoverable()
    {
        var model = new KeyedServerHolder { Items = [new KeyedServer { Id = "a", Name = "a" }] };
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(KeyedServerHolder.Items), out var items).ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();

        array!.TryAdd(new KeyedServer { Id = "a", Name = "duplicate" }).ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();

        array.TryAdd(new KeyedServer { Id = "b", Name = "b" }).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        model.Items.Count.ShouldBe(2);
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void UnassignedSentinelsDoNotTriggerDuplicateRejection()
    {
        var model = new AssignedServerHolder
        {
            Items = [new AssignedServer { Id = 7, Name = "assigned" }],
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(AssignedServerHolder.Items), out var items)
            .ShouldBeTrue();
        var array = items.Array.ShouldNotBeNull();

        // Assigned duplicates are still rejected beside unassigned-absent lists.
        array!.TryAdd(new AssignedServer { Id = 7, Name = "duplicate" }).ShouldBeFalse();
        model.Items.Count.ShouldBe(1);

        // Distinct assigned keys are accepted.
        array.TryAdd(new AssignedServer { Id = 8, Name = "other" }).ShouldBeTrue();
        model.Items.Count.ShouldBe(2);
    }
}
