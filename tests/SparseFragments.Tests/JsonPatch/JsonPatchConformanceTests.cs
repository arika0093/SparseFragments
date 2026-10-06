using System.Text.Json.Nodes;
using SparseFragments.CompilerServices;

namespace SparseFragments.JsonPatch.Tests;

/// <summary>RFC 6902/6901 conformance suite (see https://datatracker.ietf.org/doc/html/rfc6902).</summary>
/// <remarks>
/// Expectations are derived from the RFC text, not from implementation behavior.
/// Section numbers refer to RFC 6902 unless marked RFC 6901.
/// </remarks>
public sealed class JsonPatchConformanceTests
{
    private static JsonNode? Apply(string baselineJson, string patchJson)
    {
        var baseline = JsonNode.Parse(baselineJson);
        return SparseJsonPatchBridge.Apply(
            baseline,
            baselineIsAbsent: false,
            System.Text.Encoding.UTF8.GetBytes(patchJson).AsMemory(),
            StringComparison.Ordinal,
            out _
        );
    }

    private static JsonPatchErrorKind ApplyError(string baselineJson, string patchJson)
    {
        try
        {
            Apply(baselineJson, patchJson);
        }
        catch (JsonPatchException ex)
        {
            return ex.Kind;
        }

        throw new InvalidOperationException("Expected a JsonPatchException.");
    }

    private static string Canonical(JsonNode? node) => node?.ToJsonString() ?? "null";

    // RFC 6902 section 4.6 'test': numbers compare numerically across representations.
    [Test]
    [Arguments("1", "1.0", true)]
    [Arguments("1", "10e-1", true)]
    [Arguments("0.1", "1e-1", true)]
    [Arguments("0.1", "10e-2", true)]
    [Arguments("1", "10e-2", false)]
    [Arguments("10e-2", "1.0", false)]
    [Arguments("2.5", "2.50", true)]
    [Arguments("1", "2", false)]
    [Arguments("-0.0", "0", true)]
    public void Test_NumericEquality(string actual, string expected, bool succeeds)
    {
        var patch = """[{"op":"test","path":"","value":__EXPECTED__}]""".Replace(
            "__EXPECTED__",
            expected
        );
        if (succeeds)
        {
            Apply(actual, patch);
        }
        else
        {
            ApplyError(actual, patch).ShouldBe(JsonPatchErrorKind.TestFailed);
        }
    }

    // RFC 6902 section 4.6: objects equal regardless of member order; arrays are order-sensitive.
    [Test]
    public void Test_ObjectMemberOrderIrrelevant()
    {
        Apply(
            """{"a":1,"b":[1,2],"c":{"x":true,"y":null}}""",
            """[{"op":"test","path":"","value":{"c":{"y":null,"x":true},"b":[1,2],"a":1}}]"""
        );
    }

    [Test]
    public void Test_ArrayOrderMatters()
    {
        ApplyError(
            """{"b":[1,2]}""",
            """[{"op":"test","path":"/b","value":[2,1]}]"""
        ).ShouldBe(JsonPatchErrorKind.TestFailed);
    }

    [Test]
    public void Test_NullAndStringExactness()
    {
        Apply("""{"v":null}""", """[{"op":"test","path":"/v","value":null}]""");
        Apply("""{"v":"a"}""", """[{"op":"test","path":"/v","value":"a"}]""");
        ApplyError("""{"v":"a"}""", """[{"op":"test","path":"/v","value":"A"}]""")
            .ShouldBe(JsonPatchErrorKind.TestFailed);
        ApplyError("""{"v":null}""", """[{"op":"test","path":"/v","value":false}]""")
            .ShouldBe(JsonPatchErrorKind.TestFailed);
    }

    // RFC 6901 section 3/4: escaping, root, empty names, numeric keys.
    [Test]
    public void Pointer_Escaping()
    {
        var result = Apply(
            """{"a/b":1,"m~n":2,"":3}""",
            """[{"op":"test","path":"/a~1b","value":1},{"op":"test","path":"/m~0n","value":2},{"op":"test","path":"/","value":3}]"""
        );
        Canonical(result).ShouldContain("a/b");
    }

    [Test]
    public void Pointer_RootAndNumericKeys()
    {
        var replaced = Apply("""{"a":1}""", """[{"op":"replace","path":"","value":{"b":2}}]""");
        Canonical(replaced).ShouldBe("""{"b":2}""");

        var numeric = Apply(
            """{"0":"zero"}""",
            """[{"op":"test","path":"/0","value":"zero"}]"""
        );
        Canonical(numeric).ShouldContain("zero");
    }

