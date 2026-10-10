namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity operation merge for keyed-sequence patches.</summary>
/// <remarks>
/// Merges Guid-keyed removal/edit logs and routes temporary adds through
/// cancellation and edit-application. Assigned-key logs stay in
/// <see cref="SparseKeyedSequenceComposeEmitter"/>.
/// </remarks>
internal static class SparseKeyedSequenceTempPatchMergeEmitter
{
    /// <summary>Emits the full temporary merge for patch Compose.</summary>
    internal static void AppendPatchTempMerge(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        bool hasPatch,
        string runtime
    )
    {
        var editedValueType = hasPatch
            ? SparseKeyedCollectionEmitter.ElementPatchType(member)
            : elementType;
        AppendTempRemovalMerge(code, member.Id);
        AppendThisTempAdds(code, member, hasPatch, runtime, member.Id);
        AppendTempEditMerge(code, editedValueType, hasPatch, member.Id);
    }

    /// <summary>Emits temporary partition of this-side adds for patch Compose.</summary>
    internal static void AppendThisTempPartition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        int id
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLineAt(
            4,
            "var thisTempAdded"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLineAt(
            4,
            "var thisTempAddedOrder"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLineAt(
            4,
            "if (__added is not null) foreach (var item in __added) { var __tk = "
                + tempOf
                + "(item); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item)"
                )
                + " && __tk.HasValue && __tk.Value != global::System.Guid.Empty) { if (thisTempAdded"
                + id
                + ".ContainsKey(__tk.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); thisTempAdded"
                + id
                + ".Add(__tk.Value, item); thisTempAddedOrder"
                + id
                + ".Add(__tk.Value); } }"
        );
    }

