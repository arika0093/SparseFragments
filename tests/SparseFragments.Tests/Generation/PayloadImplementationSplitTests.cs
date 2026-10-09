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
    public void SurfaceKeepsFacadesWithoutDtoOrConverterBodies()
    {
        var sources = Sources();
        var surfaceHint = sources.Keys.Single(key =>
            key.Contains("SplitParent", StringComparison.Ordinal)
            && !key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)
        );
        var surface = sources[surfaceHint];

        // Facades stay model-facing.
        surface.ShouldContain("class ChangePayload");
        surface.ShouldContain("FragmentJsonConverter");
        // Thin shell forwards to the implementation converter.
        surface.ShouldContain(
            "private sealed class FragmentJsonConverter : global::SparseFragments.Generated."
        );
        // No DTO hierarchy or converter implementation inside the model.
        surface.ShouldNotContain("static partial class __Internal_");
        surface.ShouldNotContain("public class Core_");
        surface.ShouldNotContain("public sealed class Root_");
        surface.ShouldNotContain("JsonDerivedType");
        surface.ShouldNotContain(
            ": global::System.Text.Json.Serialization.JsonConverter<Fragment>"
        );
        surface.ShouldNotContain("public override Fragment Read(");
        // Container references resolve through the shared file alias.
        surface.ShouldContain("using __Internal_");
    }

    [Test]
    public void ImplementationCarriesDtosAndConverterBodies()
    {
        var sources = Sources();
        var implementationHint = sources.Keys.Single(key =>
            key.Contains("SplitParent", StringComparison.Ordinal)
            && key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)
        );
        var implementation = sources[implementationHint];

        implementation.ShouldContain("namespace SparseFragments.Generated");
        implementation.ShouldContain("static partial class __Internal_");
        implementation.ShouldContain("public class Core_");
        implementation.ShouldContain("public sealed class Root_");
        implementation.ShouldContain("JsonDerivedType");
        implementation.ShouldContain("FragmentJsonConverter");
        implementation.ShouldContain("public override Fragment Read(");
        implementation.ShouldContain("public override void Write(");
        // Nested child payloads resolve to the implementation namespace.
        implementation.ShouldContain("global::SparseFragments.Generated.__Internal_");
        // Wire protocol is untouched by the move.
        implementation.ShouldContain("\"member\"");
        implementation.ShouldContain("$root");
    }
}
