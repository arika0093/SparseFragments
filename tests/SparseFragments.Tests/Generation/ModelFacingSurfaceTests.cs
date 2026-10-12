using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

// Issue #195, stage 5: locks the intended model-facing surface after the
// #190-#194 relocations and the UI/session return. Fragment/Patch/ChangeSet/
// ChangePayload plus the UI/editing types (Observable, ReadOnlyView,
// EditSession) nest in the annotated model; FragmentBuilder, SparsePaths,
// DescriptorFactory, and the operation/DTO/converter implementations live in
// the per-model SparseFragments.Generated container.
public sealed class ModelFacingSurfaceTests
{
    private const string ShapesSource = """
        using SparseFragments;
        namespace Surface.Probe
        {
            [SparseFragmentModel]
            public partial class SurfaceClass
            {
                public string Name { get; set; } = string.Empty;
                public SharedPoco? Child { get; set; }
            }
            [SparseFragmentModel]
            public partial record SurfaceRecord(string Name)
            {
                public string? Note { get; set; }
                public SharedPoco? Child { get; set; }
            }
            [SparseFragmentModel]
            public partial struct SurfaceStruct
            {
                public string Name { get; set; }
            }
            [SparseFragmentModel]
            internal partial class SurfaceInternal
            {
                public string Name { get; set; } = string.Empty;
            }
            public partial class SharedPoco
            {
                public string Label { get; set; } = string.Empty;
            }
            [SparseFragmentModel]
            public partial class SurfaceSecondRoot
            {
                public string Name { get; set; } = string.Empty;
                public SharedPoco? Child { get; set; }
            }
        }
        namespace Surface.Probe.First
        {
            using SparseFragments;
            [SparseFragmentModel]
            public partial class Dup
            {
                public string Name { get; set; } = string.Empty;
            }
        }
        namespace Surface.Probe.Second
        {
            using SparseFragments;
            [SparseFragmentModel]
            public partial class Dup
            {
                public string Name { get; set; } = string.Empty;
            }
        }
        """;

