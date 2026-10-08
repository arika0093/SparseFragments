using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class KeyedServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class KeyedServerHolder
{
    public List<KeyedServer> Items { get; set; } = new();
}

[SparseKey("TenantId", "Id")]
[SparseFragmentModel]
public partial class CompositeServer
{
    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class CompositeServerHolder
{
    public List<CompositeServer> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class ServerGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<KeyedServer> Servers { get; set; } = new();
}

[SparseFragmentModel]
public partial class ClusterHolder
{
    public List<ServerGroup> Groups { get; set; } = new();
}

[SparseFragmentModel]
public partial class ScalarSequenceHolder
{
    public List<string> Tags { get; set; } = new();

    public int[] Numbers { get; set; } = [];
}

[SparseFragmentModel]
public partial class ScalarDictHolder
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

[SparseFragmentModel]
public partial class StructuralDictHolder
{
    public Dictionary<string, KeyedServer> Servers { get; set; } = new();
}

[SparseFragmentModel]
public partial class AssignedServer
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class AssignedServerHolder
{
    public List<AssignedServer> Items { get; set; } = new();
}

public sealed class KeyedCollectionPatchTests
{
    private static KeyedServer Server(string id, string name = "", int count = 0) =>
        new()
        {
            Id = id,
            Name = name,
            Count = count,
        };

    private static KeyedServerHolder Holder(params KeyedServer[] items) =>
        new() { Items = items.ToList() };

    private static Optional<KeyedServerHolder.Fragment?> State(KeyedServerHolder model) =>
        Optional<KeyedServerHolder.Fragment?>.Present(KeyedServerHolder.Fragment.From(model));

    [Test]
    public void AddRemoveEditRoundTripViaBetweenAndApply()
    {
        var before = State(Holder(Server("a", "A"), Server("b", "B")));
        var after = State(Holder(Server("b", "B2", 5), Server("c", "C")));

        var patch = KeyedServerHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();

        var applied = patch.Apply(before);
        KeyedServerHolder.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
        applied.Value!.Items.Value!.Select(s => s.Id).ShouldBe(["b", "c"]);
        applied.Value!.Items.Value!.Single(s => s.Id == "b").Name.ShouldBe("B2");
    }

    [Test]
    public void ManualAddRemoveEditApi()
    {
        var basis = State(Holder(Server("a", "A")));
        var patch = new KeyedServerHolder.Patch();
        patch.Items.Add(Server("b", "B"));
        patch.Items.Edit("a").Name = "A2";
        patch.IsEmpty.ShouldBeFalse();

        var applied = patch.Apply(basis);
        applied.Value!.Items.Value!.Select(s => s.Id).ShouldBe(["a", "b"]);
        applied.Value!.Items.Value!.Single(s => s.Id == "a").Name.ShouldBe("A2");

        var remover = new KeyedServerHolder.Patch();
        remover.Items.Remove("a");
        var removed = remover.Apply(applied);
        removed.Value!.Items.Value!.Select(s => s.Id).ShouldBe(["b"]);
    }

    [Test]
    public void ReorderUsesFinalKeySequence()
    {
        var before = State(Holder(Server("a"), Server("b"), Server("c")));
        var after = State(Holder(Server("c"), Server("a"), Server("b")));

        var patch = KeyedServerHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();

        var applied = patch.Apply(before);
        applied.Value!.Items.Value!.Select(s => s.Id).ShouldBe(["c", "a", "b"]);

        var changeSet = KeyedServerHolder.ChangeSet.Between(before, after);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(changeSet.ToPayload());
        using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
        document
            .RootElement.GetProperty("changes")[0]
            .GetProperty("afterOrder")
            .EnumerateArray()
            .Select(key => key.GetString())
            .ShouldBe(["c", "a", "b"]);
    }

    [Test]
    public void DuplicateKeysAreInvalid()
    {
        var dup = State(Holder(Server("a"), Server("a")));
        var clean = State(Holder(Server("a")));

        Should.Throw<InvalidOperationException>(() => KeyedServerHolder.Patch.Between(clean, dup));

        var patch = new KeyedServerHolder.Patch();
        patch.Items.Add(Server("x"));
        Should.Throw<InvalidOperationException>(() => patch.Items.Add(Server("x")));
    }

