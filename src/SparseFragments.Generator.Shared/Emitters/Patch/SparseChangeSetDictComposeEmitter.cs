using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
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

/// <summary>Per-key sparse compose for dictionaries.</summary>
internal static class SparseChangeSetDictComposeEmitter
{
    /// <summary>Emits per-key sparse compose for a dictionary member.</summary>
    internal static void AppendDictComposeSparse(
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
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
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
        code.AppendLineAt(5, "__cb" + id + "_items = next." + KeyedItems(member) + ";");
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
        code.AppendLineAt(5, "__cb" + id + "_items = " + KeyedItems(member) + ";");
        code.AppendLineAt(4, "}");
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
        // Both whole: continuity on middle, net whole when non-empty.
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
        // Mixed whole/granular member transitions are out of the sparse fast path;
        // require endpoint contiguity via the canonical patch composition.
        code.AppendLineAt(
            6,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __map1"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + KeyedItems(member)
                + "?.Count ?? 0, "
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
                + "[__it.Key!] = __it;"
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
                + "next."
                + KeyedItems(member)
                + "?.Count ?? 0, "
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
                + "[__it.Key!] = __it;"
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
        // Keys present in only one input always survive composition.
        // Reserve their count without allocating for fully overlapping cancellations.
        code.AppendLineAt(
            5,
            "var __capacity"
                + id
                + " = (__keys"
                + id
                + ".Count - __map1"
                + id
                + ".Count) + (__keys"
                + id
                + ".Count - __map2"
                + id
                + ".Count);"
        );
        var netType =
            "global::System.Collections.Generic.Dictionary<" + keyType + ", " + trans + ".Item>";
        code.AppendLineAt(
            5,
            "var __net"
                + id
                + " = __capacity"
                + id
                + " == 0 ? new "
                + netType
                + "("
                + comparer
                + ") : new "
                + netType
                + "(__capacity"
                + id
                + ", "
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "foreach (var __k in __keys" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __has1 = __map1"
                + id
                + ".TryGetValue(__k!, out var __a1); var __has2 = __map2"
                + id
                + ".TryGetValue(__k!, out var __a2);"
        );
        code.AppendLineAt(6, "if (!__has1) { __net" + id + "[__k!] = __a2!; continue; }");
        code.AppendLineAt(6, "if (!__has2) { __net" + id + "[__k!] = __a1!; continue; }");
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
                + facade
                + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved && __a2!.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if ("
                + facade
                + ".AreEqual((object?)__a1.Before.Value, (object?)__a2.After.Value)) continue;"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a1.Before.Value!));"
            );
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a2.After.Value!));"
            );
            code.AppendLineAt(7, "var __edit = " + valueCs + ".Between(__eb, __ea);");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, __edit, false);"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, false);"
            );
        }
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsRemoved || __a2!.IsAdded)");
        code.AppendLineAt(
            6,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(6, "if (__a1!.IsAdded && __a2!.IsEdited)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + facade
                + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea2 = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a2.After.Value!));"
            );
            code.AppendLineAt(7, "var __edit2 = " + valueCs + ".Between(default, __ea2);");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, default, __a2.After, true, false, false, __edit2, false);"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, default, __a2.After, true, false, false, false);"
            );
        }
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (!"
                + facade
                + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb1 = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__a1.Before.Value!));"
            );
            code.AppendLineAt(7, "var __redit = " + valueCs + ".Between(__eb1, default);");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, default, false, true, false, __redit, false);"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, default, false, true, false, false);"
            );
        }
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        if (hasPatch)
        {
            code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsEdited)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(7, "var __cc = __a1.Edit.Compose(__a2.Edit);");
            code.AppendLineAt(7, "if (__cc.IsEmpty) { continue; }");
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, __cc, false);"
            );
            code.AppendLineAt(6, "continue;");
            code.AppendLineAt(6, "}");
            code.AppendLineAt(
                6,
                "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
        }
        else
        {
            code.AppendLineAt(6, "if (__a1!.IsEdited && __a2!.IsEdited)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__a1.After.Value, (object?)__a2.Before.Value)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
            code.AppendLineAt(
                7,
                "if ("
                    + facade
                    + ".AreEqual((object?)__a1.Before.Value, (object?)__a2.After.Value)) { continue; }"
            );
            code.AppendLineAt(
                7,
                "__net"
                    + id
                    + "[__k!] = new "
                    + trans
                    + ".Item(__k!, __a1.Before, __a2.After, false, false, true, false);"
            );
            code.AppendLineAt(6, "continue;");
            code.AppendLineAt(6, "}");
            code.AppendLineAt(
                6,
                "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
            );
        }
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
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net" + id + ") __elist" + id + ".Add(__kv.Value);"
        );
        code.AppendLineAt(6, "__cb" + id + "_items = __elist" + id + ";");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }
}
