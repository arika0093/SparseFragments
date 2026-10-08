using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class NamingWidget
{
    [System.Text.Json.Serialization.JsonPropertyName("customName")]
    public string? Value { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("a/b")]
    public int Slash { get; set; }

    public int Plain { get; set; }
}

public sealed class PatchChangeSetStjTests
{
    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    private static void AssertPatchEqual(
        Settings.Patch expected,
        Settings.Patch actual,
        Optional<Settings.Fragment?> basis
    )
    {
        Settings.Patch.Between(actual.Apply(basis), expected.Apply(basis)).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void EmptyPatchRoundTrips()
    {
        var patch = new Settings.Patch();
        var back = RoundTrip(patch);
        back.IsEmpty.ShouldBeTrue();
        var cs = Settings.ChangeSet.Between(
            Optional<Settings.Fragment?>.Missing,
            Optional<Settings.Fragment?>.Missing
        );
        RoundTrip(cs.ToPayload()).ToChangeSet().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ScalarSetRemoveNullRoundTrip()
    {
        var set = new Settings.Patch { Label = "Bob" };
        RoundTrip(set).Label.Value.ShouldBe("Bob");

        var setNull = new Settings.Patch { Label = (string?)null };
        var backNull = RoundTrip(setNull);
        backNull.Label.Kind.ShouldBe(FragmentOperationKind.Set);
        backNull.Label.Value.ShouldBeNull();

        var unset = new Settings.Patch();
        unset.Label = FragmentOperation<string?>.Remove;
        RoundTrip(unset).Label.Kind.ShouldBe(FragmentOperationKind.Remove);

        var retry = new Settings.Patch { RetryCount = 7 };
        RoundTrip(retry).RetryCount.Value.ShouldBe(7);
    }

    [Test]
    public void RootPresenceRoundTrip()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var nullState = Optional<Settings.Fragment?>.Present(null);
        var value = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("x") }
        );
        var cases = new[] { (missing, missing), (missing, nullState), (nullState, value), (value, missing), (missing, value), (value, value) };
        foreach (var (b, a) in cases)
        {
            var patch = Settings.Patch.Between(b, a);
            var back = RoundTrip(patch);
            Settings.Patch.Between(back.Apply(b), a).IsEmpty.ShouldBeTrue($"patch {b} -> {a}");
            var cs = Settings.ChangeSet.Between(b, a);
            var csBack = RoundTrip(cs.ToPayload()).ToChangeSet();
            Settings.Patch.Between(csBack.ToPatch().Apply(b), a).IsEmpty.ShouldBeTrue($"changeset {b} -> {a}");
        }

        var wholeSet = new Settings.Patch();
        wholeSet.Set(new Settings { Label = "x" });
        RoundTrip(wholeSet).Apply(missing).Value!.Label.Value.ShouldBe("x");

        var wholeNull = new Settings.Patch();
        wholeNull.SetNull();
        RoundTrip(wholeNull).Apply(value).Value.ShouldBeNull();

