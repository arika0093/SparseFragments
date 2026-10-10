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
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Sparse per-key rebase for keyed sequences.</summary>
internal static class SparseChangeSetKeyedRebaseEmitter
{
    /// <summary>Emits sparse per-key rebase for a keyed member.</summary>
    internal static void AppendKeyedRebaseSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var optElement = runtime + "Optional<" + elementType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var trans = TransNameFor(members, member);
        var hasTempRebase = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        var redactedAssignments = new List<string>
        {
            "__rh" + id + " = true;",
            "__rwhole" + id + " = " + KeyedWholeFlag(member) + ";",
            "__rwb" + id + " = " + KeyedWholeBefore(member) + ";",
            "__rwa" + id + " = " + KeyedWholeAfter(member) + ";",
            "__ritems" + id + " = " + KeyedItems(member) + ";",
            "__rbO" + id + " = " + KeyedBeforeOrder(member) + ";",
            "__raO" + id + " = " + KeyedAfterOrder(member) + ";",
        };
        if (hasTempRebase)
        {
            redactedAssignments.Add("__rtbO" + id + " = " + KeyedTempBeforeOrder(member) + ";");
            redactedAssignments.Add("__rtaO" + id + " = " + KeyedTempAfterOrder(member) + ";");
        }
        SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
            code,
            member,
            lit,
            runtime,
            conflict,
            dialect,
            HasField(member),
            redactedAssignments.ToArray()
        );
        code.AppendLineAt(4, "if (" + HasField(member) + " && !__red" + id + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __curM" + id + " = __cur." + esc + ";");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member) && !hasTempRebase)
            code.AppendLineAt(
                4,
                "if ("
                    + KeyedBeforeOrder(member)
                    + " is not null) foreach (var __baselineKey in "
                    + KeyedBeforeOrder(member)
                    + ") if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__baselineKey")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed ChangeSet.\");"
            );
        // Whole presence/null transitions: replay via member patch rebase is not sparse;
        // report member conflict unless already applied, else keep whole.
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeAfter(member)
                + ", __curM"
                + id
                + ")) { }"
        );
        code.AppendLineAt(
            5,
            "else if (Fragment.__SparseEqual_"
                + id
                + "("
                + KeyedWholeBefore(member)
                + ", __curM"
                + id
                + ")) { __rh"
                + id
                + " = true; __rwhole"
                + id
                + " = true; __rwb"
                + id
                + " = __curM"
                + id
                + "; __rwa"
                + id
                + " = "
                + KeyedWholeAfter(member)
                + "; }"
        );
        code.AppendLineAt(
            5,
            "else { __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + KeyedWholeBefore(member)
                + "), __SparseMember("
                + KeyedWholeAfter(member)
                + "), __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else if (!__curM" + id + ".IsPresent || (object?)__curM" + id + ".Value is null)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Current map + order (transient, not retained).
        code.AppendLineAt(
            5,
            "var __cmap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __corder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        if (hasTempRebase)
        {
            SparseChangeSetKeyedTempRebaseEmitter.AppendTempCurrentWalk(
                code,
                member,
                elementType,
                id
            );
        }
        else
            code.AppendLineAt(
                5,
                "foreach (var __e in __curM"
                    + id
                    + ".Value!) { var __ck = __SparseKeyOf_ChangeSet_"
                    + id
                    + "(__e); "
                    + (
                        SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                            ? "if ("
                                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                                    member,
                                    "__ck"
                                )
                                + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the current baseline of a keyed ChangeSet.\"); "
                            : ""
                    )
                    + SparseKeyedCollectionEmitter.AddUniqueEntry("__cmap" + id, "__ck", "__e")
                    + " __corder"
                    + id
                    + ".Add(__ck); }"
            );
        code.AppendLineAt(
            5,
            "var __rlist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(5, "bool __any" + id + " = false;");
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(5, "{");
        if (hasTempRebase)
        {
            SparseChangeSetKeyedTempRebaseEmitter.AppendTempItemBranch(
                code,
                member,
                trans,
                runtime,
                elementCs,
                elementFrag,
                elementType,
                conflict,
                conflictKind,
                lit,
                id
            );
        }
        // Added: replay when absent; already-applied when equal; conflict otherwise.
        code.AppendLineAt(6, "if (__it.IsAdded)");
        code.AppendLineAt(6, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                6,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__it.Key")
                    + ") { __any"
                    + id
                    + " = true; "
                    + optElement
                    + " __ua = "
                    + optElement
                    + ".Present(__it.After.Value!); "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?> __uea = "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?>.Present("
                    + elementFrag
                    + ".From(__it.After.Value!)); var __uedit = "
                    + elementCs
                    + ".Between(default, __uea); __rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, default, __ua, -1, __corder"
                    + id
                    + ".Count, true, false, false, false, __uedit, false)); } else "
                    + "if (!__cmap"
                    + id
                    + ".ContainsKey(__it.Key)) { __any"
                    + id
                    + " = true; "
                    + optElement
                    + " __na = "
                    + optElement
                    + ".Present(__it.After.Value!); "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?> __nea = "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?>.Present("
                    + elementFrag
                    + ".From(__it.After.Value!)); var __nedit = "
                    + elementCs
                    + ".Between(default, __nea); __rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, default, __na, -1, __corder"
                    + id
                    + ".Count, true, false, false, false, __nedit, false)); __cmap"
                    + id
                    + "[__it.Key] = __it.After.Value!; __corder"
                    + id
                    + ".Add(__it.Key); }"
            );
        else
            code.AppendLineAt(
                6,
                "if (!__cmap"
                    + id
                    + ".ContainsKey(__it.Key)) { __any"
                    + id
                    + " = true; "
                    + optElement
                    + " __na = "
                    + optElement
                    + ".Present(__it.After.Value!); "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?> __nea = "
                    + runtime
                    + "Optional<"
                    + elementFrag
                    + "?>.Present("
                    + elementFrag
                    + ".From(__it.After.Value!)); var __nedit = "
                    + elementCs
                    + ".Between(default, __nea); __rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key, __it.TemporaryKey, default, __na, -1, __corder"
                    + id
                    + ".Count, true, false, false, false, __nedit, false)); __cmap"
                    + id
                    + "[__it.Key] = __it.After.Value!; __corder"
                    + id
                    + ".Add(__it.Key); }"
            );
        code.AppendLineAt(
            6,
            "else if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cmap"
                + id
                + "[__it.Key])), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.After.Value!))).IsEmpty)"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__cmap"
                + id
                + "[__it.Key]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "}");
        // Removed: already-applied when absent; replay when still base; conflict otherwise.
        code.AppendLineAt(6, "else if (__it.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__cmap" + id + ".ContainsKey(__it.Key)) { }");
        code.AppendLineAt(
            6,
            "else if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cmap"
                + id
                + "[__it.Key])), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__it.Before.Value!))).IsEmpty)"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__cmap"
                + id
                + "[__it.Key]), \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(
            6,
            "else { __any"
                + id
                + " = true; int __bi = __corder"
                + id
                + ".IndexOf(__it.Key, 0); __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.TemporaryKey, __it.Before, default, __bi, -1, false, true, false, false, __it.Edit, false)); __cmap"
                + id
                + ".Remove(__it.Key); }"
        );
        code.AppendLineAt(6, "}");
        // Edited: nested rebase distinguishes clean/already-applied/conflict.
        code.AppendLineAt(6, "else if (__it.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__cmap" + id + ".TryGetValue(__it.Key, out var __cev))");
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Missing, \"The keyed element conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "var __nr = __it.Edit.RebaseOnto("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cev)));"
        );
        code.AppendLineAt(
            7,
            "foreach (var __cc in __nr.Conflicts) __conflicts.Add(__cc.WithPathPrefix(__SparseRootPath.Member("
                + lit
                + ").Key(__it.Key)));"
        );
        code.AppendLineAt(7, "if (__nr.Conflicts.Count == 0 && !__nr.Rebased.IsEmpty)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "var __applied = __nr.Rebased.ToPatch().Apply("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__cev)));"
        );
        code.AppendLineAt(8, "if (__applied.IsPresent && __applied.Value is not null)");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(8, "var __um = __applied.Value!.ToModel(); __any" + id + " = true;");
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementType
                + "> __nb = "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__cev); "
                + runtime
                + "Optional<"
                + elementType
                + "> __na2 = "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__um);"
        );
        code.AppendLineAt(8, "int __bi2 = __corder" + id + ".IndexOf(__it.Key, 0);");
        code.AppendLineAt(
            8,
            "__rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.TemporaryKey, __nb, __na2, __bi2, __bi2, false, false, true, __it.IsReordered, __nr.Rebased, false)); __cmap"
                + id
                + "[__it.Key] = __um;"
        );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "}");
        // Reorder-only: keep when current order still matches base rank; else coincidentally ordered.
        code.AppendLineAt(6, "else if (__it.IsReordered)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            6,
            "if (__cmap"
                + id
                + ".ContainsKey(__it.Key)) { __any"
                + id
                + " = true; int __bi3 = __corder"
                + id
                + ".IndexOf(__it.Key, 0); __rlist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__it.Key, __it.TemporaryKey, __it.Before, __it.After, __bi3, __bi3, false, false, false, true, __it.Edit, false)); }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        // Order reconciliation: no local order change preserves current order
        // (concurrent adds kept); a local reorder is kept only when the base
        // order still matches current, else an order conflict is reported.
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __nbO"
                + id
                + " = null; global::System.Collections.Generic.List<"
                + keyType
                + ">? __naO"
                + id
                + " = null;"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(5, "bool __unassignedOnlyAdds" + id + " = false;");
            code.AppendLineAt(
                5,
                "if ("
                    + KeyedBeforeOrder(member)
                    + " is not null && "
                    + KeyedAfterOrder(member)
                    + " is not null) { var __ordinaryAfter = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); foreach (var __orderKey in "
                    + KeyedAfterOrder(member)
                    + ") if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__orderKey")
                    + ") __ordinaryAfter.Add(__orderKey); if ("
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">("
                    + KeyedBeforeOrder(member)
                    + ", __ordinaryAfter)) foreach (var __candidate in "
                    + KeyedItems(member)
                    + " ?? new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>()) if (__candidate.IsAdded && "
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__candidate.Key")
                    + ") { __unassignedOnlyAdds"
                    + id
                    + " = true; break; } }"
            );
            code.AppendLineAt(
                5,
                "if (__unassignedOnlyAdds"
                    + id
                    + ") { __nbO"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(__corder"
                    + id
                    + "); __naO"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(__corder"
                    + id
                    + "); foreach (var __newItem in __rlist"
                    + id
                    + ") if (__newItem.IsAdded && "
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__newItem.Key")
                    + ") __naO"
                    + id
                    + ".Add(__newItem.Key); }"
            );
        }
        code.AppendLineAt(
            5,
            "if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "__unassignedOnlyAdds" + id + " || ("
                        : ""
                )
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null && "
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedBeforeOrder(member)
                + ", "
                + KeyedAfterOrder(member)
                + "))"
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? ")" : "")
                + " { "
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "if (!__unassignedOnlyAdds" + id + ") { "
                        : ""
                )
                + "__nbO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + "); __naO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + ");"
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? " }" : "")
                + " }"
        );
        code.AppendLineAt(
            5,
            "else if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "!__unassignedOnlyAdds" + id + " && "
                        : ""
                )
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if ("
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedBeforeOrder(member)
                + ", __corder"
                + id
                + ")) { __nbO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__corder"
                + id
                + "); __naO"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">("
                + KeyedAfterOrder(member)
                + "); }"
        );
        code.AppendLineAt(
            6,
            "else if (!"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">("
                + KeyedAfterOrder(member)
                + ", __corder"
                + id
                + ")) { __conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + lit
                + "), "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)"
                + KeyedBeforeOrder(member)
                + "), "
                + runtime
                + "Optional<object?>.Present((object?)"
                + KeyedAfterOrder(member)
                + "), "
                + runtime
                + "Optional<object?>.Present((object?)__corder"
                + id
                + "), \"The collection order conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(5, "}");
        if (hasTempRebase)
        {
            SparseChangeSetKeyedTempRebaseEmitter.AppendTempOrderOutput(code, id);
        }
        code.AppendLineAt(
            5,
            "if (__any"
                + id
                + ") { __rh"
                + id
                + " = true; __ritems"
                + id
                + " = __rlist"
                + id
                + "; __rbO"
                + id
                + " = __nbO"
                + id
                + "; __raO"
                + id
                + " = __naO"
                + id
                + "; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "}");
    }
}
