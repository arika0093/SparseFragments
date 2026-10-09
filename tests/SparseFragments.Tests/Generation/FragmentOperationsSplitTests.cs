using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Source-placement regression tests for the Fragment operations relocation
/// (issue #193, stages 3-4). Conversion, projection, merge, diff, and
/// deep-clone bodies plus POCO helpers must live in the per-model operations
/// class; the annotated model keeps <c>Fragment</c>/<c>FragmentBuilder</c>
/// facades with narrow CLR-mandated bridges.
/// </summary>
public sealed class FragmentOperationsSplitTests
{
    private static Dictionary<string, string> Files() =>
        new(StringComparer.Ordinal)
        {
            ["OpsModels.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class OpsParent
                {
                    public string Label { get; set; } = "";
                    public OpsChild Child { get; set; } = new();
                    [SparseMerge(MergeMode.Replace)]
                    public OpsPoco Meta { get; set; } = new();
                    public List<int> Scores { get; set; } = new();
                }
                [SparseFragmentModel]
                public partial class OpsChild
                {
                    public int Count { get; set; }
                }
                public sealed class OpsPoco
                {
                    public string Note { get; set; } = "";
                    public List<string> Notes { get; set; } = new();
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
            "SparseFragmentOperationsSplitProbe",
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
    public void SurfaceKeepsFacadesAndBridgesWithoutAlgorithms()
    {
        var sources = Sources();
        var surfaceHint = sources.Keys.Single(key =>
            key.Contains("OpsParent", StringComparison.Ordinal)
            && !key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)
        );
        var surface = sources[surfaceHint];

        // Facades delegate to the relocated operations class.
        surface.ShouldContain("FragmentOperations.From(value)");
        surface.ShouldContain("FragmentOperations.ToModel(this");
        surface.ShouldContain("FragmentOperations.Merge(this, higherPriority)");
        surface.ShouldContain("FragmentOperations.ApplyChanges(this, changes)");
        surface.ShouldContain("FragmentOperations.Diff(before, after)");
        surface.ShouldContain("FragmentOperations.DeepClone(this)");
        // Ergonomic state and builders stay model-facing.
        surface.ShouldContain("public sealed class Fragment");
        surface.ShouldContain("public sealed class FragmentBuilder");
        surface.ShouldContain("public static Fragment Empty");
        // Narrow CLR-mandated bridges stay: init-only storage with cycle
        // registration, the writable writer, and per-member equality shims.
        surface.ShouldContain("private Fragment(Fragment source,");
        surface.ShouldContain("__SparseWriteWritableTo");
        surface.ShouldContain("__SparseAreEqual");
        // Heavy algorithms and helper families are gone from the model.
        surface.ShouldNotContain("__SparseDiffCore");
        surface.ShouldNotContain("__Diff_");
        surface.ShouldNotContain(".Add(this, clone)");
        surface.ShouldNotContain("public static Fragment Merge(Fragment self,");
        surface.ShouldNotContain("public static Fragment ApplyChanges(Fragment self,");
    }

    [Test]
    public void ImplementationCarriesOperationsAndPocoHelpers()
    {
        var sources = Sources();
        var implementationHint = sources.Keys.Single(key =>
            key.Contains("OpsParent", StringComparison.Ordinal)
            && key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)
        );
        var implementation = sources[implementationHint];

        implementation.ShouldContain("internal static class ");
        implementation.ShouldContain("FragmentOperations");
        implementation.ShouldContain("public static Fragment From(");
        implementation.ShouldContain("public static Fragment Merge(Fragment self,");
        implementation.ShouldContain("public static Fragment ApplyChanges(Fragment self,");
        implementation.ShouldContain("public static Fragment Diff(");
        implementation.ShouldContain("__SparseDiffCore");
        implementation.ShouldContain("__Diff_");
        implementation.ShouldContain("ToModel(Fragment fragment");
        implementation.ShouldContain(".Add(value, clone)");
        // POCO clone specialization moved out of the annotated model.
        implementation.ShouldContain("__Clone_");
        // Nested models recurse through their own facades.
        implementation.ShouldContain(".ToModel()");
    }
}
