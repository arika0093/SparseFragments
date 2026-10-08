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
    // Patch converter uses for fragments), which stays trim/NativeAOT
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

}
