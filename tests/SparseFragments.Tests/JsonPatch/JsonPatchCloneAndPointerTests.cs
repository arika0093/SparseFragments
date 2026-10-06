using System.Text;
using System.Text.Json.Nodes;
using SparseFragments.CompilerServices;

namespace SparseFragments.JsonPatch.Tests;

/// <summary>
/// Regression coverage for issue #16: identical RFC 6902 semantics without the
/// JSON text round-trip clone or repeated JSON Pointer tokenization.
/// </summary>
public sealed class JsonPatchCloneAndPointerTests
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static JsonNode? Apply(
        JsonNode? baseline,
        bool baselineIsAbsent,
        string patchJson,
        StringComparison comparison = StringComparison.Ordinal
    ) =>
        SparseJsonPatchBridge.Apply(
            baseline,
            baselineIsAbsent,
            Utf8(patchJson),
            comparison,
            out _
        );

    private static JsonNode? ApplyJson(
        string baselineJson,
        string patchJson,
        StringComparison comparison = StringComparison.Ordinal
    ) => Apply(JsonNode.Parse(baselineJson), baselineIsAbsent: false, patchJson, comparison);

    private static JsonPatchException ApplyShouldFail(
        string baselineJson,
        string patchJson,
        StringComparison comparison = StringComparison.Ordinal
    ) =>
        Should.Throw<JsonPatchException>(() =>
            ApplyJson(baselineJson, patchJson, comparison)
        );

    [Test]
    public void PointerEscaping()
    {
        var result = ApplyJson(
            """{"a/b":1,"m~n":2}""",
            """[{"op":"replace","path":"/a~1b","value":10},{"op":"replace","path":"/m~0n","value":20}]"""
        );
        result!["a/b"]!.GetValue<int>().ShouldBe(10);
        result!["m~n"]!.GetValue<int>().ShouldBe(20);
    }

    [Test]
    public void MalformedPointers()
    {
        var cases = new[]
        {
            """[{"op":"remove","path":"Count"}]""",
            """[{"op":"remove","path":"/a~2b"}]""",
            """[{"op":"remove","path":"/dangling~"}]""",
            """[{"op":"move","from":"bad","path":"/a"}]""",
        };
        foreach (var patch in cases)
        {
            ApplyShouldFail("""{"a":1}""", patch)
                .Kind.ShouldBe(JsonPatchErrorKind.MalformedPointer);
        }
    }

    [Test]
    public void ArraysAndDash()
    {
        var result = ApplyJson(
            """{"tags":["a","b"]}""",
            """[{"op":"add","path":"/tags/1","value":"x"},{"op":"remove","path":"/tags/0"},{"op":"add","path":"/tags/-","value":"z"}]"""
        );
        result!["tags"]!.AsArray().Select(static node => node!.GetValue<string>())
            .ShouldBe(new[] { "x", "b", "z" });
    }

    [Test]
    public void MoveAndCopy()
    {
        var moved = ApplyJson(
            """{"arr":[1,2,3]}""",
            """[{"op":"move","from":"/arr/0","path":"/arr/2"}]"""
        );
        moved!["arr"]!.AsArray().Select(static node => node!.GetValue<int>())
            .ShouldBe(new[] { 2, 3, 1 });

        var copied = ApplyJson(
            """{"a":{"x":1}}""",
            """[{"op":"copy","from":"/a","path":"/b"}]"""
        );
        copied!["b"]!["x"]!.GetValue<int>().ShouldBe(1);
        // The copy is independent: mutating it leaves the source untouched.
        copied!["b"]!["x"] = 99;
        copied!["a"]!["x"]!.GetValue<int>().ShouldBe(1);

        ApplyShouldFail("""{"a":1}""", """[{"op":"move","from":"/Missing","path":"/a"}]""")
            .Kind.ShouldBe(JsonPatchErrorKind.MissingTarget);
    }

    [Test]
    public void CaseInsensitivePropertyLookup()
    {
        var result = ApplyJson(
            """{"Count":1}""",
            """[{"op":"replace","path":"/COUNT","value":10}]""",
            StringComparison.OrdinalIgnoreCase
        );
        result!["Count"]!.GetValue<int>().ShouldBe(10);

        ApplyShouldFail("""{"Count":1}""", """[{"op":"replace","path":"/COUNT","value":10}]""")
            .Kind.ShouldBe(JsonPatchErrorKind.MissingTarget);
    }

    [Test]
    public void AtomicFailureLeavesBaselineUntouched()
    {
        const string baselineJson = """{"a":1,"b":2}""";
        var baseline = JsonNode.Parse(baselineJson);
        var exception = Should.Throw<JsonPatchException>(() =>
            Apply(
                baseline,
                baselineIsAbsent: false,
                """[{"op":"replace","path":"/a","value":5},{"op":"remove","path":"/Missing"}]"""
            )
        );
        exception.Kind.ShouldBe(JsonPatchErrorKind.MissingTarget);
        baseline!.ToJsonString().ShouldBe(baselineJson);
    }

    [Test]
    public void SuccessfulApplyClonesBaseline()
    {
        const string baselineJson = """{"a":1}""";
        var baseline = JsonNode.Parse(baselineJson);
        var result = Apply(
            baseline,
            baselineIsAbsent: false,
            """[{"op":"replace","path":"/a","value":2}]"""
        );
        result!["a"]!.GetValue<int>().ShouldBe(2);
        baseline!.ToJsonString().ShouldBe(baselineJson);
    }

    [Test]
    public void RootOperations()
    {
        var added = Apply(null, baselineIsAbsent: true, """[{"op":"add","path":"","value":{"a":3}}]""");
        added!["a"]!.GetValue<int>().ShouldBe(3);

        var replaced = ApplyJson("""{"a":1}""", """[{"op":"replace","path":"","value":null}]""");
        replaced.ShouldBeNull();

        var removed = SparseJsonPatchBridge.Apply(
            JsonNode.Parse("""{"a":1}"""),
            baselineIsAbsent: false,
            Utf8("""[{"op":"remove","path":""}]"""),
            StringComparison.Ordinal,
            out var isAbsent
        );
        isAbsent.ShouldBeTrue();
        removed.ShouldBeNull();
    }

    [Test]
    public void DiffRoundTrip()
    {
        var before = JsonNode.Parse("""{"a":1,"obj":{"x":[1,2]}}""");
        var after = JsonNode.Parse("""{"a":2,"obj":{"x":[1,2,3]}}""");
        var patchBytes = SparseJsonPatchBridge.Diff(before, false, after, false);
        var result = SparseJsonPatchBridge.Apply(
            JsonNode.Parse("""{"a":1,"obj":{"x":[1,2]}}"""),
            false,
            patchBytes,
            StringComparison.Ordinal,
            out _
        );
        result!.ToJsonString().ShouldBe(after!.ToJsonString());
    }

    [Test]
    public void LargePatchAppliesInOrder()
    {
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

        var result = ApplyJson(baselineBuilder.ToString(), patchBuilder.ToString());
        for (var i = 0; i < 200; i++)
        {
            result!["p" + i]!.GetValue<int>().ShouldBe(i * 2);
        }
    }
}