    [Test]
    public void Pointer_InvalidEscapeAndIndex()
    {
        // RFC 6901: only ~0 and ~1 are valid escapes.
        ApplyError("""{"a":1}""", """[{"op":"test","path":"/a~2","value":1}]""")
            .ShouldBe(JsonPatchErrorKind.MalformedPointer);
        ApplyError("""{"a":1}""", """[{"op":"test","path":"/a~","value":1}]""")
            .ShouldBe(JsonPatchErrorKind.MalformedPointer);
        // RFC 6902 section 4.1: array indices are base-10 without leading zeros.
        ApplyError("""{"a":[1]}""", """[{"op":"test","path":"/a/01","value":1}]""")
            .ShouldBe(JsonPatchErrorKind.InvalidArrayIndex);
        // '-' appends only via 'add' (RFC 6902 section 4.1).
        ApplyError("""{"a":[1]}""", """[{"op":"test","path":"/a/-","value":1}]""")
            .ShouldBe(JsonPatchErrorKind.InvalidArrayIndex);
        var appended = Apply("""{"a":[1]}""", """[{"op":"add","path":"/a/-","value":2}]""");
        Canonical(appended).ShouldBe("""{"a":[1,2]}""");
    }

    // RFC 6902 section 4.4 'move': 'from' MUST NOT be a proper prefix of 'path'.
    [Test]
    public void Move_ProperPrefixRejected()
    {
        ApplyError(
            """{"a":{"b":1}}""",
            """[{"op":"move","from":"/a","path":"/a/b"}]"""
        ).ShouldBe(JsonPatchErrorKind.MalformedPointer);
        ApplyError(
            """{"a":1}""",
            """[{"op":"move","from":"","path":"/a"}]"""
        ).ShouldBe(JsonPatchErrorKind.MalformedPointer);
    }

    [Test]
    public void Move_SameLocationAndArrayReorder()
    {
        var same = Apply("""{"a":1}""", """[{"op":"move","from":"/a","path":"/a"}]""");
        Canonical(same).ShouldBe("""{"a":1}""");

        // RFC 6902 section 4.4: remove first, so indices shift.
        var moved = Apply(
            """{"a":[1,2,3]}""",
            """[{"op":"move","from":"/a/0","path":"/a/2"}]"""
        );
        Canonical(moved).ShouldBe("""{"a":[2,3,1]}""");
    }

    // RFC 6902 section 3/5: operations apply in order; unknown op is an error.
    [Test]
    public void Operations_AddRemoveReplaceCopy()
    {
        var result = Apply(
            """{"a":1}""",
            """[{"op":"add","path":"/b","value":2},{"op":"replace","path":"/a","value":3},{"op":"copy","from":"/a","path":"/c"},{"op":"remove","path":"/b"}]"""
        );
        Canonical(JsonNode.Parse(Canonical(result))!).ShouldBe("""{"a":3,"c":3}""");
    }

    [Test]
    public void Operations_UnknownOpRejected()
    {
        ApplyError("""{"a":1}""", """[{"op":"merge","path":"/a","value":2}]""")
            .ShouldBe(JsonPatchErrorKind.UnknownOperation);
    }

    // Duplicate member names: the JSON layer preserves them verbatim (round-trips both),
    // so no silent last-wins normalization can flow into patch application. Assert explicitly.
    [Test]
    public void Documents_DuplicateMembersPreservedVerbatim()
    {
        var doc = JsonNode.Parse("""{"a":1,"a":2}""");
        Canonical(doc).ShouldBe("""{"a":1,"a":2}""");
    }

    // SparseFragments applies a patch atomically: a failing multi-op patch changes nothing.
    [Test]
    public void Application_IsAtomicOnFailure()
    {
        var baseline = JsonNode.Parse("""{"a":1,"b":2}""");
        var before = Canonical(baseline);
        try
        {
            SparseJsonPatchBridge.Apply(
                baseline,
                baselineIsAbsent: false,
                System.Text.Encoding.UTF8.GetBytes(
                    """[{"op":"replace","path":"/a","value":9},{"op":"test","path":"/b","value":"nope"}]"""
                ).AsMemory(),
                StringComparison.Ordinal,
                out _
            );
            throw new InvalidOperationException("Expected failure.");
        }
        catch (JsonPatchException ex)
        {
            ex.Kind.ShouldBe(JsonPatchErrorKind.TestFailed);
        }

        Canonical(baseline).ShouldBe(before);
    }
}
