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

    private static SparseGeneratorConfig CreateConfig(
        SparseStructuralPolicy policy = SparseStructuralPolicy.AtomicReplace
    ) =>
        new(
            ModelAttributeMetadataName: ModelAttribute,
            IgnoreAttributeMetadataName: "SparseFragments.SparseIgnoreAttribute",
            RedactBeforeAttributeMetadataName: "SparseFragments.SparseRedactBeforeAttribute",
            MergeAttributeMetadataName: MergeAttribute,
            MergeStrategyBaseMetadataName: MergeBase,
            CloneReferenceSafeAttributeMetadataName: CloneSafe,
            KeyAttributeMetadataName: "SparseFragments.SparseKeyAttribute",
            KeyedInterfaceMetadataName: "SparseFragments.ISparseKeyed<TKey>",
            KeyPropertyName: "SparseKey",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "SPF001",
                "SPF002",
                "SPF003",
                "SPF004",
                "SPF005",
                "SPF006",
                "SPF007",
                "SPF008",
                "SPF009",
                "SPF010",
                "SPF011",
                "SPF012",
                "SPF013",
                "SPF014",
                "SPF015",
                "SPF016",
                "SPF017",
                "SPF018",
                "SPF019",
                "SPF020",
                "SPF021",
                "SPF022",
                "SPF023",
                "SPF027",
                "SPF028",
                "SPF029"
            ),
            HintNameSuffix: ".SparseFragments.g.cs",
            PromotedHintNameSuffix: ".SparsePromoted.g.cs",
            StructuralHostPrefix: "__SparseStructural_",
            StructuralPolicy: policy,
            ReservedGeneratedNames: ImmutableArray<string>.Empty
        );

    private static SparseGeneratorConfig AtomicConfig() => CreateConfig();

    private static SparseGeneratorConfig HostsConfig() =>
        CreateConfig(SparseStructuralPolicy.StructuralHosts);

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
                SparseModelAnalyzer.Analyze(symbol, AtomicConfig(), CancellationToken.None)
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

    [Test]
    public void ConfiguredMetadataNamesAndMergeValues_AreNormalizedForSharedAnalysis()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            namespace Downstream
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ModelAttribute : Attribute { }

                public enum MergeMode { Default = 10, Replace = 20, Deep = 30, Append = 40, SetUnion = 50, Custom = 60 }

                [AttributeUsage(AttributeTargets.Property)]
                public sealed class MergeAttribute : Attribute
                {
                    public MergeAttribute(MergeMode mode) { }
                    public MergeAttribute(Type strategy) { }
                }

                [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
                public sealed class IdentityAttribute : Attribute { }

                public interface IKeyed<TKey> { TKey Identity { get; } }

                public abstract class MergeStrategy<T> { }
                public sealed class CustomStringStrategy : MergeStrategy<string> { }

                [AttributeUsage(AttributeTargets.Property)]
                public sealed class CloneSafeAttribute : Attribute { }

                public struct ValueObject
                {
                    [CloneSafe]
                    public object Reference { get; set; }
                }

                [Model]
                public partial class AttributeItem
                {
                    [Identity]
                    public int Code { get; set; }
                }

                [Model]
                public partial class InterfaceItem : IKeyed<int>
                {
                    public int Identity { get; set; }
                }

                public class NestedData
                {
                    public string Name { get; set; } = "";
                }

                public partial class PromotedData
                {
                    public string Name { get; set; } = "";
                }

                [Model]
                public partial class Root
                {
                    [Merge(MergeMode.Append)]
                    public List<string> Values { get; set; } = new();
                    [Merge(typeof(CustomStringStrategy))]
                    public string CustomValue { get; set; } = "";
                    public ValueObject Wrapped { get; set; }
                    public List<AttributeItem> AttributeItems { get; set; } = new();
                    public List<InterfaceItem> InterfaceItems { get; set; } = new();
                    public NestedData Data { get; set; } = new();
                    public PromotedData Promoted { get; set; } = new();
                }
            }
            """;
        var config = new SparseGeneratorConfig(
            ModelAttributeMetadataName: "Downstream.ModelAttribute",
            IgnoreAttributeMetadataName: "Downstream.IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "Downstream.RedactBeforeAttribute",
            MergeAttributeMetadataName: "Downstream.MergeAttribute",
            MergeStrategyBaseMetadataName: "Downstream.MergeStrategy<T>",
            CloneReferenceSafeAttributeMetadataName: "Downstream.CloneSafeAttribute",
            KeyAttributeMetadataName: "Downstream.IdentityAttribute",
            KeyedInterfaceMetadataName: "Downstream.IKeyed<TKey>",
            KeyPropertyName: "Identity",
            MergeModeMap: new SparseMergeModeMap(10, 20, 30, 40, 50, 60),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "DWN001",
                "DWN002",
                "DWN003",
                "DWN004",
                "DWN005",
                "DWN006",
                "DWN007",
                "DWN008",
                "DWN009",
                "DWN010",
                "DWN011",
                "DWN012",
                "DWN013",
                "DWN014",
                "DWN015",
                "DWN016",
                "DWN017",
                "DWN018",
                "DWN019",
                "DWN020",
                "DWN021",
                "DWN022",
                "DWN023",
                "DWN027",
                "DWN028",
                "DWN029"
            ),
            HintNameSuffix: ".Downstream.g.cs",
            PromotedHintNameSuffix: ".DownstreamPromoted.g.cs",
            StructuralHostPrefix: "__DownstreamHost_",
            StructuralPolicy: SparseStructuralPolicy.StructuralHosts,
            ReservedGeneratedNames: ImmutableArray<string>.Empty
        );
        var compilation = CreateCompilation(source);
        var root = compilation.GetTypeByMetadataName("Downstream.Root")!;
        var analysis = SparseModelAnalyzer.Analyze(root, config, CancellationToken.None);

        analysis.Diagnostics.ShouldBeEmpty();
        var model = analysis.Model ?? throw new InvalidOperationException("Expected a model.");
        model.HintName.ShouldEndWith(".Downstream.g.cs");
        analysis
            .Members.Single(member => member.Property.Name == "Values")
            .MergeMode.ShouldBe(SparseMergeModes.Append);
        var attributeItems = analysis.Members.Single(member =>
            member.Property.Name == "AttributeItems"
        );
        attributeItems.Collection.IsKeyedSequence.ShouldBeTrue();
        attributeItems.Collection.KeyPropertyNames.ShouldBe(ImmutableArray.Create("Code"));
        var interfaceItems = analysis.Members.Single(member =>
            member.Property.Name == "InterfaceItems"
        );
        interfaceItems.Collection.IsKeyedSequence.ShouldBeTrue();
        interfaceItems.Collection.KeyTypeName.ShouldBe("int");
        analysis.StructuralModels.Single().HostName.ShouldStartWith("__DownstreamHost_");
        var promoted = analysis.PromotedModels.Single();
        SparseFragmentEmitter
            .GetPromotedHintName(
                promoted.Model,
                config.PromotedHintNameSuffix,
                CancellationToken.None
            )
            .ShouldEndWith(".DownstreamPromoted.g.cs");

        var runtimeDialect = new SparseRuntimeDialect(
            "global::Downstream.",
            "global::Downstream.Optional",
            "global::Downstream.MergeStrategy",
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "__downstream_strategy_"
        );
        var patchDialect = new SparseFragmentPatchEmitter.SparsePatchDialect(
            "global::Downstream.",
            "__downstream_whole",
            "__DownstreamMembersEmpty",
            static member => "__downstream_member_" + member.Id,
            static _ => string.Empty,
            "Apply",
            false,
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "global::Downstream.Conflict",
            "global::Downstream.ConflictKind",
            static payload => "global::Downstream.Rebase<" + payload + ">",
            static _ => "Patch",
            static _ => "ChangeSet",
            CollectionPatchName: static member => "Downstream" + member.Property.Name + "Patch"
        );
        var generated = SparseFragmentEmitter.BuildSource(
            model,
            analysis.Members,
            analysis.PocoCloneModels,
            analysis.StructuralModels,
            bclHashSetImplementsReadOnlySet: false,
            bclHashSetSupportsCapacity: false,
            cancellationToken: CancellationToken.None,
            config: config with
            {
                RuntimeDialect = runtimeDialect,
                PatchDialect = patchDialect,
            }
        );
        generated.ShouldNotContain("global::SparseFragments");
        generated.ShouldContain("global::Downstream.Optional<");
        generated.ShouldContain("__downstream_strategy_");
        generated.ShouldNotContain("__sparse_merge_strategy_");
        generated.ShouldContain("__downstream_member_");
        generated.ShouldNotContain("__sparse_patch_member_");
        generated.ShouldContain("DownstreamAttributeItemsPatch");

        var collisionConfig = config with
        {
            ReservedGeneratedNames = ImmutableArray.Create("Values"),
            DiagnosticIds = config.DiagnosticIds with { GeneratedNameCollision = "DWN001" },
        };
        var collision = SparseModelAnalyzer.Analyze(root, collisionConfig, CancellationToken.None);
        collision.Diagnostics.Single().DescriptorId.ShouldBe("DWN001");
    }
}
