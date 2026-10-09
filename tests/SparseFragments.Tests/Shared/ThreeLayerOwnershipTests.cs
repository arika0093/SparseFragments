using System.Collections.Immutable;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Decision-criteria tests for the three-layer ownership design (#177).
// Each test pins one testable placement criterion from
// docs/architecture/three-layer-ownership.md so later migrations (#178,
// #179, #181-#188) cannot silently shift the Runtime, Generated-Once,
// and Per-Model boundaries.
public sealed class ThreeLayerOwnershipTests
{
    private static SparseTypeModel ScalarType(string name) =>
        new(name, name, name, IsReferenceType: true, IsFragmentModel: false, null);

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

    private static SparseMemberModel SetMember(int id, string name)
    {
        var element = new SparseTypeModel(
            "string",
            "string",
            "string",
            IsReferenceType: true,
            IsFragmentModel: false,
            null,
            PatchApiPrefix: string.Empty,
            UsesDefaultScalarEquality: true
        );
        var property = new SparsePropertyModel(
            name,
            ScalarType("global::System.Collections.Generic.HashSet<string>"),
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: name,
            HasExplicitJsonPropertyName: false,
            JsonIgnoreCondition: 0
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.Set,
            SparseCloneCollectionKind.Set,
            element,
            null,
            "System.Collections.Generic.HashSet<T>",
            SparseCollectionSemantic.ScalarSequence,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None
        );
        return new SparseMemberModel(
            id,
            property,
            null,
            SparseMergeModes.SetUnion,
            collection,
            null,
            null,
            false,
            true
        );
    }

    private static SparseFragmentPatchEmitter.SparsePatchDialect Dialect(
        string prefix = "DownstreamInternal"
    ) =>
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
            PayloadImplementationContainerPrefix: prefix
        );

    // C3: shared helpers stay parameterizable by dialect-supplied contracts.
    // Equality emission must name only the injected comparer and optional
    // types, never a fixed product namespace.
    [Test]
    public void EqualityUsesOnlyInjectedComparerContracts()
    {
        var expressions = new SparseFragmentExpressions(
            "global::Downstream.CloneContext",
            "global::Downstream.Comparers",
            "global::Downstream.Mergers",
            "global::Downstream.Optional"
        );
        var text = expressions.ValueEqualityExpression(
            ScalarMember(0, "Name", "global::System.String?"),
            "__left",
            "__right"
        );
        text.ShouldContain("global::Downstream.Comparers.AreEqual(__left, __right)");
        text.ShouldNotContain("global::SparseFragments");
    }

    // C3: collection merge emission must name only the injected merger.
    [Test]
    public void CollectionMergeUsesOnlyInjectedMergerContract()
    {
        var expressions = new SparseFragmentExpressions(
            "global::Downstream.CloneContext",
            "global::Downstream.Comparers",
            "global::Downstream.Mergers",
            "global::Downstream.Optional"
        );
        var text = expressions.BuildCollectionMerge(SetMember(1, "Tags"), "__lower", "__higher");
        text.ShouldContain("global::Downstream.Mergers.MergeSet<string>(__lower, __higher)");
        text.ShouldNotContain("global::SparseFragments");
    }

    // Visibility rule: the payload DTO container prefix is explicit with no
    // Shared fallback. Missing configuration fails fast instead of falling
    // back to a product namespace.
    [Test]
    public void PayloadContainerPrefixHasNoImplicitFallback()
    {
        Should.Throw<InvalidOperationException>(() =>
            SparseChangeSetPayloadEmitter.RequirePayloadContainerName(
                Dialect(prefix: null!),
                "global::Ns.Model"
            )
        );
        Should.Throw<InvalidOperationException>(() =>
            SparseChangeSetPayloadEmitter.RequirePayloadContainerName(
                Dialect(prefix: "  "),
                "global::Ns.Model"
            )
        );
        var container = SparseChangeSetPayloadEmitter.RequirePayloadContainerName(
            Dialect(),
            "global::Ns.Model"
        );
        container.ShouldStartWith("DownstreamInternal_");
        container.ShouldNotContain("SparseFragments");
    }

    // C2: feature selection keeps its dependency chain so a product cannot
    // emit a transition family without the state family it projects.
    [Test]
    public void EmissionPlanValidationPinsFamilyDependencies()
    {
        var coherent = new SparseEmissionFeatures(
            EmitFragment: true,
            EmitPatch: true,
            EmitChangeSet: true,
            EmitChangePayload: true,
            EmitObservable: true,
            EmitJsonConverters: true
        );
        coherent.ValidateDependencies().Length.ShouldBe(0);
        var patchWithoutFragment = new SparseEmissionFeatures(
            EmitFragment: false,
            EmitPatch: true,
            EmitChangeSet: false,
            EmitChangePayload: false,
            EmitObservable: false,
            EmitJsonConverters: false
        );
        patchWithoutFragment
            .ValidateDependencies()
            .ShouldContain("EmitPatch requires EmitFragment.");
        var payloadWithoutChangeSet = new SparseEmissionFeatures(
            EmitFragment: true,
            EmitPatch: true,
            EmitChangeSet: false,
            EmitChangePayload: true,
            EmitObservable: false,
            EmitJsonConverters: false
        );
        payloadWithoutChangeSet
            .ValidateDependencies()
            .ShouldContain("EmitChangePayload requires EmitChangeSet.");
    }
}
