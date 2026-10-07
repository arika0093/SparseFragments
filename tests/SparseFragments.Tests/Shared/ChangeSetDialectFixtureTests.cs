using System.Collections.Immutable;
using System.Threading;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>
/// Shared-emitter compatibility fixture (issue #98): proves the reusable
/// ChangeSet/STJ emitters can target a non-SparseFragments runtime
/// namespace/type family without hard-coded product dependencies.
/// </summary>
public sealed class ChangeSetDialectFixtureTests
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
            0,
            SparseCollectionInfo.Unsupported,
            null,
            null,
            false,
            true
        );
    }

    private static SparseMemberModel NestedMember(int id, string name)
    {
        var property = new SparsePropertyModel(
            name,
            ScalarType("global::Ns.Child?"),
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
            new SparseTypeModel(
                "global::Ns.Child?",
                "global::Ns.Child",
                "global::Ns.Child",
                IsReferenceType: true,
                IsFragmentModel: true,
                null,
                PatchApiPrefix: string.Empty
            ),
            1,
            SparseCollectionInfo.Unsupported,
            null,
            "global::Ns.Child.Fragment",
            false,
            true
        );
    }

    private static SparseMemberModel AppendMember(int id, string name)
    {
        var property = new SparsePropertyModel(
            name,
            ScalarType("global::System.Collections.Generic.List<string>"),
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: name,
            HasExplicitJsonPropertyName: false,
            JsonIgnoreCondition: 0
        );
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
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            element,
            null,
            null,
            SparseCollectionSemantic.ScalarSequence,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None
        );
        return new SparseMemberModel(id, property, null, 2, collection, null, null, false, true);
    }

    private static ImmutableArray<SparseMemberModel> FixtureMembers() =>
        ImmutableArray.Create(
            ScalarMember(0, "Name", "global::System.String?"),
            NestedMember(2, "Child"),
            AppendMember(5, "Tags")
        );

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
            static member => "global::Downstream.Delta_" + member.Id
        );

    private static string EmitChangeSet(
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseChangeSetEmitter.AppendChangeSet(code, members, dialect);
        return code.ToString();
    }

    [Test]
    public void Standalone_EmitsSparseFragmentsRuntime()
    {
        var text = EmitChangeSet(FixtureMembers(), SparseFragmentPatchEmitter.StandaloneDialect());
        text.ShouldContain("global::SparseFragments.Optional<");
        text.ShouldContain("global::SparseFragments.RebaseResult<ChangeSet>");
        text.ShouldContain("global::SparseFragments.SparsePatchConflict");
        text.ShouldContain("global::SparseFragments.CompilerServices.SparseFragmentRuntime");
    }

    [Test]
    public void Downstream_ContainsNoSparseFragmentsRuntime()
    {
        var text = EmitChangeSet(FixtureMembers(), DownstreamDialect());
        text.ShouldNotContain("global::SparseFragments");
        text.ShouldContain("global::Downstream.Optional<");
        text.ShouldContain("global::Downstream.DownstreamRebase<ChangeSet>");
        text.ShouldContain("global::Downstream.DownstreamConflict");
        text.ShouldContain("global::Downstream.DownstreamConflictKind");
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime");
        // Nested naming is supplied by the dialect, not hard-coded.
        text.ShouldContain("global::Downstream.Delta_2");
    }

    [Test]
    public void Downstream_ChangeSetStjContainsNoSparseFragmentsRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparsePatchStjEmitter.AppendChangeSetStj(code, FixtureMembers(), DownstreamDialect());
        var text = code.ToString();
        text.ShouldNotContain("global::SparseFragments");
        text.ShouldContain("global::Downstream.Optional<");
        text.ShouldContain("global::Downstream.Delta_2");
    }

    [Test]
    public void Downstream_PatchStjContainsNoSparseFragmentsRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparsePatchStjEmitter.AppendPatchStj(code, FixtureMembers(), DownstreamDialect());
        var text = code.ToString();
        text.ShouldNotContain("global::SparseFragments");
        text.ShouldContain("global::Downstream.FragmentOperation<");
    }
}
