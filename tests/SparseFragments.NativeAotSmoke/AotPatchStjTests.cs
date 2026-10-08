using System.Text;
using System.Text.Json;

namespace SparseFragments.NativeAotSmoke;

public sealed class AotPatchStjTests
{
    private static JsonSerializerOptions AotOptions() =>
        new() { TypeInfoResolver = AotSerializerContext.Default };

    private static string WritePatch(AotWidget.Patch patch, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotWidget.Patch.PatchJsonConverter().Write(writer, patch, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotWidget.Patch ReadPatch(string json, JsonSerializerOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read())
        {
            throw new JsonException("Empty patch JSON.");
        }

        return new AotWidget.Patch.PatchJsonConverter().Read(ref reader, typeof(AotWidget.Patch), options);
    }

    private static string WriteServerPatch(AotServerHolder.Patch patch, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotServerHolder.Patch.PatchJsonConverter().Write(writer, patch, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AotServerHolder.Patch ReadServerPatch(string json, JsonSerializerOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read())
        {
            throw new JsonException("Empty patch JSON.");
        }

        return new AotServerHolder.Patch.PatchJsonConverter().Read(ref reader, typeof(AotServerHolder.Patch), options);
    }

    [Test]
    public async Task PatchEmptyRoundTripsWithSourceGen()
    {
        var options = AotOptions();
        var back = ReadPatch(WritePatch(new AotWidget.Patch(), options), options);

        await Assert.That(back.IsEmpty).IsTrue();
    }

    [Test]
    public async Task PatchScalarNestedCollectionRoundTripsWithSourceGen()
    {
        var options = AotOptions();
        var patch = new AotWidget.Patch { Name = "b", Count = 3 };
        patch.Nested.Host = "h2";
        patch.Plugins = new List<string> { "p1", "p2" };

        var back = ReadPatch(WritePatch(patch, options), options);

        await Assert.That(back.Name.Value).IsEqualTo("b");
        await Assert.That(back.Count.Value).IsEqualTo(3);
        await Assert.That(back.Nested.Host.Value).IsEqualTo("h2");
        await Assert.That(back.Plugins.Value!.SequenceEqual(["p1", "p2"])).IsTrue();
    }

    [Test]
    public async Task PatchNullAndUnsetRoundTripWithSourceGen()
    {
        var options = AotOptions();
        var nullPatch = new AotWidget.Patch { Name = (string?)null };
        var nullBack = ReadPatch(WritePatch(nullPatch, options), options);

        await Assert.That(nullBack.Name.Value is null).IsTrue();

        var unsetPatch = new AotWidget.Patch();
        unsetPatch.Nested.Unset();
        var unsetBack = ReadPatch(WritePatch(unsetPatch, options), options);
        var basis = Optional<AotWidget.Fragment?>.Present(
            AotWidget.Fragment.From(new AotWidget { Nested = new AotNested { Host = "x" } })
        );

        await Assert.That(!unsetBack.Apply(basis).Value!.Nested.IsPresent).IsTrue();
    }

    [Test]
    public async Task KeyedPatchRoundTripsWithSourceGen()
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
        var patch = AotServerHolder.Patch.Between(before, after);
        var back = ReadServerPatch(WriteServerPatch(patch, options), options);

        await Assert.That(AotServerHolder.Patch.Between(back.Apply(before), after).IsEmpty).IsTrue();

        var reorderAfter = State(
            new AotServerHolder
            {
                Items = [new AotServer { Id = "b", Name = "B" }, new AotServer { Id = "a", Name = "A" }],
            }
        );
        var reorder = AotServerHolder.Patch.Between(before, reorderAfter);
        var reorderBack = ReadServerPatch(WriteServerPatch(reorder, options), options);

        await Assert.That(AotServerHolder.Patch.Between(reorderBack.Apply(before), reorderAfter).IsEmpty).IsTrue();
    }

    [Test]
    public async Task MalformedPatchFailsWithSourceGen()
    {
        var options = AotOptions();
        var failed = false;
        try
        {
            _ = ReadPatch("""{"Nope":{"kind":"unset"}}""", options);
        }
        catch (JsonException)
        {
            failed = true;
        }

        await Assert.That(failed).IsTrue();
    }
}
