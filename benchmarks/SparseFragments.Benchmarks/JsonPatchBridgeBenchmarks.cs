using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

[SparseFragmentModel]
public partial class BridgeScalars
{
    public string? S01 { get; set; }

    public string? S02 { get; set; }

    public int S03 { get; set; }

    public int S04 { get; set; }

    public bool S05 { get; set; }

    public bool S06 { get; set; }

    public double S07 { get; set; }

    public double S08 { get; set; }

    public long S09 { get; set; }

    public long S10 { get; set; }

    public string? S11 { get; set; }

    public string? S12 { get; set; }

    public int S13 { get; set; }

    public bool S14 { get; set; }

    public double S15 { get; set; }

    public long S16 { get; set; }
}

[SparseFragmentModel]
public partial class BridgeWide
{
    public string? P00 { get; set; }

    public string? P01 { get; set; }

    public string? P02 { get; set; }

    public string? P03 { get; set; }

    public string? P04 { get; set; }

    public string? P05 { get; set; }

    public string? P06 { get; set; }

    public string? P07 { get; set; }

    public string? P08 { get; set; }

    public string? P09 { get; set; }

    public string? P10 { get; set; }

    public string? P11 { get; set; }

    public string? P12 { get; set; }

    public string? P13 { get; set; }

    public string? P14 { get; set; }

    public string? P15 { get; set; }

    public string? P16 { get; set; }

    public string? P17 { get; set; }

    public string? P18 { get; set; }

    public string? P19 { get; set; }

    public string? P20 { get; set; }

    public string? P21 { get; set; }

    public string? P22 { get; set; }

    public string? P23 { get; set; }

    public string? P24 { get; set; }

    public string? P25 { get; set; }

    public string? P26 { get; set; }

    public string? P27 { get; set; }

    public string? P28 { get; set; }

    public string? P29 { get; set; }

    public string? P30 { get; set; }

    public string? P31 { get; set; }
}

/// <summary>
/// Shared conversion helpers mirroring the generated JSON Patch bridge path
/// (fragment converter write plus <see cref="JsonNode"/> parse, and the
/// reverse) without UTF-8/string round trips, so conversion cost can be
/// measured independently from patch-engine cost.
/// </summary>
internal static class JsonPatchBridgeBench
{
    public static JsonSerializerOptions WideOptions() =>
        new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    public static JsonNode? ToNode<T>(T value, JsonConverter<T> converter, JsonSerializerOptions options)
        where T : class
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            converter.Write(writer, value, options);
            writer.Flush();
        }

        stream.Position = 0;
        return JsonNode.Parse(stream);
    }

    public static T FromNode<T>(JsonNode node, JsonConverter<T> converter, JsonSerializerOptions options)
        where T : class
    {
        var strict = new JsonSerializerOptions(options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            node.WriteTo(writer);
            writer.Flush();
        }

        if (!stream.TryGetBuffer(out var segment))
        {
            segment = new ArraySegment<byte>(stream.ToArray());
        }

        var reader = new Utf8JsonReader(
            new ReadOnlySpan<byte>(segment.Array!, segment.Offset, segment.Count)
        );
        if (!reader.Read())
        {
            throw new InvalidOperationException("The JSON payload was empty.");
        }

        return converter.Read(ref reader, typeof(T), strict)!;
    }

    public static BenchWidgetNested.Fragment ChainFragment(int depth, int port)
    {
        BenchWidgetNested.Fragment? child = null;
        for (var level = depth; level >= 0; level--)
        {
            child = new BenchWidgetNested.Fragment
            {
                Host = Optional<string>.Present("host-" + level),
                Port = Optional<int>.Present(level == depth ? port : level),
                Child =
                    child is null
                        ? Optional<BenchWidgetNested.Fragment?>.Missing
                        : Optional<BenchWidgetNested.Fragment?>.Present(child),
            };
        }

        return child!;
    }
}

