using SparseTypes;
using SparseTypes.Generated;

namespace SparseTypes.Tests;

[SparseModel]
public partial class GroupSimpleRegistrationModel
{
    public string Name { get; set; } = string.Empty;
}

public sealed class EditSessionGroupSimpleRegistrationTests
{
    [Test]
    public void SimpleRegistrationKeepsSnapshotsAndRejectsStaleAcknowledgements()
    {
        var model = new GroupSimpleRegistrationModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var handle = group.Register("profile", session);

        session.Observable.Name = "first";
        var older = group.Prepare();
        session.Observable.Name = "second";
        var newer = group.Prepare();

        older.Get(handle).Changes.Name.After.Value.ShouldBe("first");
        older.ChangedPaths.ShouldHaveSingleItem().PathText.ShouldBe("Name");
        group.AcceptChanges(newer, handle);

        Should.Throw<EditSessionStaleSubmissionException>(() => group.AcceptChanges(older, handle));
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void SessionBackedRegistrationRetainsCapturedChangeValidation()
    {
        var model = new GroupSimpleRegistrationModel { Name = "before" };
        var session = model.CreateEditSession();
        var group = new EditSessionGroup();
        var registration = new EditSessionGroupRegistration<
            GroupSimpleRegistrationModel,
            GroupSimpleRegistrationModel.ChangeSet
        >(session)
        {
            ValidateChanges = static changes =>
                changes.Name.After.Value == "blocked" ? "The snapshot is not ready." : null,
        };
        group.Register("profile", registration);
        session.Observable.Name = "blocked";

        var failure = Should.Throw<EditSessionGroupValidationException>(() => group.Prepare());

        failure.Issues.ShouldHaveSingleItem().Message.ShouldBe("The snapshot is not ready.");
        session.HasChanges.ShouldBeTrue();
    }
}
