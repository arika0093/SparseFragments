using System.Text;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

/// <summary>Measures pointer decoding separately from generated fragment materialization.</summary>
[MemoryDiagnoser]
public class JsonPointerImportBenchmarks
{
    [Params(1, 16, 128)]
    public int OperationCount { get; set; }

    [Params(false, true)]
    public bool Escaped { get; set; }

    [Params(5, 512)]
    public int TokenLength { get; set; }

    private JsonObject _baseline = null!;
    private byte[] _operations = null!;

    [GlobalSetup]
    public void Setup()
    {
        var suffix = new string('x', TokenLength - 5);
        var key = (Escaped ? "a/b~c" : "plain") + suffix;
        var path = (Escaped ? "/a~1b~0c" : "/plain") + suffix;
        _baseline = new JsonObject { [key] = 0 };
        var operations = new StringBuilder("[");
        for (var index = 1; index <= OperationCount; index++)
        {
            if (index > 1)
            {
                operations.Append(',');
            }
            operations
                .Append("{\"op\":\"replace\",\"path\":\"")
                .Append(path)
                .Append("\",\"value\":")
                .Append(index)
                .Append('}');
        }

        operations.Append(']');
        _operations = Encoding.UTF8.GetBytes(operations.ToString());
        var actual = Apply();
        if (actual?[key]?.GetValue<int>() != OperationCount || _baseline[key]?.GetValue<int>() != 0)
        {
            throw new InvalidOperationException(
                "Pointer decoding must target the original property without mutating the baseline."
            );
        }
        CheckPointerEdges();
    }

    private static void CheckPointerEdges()
    {
        var baseline = new JsonObject { [""] = new JsonObject { ["~1"] = 0 } };
        var actual = SparseJsonPatchBridge.Apply(
            baseline,
            false,
            Encoding.UTF8.GetBytes("""[{"op":"replace","path":"//~01","value":1}]"""),
            StringComparison.Ordinal,
            out _
        );
        if (actual?[""]?["~1"]?.GetValue<int>() != 1 || baseline[""]?["~1"]?.GetValue<int>() != 0)
        {
            throw new InvalidOperationException(
                "Pointer escapes must be decoded once and empty tokens retained."
            );
        }
        foreach (var encodedLength in new[] { 255, 256, 257, 514 })
        {
            var prefix = new string('x', encodedLength - 4);
            var key = prefix + "/~";
            var boundaryBaseline = new JsonObject { [key] = 0 };
            var patch = Encoding.UTF8.GetBytes(
                "[{\"op\":\"replace\",\"path\":\"/" + prefix + "~1~0\",\"value\":1}]"
            );
            var boundaryActual = SparseJsonPatchBridge.Apply(
                boundaryBaseline,
                false,
                patch,
                StringComparison.Ordinal,
                out _
            );
            if (
                boundaryActual?[key]?.GetValue<int>() != 1
                || boundaryBaseline[key]?.GetValue<int>() != 0
            )
            {
                throw new InvalidOperationException(
                    "Pointer decoding must preserve tokens across buffer-size boundaries."
                );
            }
        }
        foreach (var padding in new[] { "", new string('x', 512) })
        {
            foreach (var invalid in new[] { "~", "~2" })
            {
                var patch = Encoding.UTF8.GetBytes(
                    "[{\"op\":\"remove\",\"path\":\"/" + padding + invalid + "\"}]"
                );
                try
                {
                    _ = SparseJsonPatchBridge.Apply(
                        baseline,
                        false,
                        patch,
                        StringComparison.Ordinal,
                        out _
                    );
                }
                catch (JsonPatchException exception)
                    when (exception.Kind == JsonPatchErrorKind.MalformedPointer)
                {
                    continue;
                }
                throw new InvalidOperationException(
                    "Malformed pointer escapes must retain their error kind."
                );
            }
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
