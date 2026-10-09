using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the standalone patch algebra (Between / Compose / Invert) for a generated patch.</summary>
internal static class SparseFragmentPatchAlgebraEmitter
{
    public static void AppendPatchAlgebra(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        _ = modelType;
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var wholeOperation = runtime + "FragmentOperation<Fragment?>";
        var kind = runtime + "FragmentOperationKind";
        var expressions = new SparseFragmentExpressions(
            "__sparse_patch_context",
            dialect.RuntimeFacade,
            dialect.RuntimeFacade,
            runtime + "Optional"
        );

        SparseSemanticBetweenEmitter.AppendBetweenMethod(
            code,
            members,
            new SparseBetweenDialect(
                runtime,
                optionalFragment,
                wholeOperation,
                "__sparse_whole",
                prefix,
                dialect.MemberField,
                static member =>
                    member.ChildModel is null
                    && !SparseFragmentPatchEmitter.IsCollectionPatch(member),
                member => SparseFragmentPatchEmitter.GetMemberValueType(dialect, member),
                (member, beforeValue, afterValue) =>
                {
                    if (member.MergeStrategyType is not null)
                    {
                        return "Fragment."
                            + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member)
                            + ".AreEqual("
                            + beforeValue
                            + ", "
                            + afterValue
                            + ")";
                    }

                    if (member.ComparisonComparerType is not null)
                    {
                        return "Fragment."
                            + SparseFragmentEmitHelpers.ComparisonComparerField(member)
                            + ".Equals("
                            + beforeValue
                            + ", "
                            + afterValue
                            + ")";
                    }

                    return expressions.ValueEqualityExpression(member, beforeValue, afterValue);
                },
                (member, before, after) =>
                    SparseFragmentPatchEmitter.IsCollectionPatch(member)
                        ? SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member)
                            + ".Between("
                            + before
                            + ", "
                            + after
                            + ")"
                        : dialect.ChildPatchName(member)
                            + "."
                            + member.ChildModel!.Value.PatchApiPrefix
                            + "Between("
                            + before
                            + ", "
                            + after
                            + ")"
            ),
            target
        );

        AppendCompose(code, members, dialect, prefix, kind, target);
        AppendInvert(code, optionalFragment, prefix, target);
    }

    private static void AppendCompose(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string prefix,
        string kind,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Composes this patch with a following patch so both can be applied at once.</summary>"
            );
            code.AppendLineAt(
                2,
                "public Patch "
                    + prefix
                    + "Compose(Patch next) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Compose(this, next);"
            );
            code.AppendLineAt(
                2,
                "/// <summary>Composes two patches, applying <paramref name=\"second\"/> after <paramref name=\"first\"/>.</summary>"
            );
            code.AppendLineAt(
                2,
                "public static Patch "
                    + prefix
                    + "Compose(Patch first, Patch second) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Compose(first, second);"
            );
            AppendComposeBody(target.PatchOperations, members, dialect, prefix, kind, "self.");
            return;
        }

        AppendComposeBody(code, members, dialect, prefix, kind, string.Empty);
        code.AppendLineAt(
            2,
            "/// <summary>Composes two patches, applying <paramref name=\"second\"/> after <paramref name=\"first\"/>.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static Patch " + prefix + "Compose(Patch first, Patch second)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
        );
        code.AppendLineAt(3, "return first." + prefix + "Compose(second);");
        code.AppendLineAt(2, "}");
    }

    private static void AppendComposeBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string prefix,
        string kind,
        string receiver
    )
    {
        var owner = receiver.Length == 0 ? "this" : "self";
        var declaration =
            receiver.Length == 0
                ? "public Patch " + prefix + "Compose(Patch next)"
                : "internal static Patch " + prefix + "Compose(Patch self, Patch next)";
        code.AppendLineAt(
            2,
            "/// <summary>Composes this patch with a following patch so both can be applied at once.</summary>"
        );
        code.AppendLineAt(2, declaration);
        code.AppendLineAt(2, "{");
        if (receiver.Length != 0)
        {
            code.AppendLineAt(
                3,
                "if (self is null) throw new global::System.ArgumentNullException(nameof(self));"
            );
        }

        code.AppendLineAt(
            3,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(3, "var result = new Patch();");
        code.AppendLineAt(3, "if (next." + dialect.WholeFieldName + ".Kind != " + kind + ".Keep)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "result." + dialect.WholeFieldName + " = next." + dialect.WholeFieldName + ";"
        );
        foreach (var member in members)
        {
            code.AppendLineAt(
                4,
                "result."
                    + dialect.MemberField(member)
                    + " = next."
                    + dialect.MemberField(member)
                    + ";"
            );
        }

        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "result." + dialect.WholeFieldName + " = " + owner + "." + dialect.WholeFieldName + ";"
        );
        foreach (var member in members)
        {
            var field = dialect.MemberField(member);
            if (member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                code.AppendLineAt(
                    3,
                    "result."
                        + field
                        + " = next."
                        + field
                        + ".Kind == "
                        + kind
                        + ".Keep ? "
                        + owner
                        + "."
                        + field
                        + " : next."
                        + field
                        + ";"
                );
            }
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
            {
                code.AppendLineAt(
                    3,
                    "result."
                        + field
                        + " = next."
                        + field
                        + " is null ? "
                        + owner
                        + "."
                        + field
                        + " : ("
                        + owner
                        + "."
                        + field
                        + " is null ? next."
                        + field
                        + " : "
                        + owner
                        + "."
                        + field
                        + ".Compose(next."
                        + field
                        + "));"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "result."
                        + field
                        + " = next."
                        + field
                        + " is null ? "
                        + owner
                        + "."
                        + field
                        + " : ("
                        + owner
                        + "."
                        + field
                        + " is null ? next."
                        + field
                        + " : "
                        + owner
                        + "."
                        + field
                        + "."
                        + member.ChildModel!.Value.PatchApiPrefix
                        + "Compose(next."
                        + field
                        + "));"
                );
            }
        }

        code.AppendLineAt(3, "return result;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendInvert(
        SharedIndentedBuilder code,
        string optionalFragment,
        string prefix,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Inverts this patch relative to the sparse state it was applied to.</summary>"
            );
            code.AppendLineAt(
                2,
                "public Patch "
                    + prefix
                    + "Invert("
                    + optionalFragment
                    + " baseline) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Invert(this, baseline);"
            );
            var ops = target.PatchOperations;
            ops.AppendLineAt(
                2,
                "/// <summary>Inverts a patch relative to the sparse state it was applied to.</summary>"
            );
            ops.AppendLineAt(
                2,
                "internal static Patch "
                    + prefix
                    + "Invert(Patch self, "
                    + optionalFragment
                    + " baseline)"
            );
            ops.AppendLineAt(2, "{");
            ops.AppendLineAt(3, "var applied = Apply(self, baseline);");
            ops.AppendLineAt(3, "return " + prefix + "Between(applied, baseline);");
            ops.AppendLineAt(2, "}");
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Inverts this patch relative to the sparse state it was applied to.</summary>"
        );
        code.AppendLineAt(
            2,
            "public Patch " + prefix + "Invert(" + optionalFragment + " baseline)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var applied = this.Apply(baseline);");
        code.AppendLineAt(3, "return " + prefix + "Between(applied, baseline);");
        code.AppendLineAt(2, "}");
    }
}
