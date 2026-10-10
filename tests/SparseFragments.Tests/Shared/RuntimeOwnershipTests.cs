using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using SparseFragments.CompilerServices;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Ownership tests for the generated-to-runtime seam (#186). The facade
// surface is pinned by name so relocations (#178, #181-#183) change it
// deliberately, and emission tests prove every remaining helper flows
// through injected dialect contracts with no fixed product reference.
public sealed class RuntimeOwnershipTests
{
    private static readonly ImmutableArray<string> ExpectedFacadeMembers = ImmutableArray.Create(
        "AreEqual",
        "AreSequenceEqual",
        "AreSetEqual",
        "AreDictionaryEqual",
        "MergeAppendList",
        "MergeDistinctArray",
        "MergeDistinctList",
        "MergeSet",
        "TryRebaseAppend",
        "TryRebaseSetUnion",
        "TryRebaseSequenceAppend",
        "TryRebaseSequenceAppendArray",
        "TryRebaseSequenceSetUnion",
        "TryRebaseSequenceSetUnionArray",
        "CreateCloneContext",
        "CreateFromCycleContext",
        "CreateDiffCycleContext",
        "EnsureUniqueKeys",
        "KeyOrderEquals",
        "TryExplainCollectionProvenance",
        "TryExplainSetProvenance",
        "MergeAppendOrigins",
        "MergeSetUnionOrigins",
        "MergeSetOrigins"
    );

    private static SparseTypeModel ScalarType(string name) =>
        new(name, name, name, IsReferenceType: true, IsFragmentModel: false, null);

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

    private static SparseFragmentPatchEmitter.SparsePatchDialect DownstreamDialect() =>
        new(
            "global::Downstream.",
            "__sparse_whole",
            "__SparseMembersEmpty",
            static member => "__sparse_patch_member_" + member.Id,
            static _ => string.Empty,
            "Apply",
            false,
            "global::Downstream.CompilerServices.DownstreamRuntime",
            "global::Downstream.DownstreamConflict",
            "global::Downstream.DownstreamConflictKind",
            static payload => "global::Downstream.DownstreamRebase<" + payload + ">",
            static member =>
                member.ChildFragmentType is null
                    ? "Patch"
                    : member.ChildFragmentType.Replace(
                        ".Fragment",
                        ".Patch",
                        StringComparison.Ordinal
                    ),
            static member => "global::Downstream.Delta_" + member.Id,
            PayloadImplementationContainerPrefix: "DownstreamInternal"
        );

    // Every facade member has a documented layer owner in
    // docs/architecture/runtime-ownership-audit.md. Pin the surface so
    // relocations change it deliberately instead of silently.
    // 40 = 25 audited in #186 + 12 statically specialized comparison
    // overloads added in #187 (same member names, concrete collection shapes)
    // + 3 live origin-attribution helpers added in #204.
    [Test]
    public void FacadeSurfaceMatchesTheAuditedContract()
    {
        var methods = typeof(SparseFragmentRuntime).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
        );
        methods.Length.ShouldBe(40);
        methods
            .Select(static method => method.Name)
            .Distinct()
            .OrderBy(static name => name)
            .ToImmutableArray()
            .ShouldBe(ExpectedFacadeMembers.OrderBy(static name => name).ToImmutableArray());
    }

    [Test]
    public void FacadeStaysHiddenFromHandWrittenCallers()
    {
        typeof(SparseFragmentRuntime)
            .GetCustomAttribute<EditorBrowsableAttribute>()
            ?.State.ShouldBe(EditorBrowsableState.Never);
    }

    // Clone contexts are created through the injected reference comparer,
    // never a fixed runtime type.
    [Test]
    public void CloneContextFlowsThroughTheInjectedComparer()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseFragmentEmitHelpers.AppendCloneContext(
            code,
            1,
            "__sparse_clone_context",
            "global::Downstream.ReferenceComparer"
        );
        var text = code.ToString();
        text.ShouldContain(
            "var __sparse_clone_context = global::Downstream.ReferenceComparer.CreateCloneContext();"
        );
        text.ShouldNotContain("global::SparseFragments");
    }

    // Keyed primitives resolve through the configured facade type.
    [Test]
    public void KeyedPrimitivesFlowThroughTheConfiguredFacade()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseKeyedSequenceSurfaceEmitter.EmitKeyedSequencePatch(
            code,
            KeyedMember(),
            DownstreamDialect()
        );
        var text = code.ToString();
        text.ShouldContain(
            "global::Downstream.CompilerServices.DownstreamRuntime.EnsureUniqueKeys<"
        );
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime.KeyOrderEquals<");
        text.ShouldNotContain("global::SparseFragments");
    }
}
