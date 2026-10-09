using System.Collections.Immutable;
using System.Threading;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Semantic-role reference tests (#179): shared generation derives child
// and nested type references from product-owned family bindings through
// SparseSemanticReference instead of rebuilding product literals in
// emitters. The standalone vocabulary stays the default; a downstream
// product binds its own names and only its names appear in references.
public sealed class SemanticReferenceTests
{
    private static readonly SparseFamilyNames CustomFamily = new(
        Fragment: "State",
        FragmentBuilder: "StateBuilder",
        Patch: "Operation",
        ChangeSet: "Delta",
        ChangePayload: "Envelope",
        Observable: "View",
        ReadOnlyView: "Snapshot"
    );

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
            "global::Ns.Child.State",
            false,
            true
        );
    }

    private static SparseMemberModel FragmentSequenceMember()
    {
        var element = new SparseTypeModel(
            "global::Ns.Child",
            "global::Ns.Child",
            "global::Ns.Child",
            IsReferenceType: true,
            IsFragmentModel: true,
            null,
            PatchApiPrefix: string.Empty,
            UsesDefaultScalarEquality: true
        );
        var property = new SparsePropertyModel(
            "Children",
            ScalarType("global::System.Collections.Generic.List<global::Ns.Child>"),
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: "Children",
            HasExplicitJsonPropertyName: false,
            JsonIgnoreCondition: 0
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            element,
            null,
            "System.Collections.Generic.List<T>",
            SparseCollectionSemantic.ScalarSequence,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None
        );
        return new SparseMemberModel(
            3,
            property,
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
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

    private static SparseFragmentPatchEmitter.SparsePatchDialect CustomDialect() =>
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
                    ? CustomFamily.Patch
                    : SparseSemanticReference.ChildPatchType(
                        member.ChildFragmentType,
                        CustomFamily
                    ),
            static member =>
                SparseSemanticReference.ChildChangeSetType(member.ChildFragmentType!, CustomFamily),
            PayloadImplementationContainerPrefix: "DownstreamInternal"
        );

    [Test]
    public void StandaloneDerivationsPreserveTheExistingVocabulary()
    {
        var standalone = SparseFamilyNames.Standalone;
        SparseSemanticReference
            .ChildFragmentType("global::Ns.Child", standalone)
            .ShouldBe("global::Ns.Child.Fragment");
        SparseSemanticReference
            .ChildPatchType("global::Ns.Child.Fragment", standalone)
            .ShouldBe("global::Ns.Child.Patch");
        SparseSemanticReference
            .ChildChangeSetType("global::Ns.Child.Fragment", standalone)
            .ShouldBe("global::Ns.Child.ChangeSet");
        SparseFragmentPatchEmitter
            .ChildPatch(
                NestedMember(2, "Child") with
                {
                    ChildFragmentType = "global::Ns.Child.Fragment",
                }
            )
            .ShouldBe("global::Ns.Child.Patch");
        SparseFragmentPatchEmitter
            .DefaultChildChangeSet(
                NestedMember(2, "Child") with
                {
                    ChildFragmentType = "global::Ns.Child.Fragment",
                }
            )
            .ShouldBe("global::Ns.Child.ChangeSet");
    }

    [Test]
    public void CustomFamilyDerivesChildRolesWithoutProductLiterals()
    {
        SparseSemanticReference
            .ChildFragmentType("global::Ns.Child", CustomFamily)
            .ShouldBe("global::Ns.Child.State");
        SparseSemanticReference
            .ChildPatchType("global::Ns.Child.State", CustomFamily)
            .ShouldBe("global::Ns.Child.Operation");
        SparseSemanticReference
            .ChildChangeSetType("global::Ns.Child.State", CustomFamily)
            .ShouldBe("global::Ns.Child.Delta");
        // A mapped name without the state suffix gains the role name
        // instead of losing characters to a blind suffix strip.
        SparseSemanticReference
            .ChildPatchType("global::Ns.Legacy", CustomFamily)
            .ShouldBe("global::Ns.Legacy.Operation");
    }

    [Test]
    public void UiRootNamesDodgeCollisionsUnderAnyVocabulary()
    {
        SparseObservableEmitter
            .ObservableTypeName(
                ImmutableArray.Create(ScalarMember(0, "View", "global::System.String?")),
                CustomFamily
            )
            .ShouldBe("SparseView");
        SparseReadOnlyViewEmitter
            .ReadOnlyViewTypeName(
                ImmutableArray.Create(ScalarMember(0, "Name", "global::System.String?")),
                CustomFamily
            )
            .ShouldBe("Snapshot");
        // The discovery default keeps the standalone vocabulary.
        SparseObservableEmitter
            .ObservableTypeName(ImmutableArray<SparseMemberModel>.Empty)
            .ShouldBe("Observable");
    }

    [Test]
    public void EmittedEqualityNamesOnlyTheBoundStateRole()
    {
        var expressions = new SparseFragmentExpressions(
            "global::Downstream.CloneContext",
            "global::Downstream.Comparers",
            "global::Downstream.Mergers",
            "global::Downstream.Optional",
            CustomFamily
        );
        var text = expressions.ValueEqualityExpression(
            FragmentSequenceMember(),
            "__left",
            "__right"
        );
        text.ShouldContain("global::Ns.Child.State.__SparseAreEqual");
        text.ShouldContain("global::Downstream.Comparers.AreSequenceEqual");
        text.ShouldNotContain("Child.Fragment");
        text.ShouldNotContain("global::SparseFragments");
    }

    [Test]
    public void FullPatchBindsCustomChildRolesAndRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseFragmentPatchEmitter.AppendPatch(
            code,
            "global::Ns.Model",
            ImmutableArray.Create(
                ScalarMember(0, "Name", "global::System.String?"),
                NestedMember(2, "Child")
            ),
            dialect: CustomDialect()
        );
        var text = code.ToString();
        text.ShouldContain("global::Ns.Child.State?");
        text.ShouldContain("global::Ns.Child.Operation");
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime");
        text.ShouldNotContain("Child.Fragment");
        text.ShouldNotContain("Child.Patch");
        text.ShouldNotContain("global::SparseFragments");
    }

    [Test]
    public void FullChangeSetBindsCustomChildRolesAndRuntime()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseChangeSetEmitter.AppendChangeSet(
            code,
            ImmutableArray.Create(
                ScalarMember(0, "Name", "global::System.String?"),
                NestedMember(2, "Child"),
                AppendMember(5, "Tags")
            ),
            CustomDialect(),
            "global::Ns.Model"
        );
        var text = code.ToString();
        text.ShouldContain("global::Ns.Child.Delta");
        text.ShouldContain("global::Downstream.DownstreamRebase<");
        text.ShouldContain("global::Downstream.CompilerServices.DownstreamRuntime");
        text.ShouldNotContain("Child.Fragment");
        text.ShouldNotContain("Child.ChangeSet");
        text.ShouldNotContain("global::SparseFragments");
    }

    [Test]
    public void EmittedTypeNamesFollowTheBoundVocabulary()
    {
        var standalone = SparseEmissionFeatures.Standalone.GetEmittedTypeNames();
        standalone.ShouldContain("Fragment");
        standalone.ShouldContain("Patch");
        var custom = SparseEmissionFeatures.Standalone.GetEmittedTypeNames(CustomFamily);
        custom.ShouldContain("State");
        custom.ShouldContain("Operation");
        custom.ShouldContain("Delta");
        custom.ShouldContain("Envelope");
        custom.ShouldNotContain("Fragment");
        custom.ShouldNotContain("Patch");
    }
}
