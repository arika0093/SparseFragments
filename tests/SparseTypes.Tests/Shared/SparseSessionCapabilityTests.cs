using System.Collections.Immutable;
using System.Threading;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

public sealed class SparseSessionCapabilityTests
{
    private static SparseFragmentPatchEmitter.SparsePatchDialect PlainDialect() =>
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
            static _ => "global::Downstream.Patch",
            static _ => "global::Downstream.ChangeSet",
            PayloadImplementationContainerPrefix: "DownstreamInternal"
        );

    private static SparseFragmentPatchEmitter.SparsePatchDialect StateDialect() =>
        PlainDialect() with
        {
            PathType = "global::Downstream.Path",
            PathIsValueType = true,
            TypedPathInterfaceType = "global::Downstream.ITypedPath",
            TransitionContracts = new SparseTransitionContracts(
                "global::Downstream.ValueChange",
                "global::Downstream.ChangeSetContract",
                "global::Downstream.KeyedChange",
                "global::Downstream.KeyedItemChange",
                "global::Downstream.TemporaryKeyedChange",
                "global::Downstream.DictionaryChange",
                "global::Downstream.DictionaryItemChange",
                "global::Downstream.StructuralDictionaryItemChange",
                "global::Downstream.ChangeRecord",
                "global::Downstream.ChangeKind"
            ),
            StateContracts = new SparseStateContracts(
                "global::Downstream.FieldState",
                "global::Downstream.CollectionState",
                "global::Downstream.StateQueries"
            ),
        };

    private static SparseDescriptorDialect DescriptorDialect() =>
        new(
            "global::Downstream.IDescriptor",
            "global::Downstream.IDescriptorSet",
            "global::Downstream.IArrayDescriptor",
            "global::Downstream.IDictDescriptor",
            "global::Downstream.ISetDescriptor",
            "global::Downstream.Descriptor",
            "global::Downstream.DescriptorSet",
            "global::Downstream.ArrayDescriptor",
            "global::Downstream.ArrayDescriptorAccess",
            "global::Downstream.DictionaryDescriptor",
            "global::Downstream.DictionaryDescriptorAccess",
            "global::Downstream.SetDescriptor",
            "global::Downstream.SetDescriptorAccess",
            "global::Downstream.DescriptorShape",
            "global::Downstream.DescriptorValue"
        );

    private static SparseSessionCapabilityContracts AllCapabilities() =>
        new(
            HistoryInterface: "Downstream.ISessionWithHistory",
            FieldStateInterface: "Downstream.ISessionWithFieldState",
            CollectionStateInterface: "Downstream.ISessionWithCollectionState",
            BatchEditInterface: "Downstream.ISessionWithBatchEdit",
            DescriptorSessionInterface: "Downstream.ISessionWithDescriptors",
            StateSnapshotInterface: "Downstream.ISessionWithStateSnapshot"
        );

    private static SparseFragmentPatchEmitter.SparsePatchDialect SnapshotDialect() =>
        StateDialect() with
        {
            StateContracts = new SparseStateContracts(
                "global::Downstream.FieldState",
                "global::Downstream.CollectionState",
                "global::Downstream.StateQueries"
            )
            {
                QueryViewType = "global::Downstream.QueryView",
            },
        };

    private static SparseGeneratorConfig ConfigFor(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseSessionCapabilityContracts? capabilities,
        bool withDescriptors
    ) =>
        new(
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
            PatchDialect: dialect,
            EditSessionInterfaceMetadataName: "PackageShared.ISparseEditSession",
            EditSessionDialect: new SparseEditSessionDialect(
                "Downstream.Generated",
                HistoryType: "global::Downstream.History",
                HistoryOptionsType: "global::Downstream.HistoryOptions",
                HistoryHooksType: "global::Downstream.HistoryHooks",
                HistoryContractType: "global::Downstream.IHistory"
            ),
            DescriptorDialect: withDescriptors ? DescriptorDialect() : null,
            SessionCapabilityContracts: capabilities
        );

    [Test]
    public void NullCapabilitiesLeaveBasesUnchanged()
    {
        var config = ConfigFor(StateDialect(), null, withDescriptors: true);

        SparseEditSessionCapabilityEmitter
            .AppendSessionCapabilityBases(
                "global::PackageShared.ISparseEditSession<global::Downstream.Order>",
                "global::Downstream.Order",
                config,
                StateDialect(),
                hasOwnedHistory: true
            )
            .ShouldBe("global::PackageShared.ISparseEditSession<global::Downstream.Order>");
    }

    [Test]
    public void AllAvailableCapabilitiesAreAppended()
    {
        var config = ConfigFor(StateDialect(), AllCapabilities(), withDescriptors: true);

        var bases = SparseEditSessionCapabilityEmitter.AppendSessionCapabilityBases(
            string.Empty,
            "global::Downstream.Order",
            config,
            StateDialect(),
            hasOwnedHistory: true
        );

        bases.ShouldBe(
            ", global::Downstream.ISessionWithHistory"
                + ", global::Downstream.ISessionWithFieldState<global::Downstream.Order>"
                + ", global::Downstream.ISessionWithCollectionState"
                + ", global::Downstream.ISessionWithBatchEdit"
                + ", global::Downstream.ISessionWithDescriptors"
        );
        bases.ShouldNotContain("SparseTypes.");
    }

    [Test]
    public void SnapshotCapabilityAppendedOnlyWhenViewTypeIsBound()
    {
        // Bound view type: the snapshot contract joins the bases.
        var bound = SparseEditSessionCapabilityEmitter.AppendSessionCapabilityBases(
            string.Empty,
            "global::Downstream.Order",
            ConfigFor(SnapshotDialect(), AllCapabilities(), withDescriptors: true),
            SnapshotDialect(),
            hasOwnedHistory: true
        );
        bound.ShouldContain(", global::Downstream.ISessionWithStateSnapshot");
        bound.ShouldNotContain("SparseTypes.");

        // Capability bound but no view type: the base stays off so products
        // without the view keep byte-identical output.
        var unbound = SparseEditSessionCapabilityEmitter.AppendSessionCapabilityBases(
            string.Empty,
            "global::Downstream.Order",
            ConfigFor(StateDialect(), AllCapabilities(), withDescriptors: true),
            StateDialect(),
            hasOwnedHistory: true
        );
        unbound.ShouldNotContain("StateSnapshot");
    }

    [Test]
    public void ModelSessionEmitsSnapshotMemberOnlyWhenViewTypeIsBound()
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

        using var bound = new SharedIndentedBuilder(CancellationToken.None);
        SparseEditSessionEmitter.AppendModelEditSession(
            bound,
            model,
            model.ModelTypeName,
            "public",
            "Observable",
            "ReadOnlyView",
            ImmutableArray<SparseMemberModel>.Empty,
            ConfigFor(SnapshotDialect(), AllCapabilities(), withDescriptors: true)
        );
        bound.ToString().ShouldContain("CaptureStateSnapshot");

        using var unbound = new SharedIndentedBuilder(CancellationToken.None);
        SparseEditSessionEmitter.AppendModelEditSession(
            unbound,
            model,
            model.ModelTypeName,
            "public",
            "Observable",
            "ReadOnlyView",
            ImmutableArray<SparseMemberModel>.Empty,
            ConfigFor(StateDialect(), AllCapabilities(), withDescriptors: true)
        );
        unbound.ToString().ShouldNotContain("CaptureStateSnapshot");
        unbound.ToString().ShouldNotContain("StateSnapshot");
    }

    [Test]
    public void UnavailableCapabilitiesAreOmitted()
    { // No state surface, no owned history, and no descriptors: only the
        // always-emitted batch capability applies.
        var config = ConfigFor(PlainDialect(), AllCapabilities(), withDescriptors: false);

        SparseEditSessionCapabilityEmitter
            .AppendSessionCapabilityBases(
                string.Empty,
                "global::Downstream.Order",
                config,
                PlainDialect(),
                hasOwnedHistory: false
            )
            .ShouldBe(", global::Downstream.ISessionWithBatchEdit");
    }

    [Test]
    public void ModelSessionEmitsCapabilityBasesOnlyWhenAvailable()
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
        var config = ConfigFor(StateDialect(), AllCapabilities(), withDescriptors: true);
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
        source.ShouldContain("global::Downstream.ISessionWithHistory");
        source.ShouldContain("global::Downstream.ISessionWithFieldState<global::Downstream.Order>");
        source.ShouldContain("global::Downstream.ISessionWithCollectionState");
        source.ShouldContain("global::Downstream.ISessionWithBatchEdit");
        source.ShouldContain("global::Downstream.ISessionWithDescriptors");
        source.ShouldNotContain("SparseTypes.");
    }

    [Test]
    public void ModelSessionWithoutCapabilitiesStaysByteIdentical()
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
        using var before = new SharedIndentedBuilder(CancellationToken.None);
        using var after = new SharedIndentedBuilder(CancellationToken.None);

        SparseEditSessionEmitter.AppendModelEditSession(
            before,
            model,
            model.ModelTypeName,
            "public",
            "Observable",
            "ReadOnlyView",
            ImmutableArray<SparseMemberModel>.Empty,
            ConfigFor(StateDialect(), null, withDescriptors: true)
        );
        SparseEditSessionEmitter.AppendModelEditSession(
            after,
            model,
            model.ModelTypeName,
            "public",
            "Observable",
            "ReadOnlyView",
            ImmutableArray<SparseMemberModel>.Empty,
            ConfigFor(StateDialect(), new SparseSessionCapabilityContracts(), withDescriptors: true)
        );

        after.ToString().ShouldBe(before.ToString());
    }

    [Test]
    public void RelocatedSessionEmitsSameCapabilityBases()
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
        var config = ConfigFor(StateDialect(), AllCapabilities(), withDescriptors: true);
        using var standard = new SharedIndentedBuilder(CancellationToken.None);
        using var relocated = new SharedIndentedBuilder(CancellationToken.None);

        SparseEditSessionEmitter.AppendModelEditSession(
            standard,
            model,
            model.ModelTypeName,
            "public",
            "Observable",
            "ReadOnlyView",
            ImmutableArray<SparseMemberModel>.Empty,
            config
        );
        SparseRelocatedEditSessionCore.Append(
            relocated,
            model,
            ImmutableArray<SparseMemberModel>.Empty,
            config,
            "global::Downstream.OrderObservable",
            "global::Downstream.OrderReadOnlyView"
        );

        var standardBases = DeclarationLine(standard.ToString());
        var relocatedBases = DeclarationLine(relocated.ToString());
        foreach (
            var capability in new[]
            {
                "global::Downstream.ISessionWithHistory",
                "global::Downstream.ISessionWithFieldState<global::Downstream.Order>",
                "global::Downstream.ISessionWithCollectionState",
                "global::Downstream.ISessionWithBatchEdit",
                "global::Downstream.ISessionWithDescriptors",
            }
        )
        {
            standardBases.ShouldContain(capability);
            relocatedBases.ShouldContain(capability);
        }
    }

    private static string DeclarationLine(string source) =>
        source
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .First(static line => line.Contains("public sealed class EditSession : "));
}
