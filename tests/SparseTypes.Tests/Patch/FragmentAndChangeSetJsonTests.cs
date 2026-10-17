using System.Text.Json;
using SparseTypes;

namespace SparseTypes.Tests;

[SparseModel]
public partial class NamingWidget
{
    [System.Text.Json.Serialization.JsonPropertyName("customName")]
    public string? Value { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("a/b")]
    public int Slash { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Plain { get; set; }
}

[SparseModel]
public partial class WideDispatchWidget
{
    public int Alpha { get; set; }
    public int Bravo { get; set; }
    public int Charlie { get; set; }
    public int Delta { get; set; }
    public int Echo { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("fixedName")]
    public int Explicit { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    [System.Text.Json.Serialization.JsonPropertyName("Alpha_1")]
    public int IgnoredAlias { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int Hidden { get; set; }
}

public sealed class CountingSuffixNamingPolicy : JsonNamingPolicy
{
    public string Suffix { get; set; } = "_1";

    public int ConversionCount { get; private set; }

    public void ResetCount() => ConversionCount = 0;

    public override string ConvertName(string name)
    {
        ConversionCount++;
        return name + Suffix;
    }
}

public sealed class DuplicateNameNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) => "duplicate";
}

public sealed class FragmentAndChangeSetJsonTests
{
    [Test]
    public void WideFragmentReadBuildsEffectiveNamesOnceAndHonorsVisiblePriority()
    {
        var namingPolicy = new CountingSuffixNamingPolicy();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = namingPolicy,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var reader = new System.Text.Json.Utf8JsonReader(
            System.Text.Encoding.UTF8.GetBytes(
                """{"alpha_1":1,"Bravo_1":2,"Charlie_1":3,"Delta_1":4,"Echo_1":5,"fixedName":6,"hidden_1":7,"unknown":{"nested":true}}"""
            )
        );
        reader.Read().ShouldBeTrue();

        var fragment = WideDispatchWidget.Fragment.JsonConverter.Read(
            ref reader,
            typeof(WideDispatchWidget.Fragment),
            options
        )!;

        namingPolicy.ConversionCount.ShouldBe(6);
        fragment.Alpha.Value.ShouldBe(1);
        fragment.Bravo.Value.ShouldBe(2);
        fragment.Charlie.Value.ShouldBe(3);
        fragment.Delta.Value.ShouldBe(4);
        fragment.Echo.Value.ShouldBe(5);
        fragment.Explicit.Value.ShouldBe(6);
        fragment.IgnoredAlias.IsPresent.ShouldBeFalse();
        fragment.Hidden.IsPresent.ShouldBeFalse();

        namingPolicy.Suffix = "_2";
        namingPolicy.ResetCount();
        var nextReader = new System.Text.Json.Utf8JsonReader(
            System.Text.Encoding.UTF8.GetBytes(
                """{"Alpha_2":8,"Bravo_2":9,"Charlie_2":10,"Delta_2":11,"Echo_2":12,"fixedName":13,"Hidden_2":14}"""
            )
        );
        nextReader.Read().ShouldBeTrue();

        var next = WideDispatchWidget.Fragment.JsonConverter.Read(
            ref nextReader,
            typeof(WideDispatchWidget.Fragment),
            options
        )!;

        namingPolicy.ConversionCount.ShouldBe(6);
        next.Alpha.Value.ShouldBe(8);
        next.Explicit.Value.ShouldBe(13);
        next.Hidden.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void WideFragmentReadRejectsVisibleCollisionsAndDisallowedUnknownNames()
    {
        var collisionOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new DuplicateNameNamingPolicy(),
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        Should.Throw<JsonException>(() => ReadWideFragment("{}", collisionOptions));

        var strictOptions = new JsonSerializerOptions
        {
            UnmappedMemberHandling = System
                .Text
                .Json
                .Serialization
                .JsonUnmappedMemberHandling
                .Disallow,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        Should.Throw<JsonException>(() =>
            ReadWideFragment("""{"unknown":{"nested":true}}""", strictOptions)
        );
    }

    private static WideDispatchWidget.Fragment ReadWideFragment(
        string json,
        JsonSerializerOptions options
    )
    {
        var reader = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read();
        return WideDispatchWidget.Fragment.JsonConverter.Read(
            ref reader,
            typeof(WideDispatchWidget.Fragment),
            options
        )!;
    }

    [Test]
    public void FragmentWireNamesRoundTrip()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(NamingWidget.Fragment.JsonConverter);

        var fragment = new NamingWidget.Fragment
        {
            Value = Optional<string?>.Present("v"),
            Slash = Optional<int>.Present(7),
        };
        var json = JsonSerializer.Serialize(fragment, options);
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
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(NamingWidget.Fragment.JsonConverter);

        Optional<NamingWidget.Fragment?> State(NamingWidget model) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(model));
        var before = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 2,
            }
        );
        var after = State(
            new NamingWidget
            {
                Value = "b",
                Slash = 3,
                Plain = 2,
            }
        );

        var payload = NamingWidget.ChangeSet.Between(before, after).ToPayload();
        var json = JsonSerializer.Serialize(payload, options);
        json.ShouldContain("\"member\":\"Value\"");