/// <summary>
/// Isolated Fragment-to-<see cref="JsonNode"/> conversion benchmarks for
/// issue #58: scalar-heavy models, large arrays, large objects, and nested
/// objects. No patch parsing, pointer resolution, or diff work is included.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchBridgeFragmentToNodeBenchmarks
{
    private JsonSerializerOptions _options = null!;
    private BridgeScalars.Fragment _scalars = null!;
    private BenchWidget.Fragment _array = null!;
    private BenchBigObject.Fragment _object = null!;
    private BenchWidget.Fragment _nested = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = JsonPatchBridgeBench.WideOptions();
        _scalars = new BridgeScalars.Fragment
        {
            S01 = Optional<string?>.Present("alpha"),
            S02 = Optional<string?>.Present("beta"),
            S03 = Optional<int>.Present(3),
            S04 = Optional<int>.Present(4),
            S05 = Optional<bool>.Present(true),
            S06 = Optional<bool>.Present(false),
            S07 = Optional<double>.Present(1.5),
            S08 = Optional<double>.Present(2.5),
            S09 = Optional<long>.Present(9),
            S10 = Optional<long>.Present(10),
            S11 = Optional<string?>.Present("gamma"),
            S12 = Optional<string?>.Present("delta"),
            S13 = Optional<int>.Present(13),
            S14 = Optional<bool>.Present(true),
            S15 = Optional<double>.Present(15.5),
            S16 = Optional<long>.Present(16),
        };

        var tags = new List<string>(1024);
        for (var index = 0; index < 1024; index++)
        {
            tags.Add("tag-" + index);
        }

        _array = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Tags = Optional<List<string>>.Present(tags),
        };

        var scores = new Dictionary<string, int>(1024);
        for (var index = 0; index < 1024; index++)
        {
            scores["key-" + index] = index;
        }

        _object = new BenchBigObject.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(scores),
        };
        _nested = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                JsonPatchBridgeBench.ChainFragment(8, 80)
            ),
        };
    }

    [Benchmark(Description = "Bridge conversion: scalar-heavy fragment to node")]
    public JsonNode? ScalarHeavy_ToNode() =>
        JsonPatchBridgeBench.ToNode(_scalars, BridgeScalars.Fragment.JsonConverter, _options);

    [Benchmark(Description = "Bridge conversion: large-array fragment to node")]
    public JsonNode? LargeArray_ToNode() =>
        JsonPatchBridgeBench.ToNode(_array, BenchWidget.Fragment.JsonConverter, _options);

    [Benchmark(Description = "Bridge conversion: large-object fragment to node")]
    public JsonNode? LargeObject_ToNode() =>
        JsonPatchBridgeBench.ToNode(_object, BenchBigObject.Fragment.JsonConverter, _options);

    [Benchmark(Description = "Bridge conversion: nested fragment to node")]
    public JsonNode? Nested_ToNode() =>
        JsonPatchBridgeBench.ToNode(_nested, BenchWidget.Fragment.JsonConverter, _options);
}

