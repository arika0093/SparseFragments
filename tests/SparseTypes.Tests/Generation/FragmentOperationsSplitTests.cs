using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Generation;

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
                using SparseTypes;
                using System.Collections.Generic;
                [SparseModel]
                public partial class OpsParent
                {
                    public string Label { get; set; } = "";
                    public OpsChild Child { get; set; } = new();
                    [SparseMerge(MergeMode.Replace)]
                    public OpsPoco Meta { get; set; } = new();
                    public List<int> Scores { get; set; } = new();
                }
                [SparseModel]
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
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
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
            new ISourceGenerator[] { new SparseTypesGenerator().AsSourceGenerator() }
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
    public void MergedFileKeepsFacadesAndBridgesWithAlgorithms()
    {
        var sources = Sources();
        // Single-file emission: one merged hint per model carries the surface
        // plus implementations.
        var mergedHint = sources.Keys.Single(key =>
            key.Contains("OpsParent", StringComparison.Ordinal)
            && key.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal)
        );
        sources
            .Keys.Count(key =>
                key.Contains("OpsParent", StringComparison.Ordinal)
                && key.EndsWith(".Implementation.g.cs", StringComparison.Ordinal)
            )
            .ShouldBe(0);
        var merged = sources[mergedHint];

        // Facades delegate to the relocated operations class.
        merged.ShouldContain("FragmentOperations.From(value)");
        merged.ShouldContain("FragmentOperations.ToModel(this");
        merged.ShouldContain("FragmentOperations.Merge(this, higherPriority)");
        merged.ShouldContain("FragmentOperations.ApplyChanges(this, changes)");
        merged.ShouldContain("FragmentOperations.Diff(before, after)");
        merged.ShouldContain("FragmentOperations.DeepClone(this)");
        // Ergonomic state and builders stay model-facing.
        merged.ShouldContain("public sealed class Fragment");
        merged.ShouldContain("public sealed class FragmentBuilder");
        merged.ShouldContain("public static Fragment Empty");
        // Narrow CLR-mandated bridges stay: init-only storage with cycle
        // registration, the writable writer, and per-member equality shims.
        merged.ShouldContain("private Fragment(Fragment source,");
        merged.ShouldContain("__SparseWriteWritableTo");
        merged.ShouldContain("__SparseAreEqual");
        // Heavy algorithms live in the same merged file.
        merged.ShouldContain("__SparseDiffCore");
        merged.ShouldContain("__Diff_");
        merged.ShouldContain("public static Fragment Merge(Fragment self,");
        merged.ShouldContain("public static Fragment ApplyChanges(Fragment self,");
    }

    [Test]
    public void MergedFileCarriesOperationsAndPocoHelpers()
    {
        var sources = Sources();
        var mergedHint = sources.Keys.Single(key =>
            key.Contains("OpsParent", StringComparison.Ordinal)
            && key.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal)
        );
        var merged = sources[mergedHint];

        // Same-file-only helpers narrow to file-scoped types on C# 11+.
        merged.ShouldContain(" static class ");
        merged.ShouldContain("FragmentOperations");
        merged.ShouldContain("public static Fragment From(");
        merged.ShouldContain("public static Fragment Merge(Fragment self,");
        merged.ShouldContain("public static Fragment ApplyChanges(Fragment self,");
        merged.ShouldContain("public static Fragment Diff(");
        merged.ShouldContain("__SparseDiffCore");
        merged.ShouldContain("__Diff_");
        merged.ShouldContain("ToModel(Fragment fragment");
        merged.ShouldContain(".Add(value, clone)");
        // POCO clone specialization moved out of the annotated model.
        merged.ShouldContain("__Clone_");
        // Nested models recurse through their own facades.
        merged.ShouldContain(".ToModel()");
    }

    [Test]
    public void OperationsAccessibilityFollowsLanguageVersion()
    {
        var sources = Sources();
        var mergedHint = sources.Keys.Single(key =>
            key.Contains("OpsParent", StringComparison.Ordinal)
            && key.EndsWith(".SparseTypes.g.cs", StringComparison.Ordinal)
        );
        // Driver compilations default to the latest stable language (C# 11+),
        // so the same-file-only operations class narrows to file-scoped.
        sources[mergedHint].ShouldContain("file static class ");
    }
}
