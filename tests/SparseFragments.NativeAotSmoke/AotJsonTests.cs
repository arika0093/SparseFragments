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

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    // Serializes through the generated converter directly (the same path the
    // JSON Patch bridge uses), which stays trim/NativeAOT clean unlike the
    // reflection-dispatched JsonSerializer.Serialize overloads.
    private static string ToCanonicalJson(AotWidget.Fragment fragment, JsonSerializerOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            new AotWidget.Fragment.FragmentJsonConverter().Write(writer, fragment, options);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
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
    public async Task FromJsonPatchAppliesWithSourceGeneratedMetadata()
    {
        var baseline = JsonBaseline();
        var jsonPatch = AotWidget.Patch.FromJsonPatch(
            Optional<AotWidget.Fragment?>.Present(baseline),
            Utf8("""[{"op":"replace","path":"/Name","value":"b"}]"""),
            AotOptions()
        );

        await Assert.That(baseline.Apply(jsonPatch).Name.Value).IsEqualTo("b");
    }

    [Test]
    public async Task ToJsonPatchExportsMemberPath()
    {
        var baseline = JsonBaseline();
        var jsonPatch = AotWidget.Patch.FromJsonPatch(
            Optional<AotWidget.Fragment?>.Present(baseline),
            Utf8("""[{"op":"replace","path":"/Name","value":"b"}]"""),
            AotOptions()
        );
        var exported = Encoding.UTF8.GetString(
            jsonPatch.ToJsonPatch(Optional<AotWidget.Fragment?>.Present(baseline), AotOptions()).ToArray()
        );

        await Assert.That(exported.Contains("/Name")).IsTrue();
    }

    [Test]
    public async Task JsonBridgeFailsFastWithoutResolverWhenReflectionIsDisabled()
    {
        var failedFast = false;
        try
        {
            _ = AotWidget.Patch.FromJsonPatch(
                Optional<AotWidget.Fragment?>.Present(JsonBaseline()),
                Utf8("""[{"op":"replace","path":"/Name","value":"c"}]""")
            );
        }
        catch (InvalidOperationException)
        {
            failedFast = true;
        }

        await Assert.That(failedFast).IsTrue();
    }
}