        var restored = JsonSerializer
            .Deserialize<NamingWidget.ChangePayload>(json, options)!
            .ToChangeSet();
        NamingWidget.Patch.Between(restored.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void CachedIndexDoesNotGoStaleWhenSharedOptionsAreMutated()
    {
        // The cache key covers the policy reference and case handling rather
        // than the options identity, so mutating shared options between
        // direct converter calls must never reuse a stale member mapping.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        // Unknown-only reads skip member lookup, so the options stay mutable
        // while these repeated reads exercise the cached camelCase index.
        ReadWideFragment("""{"unknown":1}""", options).Alpha.IsPresent.ShouldBeFalse();
        ReadWideFragment("""{"unknown":2}""", options).Alpha.IsPresent.ShouldBeFalse();

        options.PropertyNamingPolicy = null;
        var identity = ReadWideFragment("""{"Alpha":3,"fixedName":4}""", options);
        identity.Alpha.Value.ShouldBe(3);
        identity.Explicit.Value.ShouldBe(4);
        ReadWideFragment("""{"Alpha":5}""", options).Alpha.Value.ShouldBe(5);
    }

    [Test]
    public void CachedIndexKeysCaseHandlingAcrossOptionsInstances()
    {
        var sensitive = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var insensitive = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        for (var i = 0; i < 3; i++)
        {
            ReadWideFragment("""{"ALPHA":1}""", sensitive).Alpha.IsPresent.ShouldBeFalse();
            ReadWideFragment("""{"ALPHA":2}""", insensitive).Alpha.Value.ShouldBe(2);
        }
    }

    [Test]
    public void CachedIndexSurvivesAlternatingOptionsInstances()
    {
        var camel = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        var identity = new JsonSerializerOptions
        {
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        for (var i = 0; i < 3; i++)
        {
            ReadWideFragment("""{"alpha":1}""", camel).Alpha.Value.ShouldBe(1);
            ReadWideFragment("""{"Alpha":2}""", identity).Alpha.Value.ShouldBe(2);
        }
    }

    [Test]
    public void ConcurrentReadsShareTheCachedIndex()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        System.Threading.Tasks.Parallel.For(
            0,
            64,
            _ => ReadWideFragment("""{"alpha":1,"fixedName":2}""", options).Alpha.Value.ShouldBe(1)
        );
    }

    [Test]
    public void CachedReadsAvoidPerReadIndexAllocations()
    {
        var cachedOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        const string cachedPayload =
            """{"alpha":1,"bravo":2,"charlie":3,"delta":4,"echo":5,"fixedName":6}""";
        for (var i = 0; i < 10; i++)
            ReadWideFragment(cachedPayload, cachedOptions);
        var cachedBytes = MeasureWideReadBytes(cachedPayload, cachedOptions, 200);

        // Custom policies may retain mutable state, so their indexes are
        // rebuilt on every read: the per-read allocation baseline.
        var uncachedOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new CountingSuffixNamingPolicy(),
            TypeInfoResolver =
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        const string uncachedPayload =
            """{"Alpha_1":1,"Bravo_1":2,"Charlie_1":3,"Delta_1":4,"Echo_1":5,"fixedName":6}""";
        for (var i = 0; i < 10; i++)
            ReadWideFragment(uncachedPayload, uncachedOptions);
        var uncachedBytes = MeasureWideReadBytes(uncachedPayload, uncachedOptions, 200);

        cachedBytes.ShouldBeLessThan(uncachedBytes);
    }

    private static long MeasureWideReadBytes(
        string json,
        JsonSerializerOptions options,
        int iterations
    )
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        var before = System.GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
            ReadWideFragment(bytes, options);
        return System.GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static WideDispatchWidget.Fragment ReadWideFragment(
        byte[] json,
        JsonSerializerOptions options
    )
    {
        var reader = new System.Text.Json.Utf8JsonReader(json);
        reader.Read();
        return WideDispatchWidget.Fragment.JsonConverter.Read(
            ref reader,
            typeof(WideDispatchWidget.Fragment),
            options
        )!;
    }

    [Test]
    public void IgnoredOnlyChangeFailsTransportInsteadOfGoingSilent()
    {
        Optional<NamingWidget.Fragment?> State(NamingWidget model) =>
            Optional<NamingWidget.Fragment?>.Present(NamingWidget.Fragment.From(model));
        var before = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 2,
            }
        );
        var after = State(
            new NamingWidget
            {
                Value = "a",
                Slash = 1,
                Plain = 3,
            }
        );

        // The only modified property is JSON-ignored: the ChangeSet is
        // nonempty, but its payload would travel empty.
        var ignoredOnly = NamingWidget.ChangeSet.Between(before, after);
        ignoredOnly.IsEmpty.ShouldBeFalse();
        var omitted = Should.Throw<InvalidOperationException>(() => ignoredOnly.ToPayload());
        omitted.Message.ShouldContain("Plain");

        // A mixed ignored/non-ignored edit fails too rather than partially
        // exporting.
        var mixed = NamingWidget.ChangeSet.Between(
            before,
            State(
                new NamingWidget
                {
                    Value = "b",
                    Slash = 1,
                    Plain = 3,
                }
            )
        );
        Should.Throw<InvalidOperationException>(() => mixed.ToPayload());

        // Patch-side export follows the same lossless rule.
        Should.Throw<InvalidOperationException>(() =>
            NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Plain = 3 })
        );
        NamingWidget.ChangePayload.FromPatch(new NamingWidget.Patch { Value = "b" });
    }
}
