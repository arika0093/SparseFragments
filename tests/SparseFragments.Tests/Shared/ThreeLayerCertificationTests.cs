using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.CompilerServices;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Certification suite for the three-layer Runtime/Generated-Once/Per-Model
// design (issue #189). The ownership table in
// docs/architecture/three-layer-ownership.md becomes executable assertions
// here: every runtime facade member is classified, the Generated-Once
// families are pinned to internal BCL-only helpers emitted once per
// compilation, and the per-model call-site wiring is verified live against
// the actual merge state (track-2 README-honesty option (b): the explicit
// product namespace calls shared helpers; the null-namespace single-file
// path keeps legacy per-model copies). All gates are deterministic; no
// wall-clock measurement is asserted.
public sealed class ThreeLayerCertificationTests
{
    private const string SharedNamespace = "SparseFragments.Generated";

    private const string ScalarModel = """
        using SparseFragments;
        using System.Collections.Generic;
        namespace CertProbe
        {
            [SparseFragmentModel]
            public partial class CertScalar
            {
                public string Label { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class CertCollection
            {
                public string Label { get; set; } = "";
                public List<string> Tags { get; set; } = new();
                public Dictionary<string, string> Labels { get; set; } = new();
            }
        }
        """;

    private const string KeyedModel = """
        using SparseFragments;
        using System.Collections.Generic;
        namespace CertProbe
        {
            [SparseFragmentModel]
            public partial class CertKeyedItem
            {
                [SparseKey]
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class CertKeyedRoot
            {
                public string Label { get; set; } = "";
                public List<CertKeyedItem> Items { get; set; } = new();
            }
        }
        """;

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
            "SparseThreeLayerCertProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static Dictionary<string, string> Run(Dictionary<string, string> files)
    {
        var compilation = CreateCompilation(files);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        driver
            .GetRunResult()
            .Diagnostics.Where(static d => d.Id.StartsWith("SPF", StringComparison.Ordinal))
            .ShouldBeEmpty();
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

    private static Dictionary<string, string> FullFixture() =>
        Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Scalar.cs"] = ScalarModel,
                ["Keyed.cs"] = KeyedModel,
            }
        );

    private static string SharedSource(Dictionary<string, string> sources, string family) =>
        sources.Single(pair => pair.Key.Contains(family, StringComparison.Ordinal)).Value;

    private static string PerModelSources(Dictionary<string, string> sources) =>
        string.Join(
            "\n",
            sources
                .Where(pair =>
                    !pair.Key.Contains("CloneKernels", StringComparison.Ordinal)
                    && !pair.Key.Contains("ReadOnlyAdapters", StringComparison.Ordinal)
                    && !pair.Key.Contains("RemovalIndex", StringComparison.Ordinal)
                    && !pair.Key.Contains("EditSession", StringComparison.Ordinal)
                )
                .Select(static pair => pair.Value)
        );

    // Ownership table, Runtime row: every facade member classifies into one
    // documented family (equality, merge, rebase, clone/cycle, keyed,
    // provenance). 40 methods share 24 distinct names; the count pins the
    // #186 audit plus the 12 #187 comparison specializations plus the 3 #204
    // live origin-attribution helpers.
    [Test]
    public void OwnershipTable_EveryFacadeMemberHasARuntimeLayerOwner()
    {
        var methods = typeof(SparseFragmentRuntime).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
        );
        methods.Length.ShouldBe(40);
        var names = methods.Select(static method => method.Name).ToImmutableArray();
        names.Distinct().Count().ShouldBe(24);

        string Family(string name) =>
            name switch
            {
                "AreEqual" or "AreSequenceEqual" or "AreSetEqual" or "AreDictionaryEqual" =>
                    "equality",
                "MergeAppendList" or "MergeDistinctArray" or "MergeDistinctList" or "MergeSet" =>
                    "merge",
                "TryRebaseAppend"
                or "TryRebaseSetUnion"
                or "TryRebaseSequenceAppend"
                or "TryRebaseSequenceAppendArray"
                or "TryRebaseSequenceSetUnion"
                or "TryRebaseSequenceSetUnionArray" => "rebase",
                "CreateCloneContext" or "CreateFromCycleContext" or "CreateDiffCycleContext" =>
                    "clone",
                "EnsureUniqueKeys" or "KeyOrderEquals" => "keyed",
                "TryExplainCollectionProvenance" or "TryExplainSetProvenance" => "provenance",
                "MergeAppendOrigins" or "MergeSetUnionOrigins" or "MergeSetOrigins" => "provenance",
                _ => "unclassified",
            };
        names.Select(Family).ShouldNotContain("unclassified");
        names.Where(name => Family(name) == "equality").Select(Family).ShouldNotBeEmpty();
        methods
            .All(static method => method.DeclaringType == typeof(SparseFragmentRuntime))
            .ShouldBeTrue();
    }

    // Ownership table, Generated-Once rows: the three helper families plus
    // the edit-session cores are emitted exactly once per compilation,
    // normally internal, and BCL-only (no runtime/dialect tokens).
    [Test]
    public void OwnershipTable_GeneratedOnceFamiliesAreInternalBclOnlyAndEmittedOnce()
    {
        var sources = FullFixture();
        sources
            .Keys.Count(key => key.Contains("CloneKernels", StringComparison.Ordinal))
            .ShouldBe(1);
        sources
            .Keys.Count(key => key.Contains("ReadOnlyAdapters", StringComparison.Ordinal))
            .ShouldBe(1);
        sources
            .Keys.Count(key => key.Contains("RemovalIndex", StringComparison.Ordinal))
            .ShouldBe(1);

        foreach (var family in new[] { "CloneKernels", "ReadOnlyAdapters", "RemovalIndex" })
        {
            var source = SharedSource(sources, family);
            source.ShouldContain("internal ");
            source.ShouldContain("namespace " + SharedNamespace);
            source.ShouldNotContain("public class Sparse");
            source.ShouldNotContain("public static class Sparse");
            source.ShouldNotContain("public sealed class Sparse");
            // Product-neutral: shared-to-shared references stay inside the
            // owning namespace; no runtime type is ever named.
            foreach (
                var line in source
                    .Split('\n')
                    .Where(line => line.Contains("global::SparseFragments"))
            )
            {
                line.ShouldContain("global::" + SharedNamespace + ".");
            }
            source.ShouldNotContain("Optional<");
            source.ShouldNotContain("Conflict");
            source.ShouldNotContain("MergeStrategy");
        }
        SharedSource(sources, "CloneKernels").ShouldContain("class SparseCloneKernels");
        SharedSource(sources, "RemovalIndex").ShouldContain("class SparseRemovalIndex<");
    }

    // Per-model wiring is live on the explicit-namespace path: per-model
    // output names the qualified shared helpers and defines no legacy
    // per-model copies of them.
    [Test]
    public void OwnershipTable_PerModelCallSitesInvokeSharedHelpersLive()
    {
        var sources = FullFixture();
        var perModel = PerModelSources(sources);
        perModel.ShouldContain("global::" + SharedNamespace + ".SparseCloneKernels");
        perModel.ShouldContain("global::" + SharedNamespace + ".SparseReadOnlyListAdapter");
        perModel.ShouldContain("global::" + SharedNamespace + ".SparseRemovalIndex<");
        perModel.ShouldNotContain("class SparseCloneKernels");
        perModel.ShouldNotContain("__SparseReadOnlyCollection");
        perModel.ShouldNotContain("__SparseReadOnlyDictionary");
        perModel.ShouldNotContain("__SparseIndexedRemovals");
        perModel.ShouldNotContain("__SparseAddIndexedRemoval");
        perModel.ShouldNotContain("__SparseContainsRemoved");
        perModel.ShouldNotContain("__SparseRebuildRemovedIndex");
        perModel.ShouldNotContain("__SparseRemovedSlot");
    }

    // README-honesty option (b): the null-namespace single-file path keeps
    // legacy per-model private copies instead of calling shared helpers.
    [Test]
    public void NullNamespace_KeepsLegacyPerModelCopies()
    {
        var config = new SparseGeneratorConfig(
            ModelAttributeMetadataName: "Downstream.ModelAttribute",
            IgnoreAttributeMetadataName: "Downstream.IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "Downstream.RedactBeforeAttribute",
            MergeAttributeMetadataName: "Downstream.MergeAttribute",
            MergeStrategyBaseMetadataName: "Downstream.MergeStrategy<T>",
            CloneReferenceSafeAttributeMetadataName: "Downstream.CloneSafeAttribute",
            KeyAttributeMetadataName: "Downstream.IdentityAttribute",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
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
                "DWN013",
                "DWN014",
                "DWN017",
                "DWN019",
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
            RuntimeDialect: new SparseRuntimeDialect(
                "global::Downstream.",
                "global::Downstream.Optional",
                "global::Downstream.MergeStrategy",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "__downstream_merge_"
            ),
            PatchDialect: new SparseFragmentPatchEmitter.SparsePatchDialect(
                "global::Downstream.",
                "__sparse_whole",
                "__SparseMembersEmpty",
                static member => "__sparse_patch_member_" + member.Id,
                static _ => string.Empty,
                "Apply",
                false,
                "global::Downstream.Runtime",
                "global::Downstream.Conflict",
                "global::Downstream.ConflictKind",
                static payload => "global::Downstream.Rebase<" + payload + ">",
                static _ => "global::Downstream.Patch",
                static _ => "global::Downstream.ChangeSet",
                PayloadImplementationContainerPrefix: "DownstreamInternal"
            )
        );
        config.GeneratedImplementationNamespace.ShouldBeNull();
        var element = new SparseTypeModel(
            "string",
            "string",
            "string",
            IsReferenceType: true,
            IsFragmentModel: false,
            null,
            UsesDefaultScalarEquality: true
        );
        var members = ImmutableArray.Create(
            new SparseMemberModel(
                1,
                new SparsePropertyModel(
                    "Tags",
                    new SparseTypeModel(
                        "global::System.Collections.Generic.List<string>",
                        "global::System.Collections.Generic.List<string>",
                        "global::System.Collections.Generic.List<string>",
                        IsReferenceType: true,
                        IsFragmentModel: false,
                        null
                    ),
                    JsonPropertyName: "Tags"
                ),
                null,
                SparseMergeModes.Append,
                new SparseCollectionInfo(
                    SparseCollectionKind.List,
                    SparseCloneCollectionKind.List,
                    element,
                    null,
                    null,
                    SparseCollectionSemantic.ScalarSequence,
                    ImmutableArray<string>.Empty,
                    null,
                    SparseKeyKind.None
                ),
                null,
                null,
                false,
                true
            )
        );
        var text = SparseFragmentEmitter.BuildSource(
            new SparseModelInfo(
                "ReadModel",
                "global::Ns.ReadModel",
                "Ns",
                IsGlobalNamespace: false,
                IsStruct: false,
                IsRecord: false,
                HintName: "ReadModel.g.cs",
                Constructor: null,
                IgnoredSettablePropertyNames: ImmutableArray<string>.Empty
            ),
            members,
            ImmutableArray<SparsePocoCloneModel>.Empty,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            ImmutableArray<SparseStructuralModel>.Empty,
            bclHashSetImplementsReadOnlySet: false,
            bclHashSetSupportsCapacity: false,
            CancellationToken.None,
            config
        );
        text.ShouldContain("__SparseReadOnlyCollection");
        text.ShouldNotContain("global::Downstream.Generated.SparseCloneKernels");
        text.ShouldNotContain("global::Downstream.Generated.SparseReadOnlyListAdapter");
    }

    // Visibility rule: shared implementations never escape through public
    // API. The payload DTO exception (E1) lives in per-model nested
    // containers, not in these families.
    [Test]
    public void VisibilityRule_SharedImplementationsStayOutOfPublicSurface()
    {
        var compilation = CreateCompilation(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Order.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                namespace ConsumerNs
                {
                    [SparseFragmentModel]
                    public partial class Order
                    {
                        public string Label { get; set; } = "";
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """,
            }
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        using var stream = new MemoryStream();
        updated.Emit(stream).Success.ShouldBeTrue();
        var consumer = global::System.Reflection.Assembly.Load(stream.ToArray());
        consumer
            .GetType("SparseFragments.Generated.SparseCloneKernels")!
            .IsNotPublic.ShouldBeTrue();
        consumer
            .GetExportedTypes()
            .Where(static type =>
                type.Name.Contains("SparseClone", StringComparison.Ordinal)
                || type.Name.Contains("Adapter", StringComparison.Ordinal)
                || type.Name.Contains("RemovalIndex", StringComparison.Ordinal)
            )
            .ShouldBeEmpty();
    }

    // d7011db gating: without the BCL IReadOnlySet type the clone kernels
    // name no IReadOnlySet at all; the portable variant gates it in a bridge.
    // Both stay C# 9 clean for the minimum language floor.
    [Test]
    public void PortableSetView_MatchesTheD7011dbGating()
    {
        var modern = SparseGeneratedOnceEmitter.RenderCloneKernels(
            "Downstream.Generated",
            false,
            true,
            CancellationToken.None
        );
        var portable = SparseGeneratedOnceEmitter.RenderCloneKernels(
            "Downstream.Generated",
            true,
            false,
            CancellationToken.None
        );
        modern.ShouldNotContain("IReadOnlySet<");
        modern.ShouldNotContain("__SparseReadOnlySet<T>");
        portable.ShouldContain("__SparseReadOnlySet<T>");
        portable.ShouldContain("IReadOnlySet<");
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (
            var source in new[]
            {
                modern,
                portable,
                SparseGeneratedOnceEmitter.RenderReadOnlyAdapters(
                    "Downstream.Generated",
                    CancellationToken.None
                ),
                SparseGeneratedOnceEmitter.RenderRemovalIndex(
                    "Downstream.Generated",
                    CancellationToken.None
                ),
            }
        )
        {
            CSharpSyntaxTree
                .ParseText(source, parseOptions)
                .GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty();
        }
    }

    // Downstream BCL-only conformance: the same clone/adapter/removal
    // behavior executes identically under a standard-shaped and a renamed
    // downstream namespace with no SparseFragments reference.
    [Test]
    public void StandardAndDownstream_DialectsAgreeOnSemantics()
    {
        ExecuteHelperProbe("Cert.Standard.Generated").ShouldBe("probe-ok");
        ExecuteHelperProbe("Cert.Downstream.Generated").ShouldBe("probe-ok");
    }

    // Cross-assembly contracts: an external assembly uses the runtime
    // Descriptor and EditSession contracts plus the public model families
    // without naming any generated implementation type, then executes.
    [Test]
    public void CrossAssembly_ExternalConsumerUsesContractsWithoutImplTypes()
    {
        var compilation = CreateCompilation(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Order.cs"] = """
                using SparseFragments;
                using System.Collections.Generic;
                namespace ConsumerNs
                {
                    [SparseFragmentModel]
                    public partial class Order
                    {
                        public string Label { get; set; } = "";
                        public List<string> Tags { get; set; } = new();
                    }
                }
                """,
            }
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        using var stream = new MemoryStream();
        updated.Emit(stream).Success.ShouldBeTrue();
        var consumerBytes = stream.ToArray();
        var consumer = global::System.Reflection.Assembly.Load(consumerBytes);

        const string checkSource = """
            using System.Collections.Generic;
            using System.Linq;
            using ConsumerNs;
            using SparseFragments;
            using SparseFragments.Generated;
            public static class ConsumerCheck
            {
                public static string Run()
                {
                    var before = new ConsumerNs.Order { Label = "a" };
                    var after = new ConsumerNs.Order { Label = "b", Tags = new List<string> { "x" } };
                    var changes = ConsumerNs.Order.ChangeSet.Between(before, after);
                    if (!changes.TryApplyTo(before, out var updated, out _)) return "apply-failed";
                    var session = after.CreateEditSession();
                    if (!session.Descriptors.TryGet("Label", out _)) return "descriptor-failed";
                    IDescriptorSet descriptors = session.Descriptors;
                    if (descriptors is null) return "descriptor-null";
                    var patch = changes.ToPatch();
                    var current = new ConsumerNs.Order { Label = "a" };
                    var applied = patch.ApplyTo(current);
                    return updated.Label + ":" + string.Join(",", updated.Tags) + ":" + applied.Label;
                }
            }
            """;
        // Only public contracts are named: the runtime descriptor namespace
        // (SparseFragments.Generated ships in the compiled runtime assembly)
        // and the per-model families. No generated implementation helper.
        checkSource.ShouldNotContain("SparseClone");
        checkSource.ShouldNotContain("Adapter");
        checkSource.ShouldNotContain("RemovalIndex");
        checkSource.ShouldNotContain("EditSessionCore");
        var checkTree = CSharpSyntaxTree.ParseText(checkSource, path: "ConsumerCheck.cs");
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var checkReferences = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        checkReferences.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        checkReferences.Add(MetadataReference.CreateFromImage(consumerBytes));
        var checkCompilation = CSharpCompilation.Create(
            "ConsumerCheck",
            [checkTree],
            checkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        checkCompilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        using var checkStream = new MemoryStream();
        checkCompilation.Emit(checkStream).Success.ShouldBeTrue();
        var checkAssembly = global::System.Reflection.Assembly.Load(checkStream.ToArray());
        var resolver = new ResolveEventHandler(
            (sender, args) =>
            {
                var name = new global::System.Reflection.AssemblyName(args.Name);
                if (string.Equals(name.Name, consumer.GetName().Name, StringComparison.Ordinal))
                {
                    return consumer;
                }

                return null;
            }
        );
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            checkAssembly
                .GetType("ConsumerCheck")!
                .GetMethod("Run")!
                .Invoke(null, null)
                .ShouldBe("b:x:b");
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    // C4/C5 size matrix: shared helper bytes stay constant as model count
    // grows, so four models cost less than four times one model. Two
    // independent compilations each emit their own copy under the same hint
    // name: cross-assembly replication is by design, per-model duplication
    // is removed.
    [Test]
    public void SizeMatrix_SharedBytesStayConstantAsModelsGrow()
    {
        static string CollectionModel(string name) =>
            "using SparseFragments;\nusing System.Collections.Generic;\nnamespace CertProbe\n{\n"
            + "    [SparseFragmentModel]\n    public partial class "
            + name
            + "\n    {\n"
            + "        public string Label { get; set; } = \"\";\n"
            + "        public List<string> Tags { get; set; } = new();\n"
            + "    }\n}\n";
        var one = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["M1.cs"] = CollectionModel("CertModelOne"),
            }
        );
        var four = Run(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["M1.cs"] = CollectionModel("CertModelOne"),
                ["M2.cs"] = CollectionModel("CertModelTwo"),
                ["M3.cs"] = CollectionModel("CertModelThree"),
                ["M4.cs"] = CollectionModel("CertModelFour"),
            }
        );
        foreach (var family in new[] { "CloneKernels", "ReadOnlyAdapters" })
        {
            SharedSource(one, family).ShouldBe(SharedSource(four, family));
        }
        // Each compilation emits its own copy under the same hint name.
        SharedSource(one, "CloneKernels").Length.ShouldBeGreaterThan(0);
        one.Keys.ShouldContain(SharedSourceKey(four, "CloneKernels"));

        static long TotalBytes(Dictionary<string, string> sources) =>
            sources.Values.Sum(static source => (long)source.Length);
        TotalBytes(four).ShouldBeLessThan(4 * TotalBytes(one));
        four.Values.Sum(source =>
                source.Split('\n').Count(line => line.Contains("class SparseCloneKernels"))
            )
            .ShouldBe(1);
    }

    // Minimum language floor: every emitted source (shared, per-model,
    // edit-session cores) parses under C# 9 with no errors.
    [Test]
    public void LanguageFloor_AllSourcesParseUnderCSharp9()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (var source in FullFixture().Values)
        {
            CSharpSyntaxTree
                .ParseText(source, parseOptions)
                .GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ShouldBeEmpty();
        }
    }

    private static string SharedSourceKey(Dictionary<string, string> sources, string family) =>
        sources.Keys.Single(key => key.Contains(family, StringComparison.Ordinal));

    private static string ExecuteHelperProbe(string implementationNamespace)
    {
        var clone = SparseGeneratedOnceEmitter.RenderCloneKernels(
            implementationNamespace,
            false,
            true,
            CancellationToken.None
        );
        var adapters = SparseGeneratedOnceEmitter.RenderReadOnlyAdapters(
            implementationNamespace,
            CancellationToken.None
        );
        var removal = SparseGeneratedOnceEmitter.RenderRemovalIndex(
            implementationNamespace,
            CancellationToken.None
        );
        foreach (var source in new[] { clone, adapters, removal })
        {
            source.ShouldNotContain("SparseFragments");
        }
        var probe = """
            using System.Collections.Generic;
            public static class CertProbeDriver
            {
                public static string Run()
                {
                    var source = new List<int> { 1, 2, 3 };
                    var view = new __NS__.SparseReadOnlyListAdapter<int, int>(source, static value => value * 2);
                    if (view.Count != 3 || view[0] != 2 || view[2] != 6) return "adapters-failed";
                    var context = new Dictionary<object, object>();
                    var clone = __NS__.SparseCloneKernels.__CloneList<string, List<string>>(new List<string> { "a" }, context, null);
                    if (clone.Count != 1 || ReferenceEquals(clone, source)) return "clone-failed";
                    var removed = new List<int>();
                    int[]? lookup = null;
                    __NS__.SparseRemovalIndex<int>.AddRemoval(removed, ref lookup, 7, keepReservedIndex: false);
                    if (!__NS__.SparseRemovalIndex<int>.ContainsRemoved(removed, lookup, 7)) return "removal-failed";
                    __NS__.SparseRemovalIndex<int>.CancelRemoval(removed, ref lookup, 7);
                    if (removed.Count != 0) return "removal-cancel";
                    return "probe-ok";
                }
            }
            """.Replace("__NS__", "global::" + implementationNamespace, StringComparison.Ordinal);
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(clone, path: "Clone.g.cs"),
            CSharpSyntaxTree.ParseText(adapters, path: "Adapters.g.cs"),
            CSharpSyntaxTree.ParseText(removal, path: "Removal.g.cs"),
            CSharpSyntaxTree.ParseText(probe, path: "Probe.cs"),
        };
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "CertDownstreamProbe",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        using var stream = new MemoryStream();
        compilation.Emit(stream).Success.ShouldBeTrue();
        var assembly = global::System.Reflection.Assembly.Load(stream.ToArray());
        return (string)assembly.GetType("CertProbeDriver")!.GetMethod("Run")!.Invoke(null, null)!;
    }
}
