using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>
/// Downstream policy contract for product generators (issue #121): feature
/// selection, member transport and rebase policies, write contracts and
/// product-name validation, all without copying emitters.
/// </summary>
public sealed class DownstreamPolicyTests
{
    private static SparseTypeModel ScalarType(string name) =>
        new(
            name,
            name,
            name,
            IsReferenceType: name.Contains("String", StringComparison.Ordinal),
            IsFragmentModel: false,
            null
        );

    private static SparseMemberModel ScalarMember(int id, string name, string typeName)
    {
        var property = new SparsePropertyModel(
            name,
            ScalarType(typeName),
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: name,
            HasExplicitJsonPropertyName: false,
            JsonIgnoreCondition: 0
        );
        return new SparseMemberModel(
            id,
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

    private static ImmutableArray<SparseMemberModel> SecretMembers() =>
        ImmutableArray.Create(
            ScalarMember(0, "Label", "global::System.String?"),
            ScalarMember(1, "RetryCount", "global::System.Int32"),
            ScalarMember(2, "Secret", "global::System.String?")
        );

    private static SparseMemberModel KeyedMember()
    {
        var element = ScalarType("global::Ns.Item");
        var list = ScalarType("global::System.Collections.Generic.List<global::Ns.Item>");
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            element,
            null,
            "System.Collections.Generic.List<T>",
            SparseCollectionSemantic.KeyedSequence,
            ImmutableArray.Create("Id"),
            "global::System.Int32",
            SparseKeyKind.Property
        );
        return new SparseMemberModel(
            4,
            new SparsePropertyModel("Items", list, JsonPropertyName: "Items"),
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseRuntimeDialect DownstreamRuntime() =>
        new(
            "global::Downstream.",
            "global::Downstream.Optional",
            "global::Downstream.MergeStrategy",
            "global::Downstream.Runtime",
            "global::Downstream.Runtime",
            "global::Downstream.Runtime",
            "global::Downstream.Runtime",
            "__downstream_merge_"
        );

    private static SparseFragmentPatchEmitter.SparsePatchDialect DownstreamDialect(
        ImmutableArray<SparseMemberPolicy> policies = default,
        SparseRebasePolicy? rebase = null,
        SparseWriteContract? write = null
    ) =>
        new(
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
            static member => "Patch",
            static member => "global::Downstream.Delta_" + member.Id,
            MemberPolicies: policies,
            RebasePolicy: rebase,
            WriteContract: write,
            PayloadImplementationContainerPrefix: "DownstreamInternal"
        );

    private static SparseGeneratorConfig DownstreamConfig(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseEmissionFeatures? features = null,
        ImmutableArray<string> productNames = default
    ) =>
        new(
            ModelAttributeMetadataName: "Downstream.ModelAttribute",
            IgnoreAttributeMetadataName: "Downstream.IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "Downstream.RedactBeforeAttribute",
            MergeAttributeMetadataName: "Downstream.MergeAttribute",
            MergeStrategyBaseMetadataName: "Downstream.MergeStrategy<T>",
            CloneReferenceSafeAttributeMetadataName: "Downstream.CloneSafeAttribute",
            KeyAttributeMetadataName: "Downstream.IdentityAttribute",
            KeyedInterfaceMetadataName: "Downstream.IKeyed<TKey>",
            KeyPropertyName: "Identity",
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
            RuntimeDialect: DownstreamRuntime(),
            PatchDialect: dialect,
            EmissionFeatures: features,
            ProductExtensionNames: productNames
        );

    private static SparseModelInfo ReadModel() =>
        new(
            "ReadModel",
            "global::Ns.ReadModel",
            "Ns",
            IsGlobalNamespace: false,
            IsStruct: false,
            IsRecord: false,
            HintName: "ReadModel.g.cs",
            Constructor: null,
            IgnoredSettablePropertyNames: ImmutableArray<string>.Empty
        );

    private static string BuildSource(
        SparseGeneratorConfig config,
        ImmutableArray<SparseMemberModel> members
    ) =>
        SparseFragmentEmitter.BuildSource(
            ReadModel(),
            members,
            ImmutableArray<SparsePocoCloneModel>.Empty,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            ImmutableArray<SparseStructuralModel>.Empty,
            bclHashSetImplementsReadOnlySet: false,
            bclHashSetSupportsCapacity: false,
            CancellationToken.None,
            config
        );

    private static int CountOccurrences(string text, string marker) =>
        (text.Length - text.Replace(marker, string.Empty, StringComparison.Ordinal).Length)
        / marker.Length;

    [Test]
    public void StandaloneDefaults_EmitEveryFamily()
    {
        var text = BuildSource(DownstreamConfig(DownstreamDialect()), SecretMembers());

        text.ShouldContain("public sealed class Patch");
        text.ShouldContain("public sealed class ChangeSet");
        text.ShouldContain("ChangePayload");
        text.ShouldContain("public sealed class Observable");
        text.ShouldContain("FragmentJsonConverter");
        text.ShouldContain("public ChangePayload ToPayload()");
        // The mixed partition seam (#118/#119) owns the baseline-free
        // ToPatchCore even for standalone defaults; the downstream policy
        // projection adds a strict-aware overload only when policies apply.
        text.ShouldContain("ToPatchCore");
        text.ShouldNotContain("WriteTo(global::Ns.WriteCmd)");
    }

    [Test]
    public void FeatureSelection_OmitsDeselectedFamilies()
    {
        var features = new SparseEmissionFeatures(
            EmitObservable: false,
            EmitChangePayload: false,
            EmitJsonConverters: false
        );
        var text = BuildSource(DownstreamConfig(DownstreamDialect(), features), SecretMembers());

        text.ShouldContain("public sealed class Patch");
        text.ShouldContain("public sealed class ChangeSet");
        text.ShouldNotContain("public sealed class Observable");
        // Payload DTOs are omitted, though the shared rebase-options
        // vocabulary (#120) still names caller-owned ChangePayload types.
        text.ShouldNotContain("ChangePayloadCore");
        text.ShouldNotContain("public sealed class ChangePayload");
        text.ShouldNotContain("ToPayload");
        text.ShouldNotContain("FragmentJsonConverter");
    }

    [Test]
    public void FeatureSelection_FragmentWithoutPatch_OmitsPatchOperations()
    {
        var features = new SparseEmissionFeatures(
            EmitPatch: false,
            EmitChangeSet: false,
            EmitChangePayload: false
        );
        var text = BuildSource(DownstreamConfig(DownstreamDialect(), features), SecretMembers());

        text.ShouldContain("public sealed class Fragment");
        text.ShouldContain("public void WriteTo");
        text.ShouldNotContain("public Patch");
        text.ShouldNotContain("(Patch patch)");
        text.ShouldNotContain("public sealed class ChangeSet");
    }

    [Test]
    public void FeatureSelection_OmitsChangeSetKeepsPatch()
    {
        var features = new SparseEmissionFeatures(EmitChangeSet: false, EmitChangePayload: false);
        var text = BuildSource(DownstreamConfig(DownstreamDialect(), features), SecretMembers());

        text.ShouldContain("public sealed class Patch");
        text.ShouldNotContain("public sealed class ChangeSet");
    }

    [Test]
    public void FeatureDependencies_RejectIncoherence()
    {
        new SparseEmissionFeatures(EmitPatch: false, EmitChangeSet: true)
            .ValidateDependencies()
            .ShouldNotBeEmpty();
        new SparseEmissionFeatures(EmitChangeSet: false, EmitChangePayload: true)
            .ValidateDependencies()
            .ShouldNotBeEmpty();
        new SparseEmissionFeatures(EmitFragment: false, EmitPatch: true)
            .ValidateDependencies()
            .ShouldNotBeEmpty();
        SparseEmissionFeatures.Standalone.ValidateDependencies().ShouldBeEmpty();

        var bad = DownstreamConfig(
            DownstreamDialect(),
            new SparseEmissionFeatures(EmitPatch: false, EmitChangeSet: true)
        );
        Should.Throw<ArgumentException>(() => BuildSource(bad, SecretMembers()));
    }

    [Test]
    public void EmittedTypeNames_CoverSelectedFamilies()
    {
        var names = SparseEmissionFeatures.Standalone.GetEmittedTypeNames();
        names.ShouldContain("Fragment");
        names.ShouldContain("Patch");
        names.ShouldContain("ChangeSet");
        names.ShouldContain("ChangePayload");
        names.ShouldContain("Observable");

        new SparseEmissionFeatures(EmitObservable: false)
            .GetEmittedTypeNames()
            .ShouldNotContain("Observable");
    }

    [Test]
    public void Redaction_OmitsBefore_RequiresExplicitProjection()
    {
        var dialect = DownstreamDialect(
            ImmutableArray.Create(
                new SparseMemberPolicy("Secret", SparseMemberTransport.RedactedBefore)
            )
        );
        var text = BuildSource(DownstreamConfig(dialect), SecretMembers());

        // The redacted member travels without its before-state.
        CountOccurrences(text, "{ Before = ").ShouldBe(2);
        text.ShouldContain("{ After = ");
        // A redacted payload never rebuilds a complete ChangeSet.
        text.ShouldContain("is redacted and cannot be converted to a complete ChangeSet");
        text.ShouldContain("internal Patch ToPatchCore()");
        text.ShouldContain("public Patch ToPatch()");
        text.ShouldNotContain("global::SparseFragments");
    }

    [Test]
    public void RebasePolicy_Passthrough_ProjectsAfter()
    {
        var dialect = DownstreamDialect(
            ImmutableArray.Create(
                new SparseMemberPolicy("Secret", SparseMemberTransport.RedactedBefore)
            ),
            new SparseRebasePolicy(SparseRedactedBeforeBehavior.Passthrough)
        );
        var text = BuildSource(DownstreamConfig(dialect), SecretMembers());

        text.ShouldContain(".Set(__redactedAfter2.Value)");
        text.ShouldNotContain("strict rebase policy refuses");
    }

    [Test]
    public void RebasePolicy_StrictFail_RefusesProjection()
    {
        var dialect = DownstreamDialect(
            ImmutableArray.Create(
                new SparseMemberPolicy("Secret", SparseMemberTransport.RedactedBefore)
            ),
            new SparseRebasePolicy(SparseRedactedBeforeBehavior.StrictFail)
        );
        var text = BuildSource(DownstreamConfig(dialect), SecretMembers());

        text.ShouldContain("strict rebase policy refuses");
        text.ShouldContain("InvalidOperationException");
    }

    [Test]
    public void WriteOnly_SkippedInReadProjection_KeepsTransport()
    {
        var dialect = DownstreamDialect(
            ImmutableArray.Create(new SparseMemberPolicy("Secret", SparseMemberTransport.WriteOnly))
        );
        var text = BuildSource(DownstreamConfig(dialect), SecretMembers());

        // The read projection omits the command-only member.
        text.ShouldNotContain("Secret = rootMember2,");
        // The transport variant still carries its after-state.
        text.ShouldContain(
            SparseChangeSetPayloadEmitter.PayloadMemberName("global::Ns.ReadModel", "Change", 2)
        );
        text.ShouldNotContain("Ns_ReadModelChangePayloadChange2");
        text.ShouldContain("internal Patch ToPatchCore()");
    }

    [Test]
    public void WriteContract_EmitsDistinctWriteOverload()
    {
        var dialect = DownstreamDialect(
            write: new SparseWriteContract(
                "global::Ns.WriteCmd",
                ImmutableArray.Create(new SparseWriteMember("Secret", "NewSecret"))
            )
        );
        var text = BuildSource(DownstreamConfig(dialect), SecretMembers());

        text.ShouldContain("public void WriteTo(global::Ns.WriteCmd model)");
        text.ShouldContain("model.NewSecret = __sparse_updated.Secret!;");
        text.ShouldContain("public void WriteTo(global::Ns.ReadModel model)");

        var same = DownstreamDialect(write: new SparseWriteContract("global::Ns.ReadModel"));
        CountOccurrences(
                BuildSource(DownstreamConfig(same), SecretMembers()),
                "public void WriteTo(global::Ns.ReadModel model)"
            )
            .ShouldBe(1);
    }

    [Test]
    public void DistinctReadWriteModels_CompileWithoutSameModelAssumption()
    {
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
            RuntimeDialect: new SparseRuntimeDialect(
                "global::SparseFragments.",
                "global::SparseFragments.Optional",
                "global::SparseFragments.FragmentMergeStrategy",
                "global::SparseFragments.CompilerServices.SparseFragmentRuntime",
                "global::SparseFragments.CompilerServices.SparseFragmentRuntime",
                "global::SparseFragments.CompilerServices.SparseFragmentRuntime",
                "global::SparseFragments.CompilerServices.SparseFragmentRuntime",
                "__sparse_merge_strategy_"
            ),
            PatchDialect: new SparseFragmentPatchEmitter.SparsePatchDialect(
                "global::SparseFragments.",
                "__sparse_whole",
                "__SparseMembersEmpty",
                SparseFragmentPatchEmitter.Field,
                static _ => string.Empty,
                "Apply",
                false,
                "global::SparseFragments.CompilerServices.SparseFragmentRuntime",
                "global::SparseFragments.SparseConflict",
                "global::SparseFragments.SparseConflictKind",
                static payload => "global::SparseFragments.RebaseResult<" + payload + ">",
                SparseFragmentPatchEmitter.ChildPatch,
                SparseFragmentPatchEmitter.DefaultChildChangeSet,
                MergeStrategyField: static member => "__sparse_merge_strategy_" + member.Id,
                MemberPolicies: ImmutableArray.Create(
                    new SparseMemberPolicy("Secret", SparseMemberTransport.RedactedBefore)
                ),
                WriteContract: new SparseWriteContract(
                    "global::Ns.WriteCmd",
                    ImmutableArray.Create(new SparseWriteMember("Secret", "NewSecret"))
                ),
                PayloadImplementationContainerPrefix: "SparseFragmentsInternal"
            )
        );
        var generated = BuildSource(config, SecretMembers());
        generated.ShouldContain("public void WriteTo(global::Ns.WriteCmd model)");

        const string models = """
            namespace Ns
            {
                public partial class ReadModel
                {
                    public string? Label { get; set; }
                    public int RetryCount { get; set; }
                    public string? Secret { get; set; }
                }
                public class WriteCmd
                {
                    public string? Label { get; set; }
                    public int RetryCount { get; set; }
                    public string? NewSecret { get; set; }
                }
            }
            """;
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trustedAssemblies
            .Select(static assemblyPath =>
                (MetadataReference)MetadataReference.CreateFromFile(assemblyPath)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragments.Optional<>).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "DownstreamReadWriteProbe",
            [
                CSharpSyntaxTree.ParseText(generated, path: "Generated.cs"),
                CSharpSyntaxTree.ParseText(models, path: "Models.cs"),
            ],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    [Test]
    public void KeyedPolicy_Rejected()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "Label", "global::System.String?"),
            KeyedMember()
        );
        var dialect = DownstreamDialect(
            ImmutableArray.Create(
                new SparseMemberPolicy("Items", SparseMemberTransport.RedactedBefore)
            )
        );

        SparseDownstreamPolicy
            .FindNonScalarPolicyMembers(members, dialect)
            .ShouldHaveSingleItem()
            .ShouldBe("Items");
        var code = new SharedIndentedBuilder(CancellationToken.None);
        Should.Throw<ArgumentException>(() =>
            SparseChangeSetPayloadTransferEmitter.AppendToPayload(code, members, dialect, null)
        );
    }

