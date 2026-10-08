using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Wide model proving ChangeSet payload scales with the change (issue #96).</summary>
[SparseFragmentModel]
public partial class WideWidget
{
    public string? Alpha { get; set; }

    public string? Bravo { get; set; }

    public string? Charlie { get; set; }

    public string? Delta { get; set; }

    public string? Echo { get; set; }

    public string? Foxtrot { get; set; }

    public string? Golf { get; set; }

    public string? Hotel { get; set; }

    public Nested? Nested { get; set; }
}

/// <summary>Keyed member alongside an untouched scalar sibling (issue #96).</summary>
[SparseFragmentModel]
public partial class ServerGroupHolder
{
    public string? Title { get; set; }

    public List<KeyedServer> Servers { get; set; } = new();
}

/// <summary>ChangeSet sparse semantic-transition storage (issue #96).</summary>
/// <remarks>
/// Proves unchanged members are neither retained in state nor serialized:
/// scalar/nested/keyed transitions carry only changed paths, Missing/null/value
/// round-trip exactly, and Between/FromPatch normalize equivalently with
/// ToPatch/Invert/Compose/Rebase preserved across serialization.
/// </remarks>
public sealed class SparseChangeSetStorageTests
{
    private static Optional<WideWidget.Fragment?> WideState(
        string? alpha = "a",
        string? bravo = "b",
        string? charlie = "c",
        string? delta = "d",
        string? echo = "e",
        string? foxtrot = "f",
        string? golf = "g",
        string? hotel = "h"
    ) =>
        Optional<WideWidget.Fragment?>.Present(
            new WideWidget.Fragment
            {
                Alpha = Optional<string?>.Present(alpha),
                Bravo = Optional<string?>.Present(bravo),
                Charlie = Optional<string?>.Present(charlie),
                Delta = Optional<string?>.Present(delta),
                Echo = Optional<string?>.Present(echo),
                Foxtrot = Optional<string?>.Present(foxtrot),
                Golf = Optional<string?>.Present(golf),
                Hotel = Optional<string?>.Present(hotel),
            }
        );

    [Test]
    public void SingleScalarChangeRetainsNothingElse()
    {
        var before = WideState();
        var after = WideState(alpha: "a2");
        var changes = WideWidget.ChangeSet.Between(before, after);
        changes.IsEmpty.ShouldBeFalse();

        changes.Alpha.IsChanged.ShouldBeTrue();
        changes.Alpha.Before.Value.ShouldBe("a");
        changes.Alpha.After.Value.ShouldBe("a2");

        // Unchanged siblings retain no state.
        changes.Bravo.IsChanged.ShouldBeFalse();
        changes.Bravo.Before.IsPresent.ShouldBeFalse();
        changes.Bravo.After.IsPresent.ShouldBeFalse();
        changes.Charlie.Before.IsPresent.ShouldBeFalse();
        changes.Delta.Before.IsPresent.ShouldBeFalse();
        changes.Echo.Before.IsPresent.ShouldBeFalse();
        changes.Foxtrot.Before.IsPresent.ShouldBeFalse();
        changes.Golf.Before.IsPresent.ShouldBeFalse();
        changes.Hotel.Before.IsPresent.ShouldBeFalse();
        changes.Nested.IsEmpty.ShouldBeTrue();

        // Replay still reaches the full after-state.
        WideWidget.Patch.Between(changes.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void SingleChangeSerializesWithoutUnrelatedMembers()
    {
        var before = WideState();
        var after = WideState(alpha: "a2");
        var changes = WideWidget.ChangeSet.Between(before, after);
        var json = JsonSerializer.Serialize(changes.ToPayload());

        json.ShouldContain("Alpha");
        json.ShouldContain("a2");
        json.ShouldNotContain("Bravo");
        json.ShouldNotContain("Charlie");
        json.ShouldNotContain("Delta");
        json.ShouldNotContain("Echo");
        json.ShouldNotContain("Foxtrot");
        json.ShouldNotContain("Golf");
        json.ShouldNotContain("Hotel");
        json.ShouldNotContain("Nested");

        // Payload scales with the change, not the model: one changed member
        // serializes far smaller than changing every member.
        var allChanged = WideWidget.ChangeSet.Between(
            WideState(),
            WideState("a2", "b2", "c2", "d2", "e2", "f2", "g2", "h2")
        );
        var allJson = JsonSerializer.Serialize(allChanged.ToPayload());
        (json.Length * 3 < allJson.Length).ShouldBeTrue(
            $"sparse single {json.Length} should scale vs full {allJson.Length}"
        );

        var back = JsonSerializer.Deserialize<WideWidget.ChangeSetPayload>(json)!.ToChangeSet();
        back.Alpha.IsChanged.ShouldBeTrue();
        back.Alpha.After.Value.ShouldBe("a2");
        back.Bravo.IsChanged.ShouldBeFalse();
        WideWidget.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void NestedLeafChangeRetainsOnlyLeafTransition()
    {
        Optional<Settings.Fragment?> State(string host, int port = 1) =>
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present("keep"),
                    Nested = Optional<Nested.Fragment?>.Present(
                        new Nested.Fragment
                        {
                            Host = Optional<string>.Present(host),
                            Port = Optional<int>.Present(port),
                        }
                    ),
                }
            );
        var changes = Settings.ChangeSet.Between(State("a"), State("b"));

        // Unchanged root sibling retains nothing.
        changes.Label.IsChanged.ShouldBeFalse();
        changes.Label.Before.IsPresent.ShouldBeFalse();

        // Nested subtree carries only the leaf transition.
        changes.Nested.IsEmpty.ShouldBeFalse();
        changes.Nested.Host.IsChanged.ShouldBeTrue();
        changes.Nested.Host.Before.Value.ShouldBe("a");
        changes.Nested.Host.After.Value.ShouldBe("b");
        changes.Nested.Port.IsChanged.ShouldBeFalse();
        changes.Nested.Port.Before.IsPresent.ShouldBeFalse();

        var json = JsonSerializer.Serialize(changes.ToPayload());
        json.ShouldContain("Host");
        json.ShouldNotContain("Label");
        json.ShouldNotContain("Port");

        var back = JsonSerializer.Deserialize<Settings.ChangeSetPayload>(json)!.ToChangeSet();
        back.Nested.Host.After.Value.ShouldBe("b");
        back.Nested.Port.IsChanged.ShouldBeFalse();
        Settings.Patch.Between(back.ToPatch().Apply(State("a")), State("b")).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void AtomicCustomMemberRetainsWholeChangedValue()
    {
        var before = Optional<StrategySettings.Fragment?>.Present(
            StrategySettings.Fragment.From(new StrategySettings { Values = [1, 2] })
        );
        var after = Optional<StrategySettings.Fragment?>.Present(
            StrategySettings.Fragment.From(new StrategySettings { Values = [3, 4] })
        );
        var changes = StrategySettings.ChangeSet.Between(before, after);
        changes.Values.IsChanged.ShouldBeTrue();
        changes.Values.Before.Value.ShouldBe([1, 2]);
        changes.Values.After.Value.ShouldBe([3, 4]);

        var json = JsonSerializer.Serialize(changes.ToPayload());
        json.ShouldContain("Values");
        var back = JsonSerializer
            .Deserialize<StrategySettings.ChangeSetPayload>(json)!
            .ToChangeSet();
        back.Values.After.Value.ShouldBe([3, 4]);
        StrategySettings.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedChangeKeepsSiblingSparseAndRoundTrips()
    {
        Optional<ServerGroupHolder.Fragment?> State(string? title, params KeyedServer[] servers) =>
            Optional<ServerGroupHolder.Fragment?>.Present(
                ServerGroupHolder.Fragment.From(
                    new ServerGroupHolder { Title = title, Servers = servers.ToList() }
                )
            );
        KeyedServer S(string id, string name) =>
            new()
            {
                Id = id,
                Name = name,
                Count = 1,
            };
        var before = State("keep", S("a", "A"), S("b", "B"));
        var after = State("keep", S("b", "B2"), S("c", "C"));
        var changes = ServerGroupHolder.ChangeSet.Between(before, after);

        // Untouched scalar sibling retains nothing.
        changes.Title.IsChanged.ShouldBeFalse();
        changes.Title.Before.IsPresent.ShouldBeFalse();

        // Keyed transition data survives.
        changes.Servers.IsChanged.ShouldBeTrue();
        changes.Servers.Added.Select(e => e.Id).ShouldBe(["c"]);
        changes.Servers.Removed.Select(e => e.Id).ShouldBe(["a"]);
        changes.Servers.Edited.ContainsKey("b").ShouldBeTrue();
        changes.Servers.Edited["b"].Name.After.Value.ShouldBe("B2");

        var json = JsonSerializer.Serialize(changes.ToPayload());
        json.ShouldNotContain("Title");

        var back = JsonSerializer
            .Deserialize<ServerGroupHolder.ChangeSetPayload>(json)!
            .ToChangeSet();
        back.Servers.Added.Select(e => e.Id).ShouldBe(["c"]);
        back.Servers.Removed.Select(e => e.Id).ShouldBe(["a"]);
        back.Servers.Edited["b"].Name.After.Value.ShouldBe("B2");
        back.Title.IsChanged.ShouldBeFalse();
        ServerGroupHolder.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();

        // GetChange lookup survives the sparse round-trip.
        back.Servers.GetChange("c").IsAdded.ShouldBeTrue();
        back.Servers.GetChange("a").IsRemoved.ShouldBeTrue();
        back.Servers.GetChange("zzz").IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void MissingNullValueSemanticsSurviveSparseRoundTrip()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var nullState = Optional<Settings.Fragment?>.Present(null);
        var value = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("v") }
        );
        foreach (
            var (b, a) in new[]
            {
                (missing, missing),
                (missing, nullState),
                (nullState, value),
                (value, missing),
                (value, value),
            }
        )
        {
            var changes = Settings.ChangeSet.Between(b, a);
            var back = JsonSerializer
                .Deserialize<Settings.ChangeSetPayload>(
                    JsonSerializer.Serialize(changes.ToPayload())
                )!
                .ToChangeSet();
            back.IsEmpty.ShouldBe(changes.IsEmpty);
            back.Invert().Invert().IsEmpty.ShouldBe(changes.IsEmpty);
            Settings.Patch.Between(back.ToPatch().Apply(b), a).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void BetweenAndFromPatchNormalizeEquivalently()
    {
        var baseline = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("a"),
                RetryCount = Optional<int>.Present(1),
            }
        );
        var granular = new Settings.Patch { Label = "b" };
        var whole = new Settings.Patch();
        whole.Label = "b";

        var fromGranular = Settings.ChangeSet.FromPatch(baseline, granular);
        var fromWhole = Settings.ChangeSet.FromPatch(baseline, whole);
        var canonical = Settings.ChangeSet.Between(baseline, granular.Apply(baseline));

        foreach (var changes in new[] { fromGranular, fromWhole, canonical })
        {
            changes.Label.IsChanged.ShouldBeTrue();
            changes.Label.After.Value.ShouldBe("b");
            changes.RetryCount.IsChanged.ShouldBeFalse();
            changes.RetryCount.Before.IsPresent.ShouldBeFalse();
        }
        Settings
            .Patch.Between(
                fromGranular.ToPatch().Apply(baseline),
                fromWhole.ToPatch().Apply(baseline)
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void SparseRoundTripPreservesAlgebra()
    {
        Optional<Settings.Fragment?> S(string? label, int retry) =>
            Optional<Settings.Fragment?>.Present(
                new Settings.Fragment
                {
                    Label = Optional<string?>.Present(label),
                    RetryCount = Optional<int>.Present(retry),
                }
            );
        var b0 = S("a", 1);
        var b1 = S("b", 1);
        var b2 = S("b", 2);

        Settings.ChangeSet RoundTrip(Settings.ChangeSet value) =>
            JsonSerializer
                .Deserialize<Settings.ChangeSetPayload>(
                    JsonSerializer.Serialize(value.ToPayload())
                )!
                .ToChangeSet();

        var c1 = RoundTrip(Settings.ChangeSet.Between(b0, b1));
        var c2 = RoundTrip(Settings.ChangeSet.Between(b1, b2));
        Settings.Patch.Between(c1.Compose(c2).ToPatch().Apply(b0), b2).IsEmpty.ShouldBeTrue();
        Settings.Patch.Between(c1.Invert().ToPatch().Apply(b1), b0).IsEmpty.ShouldBeTrue();

        var current = S("c", 1);
        var rebased = RoundTrip(Settings.ChangeSet.Between(b0, b1)).RebaseOnto(current);
        rebased.HasConflicts.ShouldBeTrue();
        rebased.Conflicts.Count.ShouldBeGreaterThan(0);

        var clean = RoundTrip(Settings.ChangeSet.Between(S("a", 1), S("a", 2)));
        var cleanRebased = clean.RebaseOnto(S("b", 1));
        cleanRebased.HasConflicts.ShouldBeFalse();
        Settings
            .Patch.Between(cleanRebased.Rebased.ToPatch().Apply(S("b", 1)), S("b", 2))
            .IsEmpty.ShouldBeTrue();
    }
}
