using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using SparseFragments.CompilerServices;

public enum JsonObjectDiffShape
{
    Unchanged,
    OneChange,
    DenseChanges,
    MixedChanges,
}

[MemoryDiagnoser]
public class JsonPatchObjectDiffBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    [Params(false, true)]
    public bool Escaped { get; set; }

    [Params(
        JsonObjectDiffShape.Unchanged,
        JsonObjectDiffShape.OneChange,
        JsonObjectDiffShape.DenseChanges,
        JsonObjectDiffShape.MixedChanges
    )]
    public JsonObjectDiffShape Shape { get; set; }

    private JsonObject _before = null!;
    private JsonObject _after = null!;

    [GlobalSetup]
    public void Setup()
    {
        var source = new JsonObject();
        for (var index = 0; index < Size; index++)
        {
            source[Key(index)] = 0;
        }
        _before = (JsonObject)JsonNode.Parse(source.ToJsonString())!;
        _after = (JsonObject)_before.DeepClone();
        switch (Shape)
        {
            case JsonObjectDiffShape.OneChange:
                _after[Key(Size - 1)] = JsonNode.Parse("1");
                break;
            case JsonObjectDiffShape.DenseChanges:
                for (var index = 0; index < Size; index++)
                {
                    _after[Key(index)] = JsonNode.Parse("1");
                }
                break;
            case JsonObjectDiffShape.MixedChanges:
                _after.Remove(Key(0));
                _after[Key(Size - 1)] = JsonNode.Parse("1");
                _after[Key(Size)] = JsonNode.Parse("2");
                break;
        }

        var beforeJson = _before.ToJsonString();
        var afterJson = _after.ToJsonString();
        var patch = Diff();
        using var document = JsonDocument.Parse(patch);
        var operations = document.RootElement.EnumerateArray().ToArray();
        var expectedCount = Shape switch
        {
            JsonObjectDiffShape.Unchanged => 0,
            JsonObjectDiffShape.OneChange => 1,
            JsonObjectDiffShape.DenseChanges => Size,
            _ => 3,
        };
        if (operations.Length != expectedCount)
        {
            throw new InvalidOperationException("Object diff must include only changed members.");
        }
        for (var index = 0; index < operations.Length; index++)
        {
            var expectedKey = Shape switch
            {
                JsonObjectDiffShape.DenseChanges => Key(index),
                JsonObjectDiffShape.MixedChanges when index == 0 => Key(0),
                JsonObjectDiffShape.MixedChanges when index == 2 => Key(Size),
                _ => Key(Size - 1),
            };
            var expectedOp =
                Shape == JsonObjectDiffShape.MixedChanges
                    ? (
                        index == 0 ? "remove"
                        : index == 2 ? "add"
                        : "replace"
                    )
                    : "replace";
            var expectedPath = "/" + expectedKey.Replace("~", "~0").Replace("/", "~1");
            if (
                operations[index].GetProperty("op").GetString() != expectedOp
                || operations[index].GetProperty("path").GetString() != expectedPath
            )
            {
                throw new InvalidOperationException(
                    "Object diff must preserve operation order and pointer escaping."
                );
            }
        }
        var actual = SparseJsonPatchBridge.Apply(
            _before,
            false,
            patch,
            StringComparison.Ordinal,
            out var absent
        );
        if (
            absent
            || !JsonNode.DeepEquals(actual, _after)
            || _before.ToJsonString() != beforeJson
            || _after.ToJsonString() != afterJson
        )
        {
            throw new InvalidOperationException(
                "Object diff must round-trip without mutating either input."
            );
        }

        var equivalent = SparseJsonPatchBridge.Diff(
            JsonNode.Parse("""{"stable":1,"array":[1,2],"null":null,"change":0}"""),
            false,
            JsonNode.Parse("""{"stable":1.0,"array":[1,2],"null":null,"change":1}"""),
            false
        );
        using var equivalentDocument = JsonDocument.Parse(equivalent);
        if (
            equivalentDocument.RootElement.GetArrayLength() != 1
            || equivalentDocument.RootElement[0].GetProperty("path").GetString() != "/change"
        )
        {
            throw new InvalidOperationException(
                "Unchanged arrays, nulls and numerically equal values must be omitted."
            );
        }
        var nestedBefore = JsonNode.Parse(
            """{"a/~":{"stable":5,"child":{"value":0}},"removed":1}"""
        );
        var nestedAfter = JsonNode.Parse("""{"a/~":{"stable":5,"child":{"value":1}},"added":2}""");
        var nestedPatch = SparseJsonPatchBridge.Diff(nestedBefore, false, nestedAfter, false);
        using var nestedDocument = JsonDocument.Parse(nestedPatch);
        var nestedPaths = nestedDocument
            .RootElement.EnumerateArray()
            .Select(operation => operation.GetProperty("path").GetString());
        var nestedActual = SparseJsonPatchBridge.Apply(
            nestedBefore,
            false,
            nestedPatch,
            StringComparison.Ordinal,
            out var nestedAbsent
        );
        if (
            !nestedPaths.SequenceEqual(["/removed", "/a~1~0/child/value", "/added"])
            || nestedAbsent
            || !JsonNode.DeepEquals(nestedActual, nestedAfter)
        )
        {
            throw new InvalidOperationException(
                "Nested object diffs must preserve recursion, escaping and operation order."
            );
        }
        var independent = Diff();
        independent[0] = 0;
        if (!Diff().AsSpan().SequenceEqual(patch))
        {
            throw new InvalidOperationException(
                "Serialized results must retain independent byte storage."
            );
        }
        try
        {
            _ = SparseJsonPatchBridge.Diff(null, false, JsonValue.Create(double.NaN), false);
            throw new InvalidOperationException("Non-finite JSON numbers must be rejected.");
        }
        catch (ArgumentException)
        {
            // The next serialization must still succeed after an interrupted write.
        }
        if (!Diff().AsSpan().SequenceEqual(patch))
        {
            throw new InvalidOperationException(
                "Failed serialization must not corrupt subsequent output."
            );
        }
    }

    private string Key(int index) => (Escaped ? "a/~key-" : "key-") + index;

    [Benchmark]
    public byte[] Diff() => SparseJsonPatchBridge.Diff(_before, false, _after, false);
}
