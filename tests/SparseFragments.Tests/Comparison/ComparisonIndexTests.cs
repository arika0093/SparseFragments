using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Comparison;

// Regression tests for the per-compilation comparison-rule index (issue
// #140): repeated rule-set creation shares one assembly scan, unrelated edits
// invalidate through new compilations, and inherited precedence plus
// ambiguity behavior match the unindexed traversal.
public sealed class ComparisonIndexTests
{
    private const string ComparerSource = """
        using System.Collections.Generic;
        using SparseFragments;
        public class IndexTarget { public string Value { get; set; } = string.Empty; }
        public class IndexComparerA : IEqualityComparer<string>
        {
            public bool Equals(string? a, string? b) => a == b;
            public int GetHashCode(string value) => value.GetHashCode();
        }
        public class IndexComparerB : IndexComparerA { }
        """;

    private static SparseGeneratorConfig Configuration =>
        (SparseGeneratorConfig)(
            typeof(SparseFragmentsGenerator)
                .GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) ?? throw new InvalidOperationException("Missing generator configuration.")
        );

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"File{index}.cs")).ToArray();
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location));
        return CSharpCompilation.Create(
            "ComparisonIndexProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(
                NullableContextOptions.Enable
            )
        );
    }

    private static INamedTypeSymbol GetModel(CSharpCompilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name) ?? throw new InvalidOperationException($"Missing type '{name}'.");

    private static string? ComparerFor(CSharpCompilation compilation, string modelName)
    {
        var model = GetModel(compilation, modelName);
        var rules = SparseComparisonRules.CreateRuleSet(model, Configuration, CancellationToken.None);
        var valueType = compilation.GetSpecialType(SpecialType.System_String);
        return rules.TryGetComparerType(valueType, out var comparer) ? comparer?.Name : null;
    }

    [Test]
    public void RepeatedRuleSetsAgree()
    {
        var compilation = CreateCompilation(
            ComparerSource,
            """
            using SparseFragments;
            [SparseCompare(typeof(string), typeof(IndexComparerA))]
            [SparseFragmentModel]
            public partial class IndexRootA { public IndexTarget Child { get; set; } = new(); }
            [SparseCompare(typeof(string), typeof(IndexComparerA))]
            [SparseFragmentModel]
            public partial class IndexRootB { public IndexTarget Child { get; set; } = new(); }
            """
        );
        // The second pass reuses the per-compilation index built by the first.
        ComparerFor(compilation, "IndexTarget").ShouldBe("IndexComparerA");
        ComparerFor(compilation, "IndexTarget").ShouldBe("IndexComparerA");
        ComparerFor(compilation, "IndexRootA").ShouldBe("IndexComparerA");
    }

    [Test]
    public void UnrelatedEditKeepsRuleOutcomes()
    {
        var compilation = CreateCompilation(
            ComparerSource,
            """
            using SparseFragments;
            [SparseCompare(typeof(string), typeof(IndexComparerA))]
            [SparseFragmentModel]
            public partial class IndexRootA { public IndexTarget Child { get; set; } = new(); }
            """
        );
        ComparerFor(compilation, "IndexTarget").ShouldBe("IndexComparerA");

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("public class UnrelatedHolder { public int Count { get; set; } }", path: "Extra.cs")
        );
        ComparerFor(edited, "IndexTarget").ShouldBe("IndexComparerA");
    }

    [Test]
    public void MissingAndConflictingRootsInheritNothing()
    {
        var missing = CreateCompilation(
            ComparerSource,
            """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class IndexRootA { public IndexTarget Child { get; set; } = new(); }
            [SparseCompare(typeof(string), typeof(IndexComparerA))]
            [SparseFragmentModel]
            public partial class IndexRootB { public IndexTarget Child { get; set; } = new(); }
            """
        );
        // A referencing root without any rule vetoes inheritance.
        ComparerFor(missing, "IndexTarget").ShouldBeNull();

        var conflicting = CreateCompilation(
            ComparerSource,
            """
            using SparseFragments;
            [SparseCompare(typeof(string), typeof(IndexComparerB))]
            [SparseFragmentModel]
            public partial class IndexRootA { public IndexTarget Child { get; set; } = new(); }
            [SparseCompare(typeof(string), typeof(IndexComparerA))]
            [SparseFragmentModel]
            public partial class IndexRootB { public IndexTarget Child { get; set; } = new(); }
            """
        );
        ComparerFor(conflicting, "IndexTarget").ShouldBeNull();
    }

    [Test]
    public void AbsentRulesResolveEmpty()
    {
        var compilation = CreateCompilation(
            ComparerSource,
            """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class IndexRootA { public IndexTarget Child { get; set; } = new(); }
            public class UnrelatedHolder { public string Name { get; set; } = string.Empty; }
            """
        );
        // No root declares rules, so the reference-graph scan is skipped.
        ComparerFor(compilation, "IndexTarget").ShouldBeNull();
        ComparerFor(compilation, "IndexRootA").ShouldBeNull();
    }
}
