using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SparseFragments;

namespace SparseFragments.JsonPatch.Tests;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class PatchAotContext : JsonSerializerContext { }

/// <summary>Source-generated metadata interop for standalone models.</summary>
public sealed class JsonPatchAotTests
{
    private static JsonSerializerOptions AotOptions() =>
        new() { TypeInfoResolver = PatchAotContext.Default };

    [Test]
    public void StandaloneRoundTripWithSourceGeneratedMetadata()
    {
        var baseline = new PatchWidget.Fragment
        {
            Name = Optional<string?>.Present("a"),
            Count = Optional<int>.Present(1),
            Tags = Optional<List<string>>.Present(new List<string> { "x" }),
            Nested = Optional<PatchNested.Fragment?>.Present(
                new PatchNested.Fragment { Host = Optional<string>.Present("h") }
            ),
        };
        var options = AotOptions();
        var patch = PatchWidget.Patch.FromJsonPatch(
            Optional<PatchWidget.Fragment?>.Present(baseline),
            PatchTestHelpers.Utf8("""[{"op":"replace","path":"/Name","value":"b"}]"""),
            options
        );
        baseline.Apply(patch).Name.Value.ShouldBe("b");

        var exported = patch.ToJsonPatch(
            Optional<PatchWidget.Fragment?>.Present(baseline),
            options
        );
        PatchTestHelpers.Text(exported).ShouldContain("/Name");
    }
}
