using System.Text;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

/// <summary>Measures pointer decoding separately from generated fragment materialization.</summary>
[MemoryDiagnoser]
public class JsonPointerImportBenchmarks
{
    [Params(1, 16, 128)]
    public int OperationCount { get; set; }

    [Params(false, true)]
    public bool Escaped { get; set; }

    private JsonObject _baseline = null!;
    private byte[] _operations = null!;

    [GlobalSetup]
    public void Setup()
    {
        var key = Escaped ? "a/b~c" : "plain";
        var path = Escaped ? "/a~1b~0c" : "/plain";
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
