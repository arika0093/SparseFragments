namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity merge for keyed-sequence ChangeSet composition.</summary>
/// <remarks>
/// Handles unassigned elements that carry a stable Guid identity alongside
/// the assigned-key machinery owned by
/// <see cref="SparseChangeSetKeyedComposeEmitter"/>. The assigned path stays
/// untouched: temp items are segregated before it runs, merged here by Guid,
/// and reunited with assigned results during order finalization.
/// </remarks>
internal static class SparseChangeSetKeyedTempMergeEmitter
{
    /// <summary>Emits the whole-path before-state guard, temp-aware.</summary>
    /// <remarks>
    /// Temporary baselines stay composable: temp items correlate by Guid in
    /// the pair merge instead of failing here.
    /// </remarks>
    internal static void AppendWholeBeforeGuard(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id,
        bool hasTempCompose
    )
    {
        var check =
            "if (__second"
            + id
            + "_has) { if (next."
            + SparseChangeSetBasicsEmitter.KeyedWholeFlag(member)
            + ") { var __nextBefore = next."
            + SparseChangeSetBasicsEmitter.KeyedWholeBefore(member)
            + "; if (__nextBefore.IsPresent && (object?)__nextBefore.Value is not null) foreach (var __item in __nextBefore.Value!) if ("
            + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                member,
                "__SparseKeyOf_ChangeSet_" + id + "(__item)"
            )
            + ") throw new global::System.InvalidOperationException(\"A ChangeSet whose before-state contains an unassigned key cannot be composed.\"); } else if (next."
            + SparseChangeSetBasicsEmitter.KeyedBeforeOrder(member)
            + " is not null) foreach (var __baselineKey in next."
            + SparseChangeSetBasicsEmitter.KeyedBeforeOrder(member)
            + ") if ("
            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__baselineKey")
            + ") throw new global::System.InvalidOperationException(\"A ChangeSet whose before-state contains an unassigned key cannot be composed.\"); }";
        if (hasTempCompose)
            code.AppendLineAt(4, "if (!__hasTemp" + id + ") { " + check + " }");
        else
            code.AppendLineAt(4, check);
    }

    /// <summary>Emits the granular before-state guard, temp-aware.</summary>
    internal static void AppendGranularBeforeGuard(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id,
        bool hasTempCompose
    )
    {
        var granularCheck =
            "if (next."
            + SparseChangeSetBasicsEmitter.KeyedBeforeOrder(member)
            + " is not null) foreach (var __baselineKey in next."
            + SparseChangeSetBasicsEmitter.KeyedBeforeOrder(member)
            + ") if ("
            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__baselineKey")
            + ") throw new global::System.InvalidOperationException(\"A ChangeSet whose before-state contains an unassigned key cannot be composed.\");";
        if (hasTempCompose)
            code.AppendLineAt(5, "if (!__hasTemp" + id + ") " + granularCheck);
        else
            code.AppendLineAt(5, granularCheck);
    }

    /// <summary>Emits temp map declarations plus the temp-presence scan.</summary>
    internal static void AppendTempLocals(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        int id
    )
    {
        code.AppendLineAt(4, "bool __hasTemp" + id + " = false;");
        code.AppendLineAt(
            4,
            "var __tmap1"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            4,
            "var __tmap2"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            4,
            "var __tnet"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            4,
            "if (" + SparseChangeSetBasicsEmitter.KeyedItems(member) + " is not null)"
        );
        code.AppendLineAt(
            5,
            "foreach (var __it in "
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + ") if (__it.TemporaryKey.HasValue) { __hasTemp"
                + id
                + " = true; break; }"
        );
        code.AppendLineAt(
            4,
            "if (!__hasTemp"
                + id
                + " && next."
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + " is not null)"
        );
        code.AppendLineAt(
            5,
            "foreach (var __it in next."
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + ") if (__it.TemporaryKey.HasValue) { __hasTemp"
                + id
                + " = true; break; }"
        );
    }

    /// <summary>Emits temp segregation for first-side items.</summary>
    internal static void AppendSegregateFirst(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        string trans,
        string comparer,
        int id
    )
    {
        code.AppendLineAt(
            5,
            "var __map1"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + trans
                + ".Item>("
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + "?.Count ?? 0, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if ("
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + " is not null) foreach (var __it in "
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + ")"
        );
        code.AppendLineAt(
            6,
            "if (__hasTemp"
                + id
                + " && __it.TemporaryKey.HasValue) { if (__tmap1"
                + id
                + ".ContainsKey(__it.TemporaryKey.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); __tmap1"
                + id
                + ".Add(__it.TemporaryKey.Value, __it); }"
        );
        code.AppendLineAt(6, "else __map1" + id + "[__it.Key] = __it;");
    }

    /// <summary>Emits temp segregation for second-side items.</summary>
    internal static void AppendSegregateSecond(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        string trans,
        string comparer,
        int id
    )
    {
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
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + "?.Count ?? 0, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __unassignedNetItems"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(); if (next."
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + " is not null) foreach (var __it in next."
                + SparseChangeSetBasicsEmitter.KeyedItems(member)
                + ") { if (__hasTemp"
                + id
                + " && __it.TemporaryKey.HasValue) { if (__tmap2"
                + id
                + ".ContainsKey(__it.TemporaryKey.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); __tmap2"
                + id
                + ".Add(__it.TemporaryKey.Value, __it); } else if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__it.Key")
                + ") { if (!__it.IsAdded) throw new global::System.InvalidOperationException(\"An unassigned key cannot be used as a baseline change.\"); __unassignedNetItems"
                + id
                + ".Add(__it); } else __map2"
                + id
                + "[__it.Key] = __it; }"
        );
    }

    /// <summary>Emits temp-aware item enumeration for composed transitions.</summary>
    /// <remarks>
    /// Sentinel slots in the composed after-order consume the composed temp
    /// order one-to-one; indexes are absolute in the composed orders. Temp
    /// removals enumerate in before-temp order. Degenerate leftovers append
    /// without loss instead of being dropped.
    /// </remarks>
    internal static void AppendTempAssembly(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        int id
    )
    {
        var isUnassignedKey = SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k");
        var isUnassignedSlot = SparseKeyedCollectionEmitter.IsUnassignedExpression(
            member,
            "__nbO" + id + "[__i]"
        );
        code.AppendLineAt(
            6,
            "var __tbiMap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, int>();"
        );
        code.AppendLineAt(
            6,
            "var __firstTempBefore"
                + id
                + " = "
                + SparseChangeSetBasicsEmitter.KeyedTempBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            6,
            "if (__nbO" + id + " is not null && __firstTempBefore" + id + " is not null)"
        );
        code.AppendLineAt(
            6,
            "{ var __ti = 0; for (var __i = 0; __i < __nbO"
                + id
                + ".Count; __i++) if ("
                + isUnassignedSlot
                + " && __ti < __firstTempBefore"
                + id
                + ".Count) __tbiMap"
                + id
                + "[__firstTempBefore"
                + id
                + "[__ti++]] = __i; }"
        );
        code.AppendLineAt(6, "if (__naO" + id + " is not null)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var __slot" + id + " = 0; var __tpos" + id + " = 0;");
        code.AppendLineAt(7, "foreach (var __k in __naO" + id + ")");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(8, "if (" + isUnassignedKey + ")");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (__cb"
                + id
                + "_taO is not null && __tpos"
                + id
                + " < __cb"
                + id
                + "_taO.Count) { var __tt = __cb"
                + id
                + "_taO[__tpos"
                + id
                + "++]; if (__tnet"
                + id
                + ".TryGetValue(__tt, out var __te) && !__te.IsRemoved) { var __tbi = __tbiMap"
                + id
                + ".TryGetValue(__tt, out var __tbv) ? __tbv : -1; __elist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__te.Key, __tt, __te.Before, __te.After, __tbi, __slot"
                + id
                + ", __te.IsAdded, __te.IsRemoved, __te.IsEdited, __te.IsReordered, __te.Edit, false)); } }"
        );
        code.AppendLineAt(8, "__slot" + id + "++; continue;");
        code.AppendLineAt(8, "}");
        code.AppendLineAt(
            8,
            "if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && !__e.IsRemoved) __elist"
                + id
                + ".Add(__e);"
        );
        code.AppendLineAt(8, "__slot" + id + "++;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "if (__cb"
                + id
                + "_taO is not null) for (; __tpos"
                + id
                + " < __cb"
                + id
                + "_taO.Count; __tpos"
                + id
                + "++) { var __tt = __cb"
                + id
                + "_taO[__tpos"
                + id
                + "]; if (__tnet"
                + id
                + ".TryGetValue(__tt, out var __te) && !__te.IsRemoved) { var __tbi = __tbiMap"
                + id
                + ".TryGetValue(__tt, out var __tbv) ? __tbv : -1; __elist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__te.Key, __tt, __te.Before, __te.After, __tbi, __slot"
                + id
                + "++, __te.IsAdded, __te.IsRemoved, __te.IsEdited, __te.IsReordered, __te.Edit, false)); } }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "foreach (var __kv in __net"
                + id
                + ") if (!__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        code.AppendLineAt(
            7,
            "foreach (var __tkv in __tnet"
                + id
                + ") if (!__tkv.Value.IsRemoved) { var __te = __tkv.Value; __elist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__te.Key, __tkv.Key, __te.Before, __te.After, -1, -1, __te.IsAdded, __te.IsRemoved, __te.IsEdited, __te.IsReordered, __te.Edit, false)); }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__nbO" + id + " is not null)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "foreach (var __k in __nbO"
                + id
                + ") if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && __e.IsRemoved) __elist"
                + id
                + ".Add(__e);"
        );
        code.AppendLineAt(
            7,
            "if (__firstTempBefore"
                + id
                + " is not null) foreach (var __tt in __firstTempBefore"
                + id
                + ") if (__tnet"
                + id
                + ".TryGetValue(__tt, out var __te) && __te.IsRemoved) { var __tbi = __tbiMap"
                + id
                + ".TryGetValue(__tt, out var __tbv) ? __tbv : -1; __elist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__te.Key, __tt, __te.Before, __te.After, __tbi, -1, __te.IsAdded, __te.IsRemoved, __te.IsEdited, __te.IsReordered, __te.Edit, false)); }"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        code.AppendLineAt(
            7,
            "foreach (var __tkv in __tnet"
                + id
                + ") if (__tkv.Value.IsRemoved) { var __te = __tkv.Value; __elist"
                + id
                + ".Add(new "
                + trans
                + ".Item(__te.Key, __tkv.Key, __te.Before, __te.After, -1, -1, __te.IsAdded, __te.IsRemoved, __te.IsEdited, __te.IsReordered, __te.Edit, false)); }"
        );
        code.AppendLineAt(6, "}");
    }

    /// <remarks>
    /// The composed after-temp order is the second after-temp order plus
    /// first-only additions. Key-order sentinel slots stay aligned: the
    /// second-after branch already slots every composed temp, while the
    /// first-after fallback drops removed-temp slots and appends second-only
    /// additions. Both-orders-absent stays null with item-order fallback at
    /// assembly time.
    /// </remarks>
    internal static void AppendTempOrderFinalize(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        int id
    )
    {
        var sentinel = member.Collection.UnassignedKeyExpression ?? "default!";
        var isUnassigned = SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k");
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_tbO = "
                + SparseChangeSetBasicsEmitter.KeyedTempBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "__cb"
                + id
                + "_taO = next."
                + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member)
                + " ?? "
                + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(5, "if (__hasTemp" + id + ")");
        code.AppendLineAt(5, "{");
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
            "if (__secondTempAfter"
                + id
                + " is null) throw new global::System.InvalidOperationException(\"ChangeSet composition requires temporary orders for temporary changes.\");"
        );
        code.AppendLineAt(
            6,
            "var __cb"
                + id
                + "_taOlist = new global::System.Collections.Generic.List<global::System.Guid>(__secondTempAfter"
                + id
                + ");"
        );
        code.AppendLineAt(
            6,
            "foreach (var __tkv in __tnet"
                + id
                + ") if (__tkv.Value.IsAdded && !__cb"
                + id
                + "_taOlist.Contains(__tkv.Key)) __cb"
                + id
                + "_taOlist.Add(__tkv.Key);"
        );
        code.AppendLineAt(6, "__cb" + id + "_taO = __cb" + id + "_taOlist;");
        code.AppendLineAt(6, "if (__naO" + id + " is not null && __o2a is not null)");
        code.AppendLineAt(6, "{");
        // Second-after branch: slots already cover every composed temp; only
        // degenerate first-only additions (absent from second temps) append.
        code.AppendLineAt(
            7,
            "var __slotted"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>(__secondTempAfter"
                + id
                + ");"
        );
        code.AppendLineAt(
            7,
            "foreach (var __tkv in __tnet"
                + id
                + ") if (__tkv.Value.IsAdded && !__slotted"
                + id
                + ".Contains(__tkv.Key)) __naO"
                + id
                + ".Add("
                + sentinel
                + ");"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__naO" + id + " is not null)");
        code.AppendLineAt(6, "{");
        // First-after fallback: drop removed-temp slots, append second-only adds.
        code.AppendLineAt(
            7,
            "var __tremoved"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(
            7,
            "foreach (var __tkv in __tnet"
                + id
                + ") if (__tkv.Value.IsRemoved) __tremoved"
                + id
                + ".Add(__tkv.Key);"
        );
        code.AppendLineAt(
            7,
            "var __firstTempAfter"
                + id
                + " = "
                + SparseChangeSetBasicsEmitter.KeyedTempAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            7,
            "var __fixed"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__naO"
                + id
                + ".Count);"
        );
        code.AppendLineAt(
            7,
            "{ var __ti = 0; foreach (var __k in __naO"
                + id
                + ") { if ("
                + isUnassigned
                + ") { if (__firstTempAfter"
                + id
                + " is not null && __ti < __firstTempAfter"
                + id
                + ".Count && !__tremoved"
                + id
                + ".Contains(__firstTempAfter"
                + id
                + "[__ti])) __fixed"
                + id
                + ".Add(__k); __ti++; } else __fixed"
                + id
                + ".Add(__k); } }"
        );
        code.AppendLineAt(
            7,
            "if (__firstTempAfter"
                + id
                + " is not null) { var __slotted"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>(__firstTempAfter"
                + id
                + "); foreach (var __tkv in __tnet"
                + id
                + ") if (__tkv.Value.IsAdded && !__slotted"
                + id
                + ".Contains(__tkv.Key)) __fixed"
                + id
                + ".Add("
                + sentinel
                + "); }"
        );
        code.AppendLineAt(7, "__naO" + id + " = __fixed" + id + ";");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
    }
}