/// <summary>
/// Isolated <see cref="JsonNode"/>-to-Fragment conversion benchmarks for
/// issue #58, over the same shapes as the fragment-to-node benchmarks.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchBridgeNodeToFragmentBenchmarks
{
    private JsonSerializerOptions _options = null!;
    private JsonNode _scalars = null!;
    private JsonNode _array = null!;
    private JsonNode _object = null!;
    private JsonNode _nested = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = JsonPatchBridgeBench.WideOptions();
        var converter = BridgeScalars.Fragment.JsonConverter;
        var scalars = new BridgeScalars.Fragment
        {
            S01 = Optional<string?>.Present("alpha"),
            S03 = Optional<int>.Present(3),
            S05 = Optional<bool>.Present(true),
            S07 = Optional<double>.Present(1.5),
            S09 = Optional<long>.Present(9),
        };
        _scalars = JsonPatchBridgeBench.ToNode(scalars, converter, _options)!;

        var tags = new List<string>(1024);
        for (var index = 0; index < 1024; index++)
        {
            tags.Add("tag-" + index);
        }

        _array = JsonPatchBridgeBench.ToNode(
            new BenchWidget.Fragment
            {
                Name = Optional<string?>.Present("alpha"),
                Count = Optional<int>.Present(1),
                Tags = Optional<List<string>>.Present(tags),
            },
            BenchWidget.Fragment.JsonConverter,
            _options
        )!;

        var scores = new Dictionary<string, int>(1024);
        for (var index = 0; index < 1024; index++)
        {
            scores["key-" + index] = index;
        }

        _object = JsonPatchBridgeBench.ToNode(
            new BenchBigObject.Fragment
            {
                Scores = Optional<Dictionary<string, int>>.Present(scores),
            },
            BenchBigObject.Fragment.JsonConverter,
            _options
        )!;

        _nested = JsonPatchBridgeBench.ToNode(
            new BenchWidget.Fragment
            {
                Name = Optional<string?>.Present("alpha"),
                Count = Optional<int>.Present(1),
                Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                    JsonPatchBridgeBench.ChainFragment(8, 80)
                ),
            },
            BenchWidget.Fragment.JsonConverter,
            _options
        )!;
    }

    [Benchmark(Description = "Bridge conversion: node to scalar-heavy fragment")]
    public BridgeScalars.Fragment ScalarHeavy_FromNode() =>
        JsonPatchBridgeBench.FromNode(_scalars, BridgeScalars.Fragment.JsonConverter, _options);

    [Benchmark(Description = "Bridge conversion: node to large-array fragment")]
    public BenchWidget.Fragment LargeArray_FromNode() =>
        JsonPatchBridgeBench.FromNode(_array, BenchWidget.Fragment.JsonConverter, _options);

    [Benchmark(Description = "Bridge conversion: node to large-object fragment")]
    public BenchBigObject.Fragment LargeObject_FromNode() =>
        JsonPatchBridgeBench.FromNode(_object, BenchBigObject.Fragment.JsonConverter, _options);

    [Benchmark(Description = "Bridge conversion: node to nested fragment")]
    public BenchWidget.Fragment Nested_FromNode() =>
        JsonPatchBridgeBench.FromNode(_nested, BenchWidget.Fragment.JsonConverter, _options);
}

