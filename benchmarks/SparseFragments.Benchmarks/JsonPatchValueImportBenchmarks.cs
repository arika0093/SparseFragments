using System.Text;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

public enum JsonPatchValueShape
{
    Number,
    String,
    Object,
    Array,
    Null,
}

/// <summary>Measures JSON Patch value materialization and cloning across JSON value kinds.</summary>
[MemoryDiagnoser]
public class JsonPatchValueImportBenchmarks
{
    [Params(16, 128)]
    public int OperationCount { get; set; }

    [Params(
        JsonPatchValueShape.Number,
        JsonPatchValueShape.String,
        JsonPatchValueShape.Object,
        JsonPatchValueShape.Array,
        JsonPatchValueShape.Null
    )]
    public JsonPatchValueShape Shape { get; set; }

    private JsonObject _baseline = null!;
    private byte[] _operations = null!;

    [GlobalSetup]
    public void Setup()
    {
        var value = Shape switch
        {
            JsonPatchValueShape.Number => "10e-1",
            JsonPatchValueShape.String => "\"escaped\\\"value\\n\"",
            JsonPatchValueShape.Object => """{"nested":{"value":1},"items":[true,null,"x"]}""",
            JsonPatchValueShape.Array => """[1,{"value":"x"},null]""",
            _ => "null",
        };
        _baseline = new JsonObject { ["value"] = 0 };
        var operations = new StringBuilder("[");
        for (var index = 0; index < OperationCount; index++)
        {
            if (index > 0)
            {
                operations.Append(',');
            }

            operations
                .Append("{\"op\":\"replace\",\"path\":\"/value\",\"value\":")
                .Append(value)
                .Append('}');
        }

        operations
            .Append(", {\"op\":\"test\",\"path\":\"/value\",\"value\":")
            .Append(value)
            .Append("}]");
        _operations = Encoding.UTF8.GetBytes(operations.ToString());
        var actual = Apply();
        if (
            actual is not JsonObject result
            || !result.ContainsKey("value")
            || !JsonNode.DeepEquals(result["value"], JsonNode.Parse(value))
            || _baseline["value"]?.GetValue<int>() != 0
        )
        {
            throw new InvalidOperationException(
                "All JSON value kinds must survive patch parsing and application without mutating the baseline."
            );
        }
    }

    [Benchmark]
    public JsonNode? Apply() =>
        SparseJsonPatchBridge.Apply(
            _baseline,
            baselineIsAbsent: false,
            _operations,
            StringComparison.Ordinal,
            out _
        );
}
