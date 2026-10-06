using System.Text;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchWidget
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public bool Enabled { get; set; } = true;

    public BenchWidgetNested? Nested { get; set; }

    public List<string> Tags { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchWidgetNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; }

    public BenchWidgetNested? Child { get; set; }
}

[SparseFragmentModel]
public partial class BenchBigObject
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

[SparseFragmentModel]
public partial class BenchPointerDoc
{
    [JsonPropertyName("customName")]
    public string? Value { get; set; }

    [JsonPropertyName("a/b")]
    public int Slash { get; set; }

    [JsonPropertyName("m~n")]
    public int Tilde { get; set; }

    public int Plain { get; set; }
}

/// <summary>
/// JSON Patch import/apply benchmarks: <c>FromJsonPatch</c> across a growing
/// operation count so parse, pointer resolution, and patch-materialization
/// costs stay visible.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchImportBenchmarks
{
    [Params(1, 16, 128)]
    public int OperationCount { get; set; }

    private BenchWidget.Fragment _baseline = null!;
    private Optional<BenchWidget.Fragment?> _baselineState;
    private byte[] _operations = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baseline = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Enabled = Optional<bool>.Present(true),
            Nested = Optional<BenchWidgetNested.Fragment?>.Present(
                new BenchWidgetNested.Fragment
                {
                    Host = Optional<string>.Present("example"),
                    Port = Optional<int>.Present(80),
                }
            ),
            Tags = Optional<List<string>>.Present(new List<string> { "a", "b" }),
        };
        _baselineState = Optional<BenchWidget.Fragment?>.Present(_baseline);

        var builder = new StringBuilder();
        builder.Append('[');
        for (var index = 0; index < OperationCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            if (index % 2 == 0)
            {
                builder.Append("""{"op":"replace","path":"/Count","value":""");
                builder.Append(index);
                builder.Append('}');
            }
            else
            {
                builder.Append("""{"op":"add","path":"/Tags/-","value":"op-""");
                builder.Append(index);
                builder.Append("\"}");
            }
        }

        builder.Append(']');
        _operations = Encoding.UTF8.GetBytes(builder.ToString());
    }

    [Benchmark(Description = "JSON Patch import/apply: replace plus array appends")]
    public BenchWidget.Patch ImportApply() =>
        BenchWidget.Patch.FromJsonPatch(_baselineState, _operations);
}

/// <summary>
/// JSON Patch benchmarks over large arrays and objects: import/apply pays
/// baseline serialization plus pointer work, while export/diff pays
/// whole-value comparison and document serialization.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchDocumentScaleBenchmarks
{
    [Params(64, 512, 2048)]
    public int Size { get; set; }

    private Optional<BenchWidget.Fragment?> _arrayBaseline;
    private byte[] _arrayAppend = null!;
    private BenchWidget.Patch _arrayExportPatch = null!;
    private BenchWidget.Patch _arrayNoOpPatch = null!;

    private Optional<BenchBigObject.Fragment?> _objectBaseline;
    private byte[] _objectReplace = null!;
    private BenchBigObject.Patch _objectExportPatch = null!;
    private BenchBigObject.Patch _objectNoOpPatch = null!;

    private static Optional<BenchWidget.Fragment?> Present(BenchWidget.Fragment fragment) =>
        Optional<BenchWidget.Fragment?>.Present(fragment);

    private static Optional<BenchBigObject.Fragment?> Present(BenchBigObject.Fragment fragment) =>
        Optional<BenchBigObject.Fragment?>.Present(fragment);

    [GlobalSetup]
    public void Setup()
    {
        var tags = new List<string>(Size);
        for (var index = 0; index < Size; index++)
        {
            tags.Add("tag-" + index);
        }

        var arrayBaseline = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Tags = Optional<List<string>>.Present(new List<string>(tags)),
        };
        _arrayBaseline = Present(arrayBaseline);
        _arrayAppend = Encoding.UTF8.GetBytes(
            """[{"op":"add","path":"/Tags/-","value":"appended"}]"""
        );

        var appendedTags = new List<string>(tags) { "appended" };
        var arrayChanged = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Tags = Optional<List<string>>.Present(appendedTags),
        };
        _arrayExportPatch = BenchWidget.Patch.Between(_arrayBaseline, Present(arrayChanged));
        _arrayNoOpPatch = BenchWidget.Patch.Between(_arrayBaseline, _arrayBaseline);

        var scores = new Dictionary<string, int>(Size);
        for (var index = 0; index < Size; index++)
        {
            scores["key-" + index] = index;
        }

        var objectBaseline = new BenchBigObject.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(new Dictionary<string, int>(scores)),
        };
        _objectBaseline = Present(objectBaseline);
        _objectReplace = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/Scores/key-0","value":-1}]"""
        );

        var changedScores = new Dictionary<string, int>(scores) { ["key-0"] = -1 };
        var objectChanged = new BenchBigObject.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(changedScores),
        };
        _objectExportPatch = BenchBigObject.Patch.Between(_objectBaseline, Present(objectChanged));
        _objectNoOpPatch = BenchBigObject.Patch.Between(_objectBaseline, _objectBaseline);
    }

    [Benchmark(Description = "JSON Patch import/apply: append to a large array")]
    public BenchWidget.Patch ImportApply_LargeArrayAppend() =>
        BenchWidget.Patch.FromJsonPatch(_arrayBaseline, _arrayAppend);

    [Benchmark(Description = "JSON Patch import/apply: replace in a large object")]
    public BenchBigObject.Patch ImportApply_LargeObjectReplace() =>
        BenchBigObject.Patch.FromJsonPatch(_objectBaseline, _objectReplace);

    [Benchmark(Description = "JSON Patch export/diff: large array append")]
    public ReadOnlyMemory<byte> ExportDiff_LargeArray() =>
        _arrayExportPatch.ToJsonPatch(_arrayBaseline);

    [Benchmark(Description = "JSON Patch export/diff: large object change")]
    public ReadOnlyMemory<byte> ExportDiff_LargeObject() =>
        _objectExportPatch.ToJsonPatch(_objectBaseline);

    [Benchmark(Description = "JSON Patch export/diff: array no-op")]
    public ReadOnlyMemory<byte> ExportDiff_ArrayNoOp() =>
        _arrayNoOpPatch.ToJsonPatch(_arrayBaseline);

    [Benchmark(Description = "JSON Patch export/diff: object no-op")]
    public ReadOnlyMemory<byte> ExportDiff_ObjectNoOp() =>
        _objectNoOpPatch.ToJsonPatch(_objectBaseline);
}

