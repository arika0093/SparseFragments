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
        ImmutableArray<Diagnostic> CompilationDiagnostics
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
                generated
                    .SourceText.ToString()
                    .Contains("Extensions_", StringComparison.Ordinal)
            )
            .SourceText.ToString();
        var match = Regex.Match(
            generatedSource,
            @"public static partial class ([A-Za-z0-9_]+Extensions_[A-F0-9]{8})"
        );
        match.Success.ShouldBeTrue();
        return (match.Groups[1].Value, updatedCompilation.GetDiagnostics());
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
