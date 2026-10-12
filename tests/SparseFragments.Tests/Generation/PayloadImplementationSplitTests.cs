using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Source-placement regression tests for the payload/converter relocation
/// (issue #192, stage 3). The typed payload DTO hierarchy and the
/// <c>FragmentJsonConverter</c> bodies must live in the per-model
/// implementation source; the annotated model keeps the public
/// <c>ChangePayload</c> facade plus a thin converter shell.
/// </summary>
public sealed class PayloadImplementationSplitTests
{
    private static Dictionary<string, string> Files() =>
        new(StringComparer.Ordinal)
        {
            ["SplitModels.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class SplitParent
                {
                    public string Label { get; set; } = "";
                    public SplitChild Child { get; set; } = new();
                    public List<string> Tags { get; set; } = new();
                }
                [SparseFragmentModel]
                public partial class SplitChild
                {
                    public int Count { get; set; }
                }
                """,
        };

    private static Dictionary<string, string> Sources()
    {
        var trees = Files()
            .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
            .ToArray();
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = tpa.Select(path =>
                (MetadataReference)MetadataReference.CreateFromFile(path)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "SparsePayloadSplitProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        var driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SparseFragmentsGenerator().AsSourceGenerator() }
        );
        var result = driver.RunGenerators(compilation).GetRunResult();
        result
            .Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)
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

    [Test]
    public void MergedFileKeepsFacadesWithDtoAndConverterBodies()
    {
        var sources = Sources();
        // Single-file emission: one merged hint per model carries the surface
        // plus implementations.
        var mergedHint = sources.Keys.Single(key =>
            key.Contains("SplitParent", StringComparison.Ordinal)
            && key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal)
        );
        sources
            .Keys.Count(key =>
                key.Contains("SplitParent", StringComparison.Ordinal)
                && key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)
            )
            .ShouldBe(0);
        var merged = sources[mergedHint];

        // Facades stay model-facing.
        merged.ShouldContain("class ChangePayload");
        merged.ShouldContain("FragmentJsonConverter");
        // Thin shell forwards to the implementation converter.
        merged.ShouldContain(
            "private sealed class FragmentJsonConverter : global::SparseFragments.Generated."
        );
        // DTO hierarchy and converter implementation share the merged file.
        merged.ShouldContain("static partial class __Internal_");
        merged.ShouldContain("public class Core_");
        merged.ShouldContain("public sealed class Root_");
        merged.ShouldContain("JsonDerivedType");
        merged.ShouldContain(": global::System.Text.Json.Serialization.JsonConverter<Fragment>");
        merged.ShouldContain("public override Fragment Read(");
        // Container references resolve through the shared file alias.
        merged.ShouldContain("using __Internal_");
    }

    [Test]
    public void MergedFileCarriesDtosAndConverterBodies()
    {
        var sources = Sources();
        var mergedHint = sources.Keys.Single(key =>
            key.Contains("SplitParent", StringComparison.Ordinal)
            && key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal)
        );
        var merged = sources[mergedHint];

        merged.ShouldContain("namespace SparseFragments.Generated");
        merged.ShouldContain("static partial class __Internal_");
        merged.ShouldContain("public class Core_");
        merged.ShouldContain("public sealed class Root_");
        merged.ShouldContain("JsonDerivedType");
        merged.ShouldContain("FragmentJsonConverter");
        merged.ShouldContain("public override Fragment Read(");
        merged.ShouldContain("public override void Write(");
        // Nested child payloads resolve to the implementation namespace.
        merged.ShouldContain("global::SparseFragments.Generated.__Internal_");
        // Wire protocol is untouched by the move.
        merged.ShouldContain("\"member\"");
        merged.ShouldContain("$root");
    }
}