    [Test]
    public void UnassignedSentinelsAreIndependentAddsAndPreserveOrder()
    {
        Optional<AssignedServerHolder.Fragment?> F(params AssignedServer[] items) =>
            Optional<AssignedServerHolder.Fragment?>.Present(
                AssignedServerHolder.Fragment.From(
                    new AssignedServerHolder { Items = items.ToList() }
                )
            );

        var before = F(new AssignedServer { Id = 7, Name = "existing" });
        var after = F(
            new AssignedServer { Id = 0, Name = "first" },
            new AssignedServer { Id = 7, Name = "existing" },
            new AssignedServer { Id = 0, Name = "second" }
        );

        var patch = AssignedServerHolder.Patch.Between(before, after);
        var applied = patch.Apply(before);
        applied
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);
        var changes = AssignedServerHolder.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();
        changes
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);
        changes.Items.AfterOrder.ShouldBe([0, 7, 0]);
        changes.Items.Added.Select(item => item.Name).ShouldBe(["first", "second"]);
        changes.Items.GetChange(0).IsEmpty.ShouldBeTrue();
        var payloadRoundTrip = System
            .Text.Json.JsonSerializer.Deserialize<AssignedServerHolder.ChangeSetPayload>(
                System.Text.Json.JsonSerializer.Serialize(changes.ToPayload())
            )!
            .ToChangeSet();
        payloadRoundTrip
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(changes.ToPayload());
        using (var document = System.Text.Json.JsonDocument.Parse(payloadJson))
        {
            document
                .RootElement.GetProperty("changes")[0]
                .GetProperty("afterOrder")
                .EnumerateArray()
                .Select(key => key.GetInt32())
                .ShouldBe([0, 7, 0]);
        }

