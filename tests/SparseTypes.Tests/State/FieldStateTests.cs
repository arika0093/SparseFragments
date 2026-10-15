using SparseTypes;

namespace SparseTypes.Tests;

/// <summary>Path-first semantic field state over sessions and change sets.</summary>
public sealed class FieldStateTests
{
    [Test]
    public void CleanSessionReportsNoChanges()
    {
        var session = new Settings().CreateEditSession();

        var label = session.FieldState(Settings.SparsePath.Label);
        label.Path.ShouldBe(SparsePath.Root<Settings>().Member("Label"));
        label.IsChanged.ShouldBeFalse();
        label.Kind.ShouldBe(SparseFieldChangeKind.Unchanged);
        label.HasAncestorChange.ShouldBeFalse();
        label.HasDescendantChange.ShouldBeFalse();
        label.HasChanges.ShouldBeFalse();
        label.Before.IsPresent.ShouldBeFalse();
        label.After.IsPresent.ShouldBeFalse();

        session.FieldState(Settings.SparsePath.RetryCount).HasChanges.ShouldBeFalse();
        session.FieldState(Settings.SparsePath.Nested.Host).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ScalarChangeReportsPresenceAwareEndpoints()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;

        var state = session.FieldState(Settings.SparsePath.RetryCount);
        state.IsChanged.ShouldBeTrue();
        state.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        state.HasAncestorChange.ShouldBeFalse();
        state.HasDescendantChange.ShouldBeFalse();
        state.HasChanges.ShouldBeTrue();
        state.Before.ShouldBe(Optional<int>.Present(3));
        state.After.ShouldBe(Optional<int>.Present(4));

        // Sibling paths stay clean: no smeared change.
        session.FieldState(Settings.SparsePath.Enabled).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void NullablePresentNullStaysDistinctFromMissing()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Label = null;

        var nulled = session.FieldState(Settings.SparsePath.Label);
        nulled.IsChanged.ShouldBeTrue();
        nulled.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        nulled.Before.ShouldBe(Optional<string?>.Present("default"));
        nulled.After.IsPresent.ShouldBeTrue();
        nulled.After.Value.ShouldBeNull();

        // Absent nested member added: null nested members stay present-null,
        // so null-to-object reads as Changed with a present-null before.
        var holderModel = new Settings { Nested = null };
        var holder = holderModel.CreateEditSession();
        holderModel.Nested = new Nested { Host = "added" };

        var nested = holder.FieldState(SparsePath.Root<Settings>().Member("Nested"));
        nested.IsChanged.ShouldBeTrue();
        nested.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        nested.Before.IsPresent.ShouldBeTrue();
        nested.Before.Value.ShouldBeNull();
        nested.After.IsPresent.ShouldBeTrue();

        // No leaf records exist under a null-to-object replacement: leaves
        // report the ancestor change instead of an exact change.
        var addedLeaf = holder.FieldState(Settings.SparsePath.Nested.Host);
        addedLeaf.IsChanged.ShouldBeFalse();
        addedLeaf.HasAncestorChange.ShouldBeTrue();

        // Present nested member removed: present before, present-null after.
        var removed = new Settings().CreateEditSession();
        removed.Observable.Nested = null;
        var gone = removed.FieldState(SparsePath.Root<Settings>().Member("Nested"));
        gone.IsChanged.ShouldBeTrue();
        gone.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        gone.Before.IsPresent.ShouldBeTrue();
        gone.After.IsPresent.ShouldBeTrue();
        gone.After.Value.ShouldBeNull();
    }

    [Test]
    public void NestedLeafReportsAncestorChain()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Nested!.Host = "edited";

        var leaf = session.FieldState(Settings.SparsePath.Nested.Host);
        leaf.IsChanged.ShouldBeTrue();
        leaf.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        leaf.Before.Value.ShouldBe("localhost");
        leaf.After.Value.ShouldBe("edited");

        // The nested builder path has no typed form; the untyped query sees
        // descendants.
        var parent = session.FieldState(Settings.SparsePath.Nested);
        parent.IsChanged.ShouldBeFalse();
        parent.Kind.ShouldBe(SparseFieldChangeKind.Unchanged);
        parent.HasAncestorChange.ShouldBeFalse();
        parent.HasDescendantChange.ShouldBeTrue();
        parent.HasChanges.ShouldBeTrue();
        parent.Before.IsPresent.ShouldBeFalse();
        parent.After.IsPresent.ShouldBeFalse();

