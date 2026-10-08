using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

public sealed class LanguageVersionEmissionTests
{
    // Representative C# 9 model: ordinary and nested partial types, nullable
    // and init-only members, a merge collection, a keyed list, a dictionary.
    private const string ModelSource = """
        using System.Collections.Generic;
        using SparseFragments;
        namespace LangVersionProbe
        {
            [SparseFragmentModel]
            public partial class ProbeSettings
            {
                public string? Label { get; set; }
                public string? Code { get; init; }
                public ProbeChild? Child { get; set; }
                [SparseMerge(MergeMode.Append)]
                public List<string> Plugins { get; set; } = new List<string>();
                public List<ProbeQuest> Quests { get; set; } = new List<ProbeQuest>();
                public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();
            }
            public partial class ProbeChild
            {
                public int Count { get; set; }
                public string Host { get; set; } = string.Empty;
            }
            public partial class ProbeQuest
            {
                [SparseKey]
                public string Slug { get; set; } = string.Empty;
                public string Title { get; set; } = string.Empty;
            }
        }
        """;

    private static ImmutableArray<string> Generate()
    {
        var tree = CSharpSyntaxTree.ParseText(ModelSource, path: "Probe.cs");
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
            "LangVersionProbe",
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
        updatedCompilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        return driver
            .GetRunResult()
            .Results.SelectMany(static result => result.GeneratedSources)
            .Select(static generated => generated.SourceText.ToString())
            .ToImmutableArray();
    }

    [Test]
    public void EmittedSourcesParseWithoutErrorsUnderCSharp9()
    {
        var sources = Generate();
        sources.Length.ShouldBeGreaterThan(0);
        foreach (var source in sources)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
            CSharpSyntaxTree
                .ParseText(source, parseOptions)
                .GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty();
        }
    }

    [Test]
    public void EmittedSourcesAvoidPostCSharp9Syntax()
    {
        var sources = Generate();
        sources.Length.ShouldBeGreaterThan(0);
        foreach (var source in sources)
        {
            // File-scoped namespaces need C# 10; the C# 9 floor uses blocks.
            Regex
                .IsMatch(source, @"(?m)^namespace\s+.+;\s*$")
                .ShouldBeFalse();
            // The "record class" spelling needs C# 10; C# 9 uses "record".
            source.ShouldNotContain("record class ");
        }
    }
}
