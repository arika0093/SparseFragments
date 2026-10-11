using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

/// <summary>Placement checks for relocated UI/editing types (issue #190).</summary>
public sealed class RelocatedUiPlacementTests
{
    private static (
        Compilation Compilation,
        string Surface,
        IReadOnlyDictionary<string, string> All
    ) Generate(string source)
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
            "RelocatedProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        var all = driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .ToDictionary(
                static generated => generated.HintName,
                static generated => generated.SourceText.ToString()
            );
        var surface = all.Values.First(text =>
            text.Contains("Extensions_", StringComparison.Ordinal)
        );
        return (updated, surface, all);
    }

    [Test]
    public void NoNestedUiTypesIncludingCollisions()
    {
        const string source = """
            using SparseFragments;
            namespace Reloc.Probe;
            [SparseFragmentModel]
            public partial class CollisionModel
            {
                public string Observable { get; set; } = string.Empty;
                public string Model { get; set; } = string.Empty;
                public string PropertyChanged { get; set; } = string.Empty;
            }
            """;
        var (compilation, _, _) = Generate(source);
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        var model = compilation.GetTypeByMetadataName("Reloc.Probe.CollisionModel");
        model.ShouldNotBeNull();
        var nested = model!.GetTypeMembers().Select(static type => type.Name).ToList();
        nested.ShouldNotContain("Observable");
        nested.ShouldNotContain("SparseObservable");
        nested.ShouldNotContain("ReadOnlyView");
        nested.ShouldNotContain("EditSession");
        // Relocated collision-safe observable exists in the generated namespace.
        compilation
            .GetSymbolsWithName("SparseObservable")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol =>
                symbol
                    .ContainingNamespace.ToDisplayString()
                    .StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .ShouldHaveSingleItem();
    }

    [Test]
    public void ImplementationHintsAreDistinct()
    {
        const string source = """
            using SparseFragments;
            namespace Reloc.Probe;
            [SparseFragmentModel]
            public partial class HintModel
            {
                public string Name { get; set; } = string.Empty;
            }
            """;
        var (_, _, all) = Generate(source);
        var hints = all.Keys.Where(static key => key.Contains("HintModel")).ToArray();
        // Surface plus implementations (observable, view, session, factory).
        hints
            .Count(static key => key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        hints
            .Count(static key => key.EndsWith(".Observable.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        hints
            .Count(static key => key.EndsWith(".ReadOnlyView.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        hints
            .Count(static key => key.EndsWith(".EditSession.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        hints
            .Count(static key => key.EndsWith(".DescriptorFactory.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Test]
    public void InternalAndGlobalModelsShareTheScheme()
    {
        const string source = """
            using SparseFragments;
            namespace Reloc.Probe;
            [SparseFragmentModel]
            internal partial class InternalModel
            {
                public string Name { get; set; } = string.Empty;
            }
            [SparseFragmentModel]
            public partial class GlobalCheck
            {
                public string Name { get; set; } = string.Empty;
            }
            """;
        var (compilation, _, _) = Generate(source);
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        var internalModel = compilation.GetTypeByMetadataName("Reloc.Probe.InternalModel");
        internalModel.ShouldNotBeNull();
        internalModel!.GetTypeMembers("Observable").ShouldBeEmpty();
        internalModel.GetTypeMembers("EditSession").ShouldBeEmpty();
        // Internal container stays internal; session still resolves.
        compilation
            .GetSymbolsWithName("EditSession")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol =>
                symbol
                    .ContainingNamespace.ToDisplayString()
                    .StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .Count()
            .ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public void ExtensionEntryPointsUseRelocatedTypes()
    {
        const string source = """
            using SparseFragments;
            namespace Reloc.Probe;
            [SparseFragmentModel]
            public partial class EntryModel
            {
                public string Name { get; set; } = string.Empty;
            }
            """;
        var (compilation, _, _) = Generate(source);
        var model = compilation.GetTypeByMetadataName("Reloc.Probe.EntryModel");
        model.ShouldNotBeNull();
        var extensions = compilation
            .GetSymbolsWithName(name => name.Contains("EntryModelExtensions_"))
            .OfType<INamedTypeSymbol>()
            .ShouldHaveSingleItem();
        var create = extensions.GetMembers("CreateEditSession");
        create.Length.ShouldBe(2);
        foreach (var method in create.OfType<IMethodSymbol>())
        {
            method
                .ReturnType.ContainingNamespace.ToDisplayString()
                .ShouldStartWith("SparseFragments.Generated");
        }
        var toObservable = extensions.GetMembers("ToObservable").ShouldHaveSingleItem();
        ((IMethodSymbol)toObservable)
            .ReturnType.ToDisplayString()
            .ShouldContain("SparseFragments.Generated");
    }

    [Test]
    public void CollisionNamesAndTrickyLiteralsStayIntact()
    {
        // Hardening for the string-based relocation rewrites (#190/#191): a
        // member colliding with rewritten identifiers (observable, pathPrefix)
        // plus a string literal carrying this./__model./child-ref text must
        // compile, with code rewritten and literals verbatim.
        const string source = """
            using SparseFragments;
            using System.ComponentModel;
            namespace Reloc.Probe;
            [SparseFragmentModel]
            public partial class TrickyModel
            {
                [Description("call this.foo; child ref global::Reloc.Probe.TrickyChild.Observable; use __model.bar")]
                public string Note { get; set; } = string.Empty;
                public string observable { get; set; } = string.Empty;
                public string pathPrefix { get; set; } = string.Empty;
                public TrickyChild Child { get; set; } = new();
            }
            [SparseFragmentModel]
            public partial class TrickyChild
            {
                public string Name { get; set; } = string.Empty;
            }
            """;
        var (compilation, _, all) = Generate(source);
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        var factory = all.Values.First(text =>
            text.Contains("internal static class DescriptorFactory", StringComparison.Ordinal)
        );
        // Member access in code follows the factory parameter.
        factory.ShouldContain("observable.observable");
        factory.ShouldContain("observable.pathPrefix");
        // The attribute literal survives verbatim on the surface bridge.
        var surface = all.Values.First(text =>
            text.Contains("__SparseAttributes_", StringComparison.Ordinal)
        );
        surface.ShouldContain(
            "\"call this.foo; child ref global::Reloc.Probe.TrickyChild.Observable; use __model.bar\""
        );
        foreach (var text in all.Values)
        {
            text.ShouldNotContain("\"call observable.foo");
            text.ShouldNotContain("observable.bar\"");
        }
    }

    [Test]
    public void DescriptorFactoryLivesOutsideModel()
    {
        const string source = """
            using SparseFragments;
            namespace Reloc.Probe;
            [SparseFragmentModel]
            public partial class FactoryModel
            {
                public string Name { get; set; } = string.Empty;
            }
            """;
        var (compilation, _, all) = Generate(source);
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        var model = compilation.GetTypeByMetadataName("Reloc.Probe.FactoryModel");
        model.ShouldNotBeNull();
        model!.GetTypeMembers("DescriptorFactory").ShouldBeEmpty();
        compilation
            .GetSymbolsWithName("DescriptorFactory")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol =>
                symbol
                    .ContainingNamespace.ToDisplayString()
                    .StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .ShouldHaveSingleItem();
        all.Keys.Count(static key =>
                key.EndsWith(".DescriptorFactory.g.cs", StringComparison.Ordinal)
            )
            .ShouldBe(1);
    }
}