/// <summary>
/// JSON Patch benchmarks over increasing object-graph depth: deep JSON
/// pointers stress parent resolution on import and recursive diff on export.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchDepthBenchmarks
{
    [Params(2, 8, 32)]
    public int Depth { get; set; }

    private Optional<BenchWidget.Fragment?> _baseline;
    private byte[] _deepReplace = null!;
    private BenchWidget.Patch _deepExportPatch = null!;

    private static BenchWidgetNested.Fragment ChainFragment(int depth, int port)
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

    [GlobalSetup]
    public void Setup()
    {
        var baseline = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Nested = Optional<BenchWidgetNested.Fragment?>.Present(ChainFragment(Depth, 80)),
        };
        _baseline = Optional<BenchWidget.Fragment?>.Present(baseline);

        var pointer = new StringBuilder("/Nested");
        for (var level = 0; level < Depth; level++)
        {
            pointer.Append("/Child");
        }

        pointer.Append("/Port");
        _deepReplace = Encoding.UTF8.GetBytes(
            "[{\"op\":\"replace\",\"path\":\"" + pointer + "\",\"value\":8080}]"
        );

        var changed = new BenchWidget.Fragment
        {
            Name = Optional<string?>.Present("alpha"),
            Count = Optional<int>.Present(1),
            Nested = Optional<BenchWidgetNested.Fragment?>.Present(ChainFragment(Depth, 8080)),
        };
        _deepExportPatch = BenchWidget.Patch.Between(
            _baseline,
            Optional<BenchWidget.Fragment?>.Present(changed)
        );
    }

    [Benchmark(Description = "JSON Patch import/apply: deep nested replace")]
    public BenchWidget.Patch ImportApply_NestedDeep() =>
        BenchWidget.Patch.FromJsonPatch(_baseline, _deepReplace);

    [Benchmark(Description = "JSON Patch export/diff: deep nested change")]
    public ReadOnlyMemory<byte> ExportDiff_NestedDeep() =>
        _deepExportPatch.ToJsonPatch(_baseline);
}

/// <summary>
/// JSON Patch benchmarks over pointer-heavy documents: escaped segments and
/// custom wire names exercise pointer parsing plus property-name mapping.
/// </summary>
[MemoryDiagnoser]
public class JsonPatchPointerBenchmarks
{
    private Optional<BenchPointerDoc.Fragment?> _baseline;
    private byte[] _pointerOperations = null!;
    private BenchPointerDoc.Patch _exportPatch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var baseline = new BenchPointerDoc.Fragment
        {
            Value = Optional<string?>.Present("v"),
            Slash = Optional<int>.Present(1),
            Tilde = Optional<int>.Present(2),
            Plain = Optional<int>.Present(3),
        };
        _baseline = Optional<BenchPointerDoc.Fragment?>.Present(baseline);

        _pointerOperations = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/a~1b","value":10},{"op":"replace","path":"/m~0n","value":20},{"op":"replace","path":"/customName","value":"w"},{"op":"replace","path":"/Plain","value":30}]"""
        );

        var changed = new BenchPointerDoc.Fragment
        {
            Value = Optional<string?>.Present("w"),
            Slash = Optional<int>.Present(10),
            Tilde = Optional<int>.Present(20),
            Plain = Optional<int>.Present(30),
        };
        _exportPatch = BenchPointerDoc.Patch.Between(
            _baseline,
            Optional<BenchPointerDoc.Fragment?>.Present(changed)
        );
    }

    [Benchmark(Description = "JSON Patch import/apply: pointer-heavy document")]
    public BenchPointerDoc.Patch ImportApply_PointerHeavy() =>
        BenchPointerDoc.Patch.FromJsonPatch(_baseline, _pointerOperations);

    [Benchmark(Description = "JSON Patch export/diff: pointer-heavy document")]
    public ReadOnlyMemory<byte> ExportDiff_PointerHeavy() =>
        _exportPatch.ToJsonPatch(_baseline);
}
