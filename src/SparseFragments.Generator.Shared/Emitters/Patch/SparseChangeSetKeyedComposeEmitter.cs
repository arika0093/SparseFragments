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
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Per-key sparse compose for keyed sequences.</summary>
internal static class SparseChangeSetKeyedComposeEmitter
{
    internal static void AppendKeyedComposeSparse(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var trans = TransNameFor(members, member);
        code.AppendLineAt(3, "bool __cb" + id + "_has = false;");
        code.AppendLineAt(3, "bool __cb" + id + "_whole = false;");
        code.AppendLineAt(3, opt + " __cb" + id + "_wb = default;");
        code.AppendLineAt(3, opt + " __cb" + id + "_wa = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __cb"
                + id
                + "_items = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __cb" + id + "_bO = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __cb" + id + "_aO = null;"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __first" + id + "_has = " + HasField(member) + ";");
        code.AppendLineAt(4, "var __second" + id + "_has = next." + HasField(member) + ";");
        code.AppendLineAt(4, "if (!__first" + id + "_has)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_has = __second"
                + id
                + "_has; __cb"
                + id
                + "_whole = next."
                + KeyedWholeFlag(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_wb = next."
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_items = next."
                + KeyedItems(member)
                + "; __cb"
                + id
                + "_bO = next."
                + KeyedBeforeOrder(member)
                + "; __cb"
                + id
                + "_aO = next."
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (!__second" + id + "_has)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__cb" + id + "_has = true; __cb" + id + "_whole = " + KeyedWholeFlag(member) + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_wb = "
                + KeyedWholeBefore(member)
                + "; __cb"
                + id
                + "_wa = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_items = "
                + KeyedItems(member)
                + "; __cb"
                + id
                + "_bO = "
                + KeyedBeforeOrder(member)
                + "; __cb"
                + id
                + "_aO = "
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(4, "}");
        // Whole member transitions (Missing/null/value): endpoint composition.
        code.AppendLineAt(
            4,
            "else if (" + KeyedWholeFlag(member) + " || next." + KeyedWholeFlag(member) + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            5,
            "var __w1b = "
                + KeyedWholeBefore(member)
                + "; var __w1a = "
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "var __w2b = next."
                + KeyedWholeBefore(member)
                + "; var __w2a = next."
                + KeyedWholeAfter(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "if (" + KeyedWholeFlag(member) + " && next." + KeyedWholeFlag(member) + ")"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!Fragment.__SparseEqual_"
                + id
                + "(__w1a, __w2b)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            6,
            "if (Fragment.__SparseEqual_" + id + "(__w1b, __w2a)) { __cb" + id + "_has = false; }"
        );
        code.AppendLineAt(
            6,
            "else { __cb"
                + id
                + "_whole = true; __cb"
                + id
                + "_wb = __w1b; __cb"
                + id
                + "_wa = __w2a; }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Granular + granular per-key merge.
        code.AppendLineAt(
            5,
            "var __map1"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ") __map1"
                + id
                + "[__it.Key] = __it;"
        );
        code.AppendLineAt(
            5,
            "var __map2"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if (next."
                + KeyedItems(member)
                + " is not null) foreach (var __it in next."
                + KeyedItems(member)
                + ") __map2"
                + id
                + "[__it.Key] = __it;"
        );
        code.AppendLineAt(
            5,
            "var __net"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __keys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__map1"
                + id
                + ".Keys, "
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "__keys" + id + ".UnionWith(__map2" + id + ".Keys);");
        // Index only second-only keys that occur in the retained first order.
        // Pure additions need no set allocation; small orders and a few queries
        // keep linear scans, since indexing sixteen-item edit benchmarks cost more.
        var continuityDictionary =
            "global::System.Collections.Generic.Dictionary<" + keyType + ", " + trans + ".Item>";
        code.AppendLineAt(
            5,
            "static global::System.Collections.Generic.HashSet<"
                + keyType
                + ">? __IndexContinuity"
                + id
                + "(global::System.Collections.Generic.List<"
                + keyType
                + "> order, "
                + continuityDictionary
                + " first, "
                + continuityDictionary
                + " second, int capacity)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "global::System.Collections.Generic.HashSet<" + keyType + ">? present = null;"
        );
        code.AppendLineAt(
            6,
            "foreach (var key in order!) if (second.ContainsKey(key) && !first.ContainsKey(key)) (present ??= new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(capacity, "
                + comparer
                + ")).Add(key);"
        );
        code.AppendLineAt(6, "return present;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "var __indexContinuity"
                + id
                + " = "
                + KeyedAfterOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + ".Count > 16 && __keys"
                + id
                + ".Count - __map1"
                + id
                + ".Count > 4;"
        );
        code.AppendLineAt(
            5,
            "var __presentSecondOnly"
                + id
                + " = __indexContinuity"
                + id
                + " ? __IndexContinuity"
                + id
                + "("
                + KeyedAfterOrder(member)
                + "!, __map1"
                + id
                + ", __map2"
                + id
                + ", global::System.Math.Min("
                + KeyedAfterOrder(member)
                + "!.Count, __keys"
                + id
                + ".Count - __map1"
                + id
                + ".Count)) : null;"
        );
        code.AppendLineAt(5, "foreach (var __k in __keys" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __has1 = __map1"
                + id
                + ".TryGetValue(__k, out var __a1); var __has2 = __map2"
                + id
                + ".TryGetValue(__k, out var __a2);"
        );
        // Directional continuity: second's expected before must hold in
        // first's after. First-only keys need no check (second adapts); second-only
        // keys validate presence against first's retained key orders so disjoint
        // edits compose while remove/edit of absent keys still fail.
        code.AppendLineAt(6, "if (!__has1)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __o1aChk = " + KeyedAfterOrder(member) + ";");
        code.AppendLineAt(
            7,
            "if (__o1aChk is not null) { bool __in1; if (__indexContinuity"
                + id
                + ") __in1 = __presentSecondOnly"
                + id
                + "?.Contains(__k) == true; else { __in1 = false; foreach (var __ok in __o1aChk) if ("
                + comparer
                + ".Equals(__ok, __k)) { __in1 = true; break; } } if (__a2!.IsAdded ? __in1 : !__in1) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(7, "__net" + id + "[__k] = __a2!; continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (!__has2) { __net" + id + "[__k] = __a1!; continue; }");
        // Both changed: validate continuity and merge by membership kind.
        // Added (Missing->after) cases.
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved && __a2!.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if ("
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!))).IsEmpty) continue;"
        );
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!));"
        );
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!));"
        );
        code.AppendLineAt(7, "var __edit = " + elementCs + ".Between(__eb, __ea);");
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, false, __edit, false);"
        );
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved || __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        // Added then edited: continuity first.After == second.Before.
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea2 = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.After.Value!));"
        );
        code.AppendLineAt(7, "var __edit2 = " + elementCs + ".Between(default, __ea2);");
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, default, __a2.After, -1, __a2.AfterIndex, true, false, false, false, __edit2, false);"
        );
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        // Edited then removed: continuity first.After == second.Before.
        code.AppendLineAt(6, "if ((__a1!.IsEdited || __a1!.IsReordered) && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__a1.IsEdited && !"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before.IsPresent ? __a1.Before : __a2.Before, default, __a1.BeforeIndex >= 0 ? __a1.BeforeIndex : __a2.BeforeIndex, -1, false, true, false, false, "
                + elementCs
                + ".Between(__a1.IsEdited ? "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a1.Before.Value!)) : default, default), false);"
        );
        // Fixup Before when first was reorder-only (no Before stored? reorder-only has Before present).
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        // Edited/edited (or reorder-involved) continuity via nested compose; scalar fallback via equality.
        code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __cc = __a1.Edit.Compose(__a2.Edit);");
        code.AppendLineAt(7, "if (__cc.IsEmpty) { continue; }");
        code.AppendLineAt(
            7,
            "__net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, __a1.IsReordered || __a2.IsReordered, __cc, false);"
        );
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsEdited || __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__a1.IsEdited && __a2.IsReordered && !__a2.IsEdited) { __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a1.Before, __a1.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, true, __a1.Edit, false); continue; }"
        );
        code.AppendLineAt(
            7,
            "if (__a2.IsEdited && __a1.IsReordered && !__a1.IsEdited) { var __cc2 = __a2.Edit; __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __a2.Before, __a2.After, __a1.BeforeIndex, __a2.AfterIndex, false, false, true, true, __cc2, false); continue; }"
        );
        code.AppendLineAt(
            7,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(6, "}");
        // Reorder-only on both or one side: continuity holds (same values); net keeps reorder flag.
        code.AppendLineAt(
            6,
            "if (__a1!.IsReordered || __a2!.IsReordered) { var __keep = __a2!.IsReordered || __a1!.IsReordered; var __b = __a1.Before.IsPresent ? __a1.Before : __a2.Before; var __a = __a2.After.IsPresent ? __a2.After : __a1.After; var __bi = __a1.BeforeIndex >= 0 ? __a1.BeforeIndex : __a2.BeforeIndex; var __ai = __a2.AfterIndex >= 0 ? __a2.AfterIndex : __a1.AfterIndex; __net"
                + id
                + "[__k] = new "
                + trans
                + ".Item(__k, __b, __a, __bi, __ai, false, false, false, true, "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a.Value!))), false); continue; }"
        );
        code.AppendLineAt(
            6,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "}");
        // Net no-op normalization is implicit (empty dict => has false below).
        // Orders: next wins when present (filtered to net keys), else first filtered (mirrors Patch order compose).
        code.AppendLineAt(
            5,
            "var __netRemoved"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "var __addedCount" + id + " = 0;");
        code.AppendLineAt(
            5,
            "foreach (var __kv in __net"
                + id
                + ") { if (__kv.Value.IsAdded) __addedCount"
                + id
                + "++; if (__kv.Value.IsRemoved) __netRemoved"
                + id
                + ".Add(__kv.Key); }"
        );
        // The union key set is no longer needed after merging. Reuse its capacity
        // for pending additions when additions would otherwise cause repeated scans.
        // These keys are a subset of the original union, so the set never grows.
        code.AppendLineAt(5, "var __indexAddedOrder" + id + " = __addedCount" + id + " > 4;");
        code.AppendLineAt(
            5,
            "if (__indexAddedOrder"
                + id
                + ") { __keys"
                + id
                + ".Clear(); foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded) __keys"
                + id
                + ".Add(__kv.Key); }"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o1b = "
                + KeyedBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o1a = "
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o2b = next."
                + KeyedBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o2a = next."
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<" + keyType + ">? __nbO" + id + " = __o1b; "
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<" + keyType + ">? __naO" + id + " = null;"
        );
        code.AppendLineAt(5, "if (__o2a is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __no = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__o2a.Count); foreach (var __k in __o2a) if (!__netRemoved"
                + id
                + ".Contains(__k)) { __no.Add(__k); if (__indexAddedOrder"
                + id
                + ") __keys"
                + id
                + ".Remove(__k); }"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded && (__indexAddedOrder"
                + id
                + " ? __keys"
                + id
                + ".Remove(__kv.Key) : !__no.Contains(__kv.Key, "
                + comparer
                + "))) __no.Add(__kv.Key);"
        );
        code.AppendLineAt(6, "__naO" + id + " = __no;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__o1a is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __no = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__o1a.Count); foreach (var __k in __o1a) if (!__netRemoved"
                + id
                + ".Contains(__k)) { __no.Add(__k); if (__indexAddedOrder"
                + id
                + ") __keys"
                + id
                + ".Remove(__k); }"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded && (__indexAddedOrder"
                + id
                + " ? __keys"
                + id
                + ".Remove(__kv.Key) : !__no.Contains(__kv.Key, "
                + comparer
                + "))) __no.Add(__kv.Key);"
        );
        code.AppendLineAt(6, "__naO" + id + " = __no;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__net" + id + ".Count == 0) { }");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            6,
            "var __elist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(__net"
                + id
                + ".Count);"
        );
        // Enumeration: net after-order then net removed in net before-order (mirrors Between).
        code.AppendLineAt(6, "if (__naO" + id + " is not null)");
        code.AppendLineAt(
            6,
            "{ foreach (var __k in __naO"
                + id
                + ") if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && !__e.IsRemoved) __elist"
                + id
                + ".Add(__e); }"
        );
        code.AppendLineAt(
            6,
            "else foreach (var __kv in __net"
                + id
                + ") if (!__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        code.AppendLineAt(6, "if (__nbO" + id + " is not null)");
        code.AppendLineAt(
            6,
            "{ foreach (var __k in __nbO"
                + id
                + ") if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && __e.IsRemoved) __elist"
                + id
                + ".Add(__e); }"
        );
        code.AppendLineAt(
            6,
            "else foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        code.AppendLineAt(6, "__cb" + id + "_items = __elist" + id + ";");
        code.AppendLineAt(
            6,
            "__cb" + id + "_bO = __nbO" + id + "; __cb" + id + "_aO = __naO" + id + ";"
        );
        // Normalize order-only equality to sparse (no orders when equal).
        code.AppendLineAt(
            6,
            "if (__nbO"
                + id
                + " is not null && __naO"
                + id
                + " is not null && "
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__nbO"
                + id
                + ", __naO"
                + id
                + ") && __net"
                + id
                + ".Count != 0) { bool __onlyOrder = true; foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded || __kv.Value.IsRemoved || __kv.Value.IsEdited) { __onlyOrder = false; break; } if (__onlyOrder && __elist"
                + id
                + ".Count == 0) { __cb"
                + id
                + "_has = false; __cb"
                + id
                + "_items = null; __cb"
                + id
                + "_bO = null; __cb"
                + id
                + "_aO = null; } }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }
}
