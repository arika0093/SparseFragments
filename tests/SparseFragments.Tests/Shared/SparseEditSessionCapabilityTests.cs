using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>Capability-driven session-core emission regressions (issue #184).</summary>
public sealed class SparseEditSessionCapabilityTests
{
    private static SparseGeneratorConfig Config(
        SparseEditSessionDialect? session = null,
        SparseRuntimeDialect? runtime = null,
        SparseFragmentPatchEmitter.SparsePatchDialect? patch = null,
        SparseEmissionFeatures? features = null
    ) =>
        new(
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
            HintNameSuffix: ".Downstream.g.cs",
            PromotedHintNameSuffix: ".DownstreamPromoted.g.cs",
            StructuralHostPrefix: "__DownstreamHost_",
            RuntimeDialect: runtime,
            PatchDialect: patch,
            EmissionFeatures: features,
            EditSessionDialect: session
        );

    private static SparseRuntimeDialect Runtime(string ns) =>
        new(
            "global::" + ns + ".",
            "global::" + ns + ".Optional",
            "global::" + ns + ".MergeStrategy",
            "global::" + ns + ".Runtime",
            "global::" + ns + ".Runtime",
            "global::" + ns + ".Runtime",
            "global::" + ns + ".Runtime",
            "__ns_merge_"
        );

    private static SparseFragmentPatchEmitter.SparsePatchDialect Patch(string ns) =>
        new(
            "global::" + ns + ".",
            "__sparse_whole",
            "__SparseMembersEmpty",
            static member => "__sparse_patch_member_" + member.Id,
            static _ => string.Empty,
            "Apply",
            false,
            "global::" + ns + ".Runtime",
            "global::" + ns + ".Conflict",
            "global::" + ns + ".ConflictKind",
            payload => "global::" + ns + ".Rebase<" + payload + ">",
            _ => "global::" + ns + ".Patch",
            _ => "global::" + ns + ".ChangeSet",
            PayloadImplementationContainerPrefix: "DownstreamInternal"
        );

    [Test]
    public void NoModels_RequestNothing()
    {
        SparseEditSessionCapabilities
            .ForCompilation(hasSessionModels: false, needsCurrentView: true)
            .ShouldBe(SparseEditSessionCapability.None);
    }

    [Test]
    public void ManyModels_DeDuplicateToOneCapability()
    {
        var first = SparseEditSessionCapabilities.ForCompilation(true, true);
        var second = SparseEditSessionCapabilities.ForCompilation(true, true);
        first.ShouldBe(second);
        SparseEditSessionCapabilities.NeedsCore(first).ShouldBeTrue();
        SparseEditSessionCapabilities.NeedsWithCurrent(first).ShouldBeTrue();
    }

    [Test]
    public void WithoutCurrentView_RequestCoreOnly()
    {
        var capability = SparseEditSessionCapabilities.ForCompilation(true, false);
        capability.ShouldBe(SparseEditSessionCapability.Core);
        SparseEditSessionCapabilities.NeedsCore(capability).ShouldBeTrue();
        SparseEditSessionCapabilities.NeedsWithCurrent(capability).ShouldBeFalse();
    }

    [Test]
    public void MissingDialects_AreActionable()
    {
        var config = Config();
        var errors = SparseEditSessionCapabilities.ValidatePrerequisites(
            config,
            SparseEditSessionCapability.All
        );
        errors.ShouldNotBeEmpty();
        string.Join(" ", errors).ShouldContain("EditSessionDialect");
    }

    [Test]
    public void MissingPatchFamily_FailsAdapterValidation()
    {
        var config = Config(
            new SparseEditSessionDialect("Downstream.Generated"),
            Runtime("Downstream"),
            Patch("Downstream"),
            new SparseEmissionFeatures(EmitPatch: false, EmitChangeSet: false)
        );
        var errors = SparseEditSessionCapabilities.ValidatePrerequisites(
            config,
            SparseEditSessionCapability.All
        );
        errors.ShouldNotBeEmpty();
        string.Join(" ", errors).ShouldContain("EmitPatch");
    }

    [Test]
    public void DistinctStateTransitionDialect_RendersWithoutProductLeakage()
    {
        // Downstream state/transition families use distinct names and runtime
        // primitives; the reusable cores stay generic over those roles.
        var (core, current) = SparseEditSessionEmitter.RenderCoreSources(
            new SparseEditSessionDialect("Acme.Generated"),
            Runtime("Acme"),
            Patch("Acme")
        );

        core.ShouldContain("namespace Acme.Generated");
        core.ShouldContain("global::Acme.Optional<");
        core.ShouldContain("global::Acme.Conflict");
        core.ShouldContain("global::Acme.Rebase<TChangeSet>");
        core.ShouldNotContain("SparseFragments.");
        current.ShouldContain("namespace Acme.Generated");
        current.ShouldNotContain("SparseFragments.");
        SparseEditSessionAdapterContract.RequiredMembers.Length.ShouldBeGreaterThan(0);
    }
}
