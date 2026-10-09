using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

/// <summary>
/// Generated-Once removal-index regression tests (issue #183): dictionary
/// and keyed-sequence patches share one ordered-removal kernel definition,
/// and per-patch output calls the shared kernels instead of redefining the
/// slot/cancel/rebuild mechanics.
/// </summary>
public sealed class GeneratedOnceRemovalIndexTests
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
            "SparseRemovalOnceProbe",
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
    public void DictionaryAndKeyedPatchesShareOneRemovalIndex()
    {
        var sources = GeneratedSources(
            CreateCompilation(
                new Dictionary<string, string>
                {
                    ["Models.cs"] = """
                    using SparseFragments;
                    using System.Collections.Generic;
                    [SparseFragmentModel]
                    public partial class RemovalKeyedItem
                    {
                        [SparseKey]
                        public string Id { get; set; } = "";
                        public string Name { get; set; } = "";
                    }
                    [SparseFragmentModel]
                    public partial class RemovalDict
                    {
                        public Dictionary<string, string> Scores { get; set; } = new();
                    }
                    [SparseFragmentModel]
                    public partial class RemovalKeyed
                    {
                        public List<RemovalKeyedItem> Items { get; set; } = new();
                    }
                    """,
                }
            )
        );

        var shared = sources
            .Where(pair => pair.Key.EndsWith(".RemovalIndex.g.cs", StringComparison.Ordinal))
            .ToArray();
        shared.ShouldHaveSingleItem();
        var text = shared[0].Value;
        text.ShouldContain("class " + SparseGeneratedOnceNames.RemovalIndex + "<TKey>");
        CountOccurrences(text, "AddRemoval(").ShouldBe(1);
        CountOccurrences(text, "ContainsRemoved(").ShouldBe(1);

        var perModel = sources
            .Where(pair => pair.Key.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToArray();
        perModel.Length.ShouldBe(3);
        foreach (var model in perModel)
        {
            model.ShouldNotContain("__SparseRemovedSlot(");
            model.ShouldNotContain("__SparseAddIndexedRemoval(");
            model.ShouldNotContain("__SparseIndexedRemovals");
        }
        var patches = string.Join("\n", perModel);
        patches.ShouldContain("global::SparseFragments.Generated.SparseRemovalIndex<");
    }

    [Test]
    public void SharedRemovalIndexSourceCompilesCleanly()
    {
        var source = SparseGeneratedOnceRemovalIndex.BuildSource(
            "SparseFragments.Generated",
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

    [Test]
    public void RemovalRequirementsDetectKeyedShapes()
    {
        SparseGeneratedOnceRequirements
            .ForRemovalIndex(ImmutableArray<SparseMemberModel>.Empty)
            .NeedsRemovalIndex.ShouldBeFalse();
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
