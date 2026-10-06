using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the standalone patch algebra (Between / Compose / Invert) for a generated patch.</summary>
internal static class SparseFragmentPatchAlgebraEmitter
{
    public static void AppendPatchAlgebra(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        _ = modelType;
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var wholeOperation = runtime + "FragmentOperation<Fragment?>";
        var kind = runtime + "FragmentOperationKind";
        var expressions = SparseFragmentPatchEmitter.Expressions;

        SparseSemanticBetweenEmitter.AppendBetweenMethod(
            code,
            members,
            new SparseBetweenDialect(
                runtime,
                optionalFragment,
                wholeOperation,
                "__sparse_whole",
                prefix,
                SparseFragmentPatchEmitter.Field,
                static member =>
                    member.ChildModel is null
                    && !SparseFragmentPatchEmitter.IsCollectionPatch(member),
                SparseFragmentPatchEmitter.ValueType,
                (member, beforeValue, afterValue) =>
                    member.MergeStrategyType is null
                        ? expressions.ValueEqualityExpression(member, beforeValue, afterValue)
                        : "Fragment."
                            + SparseWellKnownNames.MergeStrategyFieldPrefix
                            + member.Id
                            + ".AreEqual("
                            + beforeValue
                            + ", "
                            + afterValue
                            + ")",
                static (member, before, after) =>
                    SparseFragmentPatchEmitter.IsCollectionPatch(member)
                        ? SparseFragmentPatchEmitter.CollectionPatch(member)
                            + ".Between("
                            + before
                            + ", "
                            + after
                            + ")"
                        : SparseFragmentPatchEmitter.ChildPatch(member)
                            + "."
                            + member.ChildModel!.Value.PatchApiPrefix
                            + "Between("
                            + before
                            + ", "
                            + after
                            + ")"
            )
        );

        code.AppendLineAt(
            2,
            "/// <summary>Composes this patch with a following patch so both can be applied at once.</summary>"
        );
        code.AppendLineAt(2, "public Patch " + prefix + "Compose(Patch next)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(3, "var result = new Patch();");
        code.AppendLineAt(3, "if (next.__sparse_whole.Kind != " + kind + ".Unchanged)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "result.__sparse_whole = next.__sparse_whole;");
        foreach (var member in members)
        {
            code.AppendLineAt(
                4,
                "result."
                    + SparseFragmentPatchEmitter.Field(member)
                    + " = next."
                    + SparseFragmentPatchEmitter.Field(member)
                    + ";"
            );
        }

        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "result.__sparse_whole = this.__sparse_whole;");
        foreach (var member in members)
        {
            var field = SparseFragmentPatchEmitter.Field(member);
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
                        + ".Unchanged ? this."
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
                        + " is null ? this."
                        + field
                        + " : (this."
                        + field
                        + " is null ? next."
                        + field
                        + " : this."
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
                        + " is null ? this."
                        + field
                        + " : (this."
                        + field
                        + " is null ? next."
                        + field
                        + " : this."
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