        var edited = F(new AssignedServer { Id = 7, Name = "updated" });
        var first = AssignedServerHolder.ChangeSet.Between(before, edited);
        var second = AssignedServerHolder.ChangeSet.Between(edited, after);
        first
            .Compose(second)
            .ToPatch()
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["first", "existing", "second"]);

        var manualFirst = new AssignedServerHolder.Patch();
        manualFirst.Items.Add(new AssignedServer { Name = "manual-first" });
        var manualSecond = new AssignedServerHolder.Patch();
        manualSecond.Items.Add(new AssignedServer { Name = "manual-second" });
        manualFirst
            .Compose(manualSecond)
            .Apply(before)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["existing", "manual-first", "manual-second"]);

        var concurrent = F(
            new AssignedServer { Id = 7, Name = "existing" },
            new AssignedServer { Id = 9, Name = "concurrent" }
        );
        var rebased = changes.RebaseOnto(concurrent);
        rebased.HasConflicts.ShouldBeFalse();
        rebased
            .Rebased.ToPatch()
            .Apply(concurrent)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["existing", "concurrent", "first", "second"]);
        var patchRebased = AssignedServerHolder.Patch.Rebase(before, patch, concurrent);
        patchRebased.HasConflicts.ShouldBeFalse();
        patchRebased
            .Rebased.Apply(concurrent)
            .Value!.Items.Value!.Select(item => item.Name)
            .ShouldBe(["existing", "concurrent", "first", "second"]);
    }

    [Test]
    public void UnassignedKeysAreRejectedInBaselineAndOrdinaryDuplicatesStillFail()
    {
        Optional<AssignedServerHolder.Fragment?> F(params AssignedServer[] items) =>
            Optional<AssignedServerHolder.Fragment?>.Present(
                AssignedServerHolder.Fragment.From(
                    new AssignedServerHolder { Items = items.ToList() }
                )
            );
        var invalidBaseline = F(new AssignedServer { Id = 0 });
        var valid = F(new AssignedServer { Id = 1 });
        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.Patch.Between(invalidBaseline, valid)
        );
        Should.Throw<InvalidOperationException>(() =>
            AssignedServerHolder.Patch.Between(
                valid,
                F(new AssignedServer { Id = 1 }, new AssignedServer { Id = 1 })
            )
        );
    }

    [Test]
    public void KeyMutationIsRemovePlusAdd()
    {
        var basis = State(Holder(Server("a", "A")));
        var patch = new KeyedServerHolder.Patch();
        patch.Items.Edit("a").Id = "b";

        Should.Throw<InvalidOperationException>(() => patch.Apply(basis));

        // Supported path: remove old, add new.
        var legal = new KeyedServerHolder.Patch();
        legal.Items.Remove("a");
        legal.Items.Add(Server("b", "A"));
        var applied = legal.Apply(basis);
        applied.Value!.Items.Value!.Select(s => s.Id).ShouldBe(["b"]);
    }

    [Test]
    public void CompositeKeysDistinguishTenants()
    {
        CompositeServer S(string tenant, string id, string name = "") =>
            new()
            {
                TenantId = tenant,
                Id = id,
                Name = name,
            };

        Optional<CompositeServerHolder.Fragment?> StateOf(CompositeServerHolder m) =>
            Optional<CompositeServerHolder.Fragment?>.Present(
                CompositeServerHolder.Fragment.From(m)
            );

        var before = StateOf(new CompositeServerHolder { Items = [S("t1", "a", "A")] });
        var after = StateOf(
            new CompositeServerHolder { Items = [S("t1", "a", "A"), S("t2", "a", "Other")] }
        );

        // Same Id in different tenants are distinct keys.
        var patch = CompositeServerHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        CompositeServerHolder.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
        applied.Value!.Items.Value!.Count.ShouldBe(2);
    }

    [Test]
    public void NestedKeyedCollectionsComposeAtDepth()
    {
        var before = Optional<ClusterHolder.Fragment?>.Present(
            ClusterHolder.Fragment.From(
                new ClusterHolder
                {
                    Groups = new()
                    {
                        new ServerGroup
                        {
                            Name = "g1",
                            Servers = new() { Server("a", "A") },
                        },
                    },
                }
            )
        );
        var after = Optional<ClusterHolder.Fragment?>.Present(
            ClusterHolder.Fragment.From(
                new ClusterHolder
                {
                    Groups = new()
                    {
                        new ServerGroup
                        {
                            Name = "g1",
                            Servers = new() { Server("a", "A2"), Server("b", "B") },
                        },
                        new ServerGroup { Name = "g2" },
                    },
                }
            )
        );

        var patch = ClusterHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        ClusterHolder.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();

        var g1 = applied.Value!.Groups.Value!.Single(g => g.Name == "g1");
        g1.Servers.Select(s => s.Id).ShouldBe(["a", "b"]);
        g1.Servers.Single(s => s.Id == "a").Name.ShouldBe("A2");
    }

    [Test]
    public void ScalarSequencesRemainAtomicWithDuplicates()
    {
        Optional<ScalarSequenceHolder.Fragment?> SOf(ScalarSequenceHolder m) =>
            Optional<ScalarSequenceHolder.Fragment?>.Present(ScalarSequenceHolder.Fragment.From(m));

        var before = SOf(new ScalarSequenceHolder { Tags = ["a", "a", "b"], Numbers = [1, 2] });
        var after = SOf(new ScalarSequenceHolder { Tags = ["b", "a", "a"], Numbers = [2, 1] });

        // Reorder/replacement is a whole-value operation (duplicates preserved, no keys).
        var patch = ScalarSequenceHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        ScalarSequenceHolder.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
        applied.Value!.Tags.Value!.ShouldBe(["b", "a", "a"]);
        applied.Value.Numbers.Value!.ShouldBe([2, 1]);
    }

    [Test]
    public void ScalarDictionarySetRemoveAndUpdate()
    {
        Optional<ScalarDictHolder.Fragment?> SOf(ScalarDictHolder m) =>
            Optional<ScalarDictHolder.Fragment?>.Present(ScalarDictHolder.Fragment.From(m));

        var before = SOf(
            new ScalarDictHolder
            {
                Scores = new() { ["a"] = 1, ["b"] = 2 },
            }
        );
        var after = SOf(
            new ScalarDictHolder
            {
                Scores = new() { ["b"] = 3, ["c"] = 4 },
            }
        );

        var patch = ScalarDictHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        ScalarDictHolder.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
        applied.Value!.Scores.Value!["b"].ShouldBe(3);
        applied.Value.Scores.Value!.ContainsKey("a").ShouldBeFalse();
    }

    [Test]
    public void StructuralDictionaryNestedEdit()
    {
        Optional<StructuralDictHolder.Fragment?> SOf(StructuralDictHolder m) =>
            Optional<StructuralDictHolder.Fragment?>.Present(StructuralDictHolder.Fragment.From(m));

        var before = SOf(
            new StructuralDictHolder { Servers = new() { ["web"] = Server("s1", "Old") } }
        );
        var patch = new StructuralDictHolder.Patch();
        patch.Servers.Edit("web").Name = "New";
        patch.Servers.SetEntry("db", Server("s2", "Db"));

        var applied = patch.Apply(before);
        applied.Value!.Servers.Value!["web"].Name.ShouldBe("New");
        applied.Value.Servers.Value!["db"].Id.ShouldBe("s2");

        var remover = new StructuralDictHolder.Patch();
        remover.Servers.RemoveEntry("web");
        remover.Apply(applied).Value!.Servers.Value!.ContainsKey("web").ShouldBeFalse();
    }

    [Test]
    public void ComposeInvertAndRebaseLaws()
    {
        var b0 = State(Holder(Server("a", "A")));
        var b1 = State(Holder(Server("a", "A2"), Server("b", "B")));
        var b2 = State(Holder(Server("b", "B3")));

        var first = KeyedServerHolder.Patch.Between(b0, b1);
        var second = KeyedServerHolder.Patch.Between(b1, b2);
        var composed = first.Compose(second);
        KeyedServerHolder.Patch.Between(composed.Apply(b0), b2).IsEmpty.ShouldBeTrue();

        var inverted = composed.Invert(b0);
        KeyedServerHolder
            .Patch.Between(inverted.Apply(composed.Apply(b0)), b0)
            .IsEmpty.ShouldBeTrue();

        // Rebase: untouched current keeps local.
        var kept = KeyedServerHolder.Patch.Rebase(b0, first, b0);
        kept.HasConflicts.ShouldBeFalse();
        KeyedServerHolder.Patch.Between(kept.Rebased.Apply(b0), b1).IsEmpty.ShouldBeTrue();

        // Rebase: already applied becomes empty.
        var already = KeyedServerHolder.Patch.Rebase(b0, first, b1);
        already.HasConflicts.ShouldBeFalse();
        already.Rebased.IsEmpty.ShouldBeTrue();

        // Rebase: concurrent divergent edit conflicts.
        var divergent = State(Holder(Server("a", "Conflict")));
        var conflicted = KeyedServerHolder.Patch.Rebase(b0, first, divergent);
        conflicted.HasConflicts.ShouldBeTrue();
    }

    [Test]
    public void WholeReplaceEscapeHatch()
    {
        var before = State(Holder(Server("a", "A")));
        var patch = new KeyedServerHolder.Patch();
        patch.Items.Set(new List<KeyedServer> { Server("z", "Z") });
        var applied = patch.Apply(before);
        applied.Value!.Items.Value!.Select(s => s.Id).ShouldBe(["z"]);

        var unsetPatch = new KeyedServerHolder.Patch();
        unsetPatch.Items.Remove();
        unsetPatch.Apply(before).Value!.Items.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void KeyedElementEditNormalizesToNestedKeyedOperation()
    {
        var baseline = KeyedServerHolder.Fragment.From(Holder(Server("a", "A"), Server("b", "B")));
        var present = Optional<KeyedServerHolder.Fragment?>.Present(baseline);
        var after = KeyedServerHolder.Fragment.From(Holder(Server("a", "A2"), Server("b", "B")));
        var afterOpt = Optional<KeyedServerHolder.Fragment?>.Present(after);

        // A nested keyed-element edit materializes as a keyed edit, not a positional one.
        var patch = KeyedServerHolder.Patch.Between(present, afterOpt);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(present);
        applied.Value!.Items.Value!.Single(s => s.Id == "a").Name.ShouldBe("A2");

        // The typed payload round-trip preserves the same keyed semantics.
        var changes = KeyedServerHolder.ChangeSet.Between(present, afterOpt);
        var roundTripped = System
            .Text.Json.JsonSerializer.Deserialize<KeyedServerHolder.ChangeSetPayload>(
                System.Text.Json.JsonSerializer.Serialize(changes.ToPayload())
            )!
            .ToChangeSet();
        KeyedServerHolder
            .Patch.Between(roundTripped.ToPatch().Apply(present), applied)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ScalarSequenceRoundTripsAsWholeValue()
    {
        var baseline = ScalarSequenceHolder.Fragment.From(
            new ScalarSequenceHolder { Tags = ["a", "b"], Numbers = [1] }
        );
        var present = Optional<ScalarSequenceHolder.Fragment?>.Present(baseline);
        var after = ScalarSequenceHolder.Fragment.From(
            new ScalarSequenceHolder { Tags = ["a", "b", "c"], Numbers = [1] }
        );
        var afterOpt = Optional<ScalarSequenceHolder.Fragment?>.Present(after);

        var patch = ScalarSequenceHolder.Patch.Between(present, afterOpt);
        var applied = patch.Apply(present);
        applied.Value!.Tags.Value!.ShouldBe(["a", "b", "c"]);

        var changes = ScalarSequenceHolder.ChangeSet.Between(present, afterOpt);
        var roundTripped = System
            .Text.Json.JsonSerializer.Deserialize<ScalarSequenceHolder.ChangeSetPayload>(
                System.Text.Json.JsonSerializer.Serialize(changes.ToPayload())
            )!
            .ToChangeSet();
        ScalarSequenceHolder
            .Patch.Between(roundTripped.ToPatch().Apply(present), applied)
            .IsEmpty.ShouldBeTrue();
    }
}
