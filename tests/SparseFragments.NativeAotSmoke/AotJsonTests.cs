using System.Text;
using System.Text.Json;

namespace SparseFragments.NativeAotSmoke;

public sealed class AotJsonTests
{
    private static JsonSerializerOptions AotOptions()
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = AotSerializerContext.Default };
        options.Converters.Add(new AotWidget.Fragment.FragmentJsonConverter());
        return options;
    }

    // Serializes through the generated converter directly (the same path the
    // Patch/ChangeSet converters use for fragments), which stays trim/NativeAOT
    // clean unlike the reflection-dispatched JsonSerializer.Serialize overloads.
    private static string ToCanonicalJson(AotWidget.Fragment fragment, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotWidget.Fragment.FragmentJsonConverter().Write(writer, fragment, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteChangeSet(AotWidget.ChangeSet changes, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotWidget.ChangeSet.ChangeSetJsonConverter().Write(writer, changes, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotWidget.ChangeSet ReadChangeSet(string json, JsonSerializerOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read())
        {
            throw new JsonException("Empty change-set JSON.");
        }

        return new AotWidget.ChangeSet.ChangeSetJsonConverter().Read(ref reader, typeof(AotWidget.ChangeSet), options);
    }

    private static string WriteServerChangeSet(AotServerHolder.ChangeSet changes, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotServerHolder.ChangeSet.ChangeSetJsonConverter().Write(writer, changes, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotServerHolder.ChangeSet ReadServerChangeSet(string json, JsonSerializerOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read())
        {
            throw new JsonException("Empty change-set JSON.");
        }

        return new AotServerHolder.ChangeSet.ChangeSetJsonConverter().Read(ref reader, typeof(AotServerHolder.ChangeSet), options);
    }

    private static string WriteScoresChangeSet(AotIntCollections.ChangeSet changes, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotIntCollections.ChangeSet.ChangeSetJsonConverter().Write(writer, changes, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotIntCollections.ChangeSet ReadScoresChangeSet(string json, JsonSerializerOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read())
        {
            throw new JsonException("Empty change-set JSON.");
        }

        return new AotIntCollections.ChangeSet.ChangeSetJsonConverter().Read(ref reader, typeof(AotIntCollections.ChangeSet), options);
    }

    private static AotWidget.Fragment JsonBaseline() =>
        new()
        {
            Name = Optional<string?>.Present("a"),
            Count = Optional<int>.Present(1),
            Nested = Optional<AotNested.Fragment?>.Present(
                new AotNested.Fragment { Host = Optional<string>.Present("h") }
            ),
        };

    [Test]
    public async Task FragmentSerializesPresentMembersAndOmitsMissing()
    {
        var canonical = ToCanonicalJson(JsonBaseline(), AotOptions());

        await Assert.That(canonical.Contains("\"Name\":\"a\"")).IsTrue();
        await Assert.That(!canonical.Contains("Plugins")).IsTrue();
    }

    [Test]
    public async Task FragmentPreservesExplicitNullInJson()
    {
        var explicitNullFragment = new AotWidget.Fragment { Name = Optional<string?>.Present(null) };

        await Assert.That(ToCanonicalJson(explicitNullFragment, AotOptions()).Contains("\"Name\":null")).IsTrue();
    }

    [Test]
    public async Task ChangeSetJsonRoundTripsWithSourceGeneratedMetadata()
    {
        var options = AotOptions();
        var before = Optional<AotWidget.Fragment?>.Present(JsonBaseline());
        var after = Optional<AotWidget.Fragment?>.Present(
            new AotWidget.Fragment
            {
                Name = Optional<string?>.Present("b"),
                Count = Optional<int>.Present(1),
            }
        );
        var changes = AotWidget.ChangeSet.Between(before, after);
        var back = ReadChangeSet(WriteChangeSet(changes, options), options);

        await Assert.That(AotWidget.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task KeyedChangeSetJsonPreservesIdentityAndOrder()
    {
        var options = AotOptions();
        Optional<AotServerHolder.Fragment?> State(AotServerHolder m) =>
            Optional<AotServerHolder.Fragment?>.Present(AotServerHolder.Fragment.From(m));
        var before = State(
            new AotServerHolder
            {
                Items = [new AotServer { Id = "a", Name = "A" }, new AotServer { Id = "b", Name = "B" }],
            }
        );
        var after = State(
            new AotServerHolder
            {
                Items = [new AotServer { Id = "b", Name = "B2" }, new AotServer { Id = "c", Name = "C" }],
            }
        );
        var changes = AotServerHolder.ChangeSet.Between(before, after);
        var back = ReadServerChangeSet(WriteServerChangeSet(changes, options), options);
        var applied = back.ToPatch().Apply(before);

        await Assert.That(AotServerHolder.Patch.Between(applied, after).IsEmpty).IsTrue();
        await Assert.That(applied.Value!.Items.Value!.Select(static item => item.Id).SequenceEqual(["b", "c"])).IsTrue();
        await Assert.That(applied.Value!.Items.Value!.Single(static item => item.Id == "b").Name).IsEqualTo("B2");
    }

    [Test]
    public async Task SparseDictionaryChangeSetJsonRoundTrips()
    {
        var options = AotOptions();
        Optional<AotIntCollections.Fragment?> State(AotIntCollections m) =>
            Optional<AotIntCollections.Fragment?>.Present(AotIntCollections.Fragment.From(m));
        var before = State(new AotIntCollections { Scores = new() { ["a"] = 1, ["b"] = 2 } });
        var after = State(new AotIntCollections { Scores = new() { ["b"] = 3, ["c"] = 4 } });
        var changes = AotIntCollections.ChangeSet.Between(before, after);
        var json = WriteScoresChangeSet(changes, options);

        // Sparse wire carries only the semantic transition, not unrelated entries.
        await Assert.That(!json.Contains("999")).IsTrue();
        var back = ReadScoresChangeSet(json, options);

        await Assert.That(back.Scores.GetChange("b").IsEdited).IsTrue();
        await Assert.That(back.Scores.GetChange("c").IsAdded).IsTrue();
        await Assert.That(back.Scores.GetChange("a").IsRemoved).IsTrue();
        await Assert.That(back.Scores.GetChange("zzz").IsEmpty).IsTrue();
        await Assert.That(AotIntCollections.Patch.Between(back.ToPatch().Apply(before), after).IsEmpty).IsTrue();
    }

    [Test]
    public async Task ChangeSetJsonPreservesMissingAndNull()
    {
        var options = AotOptions();
        var missing = Optional<AotWidget.Fragment?>.Missing;
        var nullState = Optional<AotWidget.Fragment?>.Present(null);
        var value = Optional<AotWidget.Fragment?>.Present(
            new AotWidget.Fragment { Name = Optional<string?>.Present("v") }
        );
        foreach (var (b, a) in new[] { (missing, nullState), (nullState, value), (value, missing) })
        {
            var changes = AotWidget.ChangeSet.Between(b, a);
            var back = ReadChangeSet(WriteChangeSet(changes, options), options);

            await Assert.That(AotWidget.Patch.Between(back.ToPatch().Apply(b), a).IsEmpty).IsTrue();
        }
    }

    [Test]
    public async Task MalformedChangeSetJsonFailsWithSourceGen()
    {
        var options = AotOptions();
        var failed = false;
        try
        {
            _ = ReadChangeSet("""{"before":{"state":"bogus"},"after":{"state":"missing"}}""", options);
        }
        catch (JsonException)
        {
            failed = true;
        }

        await Assert.That(failed).IsTrue();
    }
}
