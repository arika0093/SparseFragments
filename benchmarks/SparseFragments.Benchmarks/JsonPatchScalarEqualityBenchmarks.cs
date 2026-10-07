using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

public enum JsonScalarKind
{
    String,
    Boolean,
    Number,
}

[MemoryDiagnoser]
public class JsonPatchScalarEqualityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(JsonScalarKind.String, JsonScalarKind.Boolean, JsonScalarKind.Number)]
    public JsonScalarKind Kind { get; set; }

    [Params(false, true)]
    public bool Changed { get; set; }

    private JsonObject _before = null!;
    private JsonObject _after = null!;

    [GlobalSetup]
    public void Setup()
    {
        var source = new JsonObject();
        for (var index = 0; index < Size; index++)
            source["key" + index] = Scalar(false);
        _before = (JsonObject)JsonNode.Parse(source.ToJsonString())!;
        _after = (JsonObject)_before.DeepClone();
        if (Changed)
            _after["key" + (Size - 1)] = Scalar(true);
        var patch = Diff();
        var operations = (JsonArray)JsonNode.Parse(patch)!;
        var applied = SparseJsonPatchBridge.Apply(
            _before,
            false,
            patch,
            StringComparison.Ordinal,
            out var absent
        );
        if (
            operations.Count != (Changed ? 1 : 0)
            || absent
            || !JsonNode.DeepEquals(applied, _after)
        )
            throw new InvalidOperationException(
                "Scalar JSON equality must preserve the expected diff."
            );

        foreach (
            var pair in new[]
            {
                ("1", "1.0", true),
                ("10e-1", "1", true),
                ("true", "false", false),
                ("true", "1", false),
                ("1", "\"1\"", false),
                ("\"value\"", "\"Value\"", false),
                ("null", "false", false),
            }
        )
        {
            var diff = SparseJsonPatchBridge.Diff(
                JsonNode.Parse(pair.Item1),
                false,
                JsonNode.Parse(pair.Item2),
                false
            );
            if ((((JsonArray)JsonNode.Parse(diff)!).Count == 0) != pair.Item3)
                throw new InvalidOperationException(
                    "JSON equality must preserve type and numerical semantics."
                );
        }
    }

    private JsonNode Scalar(bool changed) =>
        JsonNode.Parse(
            Kind switch
            {
                JsonScalarKind.String => changed ? "\"changed\"" : "\"value\"",
                JsonScalarKind.Boolean => changed ? "false" : "true",
                _ => changed ? "2" : "1",
            }
        )!;

    [Benchmark]
    public byte[] Diff() => SparseJsonPatchBridge.Diff(_before, false, _after, false);
}
