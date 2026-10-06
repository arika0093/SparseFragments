using System.Collections.Immutable;
using System.Threading;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>
/// Direct unit tests for the product-neutral semantic Between emitter
/// (issue #31): root transitions, scalar changes, nested members, unchanged
/// members and explicit nulls.
/// </summary>
public sealed class SemanticBetweenEmitterTests
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

    private static SparseBetweenDialect ProductDialect(string prefix = "") =>
        new(
            "global::SparseFragments.",
            "global::SparseFragments.Optional<Fragment?>",
            "global::SparseFragments.FragmentOperation<Fragment?>",
            "__sparse_whole",
            prefix,
            static member => "__sparse_patch_member_" + member.Id,
            static member => member.ChildModel is null,
            static member => member.Property.Type.Name,
            static (member, before, after) => "AreEqual(" + before + ", " + after + ")",
            static (member, before, after) =>
                member.ChildFragmentType!.Replace(".Fragment", ".Patch", StringComparison.Ordinal)
                + ".Between("
                + before
                + ", "
                + after
                + ")"
        );

    private static string Emit(
        ImmutableArray<SparseMemberModel> members,
        SparseBetweenDialect dialect
    )
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        SparseSemanticBetweenEmitter.AppendBetweenMethod(code, members, dialect);
        return code.ToString();
    }

    [Test]
    public void Between_EmitsWholeStateTransitions()
    {
        var text = Emit(ImmutableArray<SparseMemberModel>.Empty, ProductDialect());
        text.ShouldContain(
            "public static Patch Between(global::SparseFragments.Optional<Fragment?> before, global::SparseFragments.Optional<Fragment?> after)"
        );
        text.ShouldContain("if (before.IsPresent != after.IsPresent)");
        text.ShouldContain(
            "patch.__sparse_whole = after.IsPresent ? global::SparseFragments.FragmentOperation<Fragment?>.Set(after.Value) : global::SparseFragments.FragmentOperation<Fragment?>.Unset;"
        );
        text.ShouldContain("if (!before.IsPresent) return patch;");
        text.ShouldContain(
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        // Explicit nulls collapse to a whole assignment.
        text.ShouldContain("if (before.Value is null || after.Value is null)");
        text.ShouldContain(
            "patch.__sparse_whole = global::SparseFragments.FragmentOperation<Fragment?>.Set(after.Value);"
        );
        text.ShouldContain("return patch;");
    }

    [Test]
    public void Between_EmitsScalarPresenceAndValueChanges()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "Name", "global::System.String?"),
            ScalarMember(1, "Count", "int")
        );
        var text = Emit(members, ProductDialect());
        text.ShouldContain("patch.__sparse_patch_member_0 = !afterFragment.Name.IsPresent");
        text.ShouldContain(
            "global::SparseFragments.FragmentOperation<global::System.String?>.Unset"
        );
        text.ShouldContain("AreEqual(beforeFragment.Name.Value, afterFragment.Name.Value)");
        text.ShouldContain(
            "global::SparseFragments.FragmentOperation<global::System.String?>.Set(afterFragment.Name.Value)"
        );
        text.ShouldContain("patch.__sparse_patch_member_1 = !afterFragment.Count.IsPresent");
        text.ShouldContain(
            "global::SparseFragments.FragmentOperation<int>.Set(afterFragment.Count.Value)"
        );
    }

    [Test]
    public void Between_DelegatesNestedMembersToDialect()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "Name", "global::System.String?"),
            NestedMember(2, "Child")
        );
        var text = Emit(members, ProductDialect());
        text.ShouldContain(
            "patch.__sparse_patch_member_2 = global::Ns.Child.Patch.Between(beforeFragment.Child, afterFragment.Child);"
        );
    }

    [Test]
    public void Between_HonorsCustomDialectSurface()
    {
        var members = ImmutableArray.Create(ScalarMember(0, "Name", "global::System.String?"));
        var dialect = ProductDialect() with
        {
            RuntimeNamespace = "global::Downstream.",
            WholeOperationType = "global::Downstream.WholeOp",
            WholeFieldName = "__whole",
            MethodPrefix = "Sparse",
            MemberField = static member => "m_" + member.Id,
        };
        var text = Emit(members, dialect);
        text.ShouldContain(
            "public static Patch SparseBetween(global::SparseFragments.Optional<Fragment?> before, global::SparseFragments.Optional<Fragment?> after)"
        );
        text.ShouldContain(
            "patch.__whole = after.IsPresent ? global::Downstream.WholeOp.Set(after.Value)"
        );
        text.ShouldContain("patch.m_0 = !afterFragment.Name.IsPresent");
        text.ShouldContain("global::Downstream.FragmentOperation<global::System.String?>.Set(");
    }
}