    private const string GlobalSource = """
        using SparseFragments;
        [SparseFragmentModel]
        public partial class GlobalSurfaceModel
        {
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static readonly string[] BannedNested =
    [
        "SparseObservable",
        "DescriptorFactory",
        "PatchOperations",
        "ChangeSetOperations",
        "FragmentBuilder",
        "SparsePaths",
    ];

    private static Compilation Generate(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "ModelFacingSurfaceProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return updated;
    }

    private static void AssertClean(Compilation compilation)
    {
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    private static INamedTypeSymbol RequireModel(Compilation compilation, string metadataName)
    {
        var model = compilation.GetTypeByMetadataName(metadataName);
        model.ShouldNotBeNull($"Expected model '{metadataName}'.");
        return model!;
    }

    private static void AssertIntendedSurface(
        Compilation compilation,
        INamedTypeSymbol model,
        bool expectSessionFamily
    )
    {
        var nested = model.GetTypeMembers().Select(static type => type.Name).ToArray();
        foreach (var banned in BannedNested)
            nested.ShouldNotContain(banned);
        nested
            .Where(static name => name.EndsWith("FragmentOperations", StringComparison.Ordinal))
            .ShouldBeEmpty();
        nested
            .Where(static name => name.EndsWith("FragmentJsonConverter", StringComparison.Ordinal))
            .ShouldBeEmpty();
        foreach (var expected in new[] { "Fragment", "Patch", "ChangeSet" })
            nested.ShouldContain(expected);
        // UI/editing types nest in the model; structs skip the session family.
        nested.ShouldContain("ReadOnlyView");
        if (expectSessionFamily)
        {
            foreach (var expected in new[] { "Observable", "EditSession" })
                nested.ShouldContain(expected);
        }
        else
        {
            foreach (var unexpected in new[] { "Observable", "EditSession" })
                nested.ShouldNotContain(unexpected);
        }
        // FragmentBuilder lives in the per-model Generated container.
        compilation
            .GetSymbolsWithName("FragmentBuilder")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol =>
                symbol
                    .ContainingNamespace.ToDisplayString()
                    .StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .ShouldNotBeEmpty();

        // The converter shell stays private inside Fragment and derives from
        // the relocated implementation converter (stage 3, #192).
        var fragment = model.GetTypeMembers("Fragment").ShouldHaveSingleItem();
        var shell = fragment.GetTypeMembers("FragmentJsonConverter").ShouldHaveSingleItem();
        shell.DeclaredAccessibility.ShouldBe(Accessibility.Private);
        shell
            .BaseType!.ContainingNamespace.ToDisplayString()
            .ShouldStartWith("SparseFragments.Generated");

        if (expectSessionFamily)
        {
            // Same-short-name roots share the simple extension prefix, so
            // scope the lookup to the model's own namespace.
            var modelNamespace = model.ContainingNamespace.ToDisplayString();
            var extensions = compilation
                .GetSymbolsWithName(name =>
                    name.StartsWith(model.Name + "Extensions_", StringComparison.Ordinal)
                )
                .OfType<INamedTypeSymbol>()
                .Where(static symbol => symbol.DeclaringSyntaxReferences.Length > 0)
                .Where(symbol =>
                    string.Equals(
                        symbol.ContainingNamespace.ToDisplayString(),
                        modelNamespace,
                        StringComparison.Ordinal
                    )
                )
                .ShouldHaveSingleItem();
            foreach (
                var method in extensions.GetMembers("CreateEditSession").OfType<IMethodSymbol>()
            )
            {
                // Sessions nest in the model; the return type stays model-scoped.
                method.ReturnType.ToDisplayString().ShouldContain(model.Name + ".EditSession");
            }
        }
    }

    [Test]
    public void IntendedSurfaceHoldsAcrossClassRecordStructAndInternal()
    {
        var compilation = Generate(ShapesSource);
        AssertClean(compilation);
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "Surface.Probe.SurfaceClass"),
            expectSessionFamily: true
        );
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "Surface.Probe.SurfaceRecord"),
            expectSessionFamily: true
        );
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "Surface.Probe.SurfaceStruct"),
            expectSessionFamily: false
        );
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "Surface.Probe.SurfaceInternal"),
            expectSessionFamily: true
        );
    }

    [Test]
    public void GlobalNamespaceModelKeepsTheSameSurface()
    {
        var compilation = Generate(GlobalSource);
        AssertClean(compilation);
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "GlobalSurfaceModel"),
            expectSessionFamily: true
        );
    }

    [Test]
    public void SameShortNameModelsGetDistinctStableContainers()
    {
        var compilation = Generate(ShapesSource);
        AssertClean(compilation);
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "Surface.Probe.First.Dup"),
            expectSessionFamily: true
        );
        AssertIntendedSurface(
            compilation,
            RequireModel(compilation, "Surface.Probe.Second.Dup"),
            expectSessionFamily: true
        );
        var sessions = compilation
            .GetSymbolsWithName("EditSession")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol => symbol.ContainingType is { Name: "Dup" })
            .Select(static symbol => symbol.ToDisplayString())
            .Distinct()
            .ToArray();
        // Both same-short-name roots nest their own session in the model.
        sessions.Length.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public void EditSessionMemberNameIsFreeOnTheModel()
    {
        const string source = """
            using SparseFragments;
            namespace Surface.Probe;
            [SparseFragmentModel]
            public partial class FreedSessionMemberModel
            {
                public string EditSession { get; set; } = string.Empty;
                public string Name { get; set; } = string.Empty;
            }
            """;
        var compilation = Generate(source);
        AssertClean(compilation);
        var model = RequireModel(compilation, "Surface.Probe.FreedSessionMemberModel");
        // A same-named property pushes the generated session behind a Sparse
        // prefix, mirroring observable/view collision resolution.
        model.GetTypeMembers("EditSession").ShouldBeEmpty();
        model.GetTypeMembers("SparseEditSession").ShouldHaveSingleItem();
    }

    [Test]
    public void EditSessionNestedTypeCollidesWithGenerated()
    {
        const string source = """
            using SparseFragments;
            namespace Surface.Probe;
            [SparseFragmentModel]
            public partial class CollidingSessionNestedModel
            {
                public string Name { get; set; } = string.Empty;
                public sealed class EditSession
                {
                    public string Note { get; set; } = string.Empty;
                }
            }
            """;
        var compilation = Generate(source);
        // The generated session nests in the model, so a user-nested
        // EditSession is a duplicate definition.
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldNotBeEmpty();
    }

    [Test]
    public void PromotedSharedPocoCarriesNoImplementationTypes()
    {
        var compilation = Generate(ShapesSource);
        AssertClean(compilation);
        var poco = RequireModel(compilation, "Surface.Probe.SharedPoco");
        var nested = poco.GetTypeMembers().Select(static type => type.Name).ToArray();
        foreach (var banned in BannedNested)
            nested.ShouldNotContain(banned);
    }
}
