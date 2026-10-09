using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

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
        core.ShouldNotContain("SparseFragments.");
        current.ShouldContain("namespace Downstream.Generated");
        current.ShouldContain("global::Downstream.Optional<");
        current.ShouldNotContain("SparseFragments.");
    }
}
