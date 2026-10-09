using System.Collections.Immutable;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Downstream-dialect and cross-product conformance for the Generated-Once
// plane (issue #180): an independent renamed dialect compiles and executes
// with no standalone runtime dependency, target-framework variants stay
// valid, feature selections gate families, and every family documents the
// contracts it consumes. Fixtures here are independent of the #176 public
// facade snapshots: they cover generation topology, not model surfaces.
public sealed class GeneratedOnceConformanceTests
{
    private const string ProbeDriver = """
        using System.Collections.Generic;
        public static class OnceProbeDriver
        {
            public static string RunAdapters()
            {
                var source = new List<int> { 1, 2, 3 };
                var view = new __NS__.SparseReadOnlyListAdapter<int, int>(source, static value => value * 2);
                if (view.Count != 3 || view[0] != 2 || view[2] != 6) return "list-failed";
                var dict = new Dictionary<string, int> { ["a"] = 1 };
                var dictView = new __NS__.SparseReadOnlyDictionaryAdapter<string, int, int>(dict, static value => value + 1);
                if (!dictView.TryGetValue("a", out var mapped) || mapped != 2) return "dict-failed";
                var entries = new __NS__.SparseReadOnlyDictionaryEntriesAdapter<string, int, string, int>(dict, static key => key + "!", static value => value * 10);
                foreach (var pair in entries)
                {
                    if (pair.Key != "a!" || pair.Value != 10) return "entries-failed";
                }
                return "adapters-ok";
            }
            public static string RunClone()
            {
                var source = new List<string> { "a", "b" };
                var context = new Dictionary<object, object>();
                var clone = __NS__.SparseCloneHelpers.__CloneList<string, List<string>>(source, context, null);
                if (clone.Count != 2 || ReferenceEquals(clone, source)) return "clone-failed";
                clone.Add("c");
                if (source.Count != 2) return "clone-aliasing";
                var second = __NS__.SparseCloneHelpers.__CloneList<string, List<string>>(source, context, null);
                if (!ReferenceEquals(clone, second)) return "clone-context";
                return "clone-ok";
            }
            public static string RunRemoval()
            {
                var removed = new List<int>();
                int[]? lookup = null;
                __NS__.SparseRemovalIndex<int>.AddIndexed(removed, 7, ref lookup);
                __NS__.SparseRemovalIndex<int>.AddIndexed(removed, 7, ref lookup);
                if (removed.Count != 1) return "removal-dedup";
                if (!__NS__.SparseRemovalIndex<int>.ContainsRemoved(removed, 7, lookup)) return "removal-contains";
                __NS__.SparseRemovalIndex<int>.CancelRemoval(removed, 7, ref lookup);
                if (removed.Count != 0) return "removal-cancel";
                if (__NS__.SparseRemovalIndex<int>.ContainsRemoved(removed, 7, lookup)) return "removal-absent";
                return "removal-ok";
            }
        }
        """;

    private static string CompileAndRun(
        string implementationNamespace,
        bool includePortableSetView,
        bool hashSetSupportsCapacity
    )
    {
        var clone = SparseGeneratedOnceEmitter.RenderCloneHelpers(
            implementationNamespace,
            includePortableSetView,
            hashSetSupportsCapacity,
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

        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(clone, path: "Clone.g.cs"),
            CSharpSyntaxTree.ParseText(adapters, path: "Adapters.g.cs"),
            CSharpSyntaxTree.ParseText(removal, path: "Removal.g.cs"),
            CSharpSyntaxTree.ParseText(
                ProbeDriver.Replace("__NS__", "global::" + implementationNamespace),
                path: "Probe.cs"
            ),
        };
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        // No SparseFragments runtime reference: the probe proves the
        // downstream dialect stands on BCL contracts alone.
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "DownstreamOnceProbe",
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
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var driver = assembly.GetType("OnceProbeDriver").ShouldNotBeNull();
        driver.GetMethod("RunAdapters")!.Invoke(null, null).ShouldBe("adapters-ok");
        driver.GetMethod("RunClone")!.Invoke(null, null).ShouldBe("clone-ok");
        driver.GetMethod("RunRemoval")!.Invoke(null, null).ShouldBe("removal-ok");
        return implementationNamespace;
    }

    [Test]
    public void DownstreamDialect_ExecutesBehaviorWithoutStandaloneRuntime()
    {
        CompileAndRun("Downstream.Generated", false, true);
    }

    [Test]
    public void RenamedFamilies_CoexistWithoutClash()
    {
        CompileAndRun("Acme.Product.Generated", false, true);
        CompileAndRun("Other.Product.Generated", false, true);
        SparseGeneratedOnceEmitter
            .CloneHelpersHintName("Acme.Product.Generated")
            .ShouldNotBe(
                SparseGeneratedOnceEmitter.CloneHelpersHintName("Other.Product.Generated")
            );
    }