        var wholeUnset = new Settings.Patch();
        wholeUnset.Remove();
        RoundTrip(wholeUnset).Apply(value).IsPresent.ShouldBeFalse();
    }

    [Test]
    public void NestedRoundTrip()
    {
        var patch = new Settings.Patch();
        patch.Nested.Host = "h2";
        var back = RoundTrip(patch);
        back.Nested.Host.Value.ShouldBe("h2");

        var toNull = new Settings.Patch();
        toNull.Nested.SetNull();
        var backNull = RoundTrip(toNull);
        var basis = Optional<Settings.Fragment?>.Present(
            Settings.Fragment.From(new Settings { Nested = new Nested { Host = "a" } })
        );
        backNull.Apply(basis).Value!.Nested.Value.ShouldBeNull();

        var unset = new Settings.Patch();
        unset.Nested.Remove();
        RoundTrip(unset).Apply(basis).Value!.Nested.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void ScalarCollectionRoundTrip()
    {
        Optional<ScalarSequenceHolder.Fragment?> State(ScalarSequenceHolder m) =>
            Optional<ScalarSequenceHolder.Fragment?>.Present(ScalarSequenceHolder.Fragment.From(m));
        var before = State(new ScalarSequenceHolder { Tags = ["a", "b"], Numbers = [1, 2] });
        var after = State(new ScalarSequenceHolder { Tags = ["c"], Numbers = [3] });
        var patch = ScalarSequenceHolder.Patch.Between(before, after);
        var back = RoundTrip(patch);
        ScalarSequenceHolder.Patch.Between(back.Apply(before), after).IsEmpty.ShouldBeTrue();

        var cs = ScalarSequenceHolder.ChangeSet.Between(before, after);
        var csBack = RoundTrip(cs.ToPayload()).ToChangeSet();
        ScalarSequenceHolder.Patch.Between(csBack.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void KeyedAddRemoveEditReorderRoundTrip()
    {
        Optional<KeyedServerHolder.Fragment?> F(params KeyedServer[] items) =>
            Optional<KeyedServerHolder.Fragment?>.Present(
                KeyedServerHolder.Fragment.From(new KeyedServerHolder { Items = items.ToList() })
            );
        var b0 = F(
            new KeyedServer { Id = "b", Name = "b", Count = 1 },
            new KeyedServer { Id = "c", Name = "c", Count = 2 }
        );
        var b1 = F(
            new KeyedServer { Id = "b", Name = "b2", Count = 1 },
            new KeyedServer { Id = "d", Name = "d", Count = 3 }
        );
        var patch = KeyedServerHolder.Patch.Between(b0, b1);
        var back = RoundTrip(patch);
        KeyedServerHolder.Patch.Between(back.Apply(b0), b1).IsEmpty.ShouldBeTrue();

        var reorder = F(
            new KeyedServer { Id = "c", Name = "c", Count = 2 },
            new KeyedServer { Id = "b", Name = "b", Count = 1 }
        );
        var reorderPatch = KeyedServerHolder.Patch.Between(b0, reorder);
        var reorderBack = RoundTrip(reorderPatch);
        KeyedServerHolder.Patch.Between(reorderBack.Apply(b0), reorder).IsEmpty.ShouldBeTrue();

        var manual = new KeyedServerHolder.Patch();
        manual.Items.Add(new KeyedServer { Id = "x", Name = "X" });
        manual.Items.Remove("b");
        manual.Items.Edit("c").Name = "c2";
        var manualBack = RoundTrip(manual);
        var applied = manualBack.Apply(b0);
        applied.Value!.Items.Value!.Any(s => s.Id == "x").ShouldBeTrue();
        applied.Value!.Items.Value!.Any(s => s.Id == "b").ShouldBeFalse();
        applied.Value!.Items.Value!.Single(s => s.Id == "c").Name.ShouldBe("c2");

        var whole = new KeyedServerHolder.Patch();
        whole.Items.Set([new KeyedServer { Id = "z" }]);
        RoundTrip(whole).Apply(b0).Value!.Items.Value!.Single().Id.ShouldBe("z");

        var unset = new KeyedServerHolder.Patch();
        unset.Items.Remove();
        RoundTrip(unset).Apply(b0).Value!.Items.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void CompositeKeyRoundTrip()
    {
        Optional<CompositeServerHolder.Fragment?> S(CompositeServerHolder m) =>
            Optional<CompositeServerHolder.Fragment?>.Present(CompositeServerHolder.Fragment.From(m));
        var before = S(new CompositeServerHolder { Items = [new CompositeServer { TenantId = "t1", Id = "a" }] });
        var after = S(
            new CompositeServerHolder
            {
                Items = [new CompositeServer { TenantId = "t1", Id = "a" }, new CompositeServer { TenantId = "t2", Id = "a" }],
            }
        );
        var patch = CompositeServerHolder.Patch.Between(before, after);
        var back = RoundTrip(patch);
        CompositeServerHolder.Patch.Between(back.Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void DictionaryRoundTrip()
    {
        Optional<ScalarDictHolder.Fragment?> S(ScalarDictHolder m) =>
            Optional<ScalarDictHolder.Fragment?>.Present(ScalarDictHolder.Fragment.From(m));
        var b0 = S(new ScalarDictHolder { Scores = new() { ["a"] = 1, ["b"] = 2 } });
        var b1 = S(new ScalarDictHolder { Scores = new() { ["b"] = 3, ["c"] = 4 } });
        var patch = ScalarDictHolder.Patch.Between(b0, b1);
        var back = RoundTrip(patch);
        ScalarDictHolder.Patch.Between(back.Apply(b0), b1).IsEmpty.ShouldBeTrue();

        Optional<StructuralDictHolder.Fragment?> T(StructuralDictHolder m) =>
            Optional<StructuralDictHolder.Fragment?>.Present(StructuralDictHolder.Fragment.From(m));
        var d0 = T(new StructuralDictHolder { Servers = new() { ["web"] = new KeyedServer { Id = "s1", Name = "Old" } } });
        var manual = new StructuralDictHolder.Patch();
        manual.Servers.Edit("web").Name = "New";
        manual.Servers.SetEntry("db", new KeyedServer { Id = "s2", Name = "Db" });
        var manualBack = RoundTrip(manual);
        var applied = manualBack.Apply(d0);
        applied.Value!.Servers.Value!["web"].Name.ShouldBe("New");
        applied.Value!.Servers.Value!["db"].Id.ShouldBe("s2");
    }

    [Test]
    public void CustomStrategyRoundTrip()
    {
        var before = Optional<StrategySettings.Fragment?>.Present(
            StrategySettings.Fragment.From(new StrategySettings { Values = [1, 2] })
        );
        var after = Optional<StrategySettings.Fragment?>.Present(
            StrategySettings.Fragment.From(new StrategySettings { Values = [3, 4] })
        );
        var patch = StrategySettings.Patch.Between(before, after);
        var back = RoundTrip(patch);
        StrategySettings.Patch.Between(back.Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ChangeSetBeforeAfterPresenceRoundTrip()
    {
        var missing = Optional<Settings.Fragment?>.Missing;
        var nullState = Optional<Settings.Fragment?>.Present(null);
        var value = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment { Label = Optional<string?>.Present("v") }
        );
        foreach (var (b, a) in new[] { (missing, missing), (missing, nullState), (nullState, value), (value, missing), (value, value) })
        {
            var cs = Settings.ChangeSet.Between(b, a);
            var back = RoundTrip(cs.ToPayload()).ToChangeSet();
            back.IsEmpty.ShouldBe(cs.IsEmpty);
            Settings.Patch.Between(back.ToPatch().Apply(b), a).IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void InvertComposeRebaseSurviveRoundTrip()
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

        var p1 = RoundTrip(Settings.Patch.Between(b0, b1));
        var p2 = RoundTrip(Settings.Patch.Between(b1, b2));
        Settings.Patch.Between(p1.Compose(p2).Apply(b0), b2).IsEmpty.ShouldBeTrue();
        Settings.Patch.Between(RoundTrip(p1).Invert(b0).Apply(p1.Apply(b0)), b0).IsEmpty.ShouldBeTrue();

        var c1 = RoundTrip(Settings.ChangeSet.Between(b0, b1).ToPayload()).ToChangeSet();
        var c2 = RoundTrip(Settings.ChangeSet.Between(b1, b2).ToPayload()).ToChangeSet();
        Settings.Patch.Between(c1.Compose(c2).ToPatch().Apply(b0), b2).IsEmpty.ShouldBeTrue();
        Settings.Patch.Between(c1.Invert().ToPatch().Apply(b1), b0).IsEmpty.ShouldBeTrue();

        var baseState = S("Alice", 20);
        var edited = Optional<Settings.Fragment?>.Present(
            new Settings.Fragment
            {
                Label = Optional<string?>.Present("Alice"),
                RetryCount = Optional<int>.Present(21),
            }
        );
        var current = S("Bob", 20);
        var changes = RoundTrip(Settings.ChangeSet.Between(baseState, edited).ToPayload()).ToChangeSet();
        var rebased = changes.RebaseOnto(current);
        rebased.HasConflicts.ShouldBeFalse();
        var expected = S("Bob", 21);
        Settings.Patch.Between(rebased.Patch.ToPatch().Apply(current), expected).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void FragmentWireNamesRoundTrip()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new NamingWidget.Fragment.FragmentJsonConverter());

        var fragment = new NamingWidget.Fragment
        {
            Value = Optional<string?>.Present("v"),
            Slash = Optional<int>.Present(7),
        };
        var json = JsonSerializer.Serialize(fragment, options);
        // Explicit wire names win over the naming policy, including escaped ones.
        json.ShouldContain("customName");
        json.ShouldContain("a/b");

        var back = JsonSerializer.Deserialize<NamingWidget.Fragment>(json, options)!;
        back.Value.Value.ShouldBe("v");
        back.Slash.Value.ShouldBe(7);
        back.Plain.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void ChangeSetRoundTripsWithExplicitWireNames()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new NamingWidget.Fragment.FragmentJsonConverter());

        Optional<NamingWidget.Fragment?> State(NamingWidget m) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(m));
        var before = State(new NamingWidget { Value = "a", Slash = 1, Plain = 2 });
        var after = State(new NamingWidget { Value = "b", Slash = 3, Plain = 2 });

        var changes = NamingWidget.ChangeSet.Between(before, after);
        var payload = changes.ToPayload();
        var json = JsonSerializer.Serialize(payload, options);
        json.ShouldContain("\"member\":\"Value\"");

        var back = JsonSerializer.Deserialize<NamingWidget.ChangeSetPayload>(json, options)!.ToChangeSet();
        NamingWidget.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void MalformedJsonFails()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.Patch>("""{"Nope":{"kind":"remove"}}""")
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.Patch>("""{"Label":{"kind":"bogus"}}""")
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.Patch>("""{"Label":{"kind":"set"}}""")
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.Patch>("""{"Label":{"kind":"remove","value":1}}""")
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.Patch>("""{"Label":{"kind":"unset"}}""")
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Settings.Patch>("""{"$whole":{"kind":"set"}}""")
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<KeyedServerHolder.Patch>("""{"Items":{"added":"nope"}}""")
        );
    }

    [Test]
    public void NoPublicJsonHelpers()
    {
        typeof(Settings.Patch)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name)
            .ShouldNotContain(name => name.Contains("SerializeJson") || name.Contains("DeserializeJson"));
        typeof(Settings.ChangeSet)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name)
            .ShouldNotContain(name => name.Contains("SerializeJson") || name.Contains("DeserializeJson"));
    }
}
