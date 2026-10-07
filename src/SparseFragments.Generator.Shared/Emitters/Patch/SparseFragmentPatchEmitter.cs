using System;
using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits typed standalone mutations without source routing or ownership.</summary>
/// <remarks>
/// This type owns the shared patch vocabulary (dialect, naming, whole-operation
/// helpers) and orchestrates the patch stages. Member emission, patch algebra
/// and rebase each live in their own focused emitter.
/// </remarks>
internal static class SparseFragmentPatchEmitter
{
    internal const string Runtime = "global::SparseFragments.";

    internal static string Field(SparseMemberModel member) => "__sparse_patch_member_" + member.Id;

    internal static string ValueType(SparseMemberModel member) =>
        member.ChildModel is null ? member.Property.Type.Name : member.ChildFragmentType + "?";

    internal static string ChildPatch(SparseMemberModel member) =>
        member.ChildFragmentType!.Substring(0, member.ChildFragmentType.Length - "Fragment".Length)
        + "Patch";

    internal static bool IsCollectionPatch(SparseMemberModel member) =>
        SparseKeyedCollectionEmitter.IsCollectionPatch(member);

    internal static string CollectionPatch(SparseMemberModel member) =>
        SparseKeyedCollectionEmitter.CollectionPatchName(member);

    internal static readonly SparseFragmentExpressions Expressions = new("__sparse_patch_context");

    public static void AppendFragmentMethods(SharedIndentedBuilder code, string modelType)
    {
        _ = modelType;
        code.AppendLineAt(2, "public Patch ToPatch() => new(this);");
        code.AppendLineAt(2, "public Fragment Apply(Patch patch)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
        );
        code.AppendLineAt(
            3,
            "var result = patch.Apply(" + Runtime + "Optional<Fragment?>.Present(this));"
        );
        code.AppendLineAt(
            3,
            "if (!result.IsPresent || result.Value is null) throw new global::System.InvalidOperationException(\"Apply a whole-contribution null or unset operation through Patch.Apply to preserve its optional state.\");"
        );
        code.AppendLineAt(3, "return result.Value;");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Small dialect for shared patch-core emission (whole, empty, ctor, apply).</summary>
    /// <remarks>
    /// Only genuinely-shared algebra lives here: runtime names plus field/contract hooks.
    /// Routing, replacement, and facade contracts stay in the product generators.
    /// </remarks>
    internal readonly record struct SparsePatchDialect(
        string RuntimeNamespace,
        string WholeFieldName,
        string MembersEmptyName,
        Func<SparseMemberModel, string> MemberField,
        Func<SparseMemberModel, string> NestedContract,
        string NestedApplyMethod,
        bool CastNestedApply
    );

    internal static SparsePatchDialect StandaloneDialect() =>
        new(
            Runtime,
            "__sparse_whole",
            "__SparseMembersEmpty",
            Field,
            static _ => string.Empty,
            "Apply",
            false
        );

    internal static string Operation(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "FragmentOperation";

    internal static string Kind(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "FragmentOperationKind";

    internal static string OptionalFragment(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "Optional<Fragment?>";

    internal static string MembersEmptyExpression(
        ImmutableArray<SparseMemberModel> members,
        SparsePatchDialect dialect
    )
    {
        if (members.IsEmpty)
            return "true";
        return string.Join(
            " && ",
            members.Select(member => MemberEmptyExpression(member, dialect))
        );
    }

    private static string MemberEmptyExpression(
        SparseMemberModel member,
        SparsePatchDialect dialect
    )
    {
        if (member.ChildModel is not null || IsCollectionPatch(member))
        {
            return "("
                + dialect.MemberField(member)
                + " is null || "
                + dialect.MemberField(member)
                + ".__SparseIsEmpty())";
        }

        return dialect.MemberField(member) + ".Kind == " + Kind(dialect) + ".Unchanged";
    }

    public static void AppendPatch(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool emitJsonBridge = true
    )
    {
        var optional = Runtime + "Optional<Fragment?>";
        var patchPrefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        SparsePatchStjEmitter.AppendPatchConverterAttribute(code);
        code.AppendLineAt(1, "public sealed class Patch");
        code.AppendLineAt(1, "{");
        SparseKeyedCollectionEmitter.EmitCollectionPatches(code, members);
        SparseFragmentPatchCoreEmitter.AppendPatchMembers(code, members, Runtime, Field);
        var dialect = StandaloneDialect();
        SparseFragmentPatchCoreEmitter.AppendPatchWholeOperations(
            code,
            modelType,
            string.Empty,
            string.Empty,
            members,
            dialect
        );
        SparseFragmentPatchCoreEmitter.AppendPatchConstructor(code, members, dialect);
        SparseFragmentPatchCoreEmitter.AppendPatchOptionalApply(
            code,
            members,
            dialect,
            "public " + optional + " Apply(" + optional + " current)"
        );
        SparseFragmentPatchCoreEmitter.AppendPatchApplyMembers(code, members, dialect);
        SparseFragmentPatchAlgebraEmitter.AppendPatchAlgebra(code, modelType, members);
        SparseFragmentPatchRebaseEmitter.AppendPatchRebase(code, modelType, members);
        SparsePatchInspectionEmitter.AppendPatchInspection(code, modelType, members);
        SparsePatchStjEmitter.AppendPatchStj(code, members);
        if (emitJsonBridge)
        {
            var jsonPrefix = SparseNaming.JsonPatchApiPrefix(
                members.Select(static member => member.Property.Name)
            );
            SparseJsonPatchEmitter.AppendFragmentJsonHelpers(
                code,
                "global::SparseFragments",
                Runtime + "Optional"
            );
            SparseJsonPatchEmitter.AppendFromJsonPatch(
                code,
                "global::SparseFragments",
                "SparseJsonPatch",
                Runtime + "Optional",
                jsonPrefix,
                patchPrefix + "Between"
            );
            SparseJsonPatchEmitter.AppendToJsonPatch(
                code,
                "global::SparseFragments",
                "SparseJsonPatch",
                Runtime + "Optional",
                jsonPrefix,
                "this.Apply(baseline)"
            );
        }
        code.AppendLineAt(1, "}");
        SparseChangeSetEmitter.AppendChangeSet(code, members);
    }
}