/// <summary>
/// Patch-engine baselines on already materialized <see cref="JsonNode"/>
/// values: <see cref="SparseJsonPatchBridge.Apply"/> and
/// <see cref="SparseJsonPatchBridge.Diff"/> without fragment conversion.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchBridgeEngineBenchmarks
{
    private JsonNode _scalarBefore = null!;
    private JsonNode _scalarAfter = null!;
    private byte[] _scalarPatch = null!;
    private JsonNode _arrayBefore = null!;
    private JsonNode _arrayAfter = null!;
    private byte[] _arrayPatch = null!;
    private JsonNode _objectBefore = null!;
    private JsonNode _objectAfter = null!;
    private byte[] _objectPatch = null!;
    private JsonNode _nestedBefore = null!;
    private JsonNode _nestedAfter = null!;
    private byte[] _nestedPatch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = JsonPatchBridgeBench.WideOptions();

        var scalarBefore = new BridgeScalars.Fragment
        {
            S01 = Optional<string?>.Present("alpha"),
            S03 = Optional<int>.Present(3),
        };
        var scalarAfter = new BridgeScalars.Fragment
        {
            S01 = Optional<string?>.Present("beta"),
            S03 = Optional<int>.Present(4),
        };
        _scalarBefore = JsonPatchBridgeBench.ToNode(scalarBefore, BridgeScalars.Fragment.JsonConverter, options)!;
        _scalarAfter = JsonPatchBridgeBench.ToNode(scalarAfter, BridgeScalars.Fragment.JsonConverter, options)!;
        _scalarPatch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/S01","value":"beta"}]""");

        var tags = new List<string>(1024);
        for (var index = 0; index < 1024; index++)
        {
            tags.Add("tag-" + index);
        }

        _arrayBefore = JsonPatchBridgeBench.ToNode(
            new BenchWidget.Fragment
            {
                Name = Optional<string?>.Present("alpha"),
                Tags = Optional<List<string>>.Present(new List<string>(tags)),
            },
            BenchWidget.Fragment.JsonConverter,
            options
        )!;
        _arrayAfter = JsonPatchBridgeBench.ToNode(
            new BenchWidget.Fragment
            {
                Name = Optional<string?>.Present("alpha"),
                Tags = Optional<List<string>>.Present(new List<string>(tags) { "appended" }),
            },
            BenchWidget.Fragment.JsonConverter,
            options
        )!;
        _arrayPatch = Encoding.UTF8.GetBytes("""[{"op":"add","path":"/Tags/-","value":"appended"}]""");

        var scores = new Dictionary<string, int>(1024);
        for (var index = 0; index < 1024; index++)
        {
            scores["key-" + index] = index;
        }

        var changedScores = new Dictionary<string, int>(scores) { ["key-0"] = -1 };
        _objectBefore = JsonPatchBridgeBench.ToNode(
            new BenchBigObject.Fragment
            {
                Scores = Optional<Dictionary<string, int>>.Present(new Dictionary<string, int>(scores)),
            },
            BenchBigObject.Fragment.JsonConverter,
            options
        )!;
        _objectAfter = JsonPatchBridgeBench.ToNode(
            new BenchBigObject.Fragment
            {
                Scores = Optional<Dictionary<string, int>>.Present(changedScores),
            },
            BenchBigObject.Fragment.JsonConverter,
            options
        )!;
        _objectPatch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/Scores/key-0","value":-1}]""");

        var pointer = new StringBuilder("/Nested");
        for (var level = 0; level < 8; level++)
        {
            pointer.Append("/Child");
        }

        pointer.Append("/Port");
        _nestedPatch = Encoding.UTF8.GetBytes(
            "[{\"op\":\"replace\",\"path\":\"" + pointer + "\",\"value\":8080}]"
        );
        _nestedBefore = JsonPatchBridgeBench.ToNode(
            new BenchWidget.Fragment
            {
                Name = Optional<string?>.Present("alpha"),
                Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                    JsonPatchBridgeBench.ChainFragment(8, 80)
                ),
            },
            BenchWidget.Fragment.JsonConverter,
            options
        )!;
        _nestedAfter = JsonPatchBridgeBench.ToNode(
            new BenchWidget.Fragment
            {
                Name = Optional<string?>.Present("alpha"),
                Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                    JsonPatchBridgeBench.ChainFragment(8, 8080)
                ),
            },
            BenchWidget.Fragment.JsonConverter,
            options
        )!;
    }

    [Benchmark(Description = "Bridge apply on materialized node: scalar replace")]
    public JsonNode? Apply_Scalar() =>
        SparseJsonPatchBridge.Apply(_scalarBefore, baselineIsAbsent: false, _scalarPatch, StringComparison.Ordinal, out _);

    [Benchmark(Description = "Bridge apply on materialized node: large array append")]
    public JsonNode? Apply_LargeArray() =>
        SparseJsonPatchBridge.Apply(_arrayBefore, baselineIsAbsent: false, _arrayPatch, StringComparison.Ordinal, out _);

    [Benchmark(Description = "Bridge apply on materialized node: large object replace")]
    public JsonNode? Apply_LargeObject() =>
        SparseJsonPatchBridge.Apply(_objectBefore, baselineIsAbsent: false, _objectPatch, StringComparison.Ordinal, out _);

    [Benchmark(Description = "Bridge apply on materialized node: nested replace")]
    public JsonNode? Apply_Nested() =>
        SparseJsonPatchBridge.Apply(_nestedBefore, baselineIsAbsent: false, _nestedPatch, StringComparison.Ordinal, out _);

    [Benchmark(Description = "Engine diff on materialized nodes: scalar change")]
    public byte[] Diff_Scalar() =>
        SparseJsonPatchBridge.Diff(_scalarBefore, beforeIsAbsent: false, _scalarAfter, afterIsAbsent: false);

    [Benchmark(Description = "Engine diff on materialized nodes: large array append")]
    public byte[] Diff_LargeArray() =>
        SparseJsonPatchBridge.Diff(_arrayBefore, beforeIsAbsent: false, _arrayAfter, afterIsAbsent: false);

    [Benchmark(Description = "Engine diff on materialized nodes: large object change")]
    public byte[] Diff_LargeObject() =>
        SparseJsonPatchBridge.Diff(_objectBefore, beforeIsAbsent: false, _objectAfter, afterIsAbsent: false);

    [Benchmark(Description = "Engine diff on materialized nodes: nested change")]
    public byte[] Diff_Nested() =>
        SparseJsonPatchBridge.Diff(_nestedBefore, beforeIsAbsent: false, _nestedAfter, afterIsAbsent: false);
}