    [Test]
    public void TargetFrameworkVariants_CompileAndStayDistinct()
    {
        var variants = new[]
        {
            (Portable: false, Capacity: true),
            (Portable: false, Capacity: false),
            (Portable: true, Capacity: true),
            (Portable: true, Capacity: false),
        };
        var rendered = variants
            .Select(variant =>
                SparseGeneratedOnceEmitter.RenderCloneHelpers(
                    "Downstream.Generated",
                    variant.Portable,
                    variant.Capacity,
                    CancellationToken.None
                )
            )
            .ToArray();
        rendered[0].ShouldNotContain("__SparseReadOnlySet<T>");
        rendered[2].ShouldContain("__SparseReadOnlySet<T>");
        rendered.Distinct().Count().ShouldBeGreaterThan(1);
        foreach (var variant in variants)
        {
            CompileAndRun("Downstream.Generated", variant.Portable, variant.Capacity);
        }
    }

    [Test]
    public void FeatureSelections_GateHelperFamilies()
    {
        var scalar = ScalarMember();
        var analyses = ImmutableArray.Create(
            new SparseGenerationAnalysis(
                new SparseModelInfo(
                    "Model",
                    "global::Ns.Model",
                    "Ns",
                    false,
                    false,
                    false,
                    "Ns.Model.Downstream.g.cs",
                    null
                ),
                ImmutableArray.Create(scalar),
                ImmutableArray<SparsePocoCloneModel>.Empty,
                ImmutableArray<SparseStructuralModel>.Empty,
                ImmutableArray<SparseGeneratorDiagnostic>.Empty
            )
        );
        var list = analyses[0] with { Members = ImmutableArray.Create(scalar, ListMember()) };
        var listed = ImmutableArray.Create(list);

        var full = SparseGeneratedCapabilityPlanner.Aggregate(
            listed,
            ImmutableArray<SparsePromotedModel>.Empty,
            SparseEmissionFeatures.Standalone,
            true,
            true,
            CancellationToken.None
        );
        full.Has(SparseGeneratedCapability.CloneHelpers).ShouldBeTrue();
        full.Has(SparseGeneratedCapability.ReadOnlyAdapters).ShouldBeTrue();

        var noObservable = SparseGeneratedCapabilityPlanner.Aggregate(
            listed,
            ImmutableArray<SparsePromotedModel>.Empty,
            SparseEmissionFeatures.Standalone with
            {
                EmitObservable = false,
            },
            true,
            true,
            CancellationToken.None
        );
        noObservable.Has(SparseGeneratedCapability.ReadOnlyAdapters).ShouldBeFalse();
        noObservable.Has(SparseGeneratedCapability.CloneHelpers).ShouldBeTrue();
        SparseGeneratedCapabilityPlanner
            .Validate(
                noObservable,
                SparseEmissionFeatures.Standalone with
                {
                    EmitObservable = false,
                }
            )
            .ShouldBeEmpty();
    }

    [Test]
    public void ContractMatrix_NamesOnlyCallerOwnedContracts()
    {
        // EditSession core consumes all three dialects; the generic helper
        // families need none (BCL only). The render signatures encode that:
        // helper renderers take no runtime or patch dialect.
        var session = SparseEditSessionEmitter.RenderCoreSources(
            new SparseEditSessionDialect("Downstream.Generated"),
            new SparseRuntimeDialect(
                "global::Downstream.",
                "global::Downstream.Optional",
                "global::Downstream.MergeStrategy",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "global::Downstream.Runtime",
                "__downstream_merge_"
            ),
            new SparseFragmentPatchEmitter.SparsePatchDialect(
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
        session.Core.ShouldContain("global::Downstream.Optional<");
        session.Core.ShouldContain("global::Downstream.Conflict");
        session.Core.ShouldNotContain("SparseFragments");

        var clone = SparseGeneratedOnceEmitter.RenderCloneHelpers(
            "Downstream.Generated",
            false,
            true,
            CancellationToken.None
        );
        var adapters = SparseGeneratedOnceEmitter.RenderReadOnlyAdapters(
            "Downstream.Generated",
            CancellationToken.None
        );
        var removal = SparseGeneratedOnceEmitter.RenderRemovalIndex(
            "Downstream.Generated",
            CancellationToken.None
        );
        foreach (var source in new[] { clone, adapters, removal })
        {
            source.ShouldNotContain("global::Downstream.Optional");
            source.ShouldNotContain("global::Downstream.Conflict");
            source.ShouldNotContain("global::Downstream.Runtime");
        }
    }

    private static SparseMemberModel ScalarMember()
    {
        var property = new SparsePropertyModel(
            "Name",
            new SparseTypeModel(
                "global::System.String?",
                "global::System.String?",
                "global::System.String?",
                IsReferenceType: true,
                IsFragmentModel: false,
                null
            ),
            JsonPropertyName: "Name"
        );
        return new SparseMemberModel(
            0,
            property,
            null,
            SparseMergeModes.Replace,
            SparseCollectionInfo.Unsupported,
            null,
            null,
            false,
            true
        );
    }

    private static SparseMemberModel ListMember()
    {
        var element = new SparseTypeModel(
            "string",
            "string",
            "string",
            IsReferenceType: true,
            IsFragmentModel: false,
            null,
            UsesDefaultScalarEquality: true
        );
        var property = new SparsePropertyModel(
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
        );
        return new SparseMemberModel(
            1,
            property,
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
        );
    }
}
