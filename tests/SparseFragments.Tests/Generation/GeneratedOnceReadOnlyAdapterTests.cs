using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Generated-Once read-only adapter regression tests (issue #181): two
/// read-only models share one adapter definition per compilation, and
/// per-model output instantiates the shared types instead of redefining
/// the generic adapters.
/// </summary>
public sealed class GeneratedOnceReadOnlyAdapterTests
{
    private static CSharpCompilation CreateCompilation(Dictionary<string, string> files)
    {
        var trees = files
            .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key))
            .ToArray();
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        return CSharpCompilation.Create(
            "SparseReadOnlyOnceProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static Dictionary<string, string> GeneratedSources(CSharpCompilation compilation)
    {
        var driver = CSharpGeneratorDriver
            .Create([new SparseFragmentsGenerator().AsSourceGenerator()])
            .RunGenerators(compilation);
        return driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().SourceText.ToString(),
                StringComparer.Ordinal
            );
    }

    [Test]
    public void TwoReadOnlyModelsShareOneAdapterDefinition()
    {
        var sources = GeneratedSources(
            CreateCompilation(
                new Dictionary<string, string>
                {
                    ["Models.cs"] = """
                    using SparseFragments;
                    using System.Collections.Generic;
                    [SparseFragmentModel]
                    public partial class AdapterFirst
                    {
                        public List<string> Items { get; set; } = new();
                    }
                    [SparseFragmentModel]
                    public partial class AdapterSecond
                    {
                        public Dictionary<string, string> Scores { get; set; } = new();
                    }
                    """,
                }
            )
        );

        var shared = sources
            .Where(pair => pair.Key.EndsWith(".ReadOnlyAdapters.g.cs", StringComparison.Ordinal))
            .ToArray();
        shared.ShouldHaveSingleItem();
        var text = shared[0].Value;
        text.ShouldContain("class " + SparseGeneratedOnceNames.ReadOnlyListAdapter + "<");
        text.ShouldContain("class " + SparseGeneratedOnceNames.ReadOnlyDictionaryAdapter + "<");
        CountOccurrences(text, "class " + SparseGeneratedOnceNames.ReadOnlyListAdapter + "<")
            .ShouldBe(1);

        var perModel = sources
            .Where(pair => pair.Key.EndsWith(".ReadOnlyView.g.cs", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToArray();
        perModel.Length.ShouldBe(2);
        foreach (var model in perModel)
        {
            model.ShouldNotContain("class __SparseReadOnlyCollection<");
            model.ShouldNotContain("class __SparseReadOnlyDictionary<");
            (
                model.Contains("global::SparseFragments.Generated.SparseReadOnlyListAdapter<")
                || model.Contains(
                    "global::SparseFragments.Generated.SparseReadOnlyDictionaryAdapter<"
                )
            ).ShouldBeTrue();
        }
    }

    [Test]
    public void SharedAdapterSourceCompilesCleanly()
    {
        var source = SparseGeneratedOnceReadOnlyAdapters.BuildSource(
            "SparseFragments.Generated",
            true,
            true,
            true,
            CancellationToken.None
        );
        var compilation = CreateCompilation(
            new Dictionary<string, string> { ["Shared.cs"] = source }
        );
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