    [Test]
    public void UnknownPolicyName_Reported()
    {
        var dialect = DownstreamDialect(
            ImmutableArray.Create(new SparseMemberPolicy("Missing", SparseMemberTransport.Full))
        );

        SparseDownstreamPolicy
            .FindUnknownMemberNames(SecretMembers(), dialect)
            .ShouldHaveSingleItem()
            .ShouldBe("Missing");
        Should.Throw<ArgumentException>(() =>
            SparseDownstreamPolicy.ThrowOnInvalidTransport(SecretMembers(), dialect)
        );
    }

    [Test]
    public void EmissionPlan_ValidatesDependenciesNamesAndProductCollisions()
    {
        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        SparseDownstreamPolicy.ValidateEmissionPlan(
            SecretMembers(),
            DownstreamConfig(
                DownstreamDialect(),
                new SparseEmissionFeatures(EmitPatch: false, EmitChangeSet: true)
            ),
            diagnostics,
            CancellationToken.None
        );
        diagnostics.Select(static diagnostic => diagnostic.DescriptorId).ShouldContain("DWN028");

        var collisions = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        SparseDownstreamPolicy.ValidateEmissionPlan(
            SecretMembers(),
            DownstreamConfig(DownstreamDialect(), productNames: ImmutableArray.Create("Patch")),
            collisions,
            CancellationToken.None
        );
        collisions.Select(static diagnostic => diagnostic.DescriptorId).ShouldContain("DWN009");
        collisions.Single().Argument1.ShouldBe("Patch");
    }

    [Test]
    public void EmissionPlan_StandaloneDefaults_PassUnconfigured()
    {
        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        SparseDownstreamPolicy.ValidateEmissionPlan(
            SecretMembers(),
            DownstreamConfig(DownstreamDialect()),
            diagnostics,
            CancellationToken.None
        );

        diagnostics.ShouldBeEmpty();
    }
}