/// <summary>
/// End-to-end generated JSON Patch benchmarks (public
/// <c>Patch.FromJsonPatch</c>/<c>Patch.ToJsonPatch</c>) over the same shapes,
/// so bridge conversion savings show up against the engine baselines above.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchBridgeEndToEndBenchmarks
{
    private Optional<BridgeScalars.Fragment?> _scalarBaseline;
    private byte[] _scalarPatch = null!;
    private BridgeScalars.Patch _scalarExport = null!;
    private Optional<BenchWidget.Fragment?> _arrayBaseline;
    private byte[] _arrayPatch = null!;
    private BenchWidget.Patch _arrayExport = null!;
    private Optional<BenchBigObject.Fragment?> _objectBaseline;
    private byte[] _objectPatch = null!;
    private BenchBigObject.Patch _objectExport = null!;
    private Optional<BenchWidget.Fragment?> _nestedBaseline;
    private byte[] _nestedPatch = null!;
    private BenchWidget.Patch _nestedExport = null!;

    [GlobalSetup]
    public void Setup()
    {
        var scalarBaseline = new BridgeScalars.Fragment
        {
            S01 = Optional<string?>.Present("alpha"),
            S03 = Optional<int>.Present(3),
        };
        var scalarAfter = new BridgeScalars.Fragment
        {
            S01 = Optional<string?>.Present("beta"),
            S03 = Optional<int>.Present(4),
        };
        _scalarBaseline = Optional<BridgeScalars.Fragment?>.Present(scalarBaseline);
        _scalarPatch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/S01","value":"beta"}]""");
        _scalarExport = BridgeScalars.Patch.Between(
            _scalarBaseline,
            Optional<BridgeScalars.Fragment?>.Present(scalarAfter)
        );

        var tags = new List<string>(1024);
        for (var index = 0; index < 1024; index++)
        {
            tags.Add("tag-" + index);
        }

        var arrayBaseline = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Tags = Optional<List<string>>.Present(new List<string>(tags)),
        };
        var arrayAfter = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Tags = Optional<List<string>>.Present(new List<string>(tags) { "appended" }),
        };
        _arrayBaseline = Optional<BenchWidget.Fragment?>.Present(arrayBaseline);
        _arrayPatch = Encoding.UTF8.GetBytes("""[{"op":"add","path":"/Tags/-","value":"appended"}]""");
        _arrayExport = BenchWidget.Patch.Between(
            _arrayBaseline,
            Optional<BenchWidget.Fragment?>.Present(arrayAfter)
        );

        var scores = new Dictionary<string, int>(1024);
        for (var index = 0; index < 1024; index++)
        {
            scores["key-" + index] = index;
        }

        var objectBaseline = new BenchBigObject.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(new Dictionary<string, int>(scores)),
        };
        var objectAfter = new BenchBigObject.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(scores) { ["key-0"] = -1 }
            ),
        };
        _objectBaseline = Optional<BenchBigObject.Fragment?>.Present(objectBaseline);
        _objectPatch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/Scores/key-0","value":-1}]""");
        _objectExport = BenchBigObject.Patch.Between(
            _objectBaseline,
            Optional<BenchBigObject.Fragment?>.Present(objectAfter)
        );

        var nestedBaseline = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                JsonPatchBridgeBench.ChainFragment(8, 80)
            ),
        };
        var nestedAfter = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                JsonPatchBridgeBench.ChainFragment(8, 8080)
            ),
        };
        _nestedBaseline = Optional<BenchWidget.Fragment?>.Present(nestedBaseline);
        var pointer = new StringBuilder("/Nested");
        for (var level = 0; level < 8; level++)
        {
            pointer.Append("/Child");
        }

        pointer.Append("/Port");
        _nestedPatch = Encoding.UTF8.GetBytes(
            "[{\"op\":\"replace\",\"path\":\"" + pointer + "\",\"value\":8080}]"
        );
        _nestedExport = BenchWidget.Patch.Between(
            _nestedBaseline,
            Optional<BenchWidget.Fragment?>.Present(nestedAfter)
        );
    }

    [Benchmark(Description = "End-to-end FromJsonPatch: scalar replace")]
    public BridgeScalars.Patch FromJsonPatch_Scalar() =>
        BridgeScalars.Patch.FromJsonPatch(_scalarBaseline, _scalarPatch);

    [Benchmark(Description = "End-to-end FromJsonPatch: large array append")]
    public BenchWidget.Patch FromJsonPatch_LargeArray() =>
        BenchWidget.Patch.FromJsonPatch(_arrayBaseline, _arrayPatch);

    [Benchmark(Description = "End-to-end FromJsonPatch: large object replace")]
    public BenchBigObject.Patch FromJsonPatch_LargeObject() =>
        BenchBigObject.Patch.FromJsonPatch(_objectBaseline, _objectPatch);

    [Benchmark(Description = "End-to-end FromJsonPatch: nested replace")]
    public BenchWidget.Patch FromJsonPatch_Nested() =>
        BenchWidget.Patch.FromJsonPatch(_nestedBaseline, _nestedPatch);

    [Benchmark(Description = "End-to-end ToJsonPatch: scalar change")]
    public ReadOnlyMemory<byte> ToJsonPatch_Scalar() => _scalarExport.ToJsonPatch(_scalarBaseline);

    [Benchmark(Description = "End-to-end ToJsonPatch: large array append")]
    public ReadOnlyMemory<byte> ToJsonPatch_LargeArray() => _arrayExport.ToJsonPatch(_arrayBaseline);

    [Benchmark(Description = "End-to-end ToJsonPatch: large object change")]
    public ReadOnlyMemory<byte> ToJsonPatch_LargeObject() => _objectExport.ToJsonPatch(_objectBaseline);

    [Benchmark(Description = "End-to-end ToJsonPatch: nested change")]
    public ReadOnlyMemory<byte> ToJsonPatch_Nested() => _nestedExport.ToJsonPatch(_nestedBaseline);
}

