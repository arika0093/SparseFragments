using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Typed keyed <c>GetChange(TKey)</c> lookup and reorder observation (issue #94).</summary>
public sealed class KeyedGetChangeTests
{
    private static Optional<KeyedServerHolder.Fragment?> Keyed(params KeyedServer[] items) =>
        Optional<KeyedServerHolder.Fragment?>.Present(
            KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
        );

    private static KeyedServer S(string id, string? name = null, int count = 1) =>
        new() { Id = id, Name = name ?? id, Count = count };

    private static Optional<CompositeServerHolder.Fragment?> Composite(params CompositeServer[] items) =>
        Optional<CompositeServerHolder.Fragment?>.Present(
            CompositeServerHolder.Fragment.From(new CompositeServerHolder { Items = items.ToList() })
        );

    private static CompositeServer C(string tenant, string id, string? name = null) =>
        new() { TenantId = tenant, Id = id, Name = name ?? tenant + id };

    private static void AssertEmpty(KeyedServerHolder.ChangeSet.ItemsTransition.Item item)
    {
        item.ShouldNotBeNull();
        item.IsEmpty.ShouldBeTrue();
        item.IsChanged.ShouldBeFalse();
        item.IsAdded.ShouldBeFalse();
        item.IsRemoved.ShouldBeFalse();
        item.IsEdited.ShouldBeFalse();
        item.IsReordered.ShouldBeFalse();
        // Empty lookups retain no element snapshots.
        item.Before.IsPresent.ShouldBeFalse();
        item.After.IsPresent.ShouldBeFalse();
        item.Edit.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void UnchangedKeyReturnsEmptyNotNull()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b")),
            Keyed(S("a"), S("b", "b2"))
        );
        var item = changes.Items.GetChange("a");
        AssertEmpty(item);
    }

    [Test]
    public void UnknownKeyReturnsEmptyWithoutBaselineRetention()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a")),
            Keyed(S("a", "a2"))
        );
        var item = changes.Items.GetChange("absent");
        AssertEmpty(item);
    }

    [Test]
    public void AddedRemovedEditedLookup()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("b"), S("c")),
            Keyed(S("b", "b2"), S("d"))
        );
        var transition = changes.Items;

        var added = transition.GetChange("d");
        added.IsEmpty.ShouldBeFalse();
        added.IsAdded.ShouldBeTrue();
        added.IsRemoved.ShouldBeFalse();
        added.IsEdited.ShouldBeFalse();
        added.IsReordered.ShouldBeFalse();
        added.Before.IsPresent.ShouldBeFalse();
        added.After.Value!.Id.ShouldBe("d");
        added.BeforeIndex.ShouldBe(-1);
        added.AfterIndex.ShouldBe(1);

        var removed = transition.GetChange("c");
        removed.IsEmpty.ShouldBeFalse();
        removed.IsRemoved.ShouldBeTrue();
        removed.IsAdded.ShouldBeFalse();
        removed.Before.Value!.Id.ShouldBe("c");
        removed.After.IsPresent.ShouldBeFalse();
        removed.BeforeIndex.ShouldBe(1);
        removed.AfterIndex.ShouldBe(-1);

        var edited = transition.GetChange("b");
        edited.IsEmpty.ShouldBeFalse();
        edited.IsEdited.ShouldBeTrue();
        edited.IsAdded.ShouldBeFalse();
        edited.IsRemoved.ShouldBeFalse();
        edited.Edit.Name.IsChanged.ShouldBeTrue();
        edited.Edit.Name.After.Value.ShouldBe("b2");
    }

    [Test]
    public void CompositeKeyLookupUsesTupleKey()
    {
        var changes = CompositeServerHolder.ChangeSet.Between(
            Composite(C("t1", "a")),
            Composite(C("t1", "a", "A2"), C("t2", "a"))
        );
        var transition = changes.Items;

        var edited = transition.GetChange(("t1", "a"));
        edited.IsEmpty.ShouldBeFalse();
        edited.IsEdited.ShouldBeTrue();

        var added = transition.GetChange(("t2", "a"));
        added.IsEmpty.ShouldBeFalse();
        added.IsAdded.ShouldBeTrue();
        added.After.Value!.TenantId.ShouldBe("t2");

        // Same Id in another tenant is a distinct key.
        var unknown = transition.GetChange(("t9", "a"));
        unknown.IsEmpty.ShouldBeTrue();
        unknown.IsAdded.ShouldBeFalse();
    }

    [Test]
    public void InterfaceKeyLookupUsesComparerSemantics()
    {
        Optional<SkApiCluster.Fragment?> State(SkApiCluster m) =>
            Optional<SkApiCluster.Fragment?>.Present(SkApiCluster.Fragment.From(m));
        var changes = SkApiCluster.ChangeSet.Between(
            State(new SkApiCluster { Ifaces = [new() { Tenant = "acme", Id = 1, Name = "A" }] }),
            State(new SkApiCluster { Ifaces = [new() { Tenant = "acme", Id = 1, Name = "A2" }] })
        );
        var transition = changes.Ifaces;

        // Keys are normalized (case-folded) by the element's SparseKey getter.
        var edited = transition.GetChange(new SkApiIfaceKey("ACME", 1));
        edited.IsEmpty.ShouldBeFalse();
        edited.IsEdited.ShouldBeTrue();

        // Non-normalized casing is a different key under the comparer.
        var missed = transition.GetChange(new SkApiIfaceKey("acme", 1));
        missed.IsEmpty.ShouldBeTrue();

        var unknown = transition.GetChange(new SkApiIfaceKey("ACME", 2));
        unknown.IsEmpty.ShouldBeTrue();
        unknown.IsEdited.ShouldBeFalse();
    }

    [Test]
    public void RepeatedEmptyLookupReturnsSharedSingleton()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a")),
            Keyed(S("a", "a2"))
        );
        var transition = changes.Items;

        var first = transition.GetChange("unknown");
        var second = transition.GetChange("unknown");
        ReferenceEquals(first, second).ShouldBeTrue();

        // Distinct unknown keys share the same allocation-light singleton.
        var other = transition.GetChange("other");
        ReferenceEquals(first, other).ShouldBeTrue();
        ReferenceEquals(first.Edit, other.Edit).ShouldBeTrue();

        // Repeated empty lookups on a warmed-up transition allocate nothing.
        _ = transition.GetChange("unknown");
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            _ = transition.GetChange("unknown");
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        (allocatedAfter - allocatedBefore).ShouldBe(0);
    }

    [Test]
    public void LookupMatchesEnumerationForNonEmptyChanges()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b"), S("c")),
            Keyed(S("b"), S("a"), S("d"))
        );
        var transition = changes.Items;

        var enumerated = transition.ToList();
        enumerated.ShouldNotBeEmpty();
        foreach (var item in enumerated)
        {
            item.IsEmpty.ShouldBeFalse();
            ReferenceEquals(item, transition.GetChange(item.Key)).ShouldBeTrue(
                $"lookup must return the enumerated instance for key {item.Key}"
            );
        }

        // Removed keys are non-empty changes and appear in enumeration.
        enumerated.Select(i => i.Key).ShouldBe(["b", "a", "d", "c"]);
        transition.GetChange("c").IsRemoved.ShouldBeTrue();

        // Truly unchanged keys never appear in enumeration.
        var quiet = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b"), S("c")),
            Keyed(S("b"), S("a"), S("c"))
        );
        quiet.Items.Select(i => i.Key).ShouldBe(["b", "a"]);
        AssertEmpty(quiet.Items.GetChange("c"));
    }

    [Test]
    public void PureRemovalDoesNotReorderSurvivors()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b"), S("c")),
            Keyed(S("b"), S("c"))
        );
        var transition = changes.Items;
        transition.OrderChanged.ShouldBeTrue();

        // Membership-only shifts leave survivors without semantic change.
        AssertEmpty(transition.GetChange("b"));
        AssertEmpty(transition.GetChange("c"));

        var removed = transition.GetChange("a");
        removed.IsRemoved.ShouldBeTrue();
        removed.IsReordered.ShouldBeFalse();
    }

    [Test]
    public void AbsoluteIndexesShiftWithoutReorderForEditedSurvivor()
    {
        // Removal of "a" shifts "b" from absolute index 1 to 0, but the
        // surviving relative rank ([b, c] on both sides) is unchanged.
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b"), S("c")),
            Keyed(S("b", "b2"), S("c"))
        );
        var transition = changes.Items;

        var b = transition.GetChange("b");
        b.IsEmpty.ShouldBeFalse();
        b.IsEdited.ShouldBeTrue();
        b.IsReordered.ShouldBeFalse();
        b.BeforeIndex.ShouldBe(1);
        b.AfterIndex.ShouldBe(0);
    }

    [Test]
    public void PureAdditionDoesNotReorderSurvivors()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b")),
            Keyed(S("a"), S("b"), S("c"))
        );
        var transition = changes.Items;

        // Pure-addition survivors carry no semantic change.
        AssertEmpty(transition.GetChange("a"));
        AssertEmpty(transition.GetChange("b"));

        var added = transition.GetChange("c");
        added.IsAdded.ShouldBeTrue();
        added.IsReordered.ShouldBeFalse();
    }

    [Test]
    public void SwapMarksAffectedSurvivorsReordered()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b"), S("c")),
            Keyed(S("b"), S("a"), S("c"))
        );
        var transition = changes.Items;

        var a = transition.GetChange("a");
        a.IsReordered.ShouldBeTrue();
        a.BeforeIndex.ShouldBe(0);
        a.AfterIndex.ShouldBe(1);

        var b = transition.GetChange("b");
        b.IsReordered.ShouldBeTrue();
        b.BeforeIndex.ShouldBe(1);
        b.AfterIndex.ShouldBe(0);

        // Unaffected survivor has no semantic change: empty, not enumerated.
        AssertEmpty(transition.GetChange("c"));
    }

    [Test]
    public void ReorderPlusAddRemovePreservesBothSemantics()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a"), S("b"), S("c")),
            Keyed(S("c"), S("b"), S("d"))
        );
        var transition = changes.Items;

        // Surviving rank: before [b, c], after [c, b] -> both reordered.
        transition.GetChange("b").IsReordered.ShouldBeTrue();
        transition.GetChange("c").IsReordered.ShouldBeTrue();

        var added = transition.GetChange("d");
        added.IsAdded.ShouldBeTrue();
        added.IsReordered.ShouldBeFalse();

        var removed = transition.GetChange("a");
        removed.IsRemoved.ShouldBeTrue();
        removed.IsReordered.ShouldBeFalse();
    }

    [Test]
    public void AddedRemovedItemsAreNeverReordered()
    {
        var changes = KeyedServerHolder.ChangeSet.Between(
            Keyed(S("a")),
            Keyed(S("b"))
        );
        changes.Items.GetChange("b").IsReordered.ShouldBeFalse();
        changes.Items.GetChange("a").IsReordered.ShouldBeFalse();
    }

    [Test]
    public void DictionaryGetChangeLookup()
    {
        Optional<ScalarDictHolder.Fragment?> State(ScalarDictHolder m) =>
            Optional<ScalarDictHolder.Fragment?>.Present(ScalarDictHolder.Fragment.From(m));
        var changes = ScalarDictHolder.ChangeSet.Between(
            State(new ScalarDictHolder { Scores = new() { ["a"] = 1, ["b"] = 2 } }),
            State(new ScalarDictHolder { Scores = new() { ["b"] = 3, ["c"] = 4 } })
        );
        var scores = changes.Scores;

        var edited = scores.GetChange("b");
        edited.IsEmpty.ShouldBeFalse();
        edited.IsEdited.ShouldBeTrue();
        edited.Before.Value.ShouldBe(2);
        edited.After.Value.ShouldBe(3);

        var added = scores.GetChange("c");
        added.IsAdded.ShouldBeTrue();
        added.IsEmpty.ShouldBeFalse();

        var removed = scores.GetChange("a");
        removed.IsRemoved.ShouldBeTrue();
        removed.IsEmpty.ShouldBeFalse();

        var unknown = scores.GetChange("zzz");
        unknown.ShouldNotBeNull();
        unknown.IsEmpty.ShouldBeTrue();
        unknown.IsAdded.ShouldBeFalse();
        unknown.IsRemoved.ShouldBeFalse();
        unknown.IsEdited.ShouldBeFalse();

        var again = scores.GetChange("zzz");
        ReferenceEquals(unknown, again).ShouldBeTrue();
    }

    [Test]
    public void StructuralDictionaryGetChangeLookup()
    {
        Optional<StructuralDictHolder.Fragment?> State(StructuralDictHolder m) =>
            Optional<StructuralDictHolder.Fragment?>.Present(StructuralDictHolder.Fragment.From(m));
        var changes = StructuralDictHolder.ChangeSet.Between(
            State(new StructuralDictHolder { Servers = new() { ["web"] = new KeyedServer { Id = "s1", Name = "Old" } } }),
            State(new StructuralDictHolder { Servers = new() { ["web"] = new KeyedServer { Id = "s1", Name = "New" }, ["db"] = new KeyedServer { Id = "s2", Name = "Db" } } })
        );

        var edited = changes.Servers.GetChange("web");
        edited.IsEmpty.ShouldBeFalse();
        edited.IsEdited.ShouldBeTrue();
        edited.Edit.Name.After.Value.ShouldBe("New");

        changes.Servers.GetChange("db").IsAdded.ShouldBeTrue();

        var missing = changes.Servers.GetChange("missing");
        missing.IsEmpty.ShouldBeTrue();
        missing.Edit.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void GetChangeSurvivesJsonRoundTrip()
    {
        var before = Keyed(S("a"), S("b"));
        var after = Keyed(S("a", "a2"), S("c"));
        var changes = KeyedServerHolder.ChangeSet.Between(before, after);

        var json = System.Text.Json.JsonSerializer.Serialize(changes.ToPayload());
        var back = System.Text.Json.JsonSerializer
            .Deserialize<KeyedServerHolder.ChangeSetPayload>(json)!
            .ToChangeSet();

        back.Items.GetChange("a").IsEdited.ShouldBeTrue();
        back.Items.GetChange("a").Edit.Name.After.Value.ShouldBe("a2");
        back.Items.GetChange("c").IsAdded.ShouldBeTrue();
        back.Items.GetChange("b").IsRemoved.ShouldBeTrue();
        back.Items.GetChange("unknown").IsEmpty.ShouldBeTrue();
        KeyedServerHolder.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }
}
