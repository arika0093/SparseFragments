using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>
/// Direct unit tests for the shared incremental-generator plumbing (issue #30):
/// location snapshots, multi-argument diagnostic payloads and IsExternalInit.
/// </summary>
public sealed class IncrementalPlumbingTests
{
    [Test]
    public void Snapshot_CapturesSourceLocation()
    {
        var tree = CSharpSyntaxTree.ParseText("class Probe { }", path: "Probe.cs");
        var location = tree.GetRoot()
            .DescendantTokens()
            .First(static token =>
                token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ClassKeyword)
            )
            .GetLocation();
        var snapshot = SparseLocationSnapshot.Capture(location);
        snapshot.HasValue.ShouldBeTrue();
        snapshot!.Value.FilePath.ShouldBe("Probe.cs");
        snapshot.Value.Start.ShouldBe(location.SourceSpan.Start);
        snapshot.Value.Length.ShouldBe(location.SourceSpan.Length);
    }

    [Test]
    public void Snapshot_DistinguishesNullAndNoSource()
    {
        SparseLocationSnapshot.Capture(null).HasValue.ShouldBeFalse();
        var none = SparseLocationSnapshot.Capture(Location.None);
        none.HasValue.ShouldBeTrue();
        none!.Value.FilePath.ShouldBe(string.Empty);
    }

    [Test]
    public void Payload_ComparesByIdLocationAndArguments()
    {
        var left = new SparseDiagnosticPayload(
            "SPF021",
            new SparseLocationSnapshot("A.cs", 1, 2),
            ImmutableArray.Create<string?>("dup", "extra")
        );
        var same = new SparseDiagnosticPayload(
            "SPF021",
            new SparseLocationSnapshot("A.cs", 1, 2),
            ImmutableArray.Create<string?>("dup", "extra")
        );
        var reordered = left with { Arguments = ImmutableArray.Create<string?>("extra", "dup") };
        var renamed = left with { DescriptorId = "SPF009" };

        left.ShouldBe(same);
        left.GetHashCode().ShouldBe(same.GetHashCode());
        left.ShouldNotBe(reordered);
        left.ShouldNotBe(renamed);
        left.Argument(0).ShouldBe("dup");
        left.Argument(1).ShouldBe("extra");
        left.Argument(2).ShouldBeNull();
    }

    [Test]
    public void Diagnostic_KeepsSingleArgumentCompatibility()
    {
        var tree = CSharpSyntaxTree.ParseText("class Probe { }", path: "Probe.cs");
        var location = tree.GetRoot().GetLocation();
        var diagnostic = new SparseGeneratorDiagnostic("SPF004", location, "Member");
        diagnostic.DescriptorId.ShouldBe("SPF004");
        diagnostic.Argument1.ShouldBe("Member");
        diagnostic.Arguments.Length.ShouldBe(1);

        var payload = diagnostic.ToPayload();
        payload.DescriptorId.ShouldBe("SPF004");
        payload.Arguments.ShouldBe(["Member"]);
        payload.Location.ShouldBe(SparseLocationSnapshot.Capture(location));

        var roundTripped = SparseDiagnosticPayload.FromDiagnostic(diagnostic);
        roundTripped.ShouldBe(payload);
    }

    [Test]
    public void Diagnostic_SupportsMultipleArguments()
    {
        var first = new SparseGeneratorDiagnostic(
            "X",
            null,
            ImmutableArray.Create<string?>("one", "two")
        );
        var second = new SparseGeneratorDiagnostic(
            "X",
            null,
            ImmutableArray.Create<string?>("one", "two")
        );
        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.Argument1.ShouldBe("one");

        var other = new SparseGeneratorDiagnostic("X", null, ImmutableArray.Create<string?>("one"));
        other.ShouldNotBe(first);
    }

    private static CSharpCompilation CreateCompilation(string source, bool withReferences)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var references = new List<MetadataReference>();
        if (withReferences)
        {
            var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
                Path.PathSeparator
            );
            references.AddRange(
                tpa.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            );
        }

        return CSharpCompilation.Create(
            "SparseExternalInitProbe",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    [Test]
    public void ShouldEmit_CoversTargetReferenceSurfaces()
    {
        // No references at all: no marker exists, so emission is required.
        var missing = CreateCompilation("class Probe { }", withReferences: false);
        SparseExternalInit.ShouldEmitIsExternalInit(missing, configured: true).ShouldBeTrue();

        // Disabled by configuration: never emit.
        SparseExternalInit.ShouldEmitIsExternalInit(missing, configured: false).ShouldBeFalse();

        // The shared framework ships a public marker: no emission needed.
        var framework = CreateCompilation("class Probe { }", withReferences: true);
        framework
            .GetTypeByMetadataName("System.Runtime.CompilerServices.IsExternalInit")
            .ShouldNotBeNull();
        SparseExternalInit.ShouldEmitIsExternalInit(framework, configured: true).ShouldBeFalse();
        SparseExternalInit.ShouldEmitIsExternalInit(framework, configured: false).ShouldBeFalse();
    }
}
