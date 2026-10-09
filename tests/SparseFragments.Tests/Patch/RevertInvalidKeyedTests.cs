namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class RevertNestedKeyedParent
{
    public DuplicateKeySessionModel Nested { get; set; } = new();
}

/// <summary>RevertChanges must recover from invalid temporary keyed state (#167).</summary>
public sealed class RevertInvalidKeyedTests
{
    [Test]
    public void RevertChangesRecoversFromDuplicateAssignedKeys()
    {
        var model = new DuplicateKeySessionModel
        {
            Items = [new DuplicateKeySessionItem { Id = "a" }],
        };
        var items = model.Items;
        var session = model.CreateEditSession();
        session.Observable.Items.AddModel(new DuplicateKeySessionItem { Id = "a" });

        session.HasChanges.ShouldBeTrue();

        session.RevertChanges();

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        ReferenceEquals(model.Items, items).ShouldBeTrue();
        model.Items.Count.ShouldBe(1);
        model.Items[0].Id.ShouldBe("a");
    }

    [Test]
    public void RevertChangesRecoversNestedDuplicateKeys()
    {
        var model = new RevertNestedKeyedParent
        {
            Nested = new DuplicateKeySessionModel
            {
                Items = [new DuplicateKeySessionItem { Id = "a" }],
            },
        };
        var session = model.CreateEditSession();
        session.Observable.Nested!.Items.AddModel(new DuplicateKeySessionItem { Id = "a" });

        session.HasChanges.ShouldBeTrue();

        session.RevertChanges();

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        model.Nested.Items.Count.ShouldBe(1);
        model.Nested.Items[0].Id.ShouldBe("a");
    }

    [Test]
    public void RevertChangesRecoversFromUnassignedSentinelAdd()
    {
        var model = new DuplicateKeySessionModel
        {
            Items = [new DuplicateKeySessionItem { Id = "a" }],
        };
        var session = model.CreateEditSession();
        session.Observable.Items.AddModel(new DuplicateKeySessionItem());

        session.RevertChanges();

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        model.Items.Count.ShouldBe(1);
        model.Items[0].Id.ShouldBe("a");
    }
}
