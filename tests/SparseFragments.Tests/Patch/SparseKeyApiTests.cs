using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class SkApiServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class SkApiServerHolder
{
    public List<SkApiServer> Items { get; set; } = new();
}

public readonly record struct SkApiTenantKey(string Tenant, int Id);

public partial class SkApiTenantServer
{
    public string Tenant { get; set; } = string.Empty;

    public int Id { get; set; }

    [SparseKey]
    public SkApiTenantKey Key => new(Tenant, Id);

    public string Name { get; set; } = string.Empty;
}

public partial class SkApiCompositeServer
{
    public string TenantId { get; set; } = string.Empty;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public (string TenantId, int Id) Key => (TenantId, Id);
}

public partial class SkApiTripleServer
{
    public int A { get; set; }

    public string B { get; set; } = string.Empty;

    public Guid C { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public (int A, string B, Guid C) Key => (A, B, C);
}

public partial class SkApiSwappedServer
{
    public string A { get; set; } = string.Empty;

    public string B { get; set; } = string.Empty;

    [SparseKey]
    public (string B, string A) Key => (B, A);
}

public readonly record struct SkApiIfaceKey(string Tenant, int Id);

public partial class SkApiIfaceServer
{
    public string Tenant { get; set; } = string.Empty;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public SkApiIfaceKey Key => new(Tenant.ToUpperInvariant(), Id);
}

public partial class SkApiEndpoint
{
    [SparseKey]
    public string Path { get; set; } = string.Empty;

    public int Port { get; set; }
}

public partial class SkApiNestedServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public List<SkApiEndpoint> Endpoints { get; set; } = new();
}

public partial class SkApiGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<SkApiServer> Servers { get; set; } = new();

    public List<SkApiNestedServer> Nested { get; set; } = new();
}

[SparseFragmentModel]
public partial class SkApiCluster
{
    public List<SkApiGroup> Groups { get; set; } = new();

    public List<SkApiTenantServer> Tenants { get; set; } = new();

    public List<SkApiCompositeServer> Composites { get; set; } = new();

    public List<SkApiTripleServer> Triples { get; set; } = new();

    public List<SkApiIfaceServer> Ifaces { get; set; } = new();
}

public sealed class SparseKeyApiTests
{
    private static Optional<SkApiServerHolder.Fragment?> StateOf(SkApiServerHolder model) =>
        Optional<SkApiServerHolder.Fragment?>.Present(SkApiServerHolder.Fragment.From(model));

    private static Optional<SkApiCluster.Fragment?> StateOf(SkApiCluster model) =>
        Optional<SkApiCluster.Fragment?>.Present(SkApiCluster.Fragment.From(model));

