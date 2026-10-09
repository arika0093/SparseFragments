using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using SparseFragments.Generator.Shared;

namespace PackageShared.Generator;

/// <summary>
/// Package-only downstream generator probe (#70).
/// Exercises representative Shared categories so a missing Shared source file
/// fails compilation: model/collection analysis, IR/model types, naming
/// infrastructure, emitter/helpers, and the downstream policy/config surface
/// (#121: feature selection, member transport and rebase policies, write
/// contracts). All Shared sources arrive via the
/// SparseFragments.Generator.Shared NuGet package (contentFiles +
/// build/SparseFragments.Generator.Shared.props); there is no sibling-source
/// fallback in PackageShared.Generator.csproj.
/// </summary>
[Generator]
public sealed class ProbeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            var source = ProbeSurface.BuildProbeSource(CancellationToken.None);
            ctx.AddSource("PackageShared.Probe.g.cs", source);
        });
    }
}

internal static class ProbeSurface
{
    internal static string BuildProbeSource(CancellationToken cancellationToken)
    {
        // Naming infrastructure (Naming/).
        var escaped = SparseNaming.EscapeIdentifier("class");
        var apiPrefix = SparseNaming.PatchApiPrefix(ImmutableArray<string>.Empty);
        var mergeFieldPrefix = SparseWellKnownNames.MergeStrategyFieldPrefix;
        var jsonIgnoreNever = SparseJsonNaming.JsonIgnoreNever;

        // Shared IR/model types (Models/).
        var elementType = new SparseTypeModel(
            "global::System.String",
            "global::System.String",
            "global::System.String",
            IsReferenceType: true,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            elementType,
            null,
            null,
            SparseCollectionSemantic.ScalarSequence,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None
        );
        var property = new SparsePropertyModel("Name", elementType);
        var member = new SparseMemberModel(
            1,
            property,
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            ChildIsStructural: false,
            ChildIsReferenceType: true
        );
        var policyType = new SparseTypeModel(
            "global::PackageShared.Policy",
            "global::PackageShared.Policy",
            "global::PackageShared.Policy",
            IsReferenceType: true,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );
        var policyMember = member with { RebasePolicyType = policyType };
        var key = new SparseKeyInfo(
            SparseKeyKind.None,
            ImmutableArray<string>.Empty,
            "global::System.String"
        );

        // Shared model/collection analysis (Analysis/). Referencing the analyzer
        // types proves their sources were compiled from the package.
        var analysisTypes = new[]
        {
            typeof(SparseCollectionAnalyzer),
            typeof(SparseKeyAnalyzer),
            typeof(SparseModelAnalyzer),
            typeof(SparseMergeValidation),
        };

        // Rebase policy surface (Emitters/Patch/, Analysis/). Referencing the
        // options/policy emitters and the rebase field default proves a
        // downstream generator can configure rebase semantics from the package
        // without a SparseFragments runtime reference.
        var rebaseTypes = new[]
        {
            typeof(SparseRebaseOptionEmitter),
            typeof(SparseChangeSetMemberRebaseEmitter),
            typeof(SparseFragmentPatchCollectionRebaseEmitter),
        };
        var rebaseFieldPrefix = SparseWellKnownNames.RebasePolicyFieldPrefix;

        // Downstream product contract (issue #121): feature selection, member
        // transport and rebase policies, and write contracts. Touching these
        // types keeps the probe covering the policy/config surface.
        var features = SparseEmissionFeatures.Standalone;
        var transport = SparseMemberTransport.RedactedBefore;
        var memberPolicy = new SparseMemberPolicy("Secret", transport);
        var rebase = new SparseRebasePolicy(SparseRedactedBeforeBehavior.Passthrough);
        var sessionDialect = new SparseEditSessionDialect("PackageShared.Generated");
        var capability = SparseEditSessionCapabilities.ForCompilation(true, true);
        var roles = new[]
        {
            SparseEditSessionRoles.Model,
            SparseEditSessionRoles.Fragment,
            SparseEditSessionRoles.Patch,
            SparseEditSessionRoles.ChangeSet,
            SparseEditSessionRoles.Observable,
            SparseEditSessionRoles.Current,
        };
        var adapterMembers = SparseEditSessionAdapterContract.RequiredMembers;
        var sessionRuntime = new SparseRuntimeDialect(
            "global::PackageShared.",
            "global::PackageShared.Optional",
            "global::PackageShared.MergeStrategy",
            "global::PackageShared.Runtime",
            "global::PackageShared.Runtime",
            "global::PackageShared.Runtime",
            "global::PackageShared.Runtime",
            "__package_shared_merge_"
        );
        var sessionPatch = new SparseFragmentPatchEmitter.SparsePatchDialect(
            "global::PackageShared.",
            "__sparse_whole",
            "__SparseMembersEmpty",
            static item => "__sparse_patch_member_" + item.Id,
            static _ => string.Empty,
            "Apply",
            false,
            "global::PackageShared.Runtime",
            "global::PackageShared.Conflict",
            "global::PackageShared.ConflictKind",
            static payload => "global::PackageShared.Rebase<" + payload + ">",
            static _ => "global::PackageShared.Patch",
            static _ => "global::PackageShared.ChangeSet",
            PayloadImplementationContainerPrefix: "PackageSharedInternal"
        );
        var sessionSources = SparseEditSessionEmitter.RenderCoreSources(
            sessionDialect,
            sessionRuntime,
            sessionPatch
        );
        var write = new SparseWriteContract(
            "global::PackageShared.WriteCmd",
            ImmutableArray.Create(new SparseWriteMember("Secret", "NewSecret"))
        );
        var dependencyErrors = features.ValidateDependencies();
        var emittedNames = features.GetEmittedTypeNames();
        var descriptorCapability = SparseDescriptorCapabilities.ForCompilation(
            hasDescriptors: true,
            hasCollections: true,
            hasChangeProjection: true
        );
        var descriptorErrors = SparseDescriptorCapabilities.ValidatePrerequisites(
            new SparseGeneratorConfig(
                ModelAttributeMetadataName: "ModelAttribute",
                IgnoreAttributeMetadataName: "IgnoreAttribute",
                RedactBeforeAttributeMetadataName: "RedactBeforeAttribute",
                MergeAttributeMetadataName: "MergeAttribute",
                MergeStrategyBaseMetadataName: "MergeStrategyBase",
                CloneReferenceSafeAttributeMetadataName: "CloneSafeAttribute",
                KeyAttributeMetadataName: "KeyAttribute",
                KeyedInterfaceMetadataName: "IKeyed<TKey>",
                KeyPropertyName: "Key",
                MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
                DiagnosticIds: new SparseDiagnosticIdMap(
                    "T001",
                    "T002",
                    "T003",
                    "T004",
                    "T005",
                    "T006",
                    "T007",
                    "T008",
                    "T009",
                    "T010",
                    "T011",
                    "T012",
                    "T013",
                    "T014",
                    "T015",
                    "T016",
                    "T017",
                    "T018",
                    "T019",
                    "T020",
                    "T021",
                    "T022",
                    "T023"
                ),
                HintNameSuffix: ".Probe.g.cs",
                PromotedHintNameSuffix: ".ProbePromoted.g.cs",
                StructuralHostPrefix: "__ProbeHost_"
            ),
            descriptorCapability
        );
        var kernelKinds = SparsePatchKernelCapabilities.ForCompilation(true, true, true);
        var kernelSource = SparsePatchKernelEmitter.RenderHelperSource(
            "PackageShared.Generated",
            kernelKinds
        );

        // Emitter/helpers (Emitters/, Infrastructure/).
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseFragmentEmitHelpers.AppendNullGuard(code, 1, "value");
        var expressions = new SparseFragmentExpressions(
            "__cloneContext",
            "global::PackageShared.Runtime",
            "global::PackageShared.Runtime",
            "global::PackageShared.Optional"
        );
        var clone = expressions.CloneValueExpression(elementType, "value");
        var observable = SparseObservableEmitter.ObservableTypeName(
            ImmutableArray<SparseMemberModel>.Empty
        );
        var incrementalTypes = new[]
        {
            typeof(SparseChangeSetEnumeratorEmitter),
            typeof(SparseLocationSnapshot),
            typeof(SparseExternalInit),
            typeof(RoslynSymbolCompat),
        };

        code.AppendLineAt(
            0,
            "// PackageShared probe: "
                + escaped
                + " prefix="
                + apiPrefix
                + " merge="
                + mergeFieldPrefix
                + " json="
                + jsonIgnoreNever
        );
        code.AppendLineAt(
            0,
            "// member=" + member.Id + " key=" + key.KeyTypeName + " clone=" + clone
        );
        code.AppendLineAt(
            0,
            "// policy="
                + policyMember.RebasePolicyType!.Value.Name
                + " prefix="
                + rebaseFieldPrefix
        );
        code.AppendLineAt(
            0,
            "// analysis="
                + analysisTypes.Length
                + " rebase="
                + rebaseTypes.Length
                + " incremental="
                + incrementalTypes.Length
        );
        code.AppendLineAt(0, "// observable=" + observable + " collection=" + collection.Kind);
        code.AppendLineAt(
            0,
            "// session-capability="
                + capability
                + " roles="
                + roles.Length
                + " adapter="
                + adapterMembers.Length
                + " descriptors="
                + descriptorCapability
                + " descriptor-errors="
                + descriptorErrors.Length
                + " kernels="
                + kernelKinds
                + " kernel-bytes="
                + kernelSource.Length
                + " extracted="
                + SparsePatchKernelInventory.Extracted.Length
        );
        code.AppendLineAt(
            0,
            "// policy="
                + memberPolicy.MemberName
                + ":"
                + memberPolicy.Transport
                + " rebase="
                + rebase.RedactedBefore
                + " write="
                + write.WriteModelType
                + " deps="
                + dependencyErrors.Length
                + " emitted="
                + emittedNames.Length
        );
        if (
            !sessionSources.Core.Contains(
                "global::PackageShared.Optional",
                StringComparison.Ordinal
            ) || sessionSources.Core.Contains("global::SparseFragments", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException("The edit-session dialect was not applied.");
        }
        return "// Shared generator API and template probe passed.";
    }
}
