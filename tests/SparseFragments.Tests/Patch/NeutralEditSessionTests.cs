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
    public void HasChangesPropagatesUnrelatedInvalidOperationExceptions()
    {
        var session = SparseEditSession<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            object
        >.Create(
            new NeutralSessionModel(),
            NeutralSessionModel.Fragment.From,
            (_, _) => throw new InvalidOperationException("Unrelated failure."),
            _ => new NeutralSessionModel.Patch(),
            _ => false,
            (_, baseline) => baseline,
            _ => new object()
        );

        Should
            .Throw<InvalidOperationException>(() => session.HasChanges)
            .Message.ShouldBe("Unrelated failure.");
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
    public void AcceptedSubmittedChangesAdvanceOnlyBaselineKeepsLaterLiveEdits()
    {
        var model = new NeutralSessionModel { Name = "before", Version = 1 };
        var session = model.CreateEditSession();
        var observable = session.Observable;
        var tags = model.Tags;
        model.Name = "sent";
        var submitted = session.CreateChangeSet();

        model.Name = "later";
        model.Version = 2;
        session.AcceptChanges(submitted);

        // Live model and proxy identities are untouched; only the baseline moves.
        ReferenceEquals(session.Model, model).ShouldBeTrue();
        ReferenceEquals(session.Observable, observable).ShouldBeTrue();
        ReferenceEquals(model.Tags, tags).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        var next = session.CreateChangeSet();
        next.Name.Before.Value.ShouldBe("sent");
        next.Name.After.Value.ShouldBe("later");
        next.Version.Before.Value.ShouldBe(1);
        next.Version.After.Value.ShouldBe(2);
    }

    [Test]
    public void AcceptedSubmittedChangesWithUnchangedLiveBecomesClean()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        model.Name = "sent";
        var submitted = session.CreateChangeSet();

        session.AcceptChanges(submitted);

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        session.CreatePatch().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void LaterSameFieldEditRemainsPendingAfterAccept()
    {
        var model = new NeutralSessionModel { Name = "a" };
        var session = model.CreateEditSession();
        model.Name = "b";
        var submitted = session.CreateChangeSet();
        model.Name = "c";

        session.AcceptChanges(submitted);

        var next = session.CreateChangeSet();
        next.Name.IsChanged.ShouldBeTrue();
        next.Name.Before.Value.ShouldBe("b");
        next.Name.After.Value.ShouldBe("c");
    }

    [Test]
    public void NestedDictionaryAndListAdvanceOnlyBaseline()
    {
        var model = new NeutralSessionModel
        {
            Name = "before",
            Tags = ["one"],
            Counts = new() { ["first"] = 1 },
            Child = new NeutralSessionChild { Value = "old" },
        };
        var session = model.CreateEditSession();
        var tags = model.Tags;
        var counts = model.Counts;
        model.Child.Value = "new";
        model.Tags.Add("two");
        model.Counts["second"] = 2;
        var submitted = session.CreateChangeSet();

        model.Child.Value = "later";
        model.Tags.Add("three");
        model.Counts["third"] = 3;
        session.AcceptChanges(submitted);

        ReferenceEquals(model.Tags, tags).ShouldBeTrue();
        ReferenceEquals(model.Counts, counts).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        var next = session.CreateChangeSet();
        next.Child.Value.Before.Value.ShouldBe("new");
        next.Child.Value.After.Value.ShouldBe("later");
        next.Tags.Before.Value.ShouldBe(["one", "two"]);
        next.Tags.After.Value.ShouldBe(["one", "two", "three"]);
        next.Counts.Added.ContainsKey("third").ShouldBeTrue();
        next.Counts.Added.ContainsKey("second").ShouldBeFalse();
    }

    [Test]
    public void EmptyChangeSetAcceptLeavesBaselineUnchanged()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        model.Name = "edited";
        session.HasChanges.ShouldBeTrue();
        model.Name = "before";
        var empty = session.CreateChangeSet();
        empty.IsEmpty.ShouldBeTrue();

        session.AcceptChanges(empty);

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void StaleChangeSetIsRejectedWithoutPartialWrites()
    {
        var model = new NeutralSessionModel { Name = "a" };
        var session = model.CreateEditSession();
        model.Name = "b";
        var stale = session.CreateChangeSet();
        model.Name = "c";
        session.AcceptChanges();

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(stale));

        // Baseline still reflects the no-arg accept; the live model is clean.
        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        model.Name.ShouldBe("c");
    }

    [Test]
    public void IncompatibleChangeSetFromAnotherBaselineIsRejected()
    {
        var model = new NeutralSessionModel { Name = "a" };
        var session = model.CreateEditSession();
        model.Name = "b";
        var foreign = new NeutralSessionModel { Name = "other" }.CreateChangeSet(
            new NeutralSessionModel { Name = "changed" }
        );

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(foreign));

        session.HasChanges.ShouldBeTrue();
        var current = session.CreateChangeSet();
        current.Name.Before.Value.ShouldBe("a");
        current.Name.After.Value.ShouldBe("b");
    }

    [Test]
    public void DoubleAcceptOfSameChangeSetIsRejected()
    {
        var model = new NeutralSessionModel { Name = "a" };
        var session = model.CreateEditSession();
        model.Name = "b";
        var submitted = session.CreateChangeSet();
        session.AcceptChanges(submitted);

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(submitted));

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NullChangeSetIsRejected()
    {
        var session = new NeutralSessionModel().CreateEditSession();

        Should.Throw<ArgumentNullException>(() => session.AcceptChanges(null!));
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void RepeatedCreateAcceptEditCyclesWithChangeSets()
    {
        var session = new NeutralSessionModel { Name = "n0" }.CreateEditSession();

        for (var i = 1; i <= 3; i++)
        {
            session.Model.Name = "n" + i;
            var submitted = session.CreateChangeSet();
            submitted.IsEmpty.ShouldBeFalse();
            session.AcceptChanges(submitted);
            session.HasChanges.ShouldBeFalse();
            session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void SessionPropertyChangedTracksObservableAndAcceptChanges()
    {
        var session = new NeutralSessionModel().CreateEditSession();
        var changed = new System.Collections.Generic.List<string?>();
        session.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        session.Observable.Name = "updated";
        changed.ShouldContain(nameof(session.HasChanges));
        var submitted = session.CreateChangeSet();
        session.AcceptChanges(submitted);
        changed.Count(name => name == nameof(session.HasChanges)).ShouldBeGreaterThan(1);
    }

    [Test]
    public void InitOnlyModelSupportsBaselineAcceptWithoutSubmit()
    {
        var baseline = new ImmutableSessionModel { Name = "before" };
        var current = new ImmutableSessionModel { Name = "after" };
        var session = baseline.CreateEditSession(current);
        session.HasChanges.ShouldBeTrue();
        var submitted = session.CreateChangeSet();
        submitted.IsEmpty.ShouldBeFalse();

        session.AcceptChanges(submitted);

        // Snapshot-only sessions advance the retained baseline with no in-place write.
        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        ReferenceEquals(session.Model, current).ShouldBeTrue();
        current.Name.ShouldBe("after");

        // Empty transitions stay idempotent on snapshot-only sessions.
        session.AcceptChanges(session.CreateChangeSet());
        session.HasChanges.ShouldBeFalse();
    }
}
