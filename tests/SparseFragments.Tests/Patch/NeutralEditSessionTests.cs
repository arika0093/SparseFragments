using SparseFragments;
using SparseFragments.__GeneratedSessionCore;

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
public partial class NeutralSessionAtomicPocoModel
{
    [SparseMerge(MergeMode.Replace)]
    public NeutralSessionAtomicPoco? Atomic { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public List<NeutralSessionAtomicPoco?> Items { get; set; } = [];
}

public sealed class NeutralSessionAtomicPoco
{
    public string Label { get; set; } = string.Empty;

    public NeutralSessionAtomicPoco? Nested { get; set; }

    public string __model { get; set; } = string.Empty;

    public string __SparseReadOnlyCollection { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class NeutralSessionUnconstructablePocoModel
{
    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public NeutralSessionUnconstructablePoco? Atomic { get; set; }

    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public Dictionary<NeutralSessionUnconstructablePoco, string> ByKey { get; set; } = [];

    [SparseMerge(MergeMode.Replace)]
    [SparseCloneReferenceSafe]
    public object? Opaque { get; set; }
}

public sealed class NeutralSessionUnconstructablePoco
{
    private NeutralSessionUnconstructablePoco(
        string label,
        NeutralSessionUnconstructablePoco? nested
    )
    {
        Label = label;
        Nested = nested;
    }

    public string Label { get; }

    public NeutralSessionUnconstructablePoco? Nested { get; }

    public static NeutralSessionUnconstructablePoco Create(
        string label,
        NeutralSessionUnconstructablePoco? nested = null
    ) => new(label, nested);
}

[SparseFragmentModel]
public partial class ImmutableSessionModel
{
    public string Name { get; init; } = string.Empty;
}

[SparseFragmentModel]
public partial class DuplicateKeySessionItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class DuplicateKeySessionModel
{
    public List<DuplicateKeySessionItem> Items { get; set; } = [];
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
        var session = EditSessionCore<
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
    public void ChangeSetInPlaceApplicationChecksBeforeStateAndPreservesModelIdentity()
    {
        var baseline = new NeutralSessionModel
        {
            Name = "before",
            Tags = ["one"],
            Counts = new() { ["first"] = 1 },
            Child = new NeutralSessionChild { Value = "old" },
        };
        var edited = new NeutralSessionModel
        {
            Name = "after",
            Tags = ["one", "two"],
            Counts = new() { ["first"] = 1, ["second"] = 2 },
            Child = new NeutralSessionChild { Value = "new" },
        };
        var changes = baseline.CreateChangeSet(edited);
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

        changes.TryApplyInPlace(model, out var applyConflicts).ShouldBeTrue();
        applyConflicts.ShouldBeNull();

        model.Name.ShouldBe("after");
        model.Tags.ShouldBe(["one", "two"]);
        model.Counts["second"].ShouldBe(2);
        model.Child.Value.ShouldBe("new");
        ReferenceEquals(tags, model.Tags).ShouldBeTrue();
        ReferenceEquals(counts, model.Counts).ShouldBeTrue();
        ReferenceEquals(child, model.Child).ShouldBeFalse();

        var conflicting = baseline.CreateChangeSet(
            new NeutralSessionModel
            {
                Name = "edited",
                Tags = ["one"],
                Counts = new() { ["first"] = 1 },
                Child = new NeutralSessionChild { Value = "old" },
            }
        );
        var concurrent = new NeutralSessionModel
        {
            Name = "concurrent",
            Tags = ["one"],
            Counts = new() { ["first"] = 1 },
            Child = new NeutralSessionChild { Value = "old" },
        };
        conflicting.TryApplyInPlace(concurrent, out var conflicts).ShouldBeFalse();
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
    public void HasChangesReportsDuplicateKeysWithoutHidingOtherInvalidOperations()
    {
        var model = new DuplicateKeySessionModel
        {
            Items = [new DuplicateKeySessionItem { Id = "same" }],
        };
        var session = model.CreateEditSession();
        model.Items.Add(new DuplicateKeySessionItem { Id = "same" });

        session.HasChanges.ShouldBeTrue();
        Should
            .Throw<InvalidOperationException>(() => session.CreateChangeSet())
            .Message.ShouldBe("Duplicate key in keyed collection.");

        var unrelated = EditSessionCore<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralSessionModel.Observable
        >.Create(
            new NeutralSessionModel(),
            NeutralSessionModel.Fragment.From,
            static (_, _) => throw new InvalidOperationException("Unrelated failure."),
            static changes => changes.ToPatch(),
            static changes => changes.IsEmpty,
            static (_, baseline) => baseline,
            static current => new NeutralSessionModel.Observable(current)
        );

        Should
            .Throw<InvalidOperationException>(() => _ = unrelated.HasChanges)
            .Message.ShouldBe("Unrelated failure.");

        var isEmptyFailure = EditSessionCore<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralSessionModel.Observable
        >.Create(
            new NeutralSessionModel(),
            NeutralSessionModel.Fragment.From,
            NeutralSessionModel.ChangeSet.Between,
            static changes => changes.ToPatch(),
            static _ => throw new InvalidOperationException("Duplicate key in keyed collection."),
            static (_, baseline) => baseline,
            static current => new NeutralSessionModel.Observable(current)
        );

        Should
            .Throw<InvalidOperationException>(() => _ = isEmptyFailure.HasChanges)
            .Message.ShouldBe("Duplicate key in keyed collection.");
    }

    [Test]
    public void ObservableDuplicateKeyEditLeavesTheSessionRecoverable()
    {
        var session = new DuplicateKeySessionModel
        {
            Items = [new DuplicateKeySessionItem { Id = "same" }],
        }.CreateEditSession();

        session.Observable.Items.Add(
            new DuplicateKeySessionItem.Observable(new DuplicateKeySessionItem { Id = "same" })
        );

        session.HasChanges.ShouldBeTrue();
        session.Observable.Items.RemoveAt(1);
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void CurrentIsRecursiveReadOnlyAndChangesExposeNestedPaths()
    {
        var session = new NeutralSessionModel().CreateEditSession();
        IReadOnlyList<string> tags = session.Current.Tags;
        tags.ShouldBeEmpty();
        (tags is IList<string>).ShouldBeFalse();
        IReadOnlyDictionary<string, int> counts = session.Current.Counts;
        (counts is IDictionary<string, int>).ShouldBeFalse();
        (counts.Keys is ICollection<string>).ShouldBeFalse();
        session.Current.Name.ShouldBe(string.Empty);
        session.Current.Child.Value.ShouldBe(string.Empty);

        var notifications = new List<NeutralSessionModel.ChangeSet>();
        session.ChangeSetChanged += notifications.Add;
        session.Observable.Name = "updated";
        session.Observable.Child!.Value = "nested";

        notifications.Count.ShouldBe(2);
        notifications[0].Name.IsChanged.ShouldBeTrue();
        notifications[0].Child.IsEmpty.ShouldBeTrue();
        notifications[1].Name.IsChanged.ShouldBeFalse();
        notifications[1].Child.Value.IsChanged.ShouldBeTrue();
        session.EnumerateChangedPaths().ShouldBe(["Child.Value", "Name"]);
        session.Current.Name.ShouldBe("updated");
        session.Current.Child.Value.ShouldBe("nested");
    }

    [Test]
    public void CurrentWrapsAtomicPocoValuesAndCollectionElements()
    {
        var model = new NeutralSessionAtomicPocoModel
        {
            Atomic = new NeutralSessionAtomicPoco
            {
                Label = "atomic",
                Nested = new NeutralSessionAtomicPoco { Label = "nested" },
                __model = "poco backing-name collision",
                __SparseReadOnlyCollection = "adapter-name collision",
            },
            Items = [new NeutralSessionAtomicPoco { Label = "item" }, null],
        };
        var session = model.CreateEditSession();
        var atomic = session.Current.Atomic!.Value;
        var nested = atomic.Nested!.Value;
        var item = session.Current.Items[0]!.Value;

        atomic.Label.ShouldBe("atomic");
        atomic.__model.ShouldBe("poco backing-name collision");
        atomic.__SparseReadOnlyCollection.ShouldBe("adapter-name collision");
        nested.Label.ShouldBe("nested");
        item.Label.ShouldBe("item");
        session.Current.Items[1].ShouldBeNull();
        atomic.GetType().ShouldNotBe(typeof(NeutralSessionAtomicPoco));
        nested.GetType().ShouldNotBe(typeof(NeutralSessionAtomicPoco));
        item.GetType().ShouldNotBe(typeof(NeutralSessionAtomicPoco));

        model.Atomic!.Label = "updated";
        atomic.Label.ShouldBe("updated");
        model.Atomic = null;
        session.Current.Atomic.ShouldBeNull();
    }

    [Test]
    public void CurrentWrapsUnconstructableNestedPocosAndDictionaryKeys()
    {
        var atomic = NeutralSessionUnconstructablePoco.Create(
            "atomic",
            NeutralSessionUnconstructablePoco.Create("nested")
        );
        var key = NeutralSessionUnconstructablePoco.Create("key");
        var model = new NeutralSessionUnconstructablePocoModel
        {
            Atomic = atomic,
            ByKey = new Dictionary<NeutralSessionUnconstructablePoco, string> { [key] = "value" },
            Opaque = atomic,
        };
        var session = model.CreateEditSession();
        var atomicView = session.Current.Atomic!.Value;
        var nestedView = atomicView.Nested!.Value;
        var entry = session.Current.ByKey.Single();
        var opaque = session.Current.Opaque!.Value;

        atomicView.Label.ShouldBe("atomic");
        nestedView.Label.ShouldBe("nested");
        entry.Key.Label.ShouldBe("key");
        entry.Value.ShouldBe("value");
        atomicView.GetType().ShouldNotBe(typeof(NeutralSessionUnconstructablePoco));
        nestedView.GetType().ShouldNotBe(typeof(NeutralSessionUnconstructablePoco));
        entry.Key.GetType().ShouldNotBe(typeof(NeutralSessionUnconstructablePoco));
        opaque.HasValue.ShouldBeTrue();
        opaque.GetType().ShouldNotBe(typeof(NeutralSessionUnconstructablePoco));
    }

    [Test]
    public void HasChangesCachesObservableEditsUntilTheRawModelIsExposed()
    {
        var model = new NeutralSessionModel();
        var snapshots = 0;
        var configuration = new EditSessionCoreConfiguration<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralSessionModel.Observable,
            NeutralSessionModel.ReadOnlyView
        >
        {
            FromModel = current =>
            {
                snapshots++;
                return NeutralSessionModel.Fragment.From(current);
            },
            Between = NeutralSessionModel.ChangeSet.Between,
            ToPatch = static changes => changes.ToPatch(),
            IsEmpty = static changes => changes.IsEmpty,
            AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline),
            ToObservable = static (current, changed, access) =>
                new NeutralSessionModel.Observable(current, changed, access),
            ToCurrent = static current => new NeutralSessionModel.ReadOnlyView(current),
            EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(),
            RefreshObservable = static observable => observable.__SparseRefresh(),
        };
        var session = EditSessionCore<
            NeutralSessionModel,
            NeutralSessionModel.Fragment,
            NeutralSessionModel.Patch,
            NeutralSessionModel.ChangeSet,
            NeutralSessionModel.Observable,
            NeutralSessionModel.ReadOnlyView
        >.Create(model, configuration);
        session.Observable.Name = "observable edit";
        var afterNotification = snapshots;

        session.HasChanges.ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(afterNotification);

        _ = session.Model;
        session.HasChanges.ShouldBeTrue();
        snapshots.ShouldBe(afterNotification + 1);
    }

    [Test]
    public void RetainedRawModelReferenceCannotLeaveHasChangesCacheStale()
    {
        var model = new NeutralSessionModel { Name = "before" };
        var session = model.CreateEditSession();
        var rawModel = session.Model;

        session.Observable.Name = "observable edit";
        session.HasChanges.ShouldBeTrue();
        session.Observable.Name = "before";
        session.HasChanges.ShouldBeFalse();

        rawModel.Version = 2;

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().Version.After.Value.ShouldBe(2);
    }

    [Test]
    public void RevertChangesRestoresBaselineInPlace()
    {
        var model = new NeutralSessionModel
        {
            Name = "before",
            Tags = ["one"],
            Child = new NeutralSessionChild { Value = "old" },
        };
        var tags = model.Tags;
        var session = model.CreateEditSession();
        session.Observable.Name = "after";
        session.Observable.Tags.Add("two");
        session.Observable.Child!.Value = "new";

        session.RevertChanges();

        session.HasChanges.ShouldBeFalse();
        model.Name.ShouldBe("before");
        model.Tags.ShouldBe(["one"]);
        model.Child.Value.ShouldBe("old");
        ReferenceEquals(tags, model.Tags).ShouldBeTrue();
    }

    [Test]
    public void ReloadRebasesPendingChangesAtomically()
    {
        var original = new NeutralSessionModel { Name = "base", Version = 1 };
        var session = original.CreateEditSession();
        session.Observable.Name = "local";
        var server = new NeutralSessionModel { Name = "base", Version = 2 };

        var result = session.Reload(server);

        result.HasConflicts.ShouldBeFalse();
        ReferenceEquals(session.Model, original).ShouldBeTrue();
        session.Model.Name.ShouldBe("local");
        session.Model.Version.ShouldBe(2);
        session.HasChanges.ShouldBeTrue();
        var pending = session.CreateChangeSet();
        pending.Name.Before.Value.ShouldBe("base");
        pending.Name.After.Value.ShouldBe("local");
        pending.Version.IsChanged.ShouldBeFalse();
    }

    [Test]
    public void ReloadConflictLeavesModelAndBaselineUntouched()
    {
        var original = new NeutralSessionModel { Name = "base", Version = 1 };
        var session = original.CreateEditSession();
        session.Observable.Name = "local";
        var server = new NeutralSessionModel { Name = "server", Version = 2 };

        var result = session.Reload(server);

        result.HasConflicts.ShouldBeTrue();
        result.Conflicts.ShouldContain(conflict =>
            conflict.PathText == nameof(NeutralSessionModel.Name)
        );
        ReferenceEquals(session.Model, original).ShouldBeTrue();
        session.Model.Name.ShouldBe("local");
        session.Model.Version.ShouldBe(1);
        var stillPending = session.CreateChangeSet();
        stillPending.Name.Before.Value.ShouldBe("base");
        stillPending.Name.After.Value.ShouldBe("local");
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
