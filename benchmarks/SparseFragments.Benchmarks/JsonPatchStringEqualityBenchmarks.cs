using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

[MemoryDiagnoser]
public class JsonPatchStringEqualityBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Parsed { get; set; }

    [Params(false, true)]
    public bool Escaped { get; set; }

    private JsonObject _before = null!;
    private JsonObject _after = null!;

    [GlobalSetup]
    public void Setup()
    {
        var text = Escaped ? new string('x', 512) + "\n\"\\日本語😀" : "value";
        _before = new JsonObject();
        _after = new JsonObject();
        var encoded = JsonSerializer.Serialize(text);
        for (var index = 0; index < Size; index++)
        {
            _before["key" + index] = Parsed ? JsonNode.Parse(encoded) : JsonValue.Create(text);
            _after["key" + index] = Parsed
                ? JsonNode.Parse(encoded)
                : JsonValue.Create(new string(text.ToCharArray()));
        }
        if (((JsonArray)JsonNode.Parse(Diff())!).Count != 0)
            throw new InvalidOperationException("Equivalent strings must produce an empty diff.");

        foreach (
            var pair in new[]
            {
                ("\"a\\u0062\"", "\"ab\"", true),
                ("\"a\\nb\"", "\"a\\u000ab\"", true),
                ("\"\\u65e5\\u672c\\ud83d\\ude00\"", "\"日本😀\"", true),
                ("\"a\"", "\"A\"", false),
                ("\"\"", "\" \"", false),
            }
        )
        {
            var left = (JsonValue)JsonNode.Parse(pair.Item1)!;
            var right = (JsonValue)JsonNode.Parse(pair.Item2)!;
            foreach (var leftView in new[] { left, JsonValue.Create(left.GetValue<string>())! })
            {
                foreach (
                    var rightView in new[] { right, JsonValue.Create(right.GetValue<string>())! }
                )
                {
                    var diff = SparseJsonPatchBridge.Diff(leftView, false, rightView, false);
                    if ((((JsonArray)JsonNode.Parse(diff)!).Count == 0) != pair.Item3)
                        throw new InvalidOperationException(
                            "Parsed, managed and mixed strings must preserve decoded ordinal equality."
                        );
                }
            }
        }
    }

    [Benchmark]
    public byte[] Diff() => SparseJsonPatchBridge.Diff(_before, false, _after, false);
}