    /// <summary>Emits temporary partition of next-side adds for patch Compose.</summary>
    internal static void AppendNextTempPartition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        int id
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLineAt(
            4,
            "var nextTempAdded"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLineAt(
            4,
            "if (next.__added is not null) foreach (var item in next.__added) { var __tk = "
                + tempOf
                + "(item); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item)"
                )
                + " && __tk.HasValue && __tk.Value != global::System.Guid.Empty) { if (nextTempAdded"
                + id
                + ".ContainsKey(__tk.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection patch.\"); nextTempAdded"
                + id
                + ".Add(__tk.Value, item); } }"
        );
    }

    /// <summary>Emits temporary removal merge for patch Compose.</summary>
    internal static void AppendTempRemovalMerge(SharedIndentedBuilder code, int id)
    {
        // Net temporary removals: removed by either side, minus re-added later.
        code.AppendLineAt(
            4,
            "var netRemovedTemp"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLineAt(
            4,
            "if (__removedTemp is not null) foreach (var __t in __removedTemp) if (!nextTempAdded"
                + id
                + ".ContainsKey(__t) && !netRemovedTemp"
                + id
                + ".Contains(__t)) netRemovedTemp"
                + id
                + ".Add(__t);"
        );
        code.AppendLineAt(
            4,
            "if (next.__removedTemp is not null) foreach (var __t in next.__removedTemp) if ((!thisTempAdded"
                + id
                + ".ContainsKey(__t) || ((__removedTemp is not null) && __removedTemp.Contains(__t))) && !netRemovedTemp"
                + id
                + ".Contains(__t)) netRemovedTemp"
                + id
                + ".Add(__t);"
        );
        code.AppendLineAt(
            4,
            "if (netRemovedTemp"
                + id
                + ".Count > 0) result.__removedTemp = netRemovedTemp"
                + id
                + ";"
        );
    }

    /// <summary>Emits this-side temporary adds with next-side cancellation and edits.</summary>
    internal static void AppendThisTempAdds(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        bool hasPatch,
        string runtime,
        int id
    )
    {
        code.AppendLineAt(4, "foreach (var __tt in thisTempAddedOrder" + id + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (next.__removedTemp is not null && next.__removedTemp.Contains(__tt)) continue;"
        );
        code.AppendLineAt(5, "var __titem = thisTempAdded" + id + "[__tt];");
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLineAt(
                5,
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tt, out var __tnextEdit))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var __tapplied = __tnextEdit.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__titem)));"
            );
            code.AppendLineAt(
                6,
                "if (!__tapplied.IsPresent || __tapplied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(6, "netAdded.Add(__tapplied.Value!.ToModel());");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else netAdded.Add(__titem);");
        }
        else
        {
            code.AppendLineAt(
                5,
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tt, out var __tupd)) netAdded.Add(__tupd); else netAdded.Add(__titem);"
            );
        }
        code.AppendLineAt(4, "}");
    }

    /// <summary>Emits temporary edit merge for patch Compose.</summary>
    internal static void AppendTempEditMerge(
        SharedIndentedBuilder code,
        string editedValueType,
        bool hasPatch,
        int id
    )
    {
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + editedValueType
                + ">? netEditedTemp"
                + id
                + " = null;"
        );
        code.AppendLineAt(4, "if (__editedTemp is not null) foreach (var __tkv in __editedTemp)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (next.__removedTemp is not null && next.__removedTemp.Contains(__tkv.Key)) continue;"
        );
        code.AppendLineAt(5, "if (nextTempAdded" + id + ".ContainsKey(__tkv.Key)) continue;");
        if (hasPatch)
        {
            code.AppendLineAt(
                5,
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tkv.Key, out var __tn2) && !__tn2.__SparseIsEmpty()) { var __tc = __tkv.Value.Compose(__tn2); if (__tc.__SparseIsEmpty()) { netEditedTemp"
                    + id
                    + "?.Remove(__tkv.Key); } else (netEditedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + editedValueType
                    + ">())[__tkv.Key] = __tc; }"
            );
            code.AppendLineAt(
                5,
                "else if ((next.__editedTemp is null || !next.__editedTemp.ContainsKey(__tkv.Key)) && !__tkv.Value.__SparseIsEmpty()) (netEditedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + editedValueType
                    + ">())[__tkv.Key] = __tkv.Value;"
            );
        }
        else
        {
            code.AppendLineAt(
                5,
                "(netEditedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + editedValueType
                    + ">());"
            );
            code.AppendLineAt(
                5,
                "if (next.__editedTemp is not null && next.__editedTemp.TryGetValue(__tkv.Key, out var __tnv)) netEditedTemp"
                    + id
                    + "[__tkv.Key] = __tnv; else netEditedTemp"
                    + id
                    + "[__tkv.Key] = __tkv.Value;"
            );
        }
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (next.__editedTemp is not null) foreach (var __tkv in next.__editedTemp)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (thisTempAdded" + id + ".ContainsKey(__tkv.Key)) continue;");
        if (hasPatch)
        {
            code.AppendLineAt(5, "if (__tkv.Value.__SparseIsEmpty()) continue;");
        }
        code.AppendLineAt(
            5,
            "if (netEditedTemp"
                + id
                + " is not null && netEditedTemp"
                + id
                + ".ContainsKey(__tkv.Key)) continue;"
        );
        code.AppendLineAt(
            5,
            "(netEditedTemp"
                + id
                + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + editedValueType
                + ">())[__tkv.Key] = __tkv.Value;"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (netEditedTemp"
                + id
                + " is not null && netEditedTemp"
                + id
                + ".Count > 0) result.__editedTemp = netEditedTemp"
                + id
                + ";"
        );
    }

    /// <summary>Emits temporary order lockstep for patch Compose.</summary>
    /// <remarks>
    /// Replaces the key-only order merge for temporary members: template
    /// slots filter with their keys (assigned removals and temporary
    /// removals alike), then unslotted temporary additions append with
    /// sentinel slots. Assigned slots carry no identity. The parallel lists
    /// stay aligned by construction.
    /// </remarks>
    internal static void AppendPatchOrderTempMerge(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType,
        string comparer,
        int id
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        var sentinel = member.Collection.UnassignedKeyExpression ?? "default!";
        code.AppendLineAt(4, "if (next.__order is not null && next.__order.Count > 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __nextOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            5,
            "var __nextOrderTemp = new global::System.Collections.Generic.List<global::System.Guid?>();"
        );
        code.AppendLineAt(
            5,
            "var __slotted = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(5, "for (var __oi = 0; __oi < next.__order.Count; __oi++)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(5, "var __ok = next.__order[__oi];");
        code.AppendLineAt(
            5,
            "global::System.Guid? __ot = (next.__orderTemp is not null && __oi < next.__orderTemp.Count) ? next.__orderTemp[__oi] : null;"
        );
        code.AppendLineAt(
            5,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ok")
                + ") { if (__ot.HasValue && netRemovedTemp"
                + id
                + ".Contains(__ot.Value)) continue; __nextOrder.Add(__ok); __nextOrderTemp.Add(__ot); if (__ot.HasValue) __slotted.Add(__ot.Value); }"
        );
        code.AppendLineAt(
            5,
            "else { if (netRemoved.Contains(__ok)) continue; __nextOrder.Add(__ok); __nextOrderTemp.Add(null); }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (next.__added is not null) foreach (var __item in next.__added)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(5, "var __ak = " + keyOf + "(__item);");
        code.AppendLineAt(
            5,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ak")
                + ") { var __atk = "
                + tempOf
                + "(__item); if (__atk.HasValue && __atk.Value != global::System.Guid.Empty && !__slotted.Contains(__atk.Value)) { __nextOrder.Add("
                + sentinel
                + "); __nextOrderTemp.Add(__atk); __slotted.Add(__atk.Value); } }"
        );
        code.AppendLineAt(
            5,
            "else if (!global::System.Linq.Enumerable.Contains(__nextOrder, __ak, "
                + comparer
                + ")) { __nextOrder.Add(__ak); __nextOrderTemp.Add(null); }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "foreach (var __tt in thisTempAddedOrder"
                + id
                + ") if (!__slotted.Contains(__tt)) { __nextOrder.Add("
                + sentinel
                + "); __nextOrderTemp.Add(__tt); __slotted.Add(__tt); }"
        );
        code.AppendLineAt(
            5,
            "if (__nextOrder.Count > 0) { result.__order = __nextOrder; result.__orderTemp = __nextOrderTemp; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__order is not null && __order.Count > 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __thisOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            5,
            "var __thisOrderTemp = new global::System.Collections.Generic.List<global::System.Guid?>();"
        );
        code.AppendLineAt(
            5,
            "var __slotted = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(5, "for (var __oi = 0; __oi < __order.Count; __oi++)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(5, "var __ok = __order[__oi];");
        code.AppendLineAt(
            5,
            "global::System.Guid? __ot = (__orderTemp is not null && __oi < __orderTemp.Count) ? __orderTemp[__oi] : null;"
        );
        code.AppendLineAt(
            5,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ok")
                + ") { if (__ot.HasValue && netRemovedTemp"
                + id
                + ".Contains(__ot.Value)) continue; __thisOrder.Add(__ok); __thisOrderTemp.Add(__ot); if (__ot.HasValue) __slotted.Add(__ot.Value); }"
        );
        code.AppendLineAt(
            5,
            "else { if (netRemoved.Contains(__ok)) continue; __thisOrder.Add(__ok); __thisOrderTemp.Add(null); }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (next.__added is not null) foreach (var __item in next.__added)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(5, "var __ak = " + keyOf + "(__item);");
        code.AppendLineAt(
            5,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ak")
                + ") { var __atk = "
                + tempOf
                + "(__item); if (__atk.HasValue && __atk.Value != global::System.Guid.Empty && !__slotted.Contains(__atk.Value)) { __thisOrder.Add("
                + sentinel
                + "); __thisOrderTemp.Add(__atk); __slotted.Add(__atk.Value); } else if (!__atk.HasValue) { __thisOrder.Add(__ak); __thisOrderTemp.Add(null); } }"
        );
        code.AppendLineAt(5, "else { __thisOrder.Add(__ak); __thisOrderTemp.Add(null); }");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "foreach (var __tt in thisTempAddedOrder"
                + id
                + ") if (!__slotted.Contains(__tt)) { __thisOrder.Add("
                + sentinel
                + "); __thisOrderTemp.Add(__tt); __slotted.Add(__tt); }"
        );
        code.AppendLineAt(
            5,
            "if (__thisOrder.Count > 0) { result.__order = __thisOrder; result.__orderTemp = __thisOrderTemp; }"
        );
        code.AppendLineAt(4, "}");
    }
}
