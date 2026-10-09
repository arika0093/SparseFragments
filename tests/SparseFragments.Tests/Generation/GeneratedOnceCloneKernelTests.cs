using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Generated-Once clone kernel regression tests (issue #182): two models
/// using the same collection shape share one kernel definition, and
/// per-model clone code calls the shared kernels instead of redefining
/// the generic implementations.
/// </summary>
public sealed class GeneratedOnceCloneKernelTests
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
            "SparseCloneOnceProbe",
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
    public void TwoModelsShareOneCloneKernelDefinition()
    {
        var sources = GeneratedSources(
            CreateCompilation(
                new Dictionary<string, string>
                {
                    ["Models.cs"] = """
                    using SparseFragments;
                    using System.Collections.Generic;
                    [SparseFragmentModel]
                    public partial class CloneFirst
                    {
                        public List<string> Items { get; set; } = new();
                    }
                    [SparseFragmentModel]
                    public partial class CloneSecond
                    {
                        public List<int> Values { get; set; } = new();
                        public Dictionary<string, string> Scores { get; set; } = new();
                    }
                    """,
                }
            )
        );

        var shared = sources
            .Where(pair => pair.Key.EndsWith(".CloneKernels.g.cs", StringComparison.Ordinal))
            .ToArray();
        shared.ShouldHaveSingleItem();
        var text = shared[0].Value;
        text.ShouldContain("class " + SparseGeneratedOnceNames.CloneKernels);
        CountOccurrences(text, "internal static TCollection __CloneList<T, TCollection>")
            .ShouldBe(1);
        CountOccurrences(
                text,
                "internal static TDictionary __CloneDictionary<TKey, TValue, TDictionary>"
            )
            .ShouldBe(1);

        var perModel = sources
            .Where(pair => pair.Key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToArray();
        perModel.Length.ShouldBe(2);
        foreach (var model in perModel)
        {
            model.ShouldNotContain("private static TCollection __CloneList<");
            model.ShouldNotContain("private static TDictionary __CloneDictionary<");
            model.ShouldContain("global::SparseFragments.Generated.SparseCloneKernels.__Clone");
        }
    }

    [Test]
    public void SharedKernelSourceCompilesCleanly()
    {
        foreach (var portable in new[] { false, true })
        {
            var source = SparseGeneratedOnceCloneKernels.BuildSource(
                "SparseFragments.Generated",
                portable,
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
