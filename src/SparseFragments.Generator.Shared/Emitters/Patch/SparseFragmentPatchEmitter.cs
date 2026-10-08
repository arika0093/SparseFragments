using System;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits typed standalone mutations without source routing or ownership.</summary>
/// <remarks>
/// This type owns the shared patch vocabulary (dialect, naming, whole-operation
/// helpers) and orchestrates the patch stages. Member emission, patch algebra
/// and rebase each live in their own focused emitter.
/// </remarks>
internal static class SparseFragmentPatchEmitter
{
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

    public static void AppendFragmentMethods(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        ImmutableArray<SparseMemberModel> members,
        bool canWriteInPlace
    )
    {
        code.AppendLineAt(2, "public Patch ToPatch() => new(this);");
        code.AppendLineAt(2, "public Fragment Apply(Patch patch)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
        );
        code.AppendLineAt(
            3,
            "var result = patch.Apply(" + runtimeNamespace + "Optional<Fragment?>.Present(this));"
        );
        code.AppendLineAt(
            3,
            "if (!result.IsPresent || result.Value is null) throw new global::System.InvalidOperationException(\"Apply a whole-contribution null or remove operation through Patch.Apply to preserve its optional state.\");"
        );
        code.AppendLineAt(3, "return result.Value;");
        code.AppendLineAt(2, "}");
        if (canWriteInPlace)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Writes this fragment into an existing model instance.</summary>"
            );
            code.AppendLineAt(2, "public void WriteTo(" + modelType + " model)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (model is null) throw new global::System.ArgumentNullException(nameof(model));"
            );
            code.AppendLineAt(3, "var __sparse_updated = ToModel();");
            foreach (var member in members)
            {
                var property = SparseNaming.EscapeIdentifier(member.Property.Name);
                if (
                    member.Collection.Kind == SparseCollectionKind.List
                    && member.Collection.CloneKind == SparseCloneCollectionKind.List
                )
                {
                    var listType =
                        "global::System.Collections.Generic.List<"
                        + member.Collection.ElementType.Name
                        + ">";
                    code.AppendLineAt(
                        3,
                        "if (model."
                            + property
                            + " is "
                            + listType
                            + " __sparse_list"
                            + member.Id
                            + " && __sparse_updated."
                            + property
                            + " is not null)"
                    );
                    code.AppendLineAt(3, "{");
                    code.AppendLineAt(4, "__sparse_list" + member.Id + ".Clear();");
                    code.AppendLineAt(
                        4,
                        "__sparse_list" + member.Id + ".AddRange(__sparse_updated." + property + ");"
                    );
                    code.AppendLineAt(3, "}");
                    code.AppendLineAt(
                        3,
                        "else model."
                            + property
                            + " = __sparse_updated."
                            + property
                            + "!;"
                    );
                }
                else if (member.Collection.IsDictionary)
                {
                    var dictionaryType =
                        "global::System.Collections.Generic.Dictionary<"
                        + member.Collection.ElementType.Name
                        + ", "
                        + member.Collection.ValueType!.Value.Name
                        + ">";
                    code.AppendLineAt(
                        3,
                        "if (model."
                            + property
                            + " is "
                            + dictionaryType
                            + " __sparse_dict"
                            + member.Id
                            + " && __sparse_updated."
                            + property
                            + " is not null)"
                    );
                    code.AppendLineAt(3, "{");
                    code.AppendLineAt(4, "__sparse_dict" + member.Id + ".Clear();");
                    code.AppendLineAt(
                        4,
                        "foreach (var __sparse_pair"
                            + member.Id
                            + " in __sparse_updated."
                            + property
                            + ") __sparse_dict"
                            + member.Id
                            + ".Add(__sparse_pair"
                            + member.Id
                            + ".Key, __sparse_pair"
                            + member.Id
                            + ".Value);"
                    );
                    code.AppendLineAt(3, "}");
                    code.AppendLineAt(
                        3,
                        "else model."
                            + property
                            + " = __sparse_updated."
                            + property
                            + "!;"
                    );
                }
                else
                {
                    code.AppendLineAt(3, "model." + property + " = __sparse_updated." + property + "!;");
                }
            }
            code.AppendLineAt(2, "}");
        }
    }

    /// <summary>Small dialect for shared patch-core emission (whole, empty, ctor, apply).</summary>
    internal readonly record struct SparsePatchDialect(
        string RuntimeNamespace,
        string WholeFieldName,
        string MembersEmptyName,
        Func<SparseMemberModel, string> MemberField,
        Func<SparseMemberModel, string> NestedContract,
        string NestedApplyMethod,
        bool CastNestedApply,
        string RuntimeFacade,
        string ConflictType,
        string ConflictKindType,
        Func<string, string> RebaseResult,
        Func<SparseMemberModel, string> ChildPatchName,
        Func<SparseMemberModel, string> ChildChangeSetName,
        bool HashSetSupportsCapacity = false,
        Func<SparseMemberModel, string>? MemberValueType = null,
        Func<SparseMemberModel, string>? CollectionPatchName = null,
        Func<SparseMemberModel, string>? MergeStrategyField = null
    );

    internal static string DefaultChildChangeSet(SparseMemberModel member) =>
        member.ChildFragmentType!.Substring(0, member.ChildFragmentType.Length - "Fragment".Length)
        + "ChangeSet";

    internal static string Operation(SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace + "FragmentOperation";

    internal static string GetMemberValueType(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) => dialect.MemberValueType?.Invoke(member) ?? ValueType(member);

    internal static string GetCollectionPatchName(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) => dialect.CollectionPatchName?.Invoke(member) ?? CollectionPatch(member);

    internal static string GetMergeStrategyField(
        SparsePatchDialect dialect,
        SparseMemberModel member
    ) =>
        dialect.MergeStrategyField?.Invoke(member)
        ?? SparseWellKnownNames.MergeStrategyFieldPrefix + member.Id;

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

        return dialect.MemberField(member) + ".Kind == " + Kind(dialect) + ".Keep";
    }

    public static void AppendPatch(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparsePatchDialect dialect,
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        bool canWriteInPlace = false
    )
    {
        var optional = dialect.RuntimeNamespace + "Optional<Fragment?>";
        code.AppendLineAt(1, "public sealed class Patch");
        code.AppendLineAt(1, "{");
        SparseKeyedCollectionEmitter.EmitCollectionPatches(code, members, dialect);
        SparseFragmentPatchCoreEmitter.AppendPatchMembers(code, members, dialect);
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
        code.AppendLineAt(
            2,
            "/// <summary>Applies this patch to an ordinary model and returns a new model.</summary>"
        );
        code.AppendLineAt(2, "public " + modelType + " ApplyTo(" + modelType + " current)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var updated = Fragment.From(current).Apply(this).ToModel();");
        foreach (var ignoredName in ignoredSettablePropertyNames.IsDefault
            ? ImmutableArray<string>.Empty
            : ignoredSettablePropertyNames)
        {
            var name = SparseNaming.EscapeIdentifier(ignoredName);
            code.AppendLineAt(3, "updated." + name + " = current." + name + ";");
        }
        code.AppendLineAt(3, "return updated;");
        code.AppendLineAt(2, "}");
        if (canWriteInPlace)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Applies this patch to an existing model instance.</summary>"
            );
            code.AppendLineAt(2, "public void ApplyInPlace(" + modelType + " current)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (current is null) throw new global::System.ArgumentNullException(nameof(current));"
            );
            code.AppendLineAt(
                3,
                "Fragment.From(current).Apply(this).WriteTo(current);"
            );
            code.AppendLineAt(2, "}");
        }
        SparseFragmentPatchAlgebraEmitter.AppendPatchAlgebra(code, modelType, members, dialect);
        SparseFragmentPatchRebaseEmitter.AppendPatchRebase(code, modelType, members, dialect);
        code.AppendLineAt(1, "}");
        SparseChangeSetEmitter.AppendChangeSet(
            code,
            members,
            dialect,
            modelType,
            ignoredSettablePropertyNames,
            canWriteInPlace
        );
    }
}
