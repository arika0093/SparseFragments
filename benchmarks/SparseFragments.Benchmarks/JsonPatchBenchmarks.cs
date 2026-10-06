using System.Text;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

/// <summary>JSON Patch runtime benchmarks for issue #16: realistic patch sizes over the public bridge.</summary>
[MemoryDiagnoser]
public class JsonPatchBenchmarks
{
    private JsonNode? _baseline;
    private JsonNode? _largeBaseline;
    private JsonNode? _diffBefore;
    private JsonNode? _diffAfter;
    private ReadOnlyMemory<byte> _smallPatch;
    private ReadOnlyMemory<byte> _largePatch;

    [GlobalSetup]
    public void Setup()
    {
        _baseline = JsonNode.Parse(
            """{"name":"alpha","count":1,"enabled":true,"nested":{"host":"example","port":80},"tags":["a","b","c"]}"""
        );
        _smallPatch = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/name","value":"beta"},{"op":"add","path":"/tags/-","value":"d"},{"op":"remove","path":"/nested/port"},{"op":"move","from":"/count","path":"/nested/count"},{"op":"test","path":"/enabled","value":true}]"""
        );

        var baselineBuilder = new StringBuilder("{");
        var patchBuilder = new StringBuilder("[");
        for (var i = 0; i < 200; i++)
        {
            if (i > 0)
            {
                baselineBuilder.Append(',');
                patchBuilder.Append(',');
            }

            baselineBuilder.Append("\"p").Append(i).Append("\":").Append(i);
            patchBuilder
                .Append("{\"op\":\"replace\",\"path\":\"/p")
                .Append(i)
                .Append("\",\"value\":")
                .Append(i * 2)
                .Append('}');
        }

        baselineBuilder.Append('}');
        patchBuilder.Append(']');
        _largeBaseline = JsonNode.Parse(baselineBuilder.ToString());
        _largePatch = Encoding.UTF8.GetBytes(patchBuilder.ToString());

        _diffBefore = JsonNode.Parse(baselineBuilder.ToString());
        var afterBuilder = new StringBuilder("{");
        for (var i = 0; i < 205; i++)
        {
            if (i > 0)
            {
                afterBuilder.Append(',');
            }

            // Change the first 20 values and append 5 new members.
            var value = i < 20 ? i + 1000 : i;
            afterBuilder.Append("\"p").Append(i).Append("\":").Append(value);
        }

        afterBuilder.Append('}');
        _diffAfter = JsonNode.Parse(afterBuilder.ToString());
    }

    [Benchmark(Description = "JSON Patch apply: small patch (5 ops)")]
    public JsonNode? ApplySmallPatch() =>
        SparseJsonPatchBridge.Apply(
            _baseline,
            baselineIsAbsent: false,
            _smallPatch,
            StringComparison.Ordinal,
            out _
        );

    [Benchmark(Description = "JSON Patch apply: large patch (200 ops)")]
    public JsonNode? ApplyLargePatch() =>
        SparseJsonPatchBridge.Apply(
            _largeBaseline,
            baselineIsAbsent: false,
            _largePatch,
            StringComparison.Ordinal,
            out _
        );

    [Benchmark(Description = "JSON Patch diff: 205-member object with 25 changes")]
    public byte[] DiffLarge() =>
        SparseJsonPatchBridge.Diff(_diffBefore, beforeIsAbsent: false, _diffAfter, afterIsAbsent: false);
}
