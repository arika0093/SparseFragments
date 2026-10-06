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

public sealed class KeyedCollectionPatchTests
{
    private static KeyedServer Server(string id, string name = "", int count = 0) =>
        new() { Id = id, Name = name, Count = count };

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
            new() { TenantId = tenant, Id = id, Name = name };

        Optional<CompositeServerHolder.Fragment?> StateOf(CompositeServerHolder m) =>
            Optional<CompositeServerHolder.Fragment?>.Present(CompositeServerHolder.Fragment.From(m));

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
                        new ServerGroup { Name = "g1", Servers = new() { Server("a", "A") } },
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

        var before = SOf(new ScalarDictHolder { Scores = new() { ["a"] = 1, ["b"] = 2 } });
        var after = SOf(new ScalarDictHolder { Scores = new() { ["b"] = 3, ["c"] = 4 } });

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
        KeyedServerHolder.Patch.Between(inverted.Apply(composed.Apply(b0)), b0).IsEmpty.ShouldBeTrue();

        // Rebase: untouched current keeps local.
        var kept = KeyedServerHolder.Patch.Rebase(b0, first, b0);
        kept.HasConflicts.ShouldBeFalse();
        KeyedServerHolder.Patch.Between(kept.Patch.Apply(b0), b1).IsEmpty.ShouldBeTrue();

        // Rebase: already applied becomes empty.
        var already = KeyedServerHolder.Patch.Rebase(b0, first, b1);
        already.HasConflicts.ShouldBeFalse();
        already.Patch.IsEmpty.ShouldBeTrue();

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
        unsetPatch.Items.Unset();
        unsetPatch.Apply(before).Value!.Items.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void JsonPatchPositionalEditsNormalizeToKeyedOperations()
    {
        var baseline = KeyedServerHolder.Fragment.From(Holder(Server("a", "A"), Server("b", "B")));
        var present = Optional<KeyedServerHolder.Fragment?>.Present(baseline);

        // Positional RFC 6902 edit on a keyed element normalizes to a nested keyed edit.
        var patch = KeyedServerHolder.Patch.FromJsonPatch(
            present,
            System.Text.Encoding.UTF8.GetBytes(
                """[{"op":"replace","path":"/Items/0/Name","value":"A2"}]"""
            )
        );
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(present);
        applied.Value!.Items.Value!.Single(s => s.Id == "a").Name.ShouldBe("A2");

        // Export round-trips semantically (whole-array lowering re-imports to the same state).
        var exported = patch.ToJsonPatch(present);
        var reimported = KeyedServerHolder.Patch.FromJsonPatch(present, exported);
        KeyedServerHolder.Patch
            .Between(reimported.Apply(present), applied)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsonPatchScalarSequenceRoundTripsAsWholeValue()
    {
        var baseline = ScalarSequenceHolder.Fragment.From(
            new ScalarSequenceHolder { Tags = ["a", "b"], Numbers = [1] }
        );
        var present = Optional<ScalarSequenceHolder.Fragment?>.Present(baseline);

        var patch = ScalarSequenceHolder.Patch.FromJsonPatch(
            present,
            System.Text.Encoding.UTF8.GetBytes("""[{"op":"add","path":"/Tags/-","value":"c"}]""")
        );
        var applied = patch.Apply(present);
        applied.Value!.Tags.Value!.ShouldBe(["a", "b", "c"]);

        var exported = patch.ToJsonPatch(present);
        var reimported = ScalarSequenceHolder.Patch.FromJsonPatch(present, exported);
        ScalarSequenceHolder.Patch
            .Between(reimported.Apply(present), applied)
            .IsEmpty.ShouldBeTrue();
    }
}
