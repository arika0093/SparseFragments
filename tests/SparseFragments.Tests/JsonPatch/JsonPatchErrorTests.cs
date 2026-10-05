using SparseFragments;
using static SparseFragments.JsonPatch.Tests.PatchTestHelpers;

namespace SparseFragments.JsonPatch.Tests;

/// <summary>Standalone import-bridge error kinds.</summary>
public sealed class JsonPatchErrorTests
{
    private static Optional<PatchWidget.Fragment?> StandaloneBaseline() =>
        Optional<PatchWidget.Fragment?>.Present(
            new PatchWidget.Fragment
            {
                Count = Optional<int>.Present(1),
                Tags = Optional<List<string>>.Present(new List<string> { "a" }),
            }
        );

    private static global::SparseFragments.JsonPatchException ImportStandalone(string patch) =>
        Should.Throw<global::SparseFragments.JsonPatchException>(() =>
            PatchWidget.Patch.FromJsonPatch(StandaloneBaseline(), Utf8(patch))
        );

    [Test]
    public void MalformedDocument()
    {
        var cases = new[]
        {
            """{"op":"add","path":"/Count","value":1}""",
            """[{"op":"add"}]""",
            """[{"op":"add","path":"/Count"}]""",
            """[{"op":"move","path":"/Count"}]""",
            """not json""",
            """[42]""",
        };
        foreach (var json in cases)
        {
            ImportStandalone(json)
                .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.MalformedDocument);
        }

        // Empty bytes are also malformed.
        Should
            .Throw<global::SparseFragments.JsonPatchException>(() =>
                PatchWidget.Patch.FromJsonPatch(StandaloneBaseline(), Array.Empty<byte>())
            )
            .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.MalformedDocument);
    }

    [Test]
    public void UnknownOperation()
    {
        ImportStandalone("""[{"op":"merge","path":"/Count","value":1}]""")
            .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.UnknownOperation);
    }

    [Test]
    public void MalformedPointer()
    {
        var cases = new[]
        {
            """[{"op":"remove","path":"Count"}]""",
            """[{"op":"remove","path":"/a~2b"}]""",
            """[{"op":"remove","path":"/dangling~"}]""",
        };
        foreach (var json in cases)
        {
            ImportStandalone(json)
                .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.MalformedPointer);
        }
    }

    [Test]
    public void MissingTarget()
    {
        var cases = new[]
        {
            """[{"op":"remove","path":"/Missing"}]""",
            """[{"op":"replace","path":"/Missing","value":1}]""",
            """[{"op":"test","path":"/Missing","value":1}]""",
            """[{"op":"remove","path":"/Nested/Missing"}]""",
        };
        foreach (var json in cases)
        {
            ImportStandalone(json)
                .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.MissingTarget);
        }
    }

    [Test]
    public void MissingParentForNestedAdd()
    {
        var cases = new[]
        {
            """[{"op":"add","path":"/Missing/Child","value":1}]""",
            """[{"op":"add","path":"/Nested/Missing/Deep","value":1}]""",
        };
        foreach (var json in cases)
        {
            ImportStandalone(json)
                .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.MissingParent);
        }
    }

    [Test]
    public void InvalidArrayIndex()
    {
        var cases = new[]
        {
            """[{"op":"remove","path":"/Tags/5"}]""",
            """[{"op":"remove","path":"/Tags/-"}]""",
            """[{"op":"remove","path":"/Tags/nope"}]""",
            """[{"op":"replace","path":"/Tags/01","value":"x"}]""",
        };
        foreach (var json in cases)
        {
            ImportStandalone(json)
                .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.InvalidArrayIndex);
        }
    }

    [Test]
    public void FailedTest()
    {
        ImportStandalone("""[{"op":"test","path":"/Count","value":999}]""")
            .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.TestFailed);
    }

    [Test]
    public void UnmappedProperty()
    {
        ImportStandalone("""[{"op":"add","path":"/Unknown","value":1}]""")
            .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.UnmappedProperty);
    }

    [Test]
    public void DeserializationTypeMismatch()
    {
        ImportStandalone("""[{"op":"replace","path":"/Count","value":"not-a-number"}]""")
            .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.DeserializationFailed);
    }

    [Test]
    public void MoveFromMissingTarget()
    {
        ImportStandalone("""[{"op":"move","from":"/Missing","path":"/Count"}]""")
            .Kind.ShouldBe(global::SparseFragments.JsonPatchErrorKind.MissingTarget);
    }
}
