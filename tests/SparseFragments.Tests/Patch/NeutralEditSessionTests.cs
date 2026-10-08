using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class NeutralSessionModel
{
    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public System.Collections.Generic.List<string> Tags { get; set; } = [];
}

public sealed class NeutralEditSessionTests
{
    [Test]
    public void ModelExtensionDerivesBaselineToCurrentChangeSet()
    {
        var baseline = new NeutralSessionModel { Name = "before", Version = 1 };
        var current = new NeutralSessionModel { Name = "after", Version = 1 };

        var changes = baseline.CreateChangeSet(current);

        changes.IsEmpty.ShouldBeFalse();
        changes.Name.Before.Value.ShouldBe("before");
        changes.Name.After.Value.ShouldBe("after");
        changes.Version.Before.IsPresent.ShouldBeFalse();
        changes.Version.After.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void NeutralSessionTracksCurrentAgainstAcceptedBaseline()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();

        ReferenceEquals(session.Model, model).ShouldBeTrue();
        ReferenceEquals(session.Observable.Model, model).ShouldBeTrue();
        ReferenceEquals(session.Observable, session.Observable).ShouldBeTrue();
        session.HasChanges.ShouldBeFalse();

        model.Name = "edited";
        session.HasChanges.ShouldBeTrue();
        var changes = session.CreateChangeSet();
        changes.Name.Before.Value.ShouldBe("before");
        changes.Name.After.Value.ShouldBe("edited");
        session.CreatePatch().IsEmpty.ShouldBeFalse();

        model.Name = "before";
        session.HasChanges.ShouldBeFalse();

        model.Name = "accepted";
        session.AcceptChanges();
        session.HasChanges.ShouldBeFalse();

        model.Name = "later";
        model.Version = 2;
        var afterAccept = session.CreateChangeSet();
        afterAccept.Name.Before.Value.ShouldBe("accepted");
        afterAccept.Name.After.Value.ShouldBe("later");
        afterAccept.Version.Before.Value.ShouldBe(1);
        afterAccept.Version.After.Value.ShouldBe(2);
    }

    [Test]
    public void SessionCanEditCurrentModelAgainstSeparateBaselineSnapshot()
    {
        var baseline = new NeutralSessionModel { Name = "before", Version = 1 };
        var current = new NeutralSessionModel { Name = "current", Version = 2 };
        var notifications = 0;
        var session = baseline.CreateEditSession(current, () => notifications++);

        ReferenceEquals(session.Model, current).ShouldBeTrue();
        ReferenceEquals(session.Observable.Model, current).ShouldBeTrue();
        session.Observable.Name = "proxy edit";
        notifications.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.Before.Value.ShouldBe("before");

        baseline.Name = "baseline mutated after creation";
        current.Name = "edited";
        var changes = session.CreateChangeSet();
        changes.Name.Before.Value.ShouldBe("before");
        changes.Name.After.Value.ShouldBe("edited");
        session.AcceptChanges();
        session.HasChanges.ShouldBeFalse();
        ReferenceEquals(session.Model, current).ShouldBeTrue();
    }

    [Test]
    public void SessionDetectsUnnotifiedInPlaceCollectionChanges()
    {
        var model = new NeutralSessionModel();
        var session = model.CreateEditSession();

        model.Tags.Add("changed without proxy notification");

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Tags.IsChanged.ShouldBeTrue();
    }
}
