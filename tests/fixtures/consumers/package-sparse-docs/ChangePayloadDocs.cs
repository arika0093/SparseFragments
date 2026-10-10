using System.Text.Json;
using SparseFragments;

// Compile-checked mirrors of docs/change-payload.md (#198).
// Each `// sample: <id>` region matches the same-id fenced block in the guide
// exactly after normalization (using-lines, blank lines, and DocsCheck
// assertions are ignored); verify-docs-samples.sh fails on drift. Regions
// execute as part of Run() so documented JSON shapes and conversion rules are
// verified, not just compiled. Placeholder values only: no secret plaintext
// appears in these samples (#118).
public static class ChangePayloadDocsSamples
{
    public static void Run()
    {
        ScalarSet();
        ExplicitNull();
        Remove();
        Nested();
        Keyed();
        DictionaryItems();
        WholeRoot();
        Command();
        Conversions();
        Mixed();
        Invert();
        VersionGuard();
    }

    private static void ScalarSet()
    {
        // sample: payload-scalar-set
        var setBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var setAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
        );

        var setPayload = WireSettings.ChangeSet.Between(setBefore, setAfter).ToPayload();
        var setJson = JsonSerializer.Serialize(setPayload);
        // setJson == {"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}]}
        var setRestored = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(setJson)!
            .ToChangeSet();
        // setRestored.ToPatch().Apply(setBefore) replays setAfter
        // /sample
        DocsCheck.Require(
            setJson
                == """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}]}""",
            "scalar set emits the documented envelope"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(setRestored.ToPatch().Apply(setBefore), setAfter).IsEmpty,
            "deserialized scalar set replays the transition"
        );
        DocsCheck.Require(
            setJson == ChangePayloadJsonSpecimens.Envelope,
            "minimal envelope matches the layout reference"
        );
        var setAddBefore = Optional<WireSettings.Fragment?>.Present(
            new WireSettings.Fragment { RetryCount = 1 }
        );
        var setAddAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
        );
        var setAddJson = JsonSerializer.Serialize(
            WireSettings.ChangeSet.Between(setAddBefore, setAddAfter).ToPayload()
        );
        DocsCheck.Require(
            setAddJson == ChangePayloadJsonSpecimens.ScalarAdd,
            "addition emits the documented envelope"
        );
        var setAddRestored = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(setAddJson)!
            .ToChangeSet();
        DocsCheck.Require(
            WireSettings.Patch.Between(setAddRestored.ToPatch().Apply(setAddBefore), setAddAfter)
                .IsEmpty,
            "deserialized addition replays the transition"
        );
    }

    private static void ExplicitNull()
    {
        // sample: payload-explicit-null
        var nullBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var nullAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = null, RetryCount = 1 })
        );

        var nullPayload = WireSettings.ChangeSet.Between(nullBefore, nullAfter).ToPayload();
        var nullJson = JsonSerializer.Serialize(nullPayload);
        // nullJson == {"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"null","value":null}}]}
        var nullRestored = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(nullJson)!
            .ToChangeSet();
        // nullRestored.ToPatch().Apply(nullBefore) replays nullAfter
        // /sample
        DocsCheck.Require(
            nullJson
                == """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"null","value":null}}]}""",
            "explicit null emits the documented endpoint"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(nullRestored.ToPatch().Apply(nullBefore), nullAfter).IsEmpty,
            "deserialized explicit null replays the transition"
        );
    }

    private static void Remove()
    {
        // sample: payload-remove
        var removeBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var removeAfter = Optional<WireSettings.Fragment?>.Present(
            new WireSettings.Fragment { RetryCount = 1 }
        );

        var removePayload = WireSettings.ChangeSet.Between(removeBefore, removeAfter).ToPayload();
        var removeJson = JsonSerializer.Serialize(removePayload);
        // removeJson == {"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"missing","value":null}}]}
        var removeRestored = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(removeJson)!
            .ToChangeSet();
        // !removeRestored.ToPatch().Apply(removeBefore).Value!.Label.IsPresent
        // /sample
        DocsCheck.Require(
            removeJson
                == """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"missing","value":null}}]}""",
            "removal emits the documented endpoint"
        );
        DocsCheck.Require(
            !removeRestored.ToPatch().Apply(removeBefore).Value!.Label.IsPresent,
            "deserialized removal drops the contribution"
        );
    }

    private static void Nested()
    {
        // sample: payload-nested
        var nestedBefore = Optional<WireOrder.Fragment?>.Present(
            WireOrder.Fragment.From(
                new WireOrder { Name = "a", Customer = new WireCustomer { Name = "Ann" } }
            )
        );
        var nestedAfter = Optional<WireOrder.Fragment?>.Present(
            WireOrder.Fragment.From(
                new WireOrder { Name = "a", Customer = new WireCustomer { Name = "Bob" } }
            )
        );

        var nestedPayload = WireOrder.ChangeSet.Between(nestedBefore, nestedAfter).ToPayload();
        var nestedJson = JsonSerializer.Serialize(nestedPayload);
        // nestedJson carries member "Customer" with a nested changes array and no inner version
        var nestedRestored = JsonSerializer
            .Deserialize<WireOrder.ChangePayload>(nestedJson)!
            .ToChangeSet();
        // nestedRestored.Customer.Name.After.Value == "Bob"
        // /sample
        DocsCheck.Require(
            nestedJson
                == """{"version":"0.1","changes":[{"member":"Customer","nested":{"changes":[{"member":"Name","before":{"state":"value","value":"Ann"},"after":{"state":"value","value":"Bob"}}]}}]}""",
            "nested change emits the documented envelope"
        );
        DocsCheck.Require(
            nestedRestored.Customer.Name.After.Value == "Bob",
            "deserialized nested change preserves the leaf transition"
        );
        DocsCheck.Require(
            nestedJson == ChangePayloadJsonSpecimens.Nested,
            "nested fence matches serializer output"
        );
    }

    private static void Keyed()
    {
        // sample: payload-keyed
        var keyedBefore = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "a", Host = "A" },
                        new WireServer { Id = "b", Host = "B" },
                    },
                }
            )
        );
        var keyedAfter = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "b", Host = "B2" },
                        new WireServer { Id = "c", Host = "C" },
                    },
                }
            )
        );

        var keyedPayload = WireFleet.ChangeSet.Between(keyedBefore, keyedAfter).ToPayload();
        var keyedJson = JsonSerializer.Serialize(keyedPayload);
        // keyedJson carries one edit item, one add item, one remove item,
        // plus beforeOrder ["a","b"] and afterOrder ["b","c"]
        var keyedRestored = JsonSerializer
            .Deserialize<WireFleet.ChangePayload>(keyedJson)!
            .ToChangeSet();
        // keyedRestored.Servers.GetChange("b").IsEdited == true
        // keyedRestored.Servers.GetChange("c").IsAdded == true
        // keyedRestored.Servers.GetChange("a").IsRemoved == true
        // /sample
        DocsCheck.Require(
            keyedJson == ChangePayloadJsonSpecimens.Keyed,
            "keyed change emits the documented envelope with final indexes"
        );
        DocsCheck.Require(
            !keyedJson.Contains("\"before\":null")
                && !keyedJson.Contains("\"after\":null")
                && !keyedJson.Contains("\"edit\":null"),
            "unused item fields stay omitted"
        );
        DocsCheck.Require(
            keyedRestored.Servers.GetChange("b").IsEdited
                && keyedRestored.Servers.GetChange("c").IsAdded
                && keyedRestored.Servers.GetChange("a").IsRemoved,
            "deserialized keyed change preserves add/edit/remove"
        );
        var reorderBefore = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "a", Host = "A" },
                        new WireServer { Id = "b", Host = "B" },
                    },
                }
            )
        );
        var reorderAfter = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "b", Host = "B" },
                        new WireServer { Id = "a", Host = "A" },
                    },
                }
            )
        );
        var reorderJson = JsonSerializer.Serialize(
            WireFleet.ChangeSet.Between(reorderBefore, reorderAfter).ToPayload()
        );
        DocsCheck.Require(
            reorderJson == ChangePayloadJsonSpecimens.Reorder,
            "pure reorder emits the documented envelope"
        );
        var reorderRestored = JsonSerializer
            .Deserialize<WireFleet.ChangePayload>(reorderJson)!
            .ToChangeSet();
        DocsCheck.Require(
            reorderRestored.Servers.AfterOrder.SequenceEqual(new[] { "b", "a" }),
            "deserialized reorder preserves the key sequence"
        );
    }

    private static void DictionaryItems()
    {
        var dictBefore = Optional<WireScores.Fragment?>.Present(
            WireScores.Fragment.From(
                new WireScores { Scores = new() { ["a"] = 1, ["b"] = 2 } }
            )
        );
        var dictAfter = Optional<WireScores.Fragment?>.Present(
            WireScores.Fragment.From(
                new WireScores { Scores = new() { ["b"] = 3, ["c"] = 4 } }
            )
        );
        var dictJson = JsonSerializer.Serialize(
            WireScores.ChangeSet.Between(dictBefore, dictAfter).ToPayload()
        );
        DocsCheck.Require(
            dictJson == ChangePayloadJsonSpecimens.Dictionary,
            "dictionary change emits the documented envelope"
        );
        var dictRestored = JsonSerializer
            .Deserialize<WireScores.ChangePayload>(dictJson)!
            .ToChangeSet();
        DocsCheck.Require(
            WireScores.Patch.Between(dictRestored.ToPatch().Apply(dictBefore), dictAfter).IsEmpty,
            "deserialized dictionary change replays the transition"
        );
    }

    private static void WholeRoot()
    {
        var rootBefore = Optional<WireSettings.Fragment?>.Missing;
        var rootAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "present", RetryCount = 3 })
        );
        var rootJson = JsonSerializer.Serialize(
            WireSettings.ChangeSet.Between(rootBefore, rootAfter).ToPayload()
        );
        DocsCheck.Require(
            rootJson == ChangePayloadJsonSpecimens.Root,
            "whole-root transition emits the documented envelope"
        );
        var rootRestored = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(rootJson)!
            .ToChangeSet();
        DocsCheck.Require(
            WireSettings.Patch.Between(rootRestored.ToPatch().Apply(rootBefore), rootAfter)
                .IsEmpty,
            "deserialized whole-root transition replays"
        );
    }

    private static void Command()
    {
        // sample: payload-command
        var rotation = new WireSecret.Patch { Password = "rotated-value" };

        // A command needs no baseline: the envelope redacts what it never observed.
        var command = WireSecret.ChangePayload.FromPatch(rotation);
        var commandJson = JsonSerializer.Serialize(command);
        // commandJson == {"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated-value"}}]}
        var applied = WireSecret
            .Fragment.From(new WireSecret { DisplayName = "a", Password = "previous-placeholder" })
            .Apply(command.ToPatch());
        // applied.Password.Value == "rotated-value"
        // /sample
        DocsCheck.Require(
            commandJson
                == """{"version":"0.1","changes":[{"member":"Password","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated-value"}}]}""",
            "baseline-free command emits the documented envelope"
        );
        DocsCheck.Require(
            applied.Password.Value == "rotated-value",
            "command patch applies without a baseline"
        );
        var rejected = false;
        try
        {
            command.ToChangeSet();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        DocsCheck.Require(rejected, "redacted command cannot form a ChangeSet");
        var staticRejected = false;
        try
        {
            WireSecret.ChangeSet.FromPayload(command);
        }
        catch (ArgumentException)
        {
            staticRejected = true;
        }
        DocsCheck.Require(staticRejected, "FromPayload rejects redacted envelopes");
    }

    private static void Conversions()
    {
        // sample: payload-conversions
        var convertBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var convertAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
        );

        // ChangeSet -> envelope -> ChangeSet keeps the before-state.
        var transition = WireSettings.ChangeSet.Between(convertBefore, convertAfter);
        var envelope = transition.ToPayload();
        var roundTripped = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(JsonSerializer.Serialize(envelope))!
            .ToChangeSet();
        // roundTripped.ToPatch().Apply(convertBefore) replays convertAfter

        // ChangeSet -> Patch discards the before-state explicitly.
        // Patch + known baseline -> ChangeSet reattaches it.
        var operations = transition.ToPatch();
        var reattached = WireSettings.ChangeSet.FromPatch(convertBefore, operations);
        // reattached.ToPatch().Apply(convertBefore) replays convertAfter

        // Envelope -> ChangeSet requires complete history: FromPayload rejects redacted envelopes.
        var complete = WireSettings.ChangeSet.FromPayload(envelope);
        // complete.ToPatch().Apply(convertBefore) replays convertAfter
        // /sample
        DocsCheck.Require(
            WireSettings.Patch.Between(roundTripped.ToPatch().Apply(convertBefore), convertAfter)
                .IsEmpty,
            "envelope round trip preserves the transition"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(reattached.ToPatch().Apply(convertBefore), convertAfter)
                .IsEmpty,
            "reattached baseline replays the transition"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(complete.ToPatch().Apply(convertBefore), convertAfter).IsEmpty,
            "FromPayload accepts complete envelopes"
        );
    }

    private static void Mixed()
    {
        // sample: payload-mixed
        var mixed = JsonSerializer.Deserialize<WireSecret.ChangePayload>(
            """{"version":"0.1","changes":[{"member":"DisplayName","before":{"state":"value","value":"a"},"after":{"state":"value","value":"b"}},{"member":"Password","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated-value"}}]}"""
        )!;
        var mixedCurrent = new WireSecret
        {
            DisplayName = "a",
            Password = "current-placeholder",
        };
        if (!mixed.TryApplyMixedTo(mixedCurrent, out var mixedUpdated, out var mixedOutcome))
        {
            throw new InvalidOperationException("The change conflicts with the current model.");
        }

        // mixedUpdated.DisplayName == "b"
        // mixedUpdated.Password == "rotated-value"
        // mixedOutcome.WriteOnlyPaths reports ["Password"]
        // /sample
        DocsCheck.Require(
            mixedUpdated!.DisplayName == "b" && mixedUpdated.Password == "rotated-value",
            "mixed request applies the rebased transition and the write-only set"
        );
        DocsCheck.Require(
            mixedOutcome.WriteOnlyPaths.SequenceEqual(new[] { "Password" }),
            "mixed outcome names the write-only path"
        );
    }

    private static void Invert()
    {
        // sample: payload-invert
        var invertBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var invertAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
        );
        var invertible = WireSettings.ChangeSet.Between(invertBefore, invertAfter).ToPayload();

        // Fully baseline-aware envelopes invert with nothing skipped.
        var rollback = invertible.InvertReversibleChanges(out var skipped);
        // skipped is empty
        // rollback.ToPatch().Apply(invertAfter) walks back to invertBefore

        // Write-only members have no prior value, so they stay out of the rollback.
        var writeOnly = WireSecret.ChangePayload.FromPatch(
            new WireSecret.Patch { Password = "rotated-value" }
        );
        var partial = writeOnly.InvertReversibleChanges(out var skippedWriteOnly);
        // skippedWriteOnly reports ["Password"]
        // partial carries no member change
        // /sample
        DocsCheck.Require(skipped.Count == 0, "complete envelope inverts with nothing skipped");
        DocsCheck.Require(
            WireSettings.Patch.Between(rollback.ToPatch().Apply(invertAfter), invertBefore).IsEmpty,
            "inverted envelope walks back"
        );
        DocsCheck.Require(
            skippedWriteOnly.SequenceEqual(new[] { "Password" }),
            "write-only path is reported as skipped"
        );
        DocsCheck.Require(partial.IsEmpty, "write-only envelope has no reversible change");
    }

    private static void VersionGuard()
    {
        // sample: payload-version
        var guarded = WireSettings
            .ChangeSet.Between(
                Optional<WireSettings.Fragment?>.Present(
                    WireSettings.Fragment.From(new WireSettings { Label = "before" })
                ),
                Optional<WireSettings.Fragment?>.Present(
                    WireSettings.Fragment.From(new WireSettings { Label = "after" })
                )
            )
            .ToPayload();
        guarded.Version = "0.2";

        // Every interpretation path rejects the same unknown version before touching model state.
        var versionRejected = 0;
        try
        {
            guarded.ToChangeSet();
        }
        catch (ArgumentException)
        {
            versionRejected++;
        }
        try
        {
            guarded.ToPatch();
        }
        catch (ArgumentException)
        {
            versionRejected++;
        }
        // versionRejected == 2
        // /sample
        DocsCheck.Require(versionRejected == 2, "unknown version rejected on every path");
    }
}

