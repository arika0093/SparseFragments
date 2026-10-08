using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared-baseline contiguity probes and typed match projection helpers.</summary>
internal static class SparseChangeSetMatchEmitter
{
    /// <summary>
    /// Emits the shared-baseline contiguity probes used by whole-root/memberwise composition.
    /// </summary>
    /// <remarks>
    /// Each probe checks semantic continuity only on changed paths: an empty transition
    /// matches any state, a whole-root transition compares whole endpoints, and a
    /// memberwise transition compares per-member before (or after) values with the
    /// supplied state, recursing through nested subtrees. Keyed/dictionary members use
    /// the canonical key-aware collection Between emptiness check, mirroring memberwise
    /// compose; all other members use the generated semantic member equality.
    /// </remarks>
    internal static void AppendMatchHelpers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = runtime;
        foreach (var side in new[] { "Before", "After" })
        {
            var field = side == "Before" ? "__sparse_wholeBefore" : "__sparse_wholeAfter";
            code.AppendLineAt(
                2,
                "/// <summary>Whether the supplied state matches this transition's "
                    + (side == "Before" ? "before" : "after")
                    + " on every changed path.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal bool __Sparse" + side + "Matches(" + optionalFragment + " state)"
            );
            code.AppendLineAt(2, "{");
            var __hasSparseMatch = members.Any(static m => IsKeyed(m) || IsDict(m));
            code.AppendLineAt(3, "if (IsEmpty) return true;");
            code.AppendLineAt(
                3,
                "if (__sparse_hasWhole) return Fragment.__SparseAreEqual(" + field + ", state);"
            );
            code.AppendLineAt(3, "if (!state.IsPresent || state.Value is null) return false;");
            code.AppendLineAt(3, "var __st = state.Value!;");
            if (__hasSparseMatch)
                AppendPragmaDisableNullKey(code, 3);
            foreach (var member in members)
            {
                var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
                if (IsNested(member))
                {
                    code.AppendLineAt(
                        3,
                        "if ("
                            + NestedField(member)
                            + " is not null && !"
                            + NestedField(member)
                            + ".__Sparse"
                            + side
                            + "Matches(__st."
                            + esc
                            + ")) return false;"
                    );
                }
                else if (IsKeyed(member) || IsDict(member))
                {
                    AppendSparseMatchMember(code, member, esc, side, runtime, dialect);
                }
                else
                {
                    var own = side == "Before" ? BeforeField(member) : AfterField(member);
                    code.AppendLineAt(3, "if (" + HasField(member) + ")");
                    code.AppendLineAt(3, "{");
                    code.AppendLineAt(
                        4,
                        "if (!Fragment.__SparseEqual_"
                            + member.Id
                            + "(__st."
                            + esc
                            + ", "
                            + own
                            + ")) return false;"
                    );
                    code.AppendLineAt(3, "}");
                }
            }
            if (__hasSparseMatch)
                AppendPragmaRestoreNullKey(code, 3);
            code.AppendLineAt(3, "return true;");
            code.AppendLineAt(2, "}");
        }
    }

    /// <summary>Emits per-key sparse contiguity probe for a keyed/dict member.</summary>
    internal static void AppendSparseMatchMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string side,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var isKeyed = IsKeyed(member);
        var keyType = KeyTypeOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var ownWhole = side == "Before" ? KeyedWholeBefore(member) : KeyedWholeAfter(member);
        var ownOrder = side == "Before" ? KeyedBeforeOrder(member) : KeyedAfterOrder(member);
        code.AppendLineAt(3, "if (" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        // Whole presence/null transitions compare full endpoints.
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__st."
                + esc
                + ", "
                + ownWhole
                + ")) return false;"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __cur" + id + " = __st." + esc + ";");
        code.AppendLineAt(
            4,
            "if (!__cur"
                + id
                + ".IsPresent || (object?)__cur"
                + id
                + ".Value is null) return false;"
        );
        if (isKeyed)
        {
            // Order interacts only where stored (orderChanged); otherwise membership only.
            code.AppendLineAt(4, "if (" + ownOrder + " is not null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var __curOrder"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">();"
            );
            code.AppendLineAt(
                5,
                "foreach (var __e in __cur"
                    + id
                    + ".Value!) __curOrder"
                    + id
                    + ".Add(__SparseKeyOf_ChangeSet_"
                    + id
                    + "(__e));"
            );
            code.AppendLineAt(
                5,
                "if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(__curOrder"
                    + id
                    + ", "
                    + ownOrder
                    + ")) return false;"
            );
            code.AppendLineAt(4, "}");
        }
        // Per-key endpoint checks from stored items (no full snapshot retention).
        code.AppendLineAt(
            4,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(4, "{");
        if (isKeyed)
        {
            var elementFrag = ElementFragmentOf(member);
            // Nested match helpers validate only the child paths represented by Edit.
            var mismatchBefore =
                "!"
                + "__it.Edit.__SparseBeforeMatches("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__e)))";
            var mismatchAfter =
                "!"
                + "__it.Edit.__SparseAfterMatches("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__e)))";
            // Emitter-side branch on Before/After avoids unreachable runtime
            // string comparisons in generated code.
            if (side == "Before")
            {
                // Added keys must be absent before; all other changed keys must be
                // present with Before equal (fragment-aware).
                code.AppendLineAt(5, "if (__it.IsAdded) continue;");
                code.AppendLineAt(
                    5,
                    "bool __found = false; foreach (var __e in __cur"
                        + id
                        + ".Value!) if ("
                        + comparer
                        + ".Equals(__SparseKeyOf_ChangeSet_"
                        + id
                        + "(__e), __it.Key)) { __found = true; if ("
                        + mismatchBefore
                        + ") return false; break; }"
                );
                code.AppendLineAt(5, "if (!__found) return false;");
            }
            else
            {
                // Removed keys must be absent after; all other changed keys must be
                // present with After equal.
                code.AppendLineAt(5, "if (__it.IsRemoved) continue;");
                code.AppendLineAt(
                    5,
                    "bool __found = false; foreach (var __e in __cur"
                        + id
                        + ".Value!) if ("
                        + comparer
                        + ".Equals(__SparseKeyOf_ChangeSet_"
                        + id
                        + "(__e), __it.Key)) { __found = true; if ("
                        + mismatchAfter
                        + ") return false; break; }"
                );
                code.AppendLineAt(5, "if (!__found) return false;");
            }
        }
        else
        {
            // Scalar and fragment dictionary values share comparer-based endpoint
            // checks here (nested fragment deltas are validated by value equality
            // at the match-probe level; structural conflicts surface in rebase).
            EmitDictMatchProbes(code, member, id, side, runtime, facade);
        }
        code.AppendLineAt(4, "}");
        // Added keys must be absent on the Before side and present on the After side is
        // covered above per-item; missing keys otherwise match (disjoint paths).
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    internal static void EmitDictMatchProbes(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id,
        string side,
        string runtime,
        string facade
    )
    {
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        // Dictionary keys in generated models are reference types; the null-forgiving
        // operator satisfies interface-dictionary nullable analysis.
        if (side == "Before")
        {
            code.AppendLineAt(5, "if (__it.IsAdded) continue;");
            if (hasPatch)
                code.AppendLineAt(
                    5,
                    "if (!__cur"
                        + id
                        + ".Value!.TryGetValue(__it.Key!, out var __cv) || !__it.Edit.__SparseBeforeMatches("
                        + runtime
                        + "Optional<"
                        + valueFrag
                        + "?>.Present("
                        + valueFrag
                        + ".From(__cv)))) return false;"
                );
            else
                code.AppendLineAt(
                    5,
                    "if (!__cur"
                        + id
                        + ".Value!.TryGetValue(__it.Key!, out var __cv) || !"
                        + facade
                        + ".AreEqual((object?)__cv, (object?)__it.Before.Value)) return false;"
                );
        }
        else
        {
            code.AppendLineAt(5, "if (__it.IsRemoved) continue;");
            if (hasPatch)
                code.AppendLineAt(
                    5,
                    "if (!__cur"
                        + id
                        + ".Value!.TryGetValue(__it.Key!, out var __cv2) || !__it.Edit.__SparseAfterMatches("
                        + runtime
                        + "Optional<"
                        + valueFrag
                        + "?>.Present("
                        + valueFrag
                        + ".From(__cv2)))) return false;"
                );
            else
                code.AppendLineAt(
                    5,
                    "if (!__cur"
                        + id
                        + ".Value!.TryGetValue(__it.Key!, out var __cv2) || !"
                        + facade
                        + ".AreEqual((object?)__cv2, (object?)__it.After.Value)) return false;"
                );
        }
    }

    internal static void EmitDictGranularReturn(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string comparer,
        string keyType,
        string valueType,
        string editedType,
        bool hasPatch
    )
    {
        code.AppendLineAt(
            3,
            "var __stored = "
                + KeyedItems(member)
                + " ?? new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            3,
            "var __added = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "); var __removed = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + "); var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">("
                + comparer
                + ");"
        );
        if (hasPatch)
            code.AppendLineAt(
                3,
                "foreach (var __it in __stored) { if (__it.IsAdded) __added[__it.Key] = __it.After.Value!; else if (__it.IsRemoved) __removed[__it.Key] = __it.Before.Value!; else if (__it.IsEdited) __edited[__it.Key] = __it.Edit; }"
            );
        else
            code.AppendLineAt(
                3,
                "foreach (var __it in __stored) { if (__it.IsAdded) __added[__it.Key] = __it.After.Value!; else if (__it.IsRemoved) __removed[__it.Key] = __it.Before.Value!; else if (__it.IsEdited) __edited[__it.Key] = __it.After.Value!; }"
            );
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(default, default, __added, __removed, __edited, __stored, false);"
        );
    }

    internal static void EmitDictEmptyReturn(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string comparer
    )
    {
        var emptyEdited =
            (member.Collection.ValueType?.IsFragmentModel == true)
                ? ValueChangeSetOf(member)
                : ValueTypeOf(member);
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(default, default, new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + ValueTypeOf(member)
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.Dictionary<"
                + KeyTypeOf(member)
                + ", "
                + emptyEdited
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
    }
}
