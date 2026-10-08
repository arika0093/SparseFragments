using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class NeutralSessionModel
{
    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public System.Collections.Generic.List<string> Tags { get; set; } = [];

    public System.Collections.Generic.Dictionary<string, int> Counts { get; set; } = [];

    public NeutralSessionChild Child { get; set; } = new();
}

[SparseFragmentModel]
public partial class NeutralSessionChild
{
    public string Value { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class ImmutableSessionModel
{
    public string Name { get; init; } = string.Empty;
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

    [Test]
    public void FragmentAndPatchWriteIntoExistingModelAndPreserveCollectionIdentity()
    {
        var patchModel = new NeutralSessionModel { Name = "before" };
        var patchTags = patchModel.Tags;
        new NeutralSessionModel.Patch { Name = "patched" }.ApplyInPlace(patchModel);
        patchModel.Name.ShouldBe("patched");
        ReferenceEquals(patchTags, patchModel.Tags).ShouldBeTrue();

        var fragmentModel = new NeutralSessionModel { Name = "before" };
        new NeutralSessionModel.Fragment { Name = "fragment" }.WriteTo(fragmentModel);
        fragmentModel.Name.ShouldBe("fragment");
    }

    [Test]
    public void ChangeSetRequiresExplicitPatchForInPlaceWhileTryApplyToStaysConflictAware()
    {
        var model = new NeutralSessionModel
        {
            Name = "before",
            Tags = ["one"],
            Counts = new() { ["first"] = 1 },
            Child = new NeutralSessionChild { Value = "old" },
        };
        var tags = model.Tags;
        var counts = model.Counts;
        var child = model.Child;
        var session = model.CreateEditSession();
        model.Name = "after";
        model.Tags.Add("two");
        model.Counts["second"] = 2;
        model.Child.Value = "new";

        // Blind overwrite stays explicit via ToPatch(); ChangeSet has no ApplyInPlace.
        session.CreateChangeSet().ToPatch().ApplyInPlace(model);

        model.Name.ShouldBe("after");
        model.Tags.ShouldBe(["one", "two"]);
        model.Counts["second"].ShouldBe(2);
        model.Child.Value.ShouldBe("new");
        ReferenceEquals(tags, model.Tags).ShouldBeTrue();
        ReferenceEquals(counts, model.Counts).ShouldBeTrue();
        ReferenceEquals(child, model.Child).ShouldBeFalse();

        var baseline = new NeutralSessionModel { Name = "before" };
        var edited = new NeutralSessionModel { Name = "edited" };
        var concurrent = new NeutralSessionModel { Name = "concurrent" };
        var conflicting = baseline.CreateChangeSet(edited);
        conflicting.TryApplyTo(concurrent, out _, out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        conflicts.Count.ShouldBeGreaterThan(0);
        concurrent.Name.ShouldBe("concurrent");
    }

    [Test]
    public async Task AcceptedSubmitWithoutServerStateKeepsLaterLocalEdits()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        model.Name = "sent";

        var result = await session.SubmitAsync((_, _) =>
        {
            model.Version = 2;
            return System.Threading.Tasks.Task.FromResult(
                SparseSubmitResponse<NeutralSessionModel>.Accepted()
            );
        });

        result.Status.ShouldBe(SparseSubmitStatus.Rebased);
        model.Name.ShouldBe("sent");
        model.Version.ShouldBe(2);
        session.CreateChangeSet().Name.IsChanged.ShouldBeFalse();
        session.CreateChangeSet().Version.IsChanged.ShouldBeTrue();
    }

    [Test]
    public async Task AcceptedServerStateIsAppliedWhileLaterDisjointEditsAreRebased()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        model.Name = "sent";

        var result = await session.SubmitAsync((_, _) =>
        {
            model.Version = 2;
            return System.Threading.Tasks.Task.FromResult(
                SparseSubmitResponse<NeutralSessionModel>.Accepted(
                    new NeutralSessionModel { Name = "normalized", Version = 1 }
                )
            );
        });

        result.Status.ShouldBe(SparseSubmitStatus.Rebased);
        model.Name.ShouldBe("normalized");
        model.Version.ShouldBe(2);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task AcceptedServerConflictDoesNotOverwriteLaterLocalEdit()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        model.Name = "sent";

        var result = await session.SubmitAsync((_, _) =>
        {
            model.Name = "later";
            return System.Threading.Tasks.Task.FromResult(
                SparseSubmitResponse<NeutralSessionModel>.Accepted(
                    new NeutralSessionModel { Name = "server" }
                )
            );
        });

        result.Status.ShouldBe(SparseSubmitStatus.Conflicted);
        result.Conflicts.Count.ShouldBeGreaterThan(0);
        model.Name.ShouldBe("later");
    }

    [Test]
    public async Task RejectedServerStateRebasesWholeLocalEdit()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        model.Name = "local";

        var result = await session.SubmitAsync((_, _) =>
            System.Threading.Tasks.Task.FromResult(
                SparseSubmitResponse<NeutralSessionModel>.Rejected(
                    new NeutralSessionModel { Name = "before", Version = 2 }
                )
            )
        );

        result.Status.ShouldBe(SparseSubmitStatus.Rebased);
        model.Name.ShouldBe("local");
        model.Version.ShouldBe(2);
    }

