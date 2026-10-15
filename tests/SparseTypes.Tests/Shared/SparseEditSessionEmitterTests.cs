using System.Collections.Immutable;
using System.Threading;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

public sealed class SparseEditSessionEmitterTests
{
    private static SparseRuntimeDialect RuntimeDialect() =>
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

    private static SparseFragmentPatchEmitter.SparsePatchDialect PatchDialect() =>
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
        {
            ReconcileConflictExpression = static (_, reason) =>
                "global::Downstream.Conflict.Reconcile(" + reason + ")",
        };

    [Test]
    public void CoreSources_UseConfiguredSessionAndRuntimeDialects()
    {
        var (core, current) = SparseEditSessionEmitter.RenderCoreSources(
            new SparseEditSessionDialect("Downstream.Generated"),
            RuntimeDialect(),
            PatchDialect()
        );

        core.ShouldContain("namespace Downstream.Generated");
        core.ShouldContain("global::Downstream.Optional<");
        core.ShouldContain("global::Downstream.Conflict");
        core.ShouldContain("global::Downstream.Rebase<TChangeSet>");
        core.ShouldNotContain("SparseTypes.");
        current.ShouldContain("namespace Downstream.Generated");
        current.ShouldContain("global::Downstream.Optional<");
        current.ShouldNotContain("SparseTypes.");
    }

    [Test]
    public void CoreSources_ExposeForkMergeThroughDialects()
    {
        var (core, current) = SparseEditSessionEmitter.RenderCoreSources(
            new SparseEditSessionDialect("Downstream.Generated"),
            RuntimeDialect(),
            PatchDialect()
        );

        core.ShouldContain("Fork()");
        core.ShouldContain("TryMergeFrom");
        core.ShouldContain("global::Downstream.Conflict");
        core.ShouldContain("global::Downstream.Rebase<TChangeSet>");
        core.ShouldNotContain("SparseTypes.");
        current.ShouldContain("Fork()");
        current.ShouldContain("TryMergeFrom");
        current.ShouldNotContain("SparseTypes.");
    }

    [Test]
    public void ModelSessionDoesNotEmitLegacyOpenDraftSurface()
    {
        var model = new SparseModelInfo(
            "Order",
            "global::Downstream.Order",
            "Downstream",
            IsGlobalNamespace: false,
            IsStruct: false,
            IsRecord: false,
            HintName: "Order.g.cs",
            Constructor: null
        );
        var config = new SparseGeneratorConfig(
            ModelAttributeMetadataName: "ModelAttribute",
            IgnoreAttributeMetadataName: "IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "RedactBeforeAttribute",
            MergeAttributeMetadataName: "MergeAttribute",
            MergeStrategyBaseMetadataName: "MergeStrategyBase",
            CloneReferenceSafeAttributeMetadataName: "CloneSafeAttribute",
            KeyAttributeMetadataName: "KeyAttribute",
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
                "T013",
                "T014",
                "T017",
                "T019",
                "T021",
                "T022",
                "T023"
            ),
            HintNameSuffix: ".Downstream.g.cs",
            PromotedHintNameSuffix: ".DownstreamPromoted.g.cs",
            StructuralHostPrefix: "__DownstreamHost_",
            PatchDialect: PatchDialect(),
            EditSessionInterfaceMetadataName: "PackageShared.ISparseEditSession",
            EditSessionDialect: new SparseEditSessionDialect("Downstream.Generated"),
            EditSessionWithChangeSetInterfaceMetadataName: "PackageShared.ISparseEditSessionWithChangeSet"
        );
        using var code = new SharedIndentedBuilder(CancellationToken.None);

        SparseEditSessionEmitter.AppendModelEditSession(
            code,
            model,
            model.ModelTypeName,
            "public",
            "Observable",
            "ReadOnlyView",
            ImmutableArray<SparseMemberModel>.Empty,
            config
        );

        var source = code.ToString();
        source.ShouldNotContain("OpenScope");
        source.ShouldNotContain("HasOpenDrafts");
        source.ShouldNotContain("ISessionOpenDraftState");
        source.ShouldContain("public bool TryReconcileAndApply(");
        source.ShouldContain("_session.TryReconcileAndApply(submitted, persisted");
        source.ShouldContain("global::Downstream.Conflict.Reconcile");
        source.ShouldContain("PreviewChangeSet()");
        source.ShouldContain("PrepareChangeSet()");
        source.ShouldContain("/// <summary>Reconciles an acknowledged submission");
        source.ShouldContain(
            "global::PackageShared.ISparseEditSessionWithChangeSet<global::Downstream.Order, global::Downstream.Order.ChangeSet>"
        );
        source.ShouldContain(
            "global::PackageShared.ISparseEditSessionWithChangeSet<global::Downstream.Order, global::Downstream.Order.ChangeSet>.IsEmpty"
        );
        source.ShouldNotContain("SparseTypes.");
    }
}