// sample: payload-models
[SparseFragmentModel]
public partial class WireSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}

[SparseFragmentModel]
public partial class WireOrder
{
    public string? Name { get; set; }

    public WireCustomer? Customer { get; set; }
}

public partial class WireCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class WireFleet
{
    public List<WireServer> Servers { get; set; } = new();
}

public partial class WireServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class WireSecret
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}
// /sample

[SparseFragmentModel]
public partial class WireScores
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

// Fixture-generated JSON specimens for the wire reference (#212).
// Each json-sample region below holds the exact serializer output that the
// same-id fenced block in docs/change-payload.md must match deeply (array
// order significant); the DocsCheck assertions above compare the live
// serializer output against these constants byte for byte, so a one-field
// corruption on either side fails.
public static class ChangePayloadJsonSpecimens
{
    // json-sample: payload-envelope
    public const string Envelope = """{"version":"0.1","changes":[{"member":"Label","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}]}""";
    // /json-sample

    // json-sample: payload-scalar-add
    public const string ScalarAdd = """{"version":"0.1","changes":[{"member":"Label","before":{"state":"missing","value":null},"after":{"state":"value","value":"after"}}]}""";
    // /json-sample

    // json-sample: payload-nested-json
    public const string Nested = """{"version":"0.1","changes":[{"member":"Customer","nested":{"changes":[{"member":"Name","before":{"state":"value","value":"Ann"},"after":{"state":"value","value":"Bob"}}]}}]}""";
    // /json-sample

