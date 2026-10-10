using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Sequential transition composition core.</summary>
internal static class SparseChangeSetComposeEmitter
{
    internal static void AppendCompose(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        _ = optionalFragment;
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"next\">The change set to apply after this change set.</param>"
            );
            code.AppendLineAt(2, "/// <returns>The composed change set.</returns>");
            code.AppendLineAt(
                2,
                "public ChangeSet Compose(ChangeSet next) => "
                    + target.ChangeSetOperationsType
                    + ".Compose(this, next);"
            );
            code.AppendLineAt(2, "/// <summary>Composes two sequential change sets.</summary>");
            code.AppendLineAt(2, "/// <param name=\"first\">The first change set.</param>");
            code.AppendLineAt(2, "/// <param name=\"second\">The second change set.</param>");
            code.AppendLineAt(2, "/// <returns>The composed change set.</returns>");
            code.AppendLineAt(
                2,
                "public static ChangeSet Compose(ChangeSet first, ChangeSet second) => "
                    + target.ChangeSetOperationsType
                    + ".Compose(first, second);"
            );
            code = target.ChangeSetOperations;
            code.AppendLineAt(
                2,
                "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"self\">The first change set.</param>");
            code.AppendLineAt(2, "/// <param name=\"next\">The second change set.</param>");
            code.AppendLineAt(2, "/// <returns>The composed change set.</returns>");
            code.AppendLineAt(
                2,
                "internal static ChangeSet Compose(ChangeSet self, ChangeSet next)"
            );
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"next\">The change set to apply after this change set.</param>"
            );
            code.AppendLineAt(2, "/// <returns>The composed change set.</returns>");
            code.AppendLineAt(2, "public ChangeSet Compose(ChangeSet next)");
        }
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            code.AppendLineAt(
                3,
                "if (self is null) throw new global::System.ArgumentNullException(nameof(self));"
            );
            AppendSelfAliases(code, members);
        }
        var __hasSparse = members.Any(static m => IsKeyed(m) || IsDict(m));
        code.AppendLineAt(
            3,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(
            3,
            target is null ? "if (IsEmpty) return next;" : "if (self.IsEmpty) return next;"
        );
        code.AppendLineAt(
            3,
            target is null ? "if (next.IsEmpty) return this;" : "if (next.IsEmpty) return self;"
        );
        code.AppendLineAt(3, "if (__sparse_hasWhole || next.__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__sparse_hasWhole && next.__sparse_hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseAreEqual(__sparse_wholeAfter, next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "return Between(__sparse_wholeBefore, next.__sparse_wholeAfter);");
        code.AppendLineAt(4, "}");
        // Shared-baseline algebra: a whole-root endpoint composes with a
        // memberwise transition when the memberwise side is contiguous with the retained
        // whole state. Only overlapping (changed) paths are checked; disjoint paths
        // compose without full-state equality. The merged endpoint is derived by
        // applying the memberwise patch onto the retained whole state, then Between
        // normalizes (including back to no-op where before == final after).
        code.AppendLineAt(4, "if (__sparse_hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!next.__SparseBeforeMatches(__sparse_wholeAfter)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "var __mergedAfter = next.ToPatch().Apply(__sparse_wholeAfter);");
        code.AppendLineAt(5, "return Between(__sparse_wholeBefore, __mergedAfter);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            target is null
                ? "if (!__SparseAfterMatches(next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
                : "if (!self.__SparseAfterMatches(next.__sparse_wholeBefore)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            4,
            target is null
                ? "var __mergedBefore = Invert().ToPatch().Apply(next.__sparse_wholeBefore);"
                : "var __mergedBefore = Invert(self).ToPatch().Apply(next.__sparse_wholeBefore);"
        );
        code.AppendLineAt(4, "return Between(__mergedBefore, next.__sparse_wholeAfter);");
        code.AppendLineAt(3, "}");
        if (__hasSparse)
            AppendPragmaDisableNullKey(code, 3);
        foreach (var member in members)
        {
            if (!IsNested(member))
                continue;
            var child = ChildChangeSet(member, dialect);
            code.AppendLineAt(3, child + "? __c" + member.Id + ";");
            code.AppendLineAt(
                3,
                "if ("
                    + NestedField(member)
                    + " is null) __c"
                    + member.Id
                    + " = next."
                    + NestedField(member)
                    + ";"
            );
            code.AppendLineAt(
                3,
                "else if (next."
                    + NestedField(member)
                    + " is null) __c"
                    + member.Id
                    + " = "
                    + NestedField(member)
                    + ";"
            );
            code.AppendLineAt(
                3,
                "else { var __cc"
                    + member.Id
                    + " = "
                    + NestedField(member)
                    + ".Compose(next."
                    + NestedField(member)
                    + "); __c"
                    + member.Id
                    + " = __cc"
                    + member.Id
                    + ".IsEmpty ? null : __cc"
                    + member.Id
                    + "; }"
            );
        }
        // Emit per-member merge with explicit locals.
        // Scalar members keep snapshot equality; keyed/dict compose per interacting
        // key so disjoint keys compose despite differing unrelated snapshots.
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            if (IsKeyed(member))
            {
                AppendKeyedComposeSparse(code, members, member, runtime, dialect);
                continue;
            }
            if (IsDict(member))
            {
                AppendDictComposeSparse(code, members, member, runtime, dialect);
                continue;
            }
            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
            code.AppendLineAt(3, opt + " __cb" + member.Id + "_b = default;");
            code.AppendLineAt(3, opt + " __cb" + member.Id + "_a = default;");
            code.AppendLineAt(3, "bool __cb" + member.Id + "_has;");
            code.AppendLineAt(3, "if (!" + HasField(member) + ")");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__cb" + member.Id + "_has = next." + HasField(member) + ";");
            code.AppendLineAt(4, "__cb" + member.Id + "_b = next." + BeforeField(member) + ";");
            code.AppendLineAt(4, "__cb" + member.Id + "_a = next." + AfterField(member) + ";");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "else if (!next." + HasField(member) + ")");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__cb" + member.Id + "_has = true;");
            code.AppendLineAt(4, "__cb" + member.Id + "_b = " + BeforeField(member) + ";");
            code.AppendLineAt(4, "__cb" + member.Id + "_a = " + AfterField(member) + ";");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "else");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!Fragment.__SparseEqual_"
                    + member.Id
                    + "("
                    + AfterField(member)
                    + ", next."
                    + BeforeField(member)
                    + ")) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLineAt(
                4,
                "if (Fragment.__SparseEqual_"
                    + member.Id
                    + "("
                    + BeforeField(member)
                    + ", next."
                    + AfterField(member)
                    + ")) { __cb"
                    + member.Id
                    + "_has = false; }"
            );
            code.AppendLineAt(
                4,
                "else { __cb"
                    + member.Id
                    + "_has = true; __cb"
                    + member.Id
                    + "_b = "
                    + BeforeField(member)
                    + "; __cb"
                    + member.Id
                    + "_a = next."
                    + AfterField(member)
                    + "; }"
            );
            code.AppendLineAt(3, "}");
        }
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                args.Add("__c" + member.Id);
            else if (IsKeyed(member))
                AddKeyedChangeSetArgs(
                    args,
                    member,
                    "__cb" + member.Id + "_has",
                    "__cb" + member.Id + "_whole",
                    "__cb" + member.Id + "_wb",
                    "__cb" + member.Id + "_wa",
                    "__cb" + member.Id + "_items",
                    "__cb" + member.Id + "_bO",
                    "__cb" + member.Id + "_aO",
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? "__cb" + member.Id + "_tbO"
                        : null,
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? "__cb" + member.Id + "_taO"
                        : null
                );
            else if (IsDict(member))
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_has",
                        "__cb" + member.Id + "_whole",
                        "__cb" + member.Id + "_wb",
                        "__cb" + member.Id + "_wa",
                        "__cb" + member.Id + "_items",
                    }
                );
            else
                args.AddRange(
                    new[]
                    {
                        "__cb" + member.Id + "_b",
                        "__cb" + member.Id + "_a",
                        "__cb" + member.Id + "_has",
                    }
                );
        }
        if (__hasSparse)
            AppendPragmaRestoreNullKey(code, 3);
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
        if (target is null)
        {
            code.AppendLineAt(2, "/// <summary>Composes two sequential change sets.</summary>");
            code.AppendLineAt(2, "/// <param name=\"first\">The first change set.</param>");
            code.AppendLineAt(2, "/// <param name=\"second\">The second change set.</param>");
            code.AppendLineAt(2, "/// <returns>The composed change set.</returns>");
            code.AppendLineAt(
                2,
                "public static ChangeSet Compose(ChangeSet first, ChangeSet second)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
            );
            code.AppendLineAt(3, "return first." + "Compose(second);");
            code.AppendLineAt(2, "}");
        }
    }

    /// <summary>Emits per-key sparse compose for a keyed member.</summary>
    /// <remarks>
    /// Declares locals __cb_has/__cb_whole/__cb_wb/__cb_wa/__cb_items/__cb_bO/__cb_aO.
    /// Disjoint keys compose without full-snapshot equality; overlapping keys require
    /// semantic contiguity (added/removed/edited continuity via element equality and
    /// nested ChangeSet compose). Net no-ops normalize to empty. Whole member
    /// transitions fall back to full-endpoint composition.
    /// </remarks>
    internal static void AppendPragmaDisableNullKey(SharedIndentedBuilder code, int indent)
    {
        // Sparse keyed/dictionary transitions flow stable keys through per-key maps
        // across all key shapes (string/int/tuple/interface keys). The keys are
        // non-null by construction (validated during Between/STJ read), but nullable
        // analysis cannot prove it uniformly: `!` is invalid on value-type keys
        // (CS8715) while omitting it warns on reference-type keys (CS8604).
        code.AppendLineAt(
            indent,
            "#pragma warning disable CS8604 // Sparse stable keys are non-null by construction."
        );
    }

    internal static void AppendPragmaRestoreNullKey(SharedIndentedBuilder code, int indent)
    {
        code.AppendLineAt(indent, "#pragma warning restore CS8604");
    }
}
