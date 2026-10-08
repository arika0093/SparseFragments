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
            SparseMergeModes.Replace,
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
            SparseMergeModes.Deep,
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
        return new SparseMemberModel(
            id,
            property,
            null,
            SparseMergeModes.Append,
            collection,
            null,
            null,
            false,
            true
        );
    }

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

    private static SparseMemberModel DictionaryMember()
    {
        var key = ScalarType("global::System.Int32");
        var value = ScalarType("global::System.String");
        var dictionary = ScalarType(
            "global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.String>"
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.Unsupported,
            SparseCloneCollectionKind.Dictionary,
            key,
            value,
            "System.Collections.Generic.Dictionary<TKey, TValue>",
            SparseCollectionSemantic.Dictionary,
            ImmutableArray<string>.Empty,
            "global::System.Int32",
            SparseKeyKind.None
        );
        return new SparseMemberModel(
            5,
            new SparsePropertyModel("Values", dictionary, JsonPropertyName: "Values"),
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            false,
            true
        );
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
        // Provisional wire version comes from the dialect, defaulting to 0.1.
        text.ShouldContain("\"0.1\"");
        text.ShouldContain("JsonUnmappedMemberHandling.Disallow");
        text.ShouldContain("FromPayloadCore");
        text.ShouldContain("ToChangeSetCore");
        // Baseline advancement is validated sparse before-state plus patch
        // projection, with no product runtime fallback.
        text.ShouldContain("ApplyToBaseline");
        text.ShouldContain("__SparseBeforeMatches(baseline)");
    }

    [Test]
    public void Downstream_PayloadVersionIsDialectOwned()
    {
        var text = EmitChangeSet(
            FixtureMembers(),
            DownstreamDialect() with
            {
                ChangeSetPayloadVersion = "9.9",
            }
        );
        text.ShouldContain("\"9.9\"");
        text.ShouldNotContain("\"0.1\"");
        text.ShouldNotContain("global::SparseFragments");
    }

    [Test]
    public void Downstream_KeyedCollectionPatchContainsNoSparseFragmentsRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseKeyedSequenceSurfaceEmitter.EmitKeyedSequencePatch(
            code,
            KeyedMember(),
            DownstreamDialect()
        );
        var text = code.ToString();
        text.ShouldNotContain("global::SparseFragments");
        text.ShouldContain("global::Downstream.DownstreamRebase<ItemsPatch>");
        text.ShouldContain("global::Downstream.DownstreamConflict");
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime");
    }

    [Test]
    public void Downstream_DictionaryPatchContainsNoSparseFragmentsRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseDictionaryPatchEmitter.EmitDictionaryPatch(
            code,
            DictionaryMember(),
            DownstreamDialect()
        );
        var text = code.ToString();
        text.ShouldNotContain("global::SparseFragments");
        text.ShouldContain("global::Downstream.DownstreamRebase<ValuesPatch>");
        text.ShouldContain("global::Downstream.DownstreamConflict");
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime");
    }

    [Test]
    public void Downstream_FullPatchContainsNoSparseFragmentsRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseFragmentPatchEmitter.AppendPatch(
            code,
            "global::Ns.Model",
            FixtureMembers(),
            dialect: DownstreamDialect()
        );
        var text = code.ToString();
        text.ShouldNotContain("global::SparseFragments");
        text.ShouldContain("global::Downstream.Optional<");
        text.ShouldContain("global::Downstream.DownstreamRebase<Patch>");
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime");
    }
}