    [Test]
    public void ScalarSinglePropertyKeyRoundTrip()
    {
        var before = StateOf(new SkApiServerHolder { Items = [new() { Id = "a", Name = "A" }] });
        var after = StateOf(
            new SkApiServerHolder
            {
                Items = [new() { Id = "a", Name = "A2" }, new() { Id = "b", Name = "B" }],
            }
        );

        var patch = SkApiServerHolder.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        SkApiServerHolder.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ComputedValueObjectKeyDistinguishesComponents()
    {
        var before = StateOf(
            new SkApiCluster
            {
                Tenants =
                [
                    new()
                    {
                        Tenant = "t1",
                        Id = 1,
                        Name = "One",
                    },
                    new()
                    {
                        Tenant = "t2",
                        Id = 1,
                        Name = "Two",
                    },
                ],
            }
        );
        var after = StateOf(
            new SkApiCluster
            {
                Tenants =
                [
                    new()
                    {
                        Tenant = "t1",
                        Id = 1,
                        Name = "One*",
                    },
                    new()
                    {
                        Tenant = "t2",
                        Id = 1,
                        Name = "Two",
                    },
                    new()
                    {
                        Tenant = "t1",
                        Id = 2,
                        Name = "Three",
                    },
                ],
            }
        );

        var patch = SkApiCluster.Patch.Between(before, after);
        var applied = patch.Apply(before);
        SkApiCluster.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
        applied.Value!.Tenants.Value!.Count.ShouldBe(3);
        applied
            .Value!.Tenants.Value!.Single(t => t.Tenant == "t1" && t.Id == 1)
            .Name.ShouldBe("One*");
    }

    [Test]
    public void TwoComponentTupleKeyRoundTrip()
    {
        SkApiCompositeServer S(string tenant, int id, string name = "") =>
            new()
            {
                TenantId = tenant,
                Id = id,
                Name = name,
            };

        var before = StateOf(new SkApiCluster { Composites = [S("t1", 1, "A")] });
        var after = StateOf(
            new SkApiCluster { Composites = [S("t1", 1, "A2"), S("t2", 1, "Other")] }
        );

        var patch = SkApiCluster.Patch.Between(before, after);
        var applied = patch.Apply(before);
        SkApiCluster.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();

        // Same Id in different tenants are distinct keys with deterministic equality.
        var remover = new SkApiCluster.Patch();
        remover.Composites.Remove(("t2", 1));
        var removed = remover.Apply(applied);
        removed.Value!.Composites.Value!.Select(s => s.TenantId).ShouldBe(["t1"]);
    }

    [Test]
    public void TupleComponentOrderIsSignificant()
    {
        // SkApiSwappedServer declares Key as (B, A): key ("b", "a") for A="a", B="b".
        var holder = new SkApiSwappedHolder { Items = [new() { A = "a", B = "b" }] };
        var present = Optional<SkApiSwappedHolder.Fragment?>.Present(
            SkApiSwappedHolder.Fragment.From(holder)
        );

        // Declared order works.
        var remover = new SkApiSwappedHolder.Patch();
        remover.Items.Remove(("b", "a"));
        remover.Apply(present).Value!.Items.Value!.ShouldBeEmpty();

        // Swapped component order does not match the same element.
        var missed = new SkApiSwappedHolder.Patch();
        missed.Items.Remove(("a", "b"));
        missed.Apply(present).Value!.Items.Value!.Count.ShouldBe(1);
    }

    [Test]
    public void ThreeComponentTupleKeyRoundTripWithReorder()
    {
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var before = StateOf(
            new SkApiCluster
            {
                Triples =
                [
                    new()
                    {
                        A = 1,
                        B = "x",
                        C = c1,
                    },
                    new()
                    {
                        A = 2,
                        B = "y",
                        C = c2,
                    },
                ],
            }
        );
        var after = StateOf(
            new SkApiCluster
            {
                Triples =
                [
                    new()
                    {
                        A = 2,
                        B = "y",
                        C = c2,
                    },
                    new()
                    {
                        A = 1,
                        B = "x",
                        C = c1,
                    },
                ],
            }
        );

        var patch = SkApiCluster.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        applied.Value!.Triples.Value!.Select(t => t.A).ShouldBe([2, 1]);
        SkApiCluster.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ComputedKeyNormalizesIdentity()
    {
        var basis = StateOf(
            new SkApiCluster
            {
                Ifaces =
                [
                    new()
                    {
                        Tenant = "acme",
                        Id = 1,
                        Name = "A",
                    },
                ],
            }
        );

        // Same normalized identity with different casing is a duplicate.
        var duplicate = new SkApiCluster.Patch();
        duplicate.Ifaces.Add(
            new()
            {
                Tenant = "ACME",
                Id = 1,
                Name = "Dup",
            }
        );
        Should.Throw<InvalidOperationException>(() => duplicate.Apply(basis));

        // Manual edit + remove/add flow works through the normalized key.
        // Note: manually constructed keys must already be normalized, exactly as
        // the Key getter produces them; extraction normalizes model values.
        var patch = new SkApiCluster.Patch();
        patch.Ifaces.Edit(new SkApiIfaceKey("ACME", 1)).Name = "A2";
        var applied = patch.Apply(basis);
        applied.Value!.Ifaces.Value!.Single().Name.ShouldBe("A2");

        var remover = new SkApiCluster.Patch();
        remover.Ifaces.Remove(new SkApiIfaceKey("ACME", 1));
        remover.Apply(applied).Value!.Ifaces.Value!.ShouldBeEmpty();
    }

    [Test]
    public void NestedAndRecursiveKeyedModelsRoundTrip()
    {
        var before = StateOf(
            new SkApiCluster
            {
                Groups =
                [
                    new SkApiGroup
                    {
                        Name = "g1",
                        Servers = [new() { Id = "a", Name = "A" }],
                        Nested =
                        [
                            new() { Id = "n1", Endpoints = [new() { Path = "/x", Port = 80 }] },
                        ],
                    },
                ],
            }
        );
        var after = StateOf(
            new SkApiCluster
            {
                Groups =
                [
                    new SkApiGroup
                    {
                        Name = "g1",
                        Servers = [new() { Id = "a", Name = "A2" }, new() { Id = "b" }],
                        Nested =
                        [
                            new()
                            {
                                Id = "n1",
                                Endpoints =
                                [
                                    new() { Path = "/x", Port = 8080 },
                                    new() { Path = "/y", Port = 443 },
                                ],
                            },
                        ],
                    },
                    new SkApiGroup { Name = "g2" },
                ],
            }
        );

        var patch = SkApiCluster.Patch.Between(before, after);
        patch.IsEmpty.ShouldBeFalse();
        var applied = patch.Apply(before);
        SkApiCluster.Patch.Between(applied, after).IsEmpty.ShouldBeTrue();

        var g1 = applied.Value!.Groups.Value!.Single(g => g.Name == "g1");
        g1.Servers.Select(s => s.Id).ShouldBe(["a", "b"]);
        g1.Nested.Single().Endpoints.Select(e => e.Path).ShouldBe(["/x", "/y"]);
        g1.Nested.Single().Endpoints.Single(e => e.Path == "/x").Port.ShouldBe(8080);
    }

    [Test]
    public void DuplicateKeysAndKeyMutationAreRejected()
    {
        var basis = StateOf(new SkApiServerHolder { Items = [new() { Id = "a" }] });

        var dup = new SkApiServerHolder.Patch();
        dup.Items.Add(new SkApiServer { Id = "x" });
        Should.Throw<InvalidOperationException>(() => dup.Items.Add(new SkApiServer { Id = "x" }));

        var mutate = new SkApiServerHolder.Patch();
        mutate.Items.Edit("a").Id = "b";
        Should.Throw<InvalidOperationException>(() => mutate.Apply(basis));

        // Between normalizes an observed key change as remove-old + add-new.
        var renamed = StateOf(new SkApiServerHolder { Items = [new() { Id = "b" }] });
        var diff = SkApiServerHolder.Patch.Between(basis, renamed);
        diff.IsEmpty.ShouldBeFalse();
        SkApiServerHolder.Patch.Between(diff.Apply(basis), renamed).IsEmpty.ShouldBeTrue();
    }
}

[SparseFragmentModel]
public partial class SkApiSwappedHolder
{
    public List<SkApiSwappedServer> Items { get; set; } = new();
}
