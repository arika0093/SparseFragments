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
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Patch attach/project (FromPatch/ToPatch) plus direction inversion.</summary>
internal static class SparseChangeSetPatchSyncEmitter
{
    internal static void AppendFromPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optionalFragment,
        string? modelType
    )
    {
        _ = members;
        code.AppendLineAt(
            2,
            "/// <summary>Attaches a known baseline to an arbitrary patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static ChangeSet FromPatch(" + optionalFragment + " baseline, Patch patch)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
        );
        code.AppendLineAt(3, "var after = patch.Apply(baseline);");
        code.AppendLineAt(3, "return Between(baseline, after);");
        code.AppendLineAt(2, "}");
        if (modelType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Attaches an ordinary model baseline to an arbitrary patch.</summary>"
            );
            code.AppendLineAt(
                2,
                "public static ChangeSet FromPatch(" + modelType + " baseline, Patch patch)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "return FromPatch("
                    + optionalFragment
                    + ".Present(Fragment.From(baseline)), patch);"
            );
            code.AppendLineAt(2, "}");
        }
    }

    internal static void AppendToPatch(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string prefix,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var between = "Patch." + prefix + "Between";
        code.AppendLineAt(
            2,
            "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
        );
        code.AppendLineAt(2, "public Patch ToPatch()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return "
                + between
                + "(__sparse_wholeBefore, __sparse_wholeAfter);"
        );
        code.AppendLineAt(3, "var patch = new Patch();");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsNested(member))
            {
                code.AppendLineAt(3, "if (" + NestedField(member) + " is not null)");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "patch." + name + " = " + NestedField(member) + ".ToPatch();");
                code.AppendLineAt(3, "}");
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                AppendKeyedDictToPatch(code, member, name, dialect);
            }
            else
            {
                var vt = SparseFragmentPatchEmitter.GetMemberValueType(dialect, member);
                var op = runtime + "FragmentOperation<" + vt + ">";
                code.AppendLineAt(3, "if (" + HasField(member) + ")");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    "patch."
                        + name
                        + " = "
                        + AfterField(member)
                        + ".IsPresent ? "
                        + op
                        + ".Set("
                        + AfterField(member)
                        + ".Value) : "
                        + op
                        + ".Unset;"
                );
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(3, "return patch;");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendInvert(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment
    )
    {
        _ = runtime;
        _ = optionalFragment;
        // Per-key item inverters: reverse add/remove, nested edits,
        // endpoints, and indexes without recovering full snapshots.
        foreach (var member in members)
        {
            if (!IsKeyed(member) && !IsDict(member))
                continue;
            AppendItemInverter(code, members, member, runtime);
        }
        code.AppendLineAt(
            2,
            "/// <summary>Swaps the transition direction without requiring a separate baseline.</summary>"
        );
        code.AppendLineAt(2, "public ChangeSet Invert()");
        code.AppendLineAt(2, "{");
        var emptyTail = MemberEmptyTail(members);
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        if (string.IsNullOrEmpty(emptyTail))
            code.AppendLineAt(
                4,
                "return new ChangeSet(true, __sparse_wholeAfter, __sparse_wholeBefore);"
            );
        else
            code.AppendLineAt(
                4,
                "return new ChangeSet(true, __sparse_wholeAfter, __sparse_wholeBefore, "
                    + emptyTail
                    + ");"
            );
        code.AppendLineAt(3, "}");
        var args = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                args.Add(
                    NestedField(member) + " is null ? null : " + NestedField(member) + ".Invert()"
                );
            else if (IsKeyed(member))
            {
                args.AddRange(
                    new[]
                    {
                        HasField(member),
                        KeyedWholeFlag(member),
                        KeyedWholeAfter(member),
                        KeyedWholeBefore(member),
                        "__SparseInvertItems_" + member.Id + "(" + KeyedItems(member) + ")",
                        KeyedAfterOrder(member),
                        KeyedBeforeOrder(member),
                    }
                );
            }
            else if (IsDict(member))
            {
                args.AddRange(
                    new[]
                    {
                        HasField(member),
                        KeyedWholeFlag(member),
                        KeyedWholeAfter(member),
                        KeyedWholeBefore(member),
                        "__SparseInvertItems_" + member.Id + "(" + KeyedItems(member) + ")",
                    }
                );
            }
            else
                args.AddRange(new[] { AfterField(member), BeforeField(member), HasField(member) });
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits the per-key item inverter for a sparse keyed/dict member.</summary>
    internal static void AppendItemInverter(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        string runtime
    )
    {
        _ = runtime;
        var trans = TransNameFor(members, member);
        var id = member.Id;
        code.AppendLineAt(
            2,
            "private static global::System.Collections.Generic.List<"
                + trans
                + ".Item>? __SparseInvertItems_"
                + id
                + "(global::System.Collections.Generic.List<"
                + trans
                + ".Item>? items)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (items is null) return null;");
        code.AppendLineAt(
            3,
            "var __out = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(items.Count);"
        );
        code.AppendLineAt(3, "foreach (var __it in items)");
        code.AppendLineAt(3, "{");
        if (IsKeyed(member))
        {
            code.AppendLineAt(
                4,
                "__out.Add(new "
                    + trans
                    + ".Item(__it.Key, __it.After, __it.Before, __it.AfterIndex, __it.BeforeIndex, __it.IsRemoved, __it.IsAdded, __it.IsEdited, __it.IsReordered, __it.Edit.Invert(), false));"
            );
        }
        else
        {
            var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
            if (hasPatch)
                code.AppendLineAt(
                    4,
                    "__out.Add(new "
                        + trans
                        + ".Item(__it.Key, __it.After, __it.Before, __it.IsRemoved, __it.IsAdded, __it.IsEdited, __it.Edit.Invert(), false));"
                );
            else
                code.AppendLineAt(
                    4,
                    "__out.Add(new "
                        + trans
                        + ".Item(__it.Key, __it.After, __it.Before, __it.IsRemoved, __it.IsAdded, __it.IsEdited, false));"
                );
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return __out;");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits ToPatch projection for a sparse keyed/dict member.</summary>
    internal static void AppendKeyedDictToPatch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var __facade = dialect.RuntimeFacade;
        var __keyType = KeyTypeOf(member);
        var coll = "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        var isKeyed = IsKeyed(member);
        var hasValuePatch = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        code.AppendLineAt(3, "if (" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        // Whole presence/null transitions project via canonical collection Between.
        code.AppendLineAt(4, "if (" + KeyedWholeFlag(member) + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "patch."
                + escName
                + " = "
                + coll
                + ".Between("
                + KeyedWholeBefore(member)
                + ", "
                + KeyedWholeAfter(member)
                + ");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var __coll" + id + " = new " + coll + "();");
        code.AppendLineAt(
            5,
            "if ("
                + KeyedItems(member)
                + " is not null) foreach (var __it in "
                + KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(5, "{");
        if (isKeyed)
            code.AppendLineAt(6, "if (__it.IsAdded) __coll" + id + ".Add(__it.After.Value!);");
        else
            code.AppendLineAt(
                6,
                "if (__it.IsAdded) __coll" + id + ".SetEntry(__it.Key!, __it.After.Value!);"
            );
        code.AppendLineAt(6, "else if (__it.IsRemoved)");
        if (isKeyed)
            code.AppendLineAt(6, "{ __coll" + id + ".Remove(__it.Key); }");
        else
            code.AppendLineAt(6, "{ __coll" + id + ".RemoveEntry(__it.Key!); }");
        code.AppendLineAt(6, "else if (__it.IsEdited)");
        code.AppendLineAt(6, "{");
        if (hasValuePatch)
        {
            code.AppendLineAt(
                7,
                "__coll" + id + ".__SparseSetEdited(__it.Key, __it.Edit.ToPatch());"
            );
        }
        else if (isKeyed)
        {
            code.AppendLineAt(7, "__coll" + id + ".Update(__it.After.Value!);");
        }
        else
        {
            code.AppendLineAt(7, "__coll" + id + ".UpdateEntry(__it.Key!, __it.After.Value!);");
        }
        code.AppendLineAt(6, "}");
        // Reorder-only items contribute no add/remove/edit; order is applied below.
        code.AppendLineAt(5, "}");
        if (isKeyed)
        {
            // Full key orders are always retained for granular transitions; project
            // an order patch only when the order semantically changed.
            code.AppendLineAt(
                5,
                "if ("
                    + KeyedBeforeOrder(member)
                    + " is not null && "
                    + KeyedAfterOrder(member)
                    + " is not null && !"
                    + __facade
                    + ".KeyOrderEquals<"
                    + __keyType
                    + ">("
                    + KeyedBeforeOrder(member)
                    + ", "
                    + KeyedAfterOrder(member)
                    + ")) __coll"
                    + id
                    + ".SetOrder("
                    + KeyedAfterOrder(member)
                    + ");"
            );
        }
        code.AppendLineAt(5, "patch." + escName + " = __coll" + id + ";");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }
}
