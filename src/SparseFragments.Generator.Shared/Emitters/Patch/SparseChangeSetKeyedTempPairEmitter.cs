namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity pair merging for keyed ChangeSet composition.</summary>
/// <remarks>
/// Mirrors the assigned pair matrix with Guid identity so temporary-keyed
/// flows compose without sentinel collisions.
/// </remarks>
internal static class SparseChangeSetKeyedTempPairEmitter
{
    /// <summary>Emits the temp pair-merge loop over the union of temp keys.</summary>
    /// <remarks>
    /// Mirrors the assigned pair matrix with Guid identity: continuity first
    /// (second-only items validate against the first after-temp order), then
    /// membership-kind merging with the same nested element algebra.
    /// Produced items carry the sentinel key with the Guid stamp; absolute
    /// indexes are fixed during order finalization.
    /// </remarks>
    internal static void AppendTempPairMerge(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string elementType,
        int id
    )
    {
        var sentinel = member.Collection.UnassignedKeyExpression ?? "default!";
        code.AppendLineAt(5, "if (__hasTemp" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __firstTempAfter"
                + id
                + " = "
                + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            6,
            "var __secondTempAfter"
                + id
                + " = next."
                + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            6,
            "if (__firstTempAfter"
                + id
                + " is null || "
                + SparseChangeSetBasicsEmitter.KeyedTempBeforeOrder(member)
                + " is null || __secondTempAfter"
                + id
                + " is null) throw new global::System.InvalidOperationException(\"ChangeSet composition requires temporary orders for temporary changes.\");"
        );
        code.AppendLineAt(
            6,
            "var __firstTempAfterSet"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>(__firstTempAfter"
                + id
                + ");"
        );
        code.AppendLineAt(
            6,
            "var __tkeys"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>(__tmap1"
                + id
                + ".Keys);"
        );
        code.AppendLineAt(6, "__tkeys" + id + ".UnionWith(__tmap2" + id + ".Keys);");
        code.AppendLineAt(6, "foreach (var __tk in __tkeys" + id + ")");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __thas1 = __tmap1" + id + ".TryGetValue(__tk, out var __ta1);");
        code.AppendLineAt(7, "var __thas2 = __tmap2" + id + ".TryGetValue(__tk, out var __ta2);");
        // Second-only continuity against the complete first after-temp order.
        code.AppendLineAt(7, "if (!__thas1)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (__ta2!.IsAdded ? __firstTempAfterSet"
                + id
                + ".Contains(__tk) : !__firstTempAfterSet"
                + id
                + ".Contains(__tk)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "__tnet" + id + "[__tk] = __ta2!; continue;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "if (!__thas2) { __tnet" + id + "[__tk] = __ta1!; continue; }");
        AppendTempPairCases(code, member, trans, runtime, elementCs, elementFrag, sentinel, id);
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
    }

    private static void AppendTempPairCases(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string sentinel,
        int id
    )
    {
        // Both changed: validate continuity and merge by membership kind.
        code.AppendLineAt(7, "if (__ta1!.IsAdded && __ta2!.IsAdded)");
        code.AppendLineAt(
            7,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(7, "if (__ta1!.IsAdded && __ta2!.IsRemoved)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (!"
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta1.After.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta2.Before.Value!))).IsEmpty) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "if (__ta1!.IsRemoved && __ta2!.IsAdded)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if ("
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta1.Before.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta2.After.Value!))).IsEmpty) continue;"
        );
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __teb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta1.Before.Value!));"
        );
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __tea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta2.After.Value!));"
        );
        code.AppendLineAt(8, "var __tedit = " + elementCs + ".Between(__teb, __tea);");
        code.AppendLineAt(
            8,
            "__tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, __ta1.Before, __ta2.After, -1, -1, false, false, true, false, __tedit, false);"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "if (__ta1!.IsRemoved || __ta2!.IsAdded)");
        code.AppendLineAt(
            7,
            "{ throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\"); }"
        );
        code.AppendLineAt(7, "if (__ta1!.IsAdded && __ta2!.IsEdited)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(8, "var __tedit2 = __ta1.Edit.Compose(__ta2.Edit);");
        code.AppendLineAt(8, "var __taddedState = __tedit2.ToPatch().Apply(default);");
        code.AppendLineAt(
            8,
            "if (!__taddedState.IsPresent || __taddedState.Value is null) throw new global::System.InvalidOperationException(\"Composed addition did not produce an element value.\");"
        );
        code.AppendLineAt(8, "var __taddedValue = __taddedState.Value!.ToModel();");
        code.AppendLineAt(
            8,
            "__tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, default, "
                + runtime
                + "Optional<"
                + SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                + ">.Present(__taddedValue), -1, -1, true, false, false, false, __tedit2, false);"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "if ((__ta1!.IsEdited || __ta1!.IsReordered) && __ta2!.IsRemoved)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "var __tremovedEdit = __ta1.IsEdited ? __ta1.Edit.Compose(__ta2.Edit) : __ta2.Edit;"
        );
        code.AppendLineAt(
            8,
            "var __tremovedState = __tremovedEdit.Invert().ToPatch().Apply(default);"
        );
        code.AppendLineAt(
            8,
            "if (!__tremovedState.IsPresent || __tremovedState.Value is null) throw new global::System.InvalidOperationException(\"Composed removal did not produce an element value.\");"
        );
        code.AppendLineAt(8, "var __tremovedValue = __tremovedState.Value!.ToModel();");
        code.AppendLineAt(
            8,
            "__tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, "
                + runtime
                + "Optional<"
                + SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                + ">.Present(__tremovedValue), default, -1, -1, false, true, false, false, __tremovedEdit, false);"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "if (__ta1!.IsEdited && __ta2!.IsEdited)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(8, "var __tcc = __ta1.Edit.Compose(__ta2.Edit);");
        code.AppendLineAt(8, "if (__tcc.IsEmpty) { continue; }");
        code.AppendLineAt(
            8,
            "__tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, __ta1.Before, __ta2.After, -1, -1, false, false, true, __ta1.IsReordered || __ta2.IsReordered, __tcc, false);"
        );
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "if (__ta1!.IsEdited || __ta2!.IsEdited)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (__ta1.IsEdited && __ta2.IsReordered && !__ta2.IsEdited) { __tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, __ta1.Before, __ta1.After, -1, -1, false, false, true, true, __ta1.Edit, false); continue; }"
        );
        code.AppendLineAt(
            8,
            "if (__ta2.IsEdited && __ta1.IsReordered && !__ta1.IsEdited) { var __tcc2 = __ta2.Edit; __tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, __ta2.Before, __ta2.After, -1, -1, false, false, true, true, __tcc2, false); continue; }"
        );
        code.AppendLineAt(
            8,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "if (__ta1!.IsReordered || __ta2!.IsReordered) { var __tb = __ta1.Before.IsPresent ? __ta1.Before : __ta2.Before; var __ta = __ta2.After.IsPresent ? __ta2.After : __ta1.After; __tnet"
                + id
                + "[__tk] = new "
                + trans
                + ".Item("
                + sentinel
                + ", __tk, __tb, __ta, -1, -1, false, false, false, true, "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__tb.Value!)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ta.Value!))), false); continue; }"
        );
        code.AppendLineAt(
            7,
            "throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
    }
}
