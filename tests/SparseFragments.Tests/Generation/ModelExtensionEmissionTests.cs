using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

public sealed class ModelExtensionEmissionTests
{
    private const string ModelSource = """
        using SparseFragments;
        namespace Stable.Generated;
        [SparseFragmentModel]
        public partial class StableModel
        {
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static (
        string ExtensionClass,
        string ExtensionAccessibility,
        ImmutableArray<Diagnostic> CompilationDiagnostics,
        string GeneratedSource,
        Compilation UpdatedCompilation
    ) Generate(string source, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: path);
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Select(static assemblyPath =>
                (MetadataReference)MetadataReference.CreateFromFile(assemblyPath)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "StableExtensionProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out _
        );
        var generatedSource = driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Single(static generated =>
                generated.SourceText.ToString().Contains("Extensions_", StringComparison.Ordinal)
            )
            .SourceText.ToString();
        var match = Regex.Match(
            generatedSource,
            @"(public|internal) static partial class ([A-Za-z0-9_]+Extensions_[A-F0-9]{8})"
        );
        match.Success.ShouldBeTrue();
        return (
            match.Groups[2].Value,
            match.Groups[1].Value,
            updatedCompilation.GetDiagnostics(),
            generatedSource,
            updatedCompilation
        );
    }

    [Test]
    public void ExtensionContainerHasStableTypeHashAndCompiles()
    {
        var first = Generate(ModelSource, "First.cs");
        var second = Generate("// Different path and trivia.\n" + ModelSource, "Renamed.cs");

        first.ExtensionClass.ShouldBe(second.ExtensionClass);
        first.ExtensionClass.ShouldContain("StableModelExtensions_");
        first
            .CompilationDiagnostics.Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            )
            .ShouldBeEmpty();
        second
            .CompilationDiagnostics.Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            )
            .ShouldBeEmpty();
    }

    [Test]
    public void InternalModelUsesInternalGeneratedTypesAndExtensions()
    {
        const string source = """
            using SparseFragments;
            namespace Stable.Generated;
            [SparseFragmentModel]
            internal partial class InternalStableModel
            {
                public string Name { get; set; } = string.Empty;
                public string Initial { get; init; } = string.Empty;
                public string ConstructorValue { get; }
                public InternalStableChild Child { get; set; } = new();
                public InternalStableModel(string constructorValue) => ConstructorValue = constructorValue;
            }
            internal partial class InternalStableChild
            {
                public string Value { get; set; } = string.Empty;
                public string Display => Value;
            }
            """;

        var generated = Generate(source, "InternalModel.cs");

        generated.ExtensionAccessibility.ShouldBe("internal");
        generated.GeneratedSource.ShouldContain("internal partial class InternalStableModel");
        generated.GeneratedSource.ShouldContain("internal sealed class Fragment");
        generated.GeneratedSource.ShouldContain("internal sealed class FragmentBuilder");
        generated.GeneratedSource.ShouldContain("internal sealed class Patch");
        generated.GeneratedSource.ShouldContain("internal sealed class ChangeSet");
        generated.GeneratedSource.ShouldContain("internal static partial class");
        generated.GeneratedSource.ShouldContain(
            "internal static global::Stable.Generated.InternalStableModel.ChangeSet CreateChangeSet"
        );
        generated
            .CompilationDiagnostics.Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            )
            .ShouldBeEmpty();
        var generatedChild = generated.UpdatedCompilation.GetTypeByMetadataName(
            "Stable.Generated.InternalStableChild"
        );
        generatedChild.ShouldNotBeNull();
        foreach (var generatedName in new[] { "Fragment", "Patch", "ChangeSet" })
        {
            generatedChild!
                .GetTypeMembers(generatedName)
                .Single()
                .DeclaredAccessibility.ShouldBe(Accessibility.Internal);
        }
    }

    [Test]
    public void ExtensionContainerHashDisambiguatesSanitizedTypeNameCollisions()
    {
        const string firstSource = """
            using SparseFragments;
            namespace Collision.First;
            [SparseFragmentModel]
            public partial class SharedModel { public string Value { get; set; } = string.Empty; }
            """;
        const string secondSource = """
            using SparseFragments;
            namespace Collision.Second;
            [SparseFragmentModel]
            public partial class SharedModel { public string Value { get; set; } = string.Empty; }
            """;

        var first = Generate(firstSource, "FirstCollision.cs");
        var second = Generate(secondSource, "SecondCollision.cs");

        first.ExtensionClass.ShouldNotBe(second.ExtensionClass);
        first
            .CompilationDiagnostics.Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            )
            .ShouldBeEmpty();
        second
            .CompilationDiagnostics.Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
            )
            .ShouldBeEmpty();
    }
}
