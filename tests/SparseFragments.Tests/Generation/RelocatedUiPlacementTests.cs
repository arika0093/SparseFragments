using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

/// <summary>Placement checks for model-nested UI/editing types.</summary>
/// <remarks>
/// UI/editing types (<c>Observable</c>, <c>ReadOnlyView</c>,
/// <c>EditSession</c>) nest in the annotated model again; only the
/// operation/DTO/converter implementations live in the per-model
/// <c>SparseFragments.Generated</c> container. Colliding models retreat the
/// generated UI type behind a <c>Sparse</c> prefix.
/// </remarks>
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
        // The colliding observable retreats behind the Sparse prefix but
        // stays nested in the model.
        nested.ShouldNotContain("Observable");
        nested.ShouldContain("SparseObservable");
        nested.ShouldContain("ReadOnlyView");
        nested.ShouldContain("EditSession");
        // No UI type leaks into the generated namespace.
        compilation
            .GetSymbolsWithName("SparseObservable")
            .OfType<INamedTypeSymbol>()
            .Where(static symbol =>
                symbol
                    .ContainingNamespace.ToDisplayString()
                    .StartsWith("SparseFragments.Generated", StringComparison.Ordinal)
            )
            .ShouldBeEmpty();
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
        // Surface plus the split implementation file; UI/editing types nest
        // in the model, so no per-family hints remain.
        hints
            .Count(static key => key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        hints
            .Count(static key => key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal))
            .ShouldBe(1);
        hints
            .Count(static key => key.EndsWith(".Observable.g.cs", StringComparison.Ordinal))
            .ShouldBe(0);
        hints
            .Count(static key => key.EndsWith(".ReadOnlyView.g.cs", StringComparison.Ordinal))
            .ShouldBe(0);
        hints
            .Count(static key => key.EndsWith(".EditSession.g.cs", StringComparison.Ordinal))
            .ShouldBe(0);
        hints
            .Count(static key => key.EndsWith(".DescriptorFactory.g.cs", StringComparison.Ordinal))
            .ShouldBe(0);
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
        // UI/editing types nest in the internal model too.
        internalModel!.GetTypeMembers("Observable").ShouldHaveSingleItem();
        internalModel.GetTypeMembers("EditSession").ShouldHaveSingleItem();
        internalModel.DeclaredAccessibility.ShouldBe(Accessibility.Internal);
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
            // The factory returns the model-nested session type itself.
            SymbolEqualityComparer.Default.Equals(
                method.ReturnType.ContainingType,
                model
            ).ShouldBeTrue();
        }
        var toObservable = extensions.GetMembers("ToObservable").ShouldHaveSingleItem();
        ((IMethodSymbol)toObservable)
            .ReturnType.ToDisplayString()
            .ShouldContain("Reloc.Probe.EntryModel.Observable");
    }

    [Test]
    public void CollisionNamesAndTrickyLiteralsStayIntact()
    {
        // Hardening for tricky member names and literals now that UI types
        // nest in the model: a member colliding with proxy internals plus a
        // string literal carrying child-ref text must compile, with the
        // literal verbatim and no cross-file bridge.
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
        var (compilation, surface, all) = Generate(source);
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        // The attribute literal survives verbatim in the model surface.
        surface.ShouldContain(
            "\"call this.foo; child ref global::Reloc.Probe.TrickyChild.Observable; use __model.bar\""
        );
        foreach (var text in all.Values)
        {
            text.ShouldNotContain("\"call observable.foo");
            text.ShouldNotContain("observable.bar\"");
        }
        // Descriptors stay inside the model-nested Observable.
        var model = compilation.GetTypeByMetadataName("Reloc.Probe.TrickyModel");
        model.ShouldNotBeNull();
        model!.GetTypeMembers("Observable").ShouldHaveSingleItem();
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
        // No standalone factory remains: descriptors live in the
        // model-nested Observable.
        compilation.GetSymbolsWithName("DescriptorFactory").ShouldBeEmpty();
        model!
            .GetTypeMembers("Observable")
            .ShouldHaveSingleItem()
            .GetMembers()
            .Select(static member => member.Name)
            .ShouldContain(name =>
                name.StartsWith("__SparseGetDescriptors_", StringComparison.Ordinal)
            );
        all.Keys.Count(static key =>
                key.EndsWith(".DescriptorFactory.g.cs", StringComparison.Ordinal)
            )
            .ShouldBe(0);
    }
}