    // json-sample: payload-keyed-json
    public const string Keyed = """{"version":"0.1","changes":[{"member":"Servers","items":[{"key":"b","kind":"edit","beforeIndex":1,"afterIndex":0,"edit":{"changes":[{"member":"Host","before":{"state":"value","value":"B"},"after":{"state":"value","value":"B2"}}]}},{"key":"c","kind":"add","beforeIndex":-1,"afterIndex":1,"after":{"state":"value","value":{"Id":"c","Host":"C"}}},{"key":"a","kind":"remove","beforeIndex":0,"afterIndex":-1,"before":{"state":"value","value":{"Id":"a","Host":"A"}}}],"beforeOrder":["a","b"],"afterOrder":["b","c"]}]}""";
    // /json-sample

    // json-sample: payload-reorder
    public const string Reorder = """{"version":"0.1","changes":[{"member":"Servers","items":[{"key":"b","kind":"reorder","beforeIndex":1,"afterIndex":0,"isReordered":true},{"key":"a","kind":"reorder","beforeIndex":0,"afterIndex":1,"isReordered":true}],"beforeOrder":["a","b"],"afterOrder":["b","a"]}]}""";
    // /json-sample

    // json-sample: payload-dict
    public const string Dictionary = """{"version":"0.1","changes":[{"member":"Scores","items":[{"key":"c","kind":"add","after":{"state":"value","value":4}},{"key":"a","kind":"remove","before":{"state":"value","value":1}},{"key":"b","kind":"edit","before":{"state":"value","value":2},"after":{"state":"value","value":3}}]}]}""";
    // /json-sample

    // json-sample: payload-root
    public const string Root = """{"version":"0.1","changes":[{"member":"$root","before":{"state":"missing","value":null},"after":{"state":"value","value":{"members":[{"member":"Label","value":{"state":"value","value":"present"}},{"member":"RetryCount","value":{"state":"value","value":3}}]}}}]}""";
    // /json-sample
}