/// <summary>
/// Targeted wide-object/case-mismatch benchmarks for the issue #58 optional
/// follow-up: case-insensitive property lookup falls back to linear key
/// scanning after an ordinal miss. Measures conversion with every property
/// name mismatched by case, plus an end-to-end import using a mismatched path.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchBridgeCaseInsensitiveBenchmarks
{
    private JsonSerializerOptions _options = null!;
    private JsonNode _mismatched = null!;
    private Optional<BridgeWide.Fragment?> _baseline;
    private byte[] _patch = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNameCaseInsensitive = true,
        };

        var builder = new StringBuilder("{");
        for (var index = 0; index < 32; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("\"p").Append(index.ToString("00")).Append("\":\"v").Append(index).Append('"');
        }

        builder.Append('}');
        _mismatched = JsonNode.Parse(builder.ToString())!;

        var baseline = new BridgeWide.Fragment
        {
            P00 = Optional<string?>.Present("v0"),
            P31 = Optional<string?>.Present("v31"),
        };
        _baseline = Optional<BridgeWide.Fragment?>.Present(baseline);
        _patch = Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/p00","value":"w"}]""");
    }

    [Benchmark(Description = "Case-insensitive conversion: wide object, all names mismatched")]
    public BridgeWide.Fragment WideObject_CaseMismatch() =>
        JsonPatchBridgeBench.FromNode(_mismatched, BridgeWide.Fragment.JsonConverter, _options);

    [Benchmark(Description = "End-to-end FromJsonPatch: wide object, mismatched path case")]
    public BridgeWide.Patch FromJsonPatch_CaseMismatch() =>
        BridgeWide.Patch.FromJsonPatch(_baseline, _patch, _options);
}