    [Test]
    public async Task FailedSubmitDoesNotChangeBaselineAndHandlesCannotBeReused()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        model.Name = "local";
        var pending = session.BeginSubmit();

        session.IsSubmitting.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => session.BeginSubmit());
        session.Complete(pending, SparseSubmitResponse<NeutralSessionModel>.Failed())
            .Status.ShouldBe(SparseSubmitStatus.Failed);
        session.IsSubmitting.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() =>
            session.Complete(pending, SparseSubmitResponse<NeutralSessionModel>.Accepted())
        );

        var stale = session.BeginSubmit();
        session.AcceptChanges();
        Should.Throw<InvalidOperationException>(() =>
            session.Complete(stale, SparseSubmitResponse<NeutralSessionModel>.Accepted())
        );
        await System.Threading.Tasks.Task.CompletedTask;
    }

    [Test]
    public async Task SendExceptionLeavesBaselineUnchangedAndRethrows()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        model.Name = "local";

        await Should.ThrowAsync<System.InvalidOperationException>(async () =>
            await session.SubmitAsync((_, _) =>
                throw new System.InvalidOperationException("transport failed")
            )
        );

        session.IsSubmitting.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Name.Before.Value.ShouldBe("before");
    }

    [Test]
    public void SessionPropertyChangedTracksObservableAndSubmitState()
    {
        var session = new NeutralSessionModel().CreateEditSession();
        var changed = new System.Collections.Generic.List<string?>();
        session.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        session.Observable.Name = "updated";
        changed.ShouldContain(nameof(session.HasChanges));
        var pending = session.BeginSubmit();
        changed.ShouldContain(nameof(session.IsSubmitting));
        session.Complete(pending, SparseSubmitResponse<NeutralSessionModel>.Accepted());
        changed.Count(name => name == nameof(session.IsSubmitting)).ShouldBe(2);
        changed.Count(name => name == nameof(session.HasChanges)).ShouldBeGreaterThan(1);
    }

    [Test]
    public async System.Threading.Tasks.Task InitOnlyModelKeepsEditSessionsButDoesNotSupportSubmit()
    {
        var session = new ImmutableSessionModel { Name = "immutable" }.CreateEditSession();

        session.HasChanges.ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => session.BeginSubmit());
        await Should.ThrowAsync<NotSupportedException>(() =>
            session.SubmitAsync((_, _) =>
                System.Threading.Tasks.Task.FromResult(
                    SparseSubmitResponse<ImmutableSessionModel>.Accepted()
                )
            )
        );
    }
}
