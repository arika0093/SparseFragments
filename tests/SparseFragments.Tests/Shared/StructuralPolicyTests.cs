using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>
/// Parity tests for the product-neutral structural policy (issue #28).
/// The shared analysis mechanics are identical; only the treatment of
/// non-partial nested POCOs differs per explicit generator policy.
/// </summary>
public sealed class StructuralPolicyTests
{
    private const string ModelAttribute = "SparseFragments.SparseFragmentModelAttribute";
    private const string MergeAttribute = "SparseFragments.SparseMergeAttribute";
    private const string MergeBase = "SparseFragments.FragmentMergeStrategy<T>";
    private const string CloneSafe = "SparseFragments.SparseCloneReferenceSafeAttribute";

    private static SparseGeneratorConfig AtomicConfig() =>
        new(ModelAttribute, MergeAttribute, MergeBase, CloneSafe);

    private static SparseGeneratorConfig HostsConfig() =>
        new(
            ModelAttribute,
            MergeAttribute,
            MergeBase,
            CloneSafe,
            SparseStructuralPolicy.StructuralHosts
        );

    private const string Source = """
        using SparseFragments;
        [SparseFragmentModel]
        public partial class PolicyRoot
        {
            public string? Label { get; set; }
            [SparseMerge(MergeMode.Replace)]
            public PolicyNested? Nested { get; set; }
            public PolicyPartial? Partial { get; set; }
        }
        public class PolicyNested
        {
            public string? Name { get; set; }
            public int Count { get; set; }
        }
        public partial class PolicyPartial
        {
            public string? Note { get; set; }
        }
        """;

    private static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
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
        return CSharpCompilation.Create(
            "SparsePolicyProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static INamedTypeSymbol GetRoot(string source, string name = "PolicyRoot") =>
        CreateCompilation(source).GetTypeByMetadataName(name)
        ?? throw new InvalidOperationException($"Type '{name}' not found.");

    [Test]
    public void AtomicPolicy_KeepsNestedNonPartialPocoAtomic()
    {
        var analysis = SparseModelAnalyzer.Analyze(
            GetRoot(Source),
            AtomicConfig(),
            CancellationToken.None
        );
        analysis.Diagnostics.ShouldBeEmpty();
        analysis.StructuralModels.ShouldBeEmpty();
        var nested = analysis.Members.Single(member => member.Property.Name == "Nested");
        nested.ChildModel.ShouldBeNull();
        nested.ChildIsStructural.ShouldBeFalse();
        // The shared clone mechanics still apply: the POCO gets a clone helper.
        analysis.PocoCloneModels.Length.ShouldBe(1);
        analysis.PocoCloneModels[0].Model.Name.ShouldBe("PolicyNested");
    }

    [Test]
    public void StructuralHostsPolicy_GeneratesHostsForNestedPoco()
    {
        var analysis = SparseModelAnalyzer.Analyze(
            GetRoot(Source),
            HostsConfig(),
            CancellationToken.None
        );
        analysis.Diagnostics.ShouldBeEmpty();
        analysis.StructuralModels.Length.ShouldBe(1);
        analysis.StructuralModels[0].ValueTypeName.ShouldContain("PolicyNested");
        var nested = analysis.Members.Single(member => member.Property.Name == "Nested");
        nested.ChildModel.HasValue.ShouldBeTrue();
        nested.ChildIsStructural.ShouldBeTrue();
        nested.ChildFragmentType.ShouldStartWith("__SparseStructural_");
        nested.ChildFragmentType.ShouldEndWith(".Fragment");
    }

    [Test]
    public void BothPolicies_PromoteNestedPartials()
    {
        var atomic = SparseModelAnalyzer.Analyze(
            GetRoot(Source),
            AtomicConfig(),
            CancellationToken.None
        );
        var hosts = SparseModelAnalyzer.Analyze(
            GetRoot(Source),
            HostsConfig(),
            CancellationToken.None
        );
        atomic.PromotedModels.Length.ShouldBe(1);
        hosts.PromotedModels.Length.ShouldBe(1);
        atomic.PromotedModels[0].Model.Name.ShouldBe("PolicyPartial");
        hosts
            .PromotedModels[0]
            .Model.ModelTypeName.ShouldBe(atomic.PromotedModels[0].Model.ModelTypeName);
        // Promoted member semantics are policy-independent.
        hosts.PromotedModels[0].Members.ShouldBe(atomic.PromotedModels[0].Members);
    }

    [Test]
    public void BothPolicies_ShareCloneHelperMechanics()
    {
        var atomic = SparseModelAnalyzer.Analyze(
            GetRoot(Source),
            AtomicConfig(),
            CancellationToken.None
        );
        var hosts = SparseModelAnalyzer.Analyze(
            GetRoot(Source),
            AtomicConfig(),
            CancellationToken.None
        );
        hosts.PocoCloneModels.Length.ShouldBe(atomic.PocoCloneModels.Length);
        hosts
            .PocoCloneModels[0]
            .CloneHelperName.ShouldBe(atomic.PocoCloneModels[0].CloneHelperName);
    }

    [Test]
    public void Aggregate_DedupesSharedPromotedModels()
    {
        const string twoRoots = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class PolicyRootOne
            {
                public PolicyPartial? Partial { get; set; }
            }
            [SparseFragmentModel]
            public partial class PolicyRootTwo
            {
                public PolicyPartial? Partial { get; set; }
            }
            public partial class PolicyPartial
            {
                public string? Note { get; set; }
            }
            """;
        var compilation = CreateCompilation(twoRoots);
        var analyses = new[]
        {
            compilation.GetTypeByMetadataName("PolicyRootOne")!,
            compilation.GetTypeByMetadataName("PolicyRootTwo")!,
        }
            .Select(static symbol =>
                SparseModelAnalyzer.Analyze(
                    symbol,
                    new(ModelAttribute, MergeAttribute, MergeBase, CloneSafe),
                    CancellationToken.None
                )
            )
            .ToImmutableArray();
        analyses.All(static analysis => analysis.Model.HasValue).ShouldBeTrue();

        var result = SparsePromotedAggregation.Aggregate(analyses, CancellationToken.None);
        result.Distinct.Length.ShouldBe(1);
        result.Distinct[0].Model.Name.ShouldBe("PolicyPartial");
        result.Incompatible.ShouldBeEmpty();
        SparsePromotedAggregation.ToIncompatibleArguments(result).ShouldBeEmpty();
    }

    [Test]
    public void Generator_AtomicSemanticsProduceNoStructuralHosts()
    {
        var compilation = CreateCompilation(Source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        runResult
            .Diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
        var sources = runResult
            .Results.SelectMany(static r => r.GeneratedSources)
            .Select(static s => s.SourceText.ToString())
            .ToArray();
        sources.ShouldNotBeEmpty();
        sources
            .All(static source => !source.Contains("__SparseStructural_", StringComparison.Ordinal))
            .ShouldBeTrue();
        // The promoted partial still receives first-class APIs.
        sources
            .Any(static source => source.Contains("PolicyPartial", StringComparison.Ordinal))
            .ShouldBeTrue();
    }
}