        // The root sees a descendant change but no exact change.
        var root = session.FieldState(SparsePath.Root<Settings>());
        root.IsChanged.ShouldBeFalse();
        root.HasDescendantChange.ShouldBeTrue();
    }

    [Test]
    public void ReplacedNestedInstanceReportsDescendantChanges()
    {
        var model = new Settings();
        var session = model.CreateEditSession();
        model.Nested = new Nested { Host = "replaced", Port = 1 };

        // Wholesale replacement still diffs granularly: leaves carry exact
        // changes.
        session.FieldState(Settings.SparsePath.Nested.Host).IsChanged.ShouldBeTrue();
        session.FieldState(Settings.SparsePath.Nested.Port).IsChanged.ShouldBeTrue();

        // The replaced parent reports descendants, never an exact change.
        var parent = session.FieldState(Settings.SparsePath.Nested);
        parent.IsChanged.ShouldBeFalse();
        parent.HasDescendantChange.ShouldBeTrue();
    }

    [Test]
    public void WholeListReportsMemberTransition()
    {
        var session = new ScalarSequenceHolder
        {
            Tags = ["a"],
            Numbers = [1],
        }.CreateEditSession();
        session.Observable.Tags.Add("b");

        var tags = session.FieldState(ScalarSequenceHolder.SparsePath.Tags);
        tags.IsChanged.ShouldBeTrue();
        tags.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        tags.Before.Value.ShouldBe(["a"]);
        tags.After.Value.ShouldBe(["a", "b"]);

        session.FieldState(ScalarSequenceHolder.SparsePath.Numbers).HasChanges.ShouldBeFalse();
    }

    [Test]
    public void DictionaryMemberReportsDescendantOnly()
    {
        var session = new ScalarDictHolder
        {
            Scores = new Dictionary<string, int> { ["a"] = 1 },
        }.CreateEditSession();
        session.Observable.Scores["a"] = 2;

        // Dictionaries enumerate per-entry records only: no member-level exact
        // change.
        var member = session.FieldState(ScalarDictHolder.SparsePath.Scores);
        member.IsChanged.ShouldBeFalse();
        member.HasDescendantChange.ShouldBeTrue();
        member.Before.IsPresent.ShouldBeFalse();

        var entry = session.FieldState(ScalarDictHolder.SparsePath.Scores.Key("a"));
        entry.IsChanged.ShouldBeTrue();
        entry.Kind.ShouldBe(SparseFieldChangeKind.Changed);
        entry.Before.Value.ShouldBe(1);
        entry.After.Value.ShouldBe(2);
    }

    [Test]
    public void EditThenRestoreIsClean()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Label = "dirty";
        session.FieldState(Settings.SparsePath.Label).IsChanged.ShouldBeTrue();

        session.Observable.Label = "default";

        // Restoration is semantic cleanliness, not touched tracking.
        var restored = session.FieldState(Settings.SparsePath.Label);
        restored.IsChanged.ShouldBeFalse();
        restored.HasChanges.ShouldBeFalse();
        session.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void UnknownAndForeignPathsReportClean()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.Label = "dirty";

        session
            .FieldState(SparsePath.Root<Settings>().Member("Missing"))
            .HasChanges.ShouldBeFalse();
        session
            .FieldState(SparsePath.Root<Nested>().Member("Host"))
            .HasChanges.ShouldBeFalse();
        session
            .FieldState(SparsePath.Root<Settings>().Member("Missing").Member("Deeper"))
            .HasChanges.ShouldBeFalse();
    }

    [Test]
    public void ChangeSetQueryIsPointInTime()
    {
        var session = new Settings().CreateEditSession();
        session.Observable.RetryCount = 4;
        var snapshot = session.CreateChangeSet();

        session.Observable.RetryCount = 5;

        // The snapshot keeps describing its own endpoints after later edits.
        var stale = snapshot.FieldState(Settings.SparsePath.RetryCount);
        stale.After.Value.ShouldBe(4);

        var live = session.CreateChangeSet().FieldState(Settings.SparsePath.RetryCount);
        live.After.Value.ShouldBe(5);
    }
}
