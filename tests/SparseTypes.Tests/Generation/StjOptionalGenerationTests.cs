using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

/// <summary>Certifies compilation-aware optional JSON support (issues #242/#243/#244).</summary>
/// <remarks>
/// The no-STJ probe compiles a model against framework references with every
/// System.Text.Json assembly removed, then asserts the generator emits only
/// serializer-independent APIs and the combined compilation still builds.
/// The STJ probe asserts the v1 bridge appears, and the pair asserts losing
/// the reference drops only JSON artifacts with stable hint names.
/// </remarks>
public sealed class StjOptionalGenerationTests
{
    private const string ModelSource = """
        using SparseTypes;
        using System.Collections.Generic;

        [SparseModel]
        public partial class StjProbeModel
        {
            public string? Label { get; set; }

            public List<string> Tags { get; set; } = new();
        }
        """;

    private static CSharpCompilation CreateCompilation(bool includeStj)
    {
        var tree = CSharpSyntaxTree.ParseText(ModelSource, path: "StjProbeModel.cs");
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Where(path =>
                includeStj
                || !Path.GetFileName(path).StartsWith("System.Text.Json", StringComparison.Ordinal)
            )
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "StjOptionalProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static Dictionary<string, string> Run(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        result
            .Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        return result
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().SourceText.ToString(),
                StringComparer.Ordinal
            );
    }

    private static void CombinedCompilationHasNoErrors(
        CSharpCompilation compilation,
        Dictionary<string, string> sources
    )
    {
        var generated = sources.Select(pair =>
            CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key)
        );
        var combined = compilation.AddSyntaxTrees(generated);
        combined
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Test]
    public void WithoutStj_EmitsSerializerIndependentCoreOnly()
    {
        var compilation = CreateCompilation(includeStj: false);
        var sources = Run(compilation);

        // Core families still emitted.
        sources.Keys.ShouldContain(static hint => hint.Contains("StjProbeModel"));
        var merged = string.Concat(sources.Values);
        merged.ShouldContain("ChangePayload");
        merged.ShouldContain("ToPayload");

        // No JSON surface anywhere.
        foreach (var pair in sources)
            pair.Value.ShouldNotContain("System.Text.Json");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void WithStj_EmitsVersion1Bridge()
    {
        var compilation = CreateCompilation(includeStj: true);
        var sources = Run(compilation);
        var merged = string.Concat(sources.Values);

        merged.ShouldContain("ChangePayloadEndpointJsonConverter");
        merged.ShouldContain("ChangePayloadItemKindJsonConverter");
        merged.ShouldContain("JsonPolymorphic");
        merged.ShouldContain("JsonUnmappedMemberHandling");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void LosingStjReferenceDropsOnlyJsonArtifacts()
    {
        var withStj = Run(CreateCompilation(includeStj: true));
        var withoutStj = Run(CreateCompilation(includeStj: false));

        // Deterministic regeneration: identical hint names, no stale sources.
        withoutStj.Keys.ShouldBe(withStj.Keys, ignoreOrder: true);
        foreach (var hint in withStj.Keys)
        {
            var hadJson = withStj[hint].Contains("System.Text.Json");
            var hasJson = withoutStj[hint].Contains("System.Text.Json");
            if (hadJson)
                hasJson.ShouldBeFalse(hint);
        }
        foreach (var source in withoutStj.Values)
            source.ShouldNotContain("System.Text.Json");
    }
}
