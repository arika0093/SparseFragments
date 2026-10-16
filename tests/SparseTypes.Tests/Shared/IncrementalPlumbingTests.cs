using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

/// <summary>Location snapshots, multi-argument diagnostic payloads and IsExternalInit.</summary>
public sealed class IncrementalPlumbingTests
{
    private static SparseCollectionInfo KeyedCollection(
        ImmutableArray<string> keyPropertyNames = default
    ) =>
        new(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            new SparseTypeModel("Item", "Item", "Item", true, false, null),
            null,
            "System.Collections.Generic.List<T>",
            SparseCollectionSemantic.KeyedSequence,
            keyPropertyNames.IsDefault ? ImmutableArray.Create("Id") : keyPropertyNames,
            "int",
            SparseKeyKind.Property,
            "0",
            "TemporaryId"
        );

    [Test]
    public void KeyedCollectionInfo_ComparesKeyMetadataStructurally()
    {
        var left = KeyedCollection(ImmutableArray.Create("Id"));
        var equal = KeyedCollection(ImmutableArray.Create("Id"));

        left.ShouldBe(equal);
        left.GetHashCode().ShouldBe(equal.GetHashCode());

        left.ShouldNotBe(KeyedCollection(ImmutableArray.Create("OtherId")));
        left.ShouldNotBe(KeyedCollection(ImmutableArray.Create("Id", "TenantId")));
        left.ShouldNotBe(KeyedCollection(ImmutableArray.Create("TenantId", "Id")));
        left.ShouldNotBe(left with { Kind = SparseCollectionKind.Array });
        left.ShouldNotBe(left with { CloneKind = SparseCloneCollectionKind.Array });
        left.ShouldNotBe(left with { ElementType = left.ElementType with { Name = "OtherItem" } });
        left.ShouldNotBe(
            left with
            {
                ValueType = new SparseTypeModel("Value", "Value", "Value", true, false, null),
            }
        );
        left.ShouldNotBe(left with { NamedTypeDefinition = "OtherList<T>" });
        left.ShouldNotBe(left with { Semantic = SparseCollectionSemantic.ScalarSequence });
        left.ShouldNotBe(left with { KeyTypeName = "long" });
        left.ShouldNotBe(left with { KeyKind = SparseKeyKind.None });
        left.ShouldNotBe(left with { UnassignedKeyExpression = "1" });
        left.ShouldNotBe(left with { TemporaryKeyPropertyName = "PendingId" });
        left.ShouldNotBe(left with { KeyIsReferenceType = true });

        var defaultKeys = left with { KeyPropertyNames = default };
        var emptyKeys = left with { KeyPropertyNames = ImmutableArray<string>.Empty };
        defaultKeys.ShouldBe(left with { KeyPropertyNames = default });
        defaultKeys.ShouldNotBe(emptyKeys);
        emptyKeys.ShouldBe(left with { KeyPropertyNames = ImmutableArray<string>.Empty });
    }

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
