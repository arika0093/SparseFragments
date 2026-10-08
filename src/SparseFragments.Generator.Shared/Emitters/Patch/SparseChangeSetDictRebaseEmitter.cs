using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
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

/// <summary>Sparse per-key rebase for dictionaries.</summary>
internal static class SparseChangeSetDictRebaseEmitter
{
    /// <summary>Emits sparse per-key rebase for a dictionary member.</summary>
    internal static void AppendDictRebaseSparse(
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
        var valueType = ValueTypeOf(member);
        var optValue = runtime + "Optional<" + valueType + ">";
        var facade = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var trans = TransNameFor(members, member);
        code.AppendLineAt(4, "if (" + HasField(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "var __curM" + id + " = __cur." + esc + ";");
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
                + "(new string[] { "
                + lit
                + " }, "
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
                + "(new string[] { "
                + lit
                + " }, "
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
        code.AppendLineAt(6, "if (__it.IsAdded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__curM" + id + ".Value!.ContainsKey(__it.Key!))");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "__any" + id + " = true;");
        if (hasPatch)
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, default, __it.After, true, false, false, __it.Edit, false));"
            );
        else
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, default, __it.After, true, false, false, false));"
            );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else if (!"
                + facade
                + ".AreEqual(__curM"
                + id
                + ".Value![__it.Key!], __it.After.Value))"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__it.After.Value), "
                + runtime
                + "Optional<object?>.Present((object?)__curM"
                + id
                + ".Value![__it.Key!]), \"The dictionary entry conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__it.IsRemoved)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (!__curM" + id + ".Value!.ContainsKey(__it.Key!)) { }");
        code.AppendLineAt(
            6,
            "else if (!"
                + facade
                + ".AreEqual(__curM"
                + id
                + ".Value![__it.Key!], __it.Before.Value))"
        );
        code.AppendLineAt(
            6,
            "{ __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                + conflictKind
                + ".Nested, "
                + runtime
                + "Optional<object?>.Present((object?)__it.Before.Value), "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Present((object?)__curM"
                + id
                + ".Value![__it.Key!]), \"The dictionary entry conflicts with a concurrent change.\")); }"
        );
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "__any" + id + " = true;");
        if (hasPatch)
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __it.Before, default, false, true, false, __it.Edit, false));"
            );
        else
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __it.Before, default, false, true, false, false));"
            );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        if (hasPatch)
        {
            code.AppendLineAt(
                6,
                "if (!__curM" + id + ".Value!.TryGetValue(__it.Key!, out var __cev))"
            );
            code.AppendLineAt(
                6,
                "{ __conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + lit
                    + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Missing, \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "var __nr = __it.Edit.RebaseOnto("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__cev)));"
            );
            code.AppendLineAt(
                7,
                "foreach (var __cc in __nr.Conflicts) __conflicts.Add(__cc.WithPathPrefix("
                    + lit
                    + "));"
            );
            code.AppendLineAt(7, "if (__nr.Conflicts.Count == 0 && !__nr.Patch.IsEmpty)");
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                8,
                "var __applied = __nr.Patch.ToPatch().Apply("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__cev)));"
            );
            code.AppendLineAt(8, "if (__applied.IsPresent && __applied.Value is not null)");
            code.AppendLineAt(8, "{");
            code.AppendLineAt(8, "__any" + id + " = true;");
            code.AppendLineAt(
                8,
                optValue
                    + " __nb = "
                    + optValue
                    + ".Present(__cev); "
                    + optValue
                    + " __na2 = "
                    + optValue
                    + ".Present(__applied.Value!.ToModel());"
            );
            code.AppendLineAt(
                8,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __nb, __na2, false, false, true, __nr.Patch, false));"
            );
            code.AppendLineAt(8, "}");
            code.AppendLineAt(7, "}");
            code.AppendLineAt(6, "}");
        }
        else
        {
            code.AppendLineAt(
                6,
                "if (!__curM" + id + ".Value!.TryGetValue(__it.Key!, out var __cev2))"
            );
            code.AppendLineAt(
                6,
                "{ __conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + lit
                    + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Missing, \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLineAt(6, "else if (" + facade + ".AreEqual(__cev2, __it.After.Value)) { }");
            code.AppendLineAt(6, "else if (!" + facade + ".AreEqual(__cev2, __it.Before.Value))");
            code.AppendLineAt(
                6,
                "{ __conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + lit
                    + ", ((object?)__it.Key!)?.ToString() ?? \"<null>\" }, "
                    + conflictKind
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.Before.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__it.After.Value), "
                    + runtime
                    + "Optional<object?>.Present((object?)__cev2), \"The dictionary entry conflicts with a concurrent change.\")); }"
            );
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(7, "__any" + id + " = true;");
            code.AppendLineAt(
                7,
                "__rlist"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__it.Key!, __it.Before, __it.After, false, false, true, false));"
            );
            code.AppendLineAt(6, "}");
        }
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
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
                + "; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "}");
    }
}
