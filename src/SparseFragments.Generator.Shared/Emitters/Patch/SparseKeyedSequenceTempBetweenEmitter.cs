namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity diff and apply for keyed-sequence patches.</summary>
/// <remarks>
/// Correlates unassigned elements by Guid for patch Between and matches
/// temporary operations against baselines for patch Apply. Assigned-key
/// behavior stays in
/// <see cref="SparseKeyedSequenceApplyEmitter"/>.
/// </remarks>
internal static class SparseKeyedSequenceTempBetweenEmitter
{
    /// <summary>Emits baseline temporary validation for patch Between.</summary>
    internal static void AppendBetweenBaselineValidation(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        code.AppendLineAt(
            4,
            "var __beforeTemp"
                + member.Id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + SparseKeyedCollectionEmitter.ElementType(member)
                + ">();"
        );
        code.AppendLineAt(
            4,
            "if (before.IsPresent && (object?)before.Value is not null) foreach (var __baselineItem in before.Value!) if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    keyOf + "(__baselineItem)"
                )
                + ") { var __bt = "
                + tempOf
                + "(__baselineItem); if (!__bt.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__bt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry(
                    "__beforeTemp" + member.Id,
                    "__bt.Value",
                    "__baselineItem"
                )
                + " }"
        );
    }

    /// <summary>Emits after-walk temporary partition for patch Between.</summary>
    internal static void AppendBetweenAfterPartition(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLineAt(
            5,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") { var __at = "
                + tempOf
                + "(item); if (!__at.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__at.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry(
                    "__afterTemp" + member.Id,
                    "__at.Value",
                    "item"
                )
                + " afterOrder.Add(k!); unassignedAfter.Add(item); __orderTemp.Add(__at.Value); continue; }"
        );
    }

    /// <summary>Emits temporary removals and edits for patch Between.</summary>
    internal static void AppendBetweenTempChanges(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        bool hasPatch,
        string facade,
        string runtime
    )
    {
        var id = member.Id;
        // Temporary removals: before identities absent after.
        code.AppendLineAt(
            4,
            "var __removedTemp"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLineAt(
            4,
            "foreach (var __tk in __beforeTemp"
                + id
                + ".Keys) if (!__afterTemp"
                + id
                + ".ContainsKey(__tk)) __removedTemp"
                + id
                + ".Add(__tk);"
        );
        code.AppendLineAt(
            4,
            "if (__removedTemp" + id + ".Count > 0) patch.__removedTemp = __removedTemp" + id + ";"
        );
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            var prefix = SparseKeyedCollectionEmitter.ElementPatchPrefix(member);
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementPatch
                    + ">? __editedTemp"
                    + id
                    + " = null;"
            );
            code.AppendLineAt(4, "foreach (var __tk in __beforeTemp" + id + ".Keys)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!__afterTemp" + id + ".TryGetValue(__tk, out var __afterTempItem)) continue;"
            );
            code.AppendLineAt(5, "var __beforeTempItem = __beforeTemp" + id + "[__tk];");
            code.AppendLineAt(
                5,
                "var __tnested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Between("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__beforeTempItem)), "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__afterTempItem)));"
            );
            code.AppendLineAt(
                5,
                "if (!__tnested.__SparseIsEmpty()) (__editedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementPatch
                    + ">())[__tk] = __tnested;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (__editedTemp"
                    + id
                    + " is not null && __editedTemp"
                    + id
                    + ".Count > 0) patch.__editedTemp = __editedTemp"
                    + id
                    + ";"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementType
                    + ">? __editedTemp"
                    + id
                    + " = null;"
            );
            code.AppendLineAt(4, "foreach (var __tk in __beforeTemp" + id + ".Keys)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!__afterTemp" + id + ".TryGetValue(__tk, out var __afterTempItem)) continue;"
            );
            code.AppendLineAt(
                5,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__beforeTemp"
                    + id
                    + "[__tk], (object?)__afterTempItem)) (__editedTemp"
                    + id
                    + " ??= new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                    + elementType
                    + ">())[__tk] = __afterTempItem;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (__editedTemp"
                    + id
                    + " is not null && __editedTemp"
                    + id
                    + ".Count > 0) patch.__editedTemp = __editedTemp"
                    + id
                    + ";"
            );
        }
    }

    /// <summary>Emits baseline temporary validation for patch Apply.</summary>
    internal static void AppendApplyBaselineValidation(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyOf
    )
    {
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLineAt(
            4,
            "var __baselineTemp"
                + member.Id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + SparseKeyedCollectionEmitter.ElementType(member)
                + ">();"
        );
        code.AppendLineAt(
            4,
            "if (current.IsPresent && (object?)current.Value is not null) foreach (var __baselineItem in current.Value!) { var __bk = "
                + keyOf
                + "(__baselineItem); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__bk")
                + ") { var __bt = "
                + tempOf
                + "(__baselineItem); if (!__bt.HasValue) throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\"); if (__bt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry(
                    "__baselineTemp" + member.Id,
                    "__bt.Value",
                    "__baselineItem"
                )
                + " } }"
        );
    }

    /// <summary>Emits the temporary map build for patch Apply baselines.</summary>
    internal static void AppendApplyBaselineMap(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyOf
    )
    {
        // Temporary baselines were validated and registered by the preceding
        // validation pass; assigned entries join the key map here.
        code.AppendLineAt(5, "var k = " + keyOf + "(item);");
        code.AppendLineAt(
            5,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                + ") continue;"
        );
        code.AppendLineAt(5, SparseKeyedCollectionEmitter.AddUniqueEntry("map", "k", "item"));
    }

    /// <summary>Emits temporary removals application for patch Apply.</summary>
    internal static void AppendApplyTempRemovals(SharedIndentedBuilder code, int id)
    {
        // Assigned removals stay silent on missing keys; temporary removals match that.
        code.AppendLineAt(
            4,
            "if (__removedTemp is not null) foreach (var __t in __removedTemp) __baselineTemp"
                + id
                + ".Remove(__t);"
        );
    }

    /// <summary>Emits temporary-aware order consumption for patch Apply.</summary>
    /// <remarks>
    /// Slots carrying a temporary identity resolve by Guid (pending adds
    /// first, then surviving baselines); slots without one keep positional
    /// legacy flow. Unconsumed temporary adds fail instead of vanishing.
    /// </remarks>
    internal static void AppendApplyOrderConsumption(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType
    )
    {
        var id = member.Id;
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLineAt(
            5,
            "var __addedTempMap = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLineAt(
            5,
            "var __legacyAdded = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        code.AppendLineAt(5, "foreach (var __pendingAdd in __unassignedAdded)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var __pak = " + keyOf + "(__pendingAdd);");
        code.AppendLineAt(
            6,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__pak")
                + ") { var __pat = "
                + tempOf
                + "(__pendingAdd); if (__pat.HasValue && __pat.Value != global::System.Guid.Empty) { if (__addedTempMap.ContainsKey(__pat.Value)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); __addedTempMap.Add(__pat.Value, __pendingAdd); continue; } }"
        );
        code.AppendLineAt(6, "__legacyAdded.Add(__pendingAdd);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "var __legacyIndex = 0;");
        code.AppendLineAt(5, "for (var __oi = 0; __oi < __order.Count; __oi++)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var __ok = __order[__oi];");
        code.AppendLineAt(
            6,
            "global::System.Guid? __ot = (__orderTemp is not null && __oi < __orderTemp.Count) ? __orderTemp[__oi] : null;"
        );
        code.AppendLineAt(
            6,
            "if (" + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__ok") + ")"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(6, "if (__ot.HasValue)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__addedTempMap.Remove(__ot.Value, out var __addElem)) result.Add(__addElem);"
        );
        code.AppendLineAt(
            7,
            "else if (__baselineTemp"
                + id
                + ".TryGetValue(__ot.Value, out var __survivor)) result.Add(__survivor);"
        );
        code.AppendLineAt(
            7,
            "else throw new global::System.InvalidOperationException(\"Order lists an unknown key.\");"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__legacyIndex >= __legacyAdded.Count) throw new global::System.InvalidOperationException(\"Order contains too many unassigned-key entries.\"); result.Add(__legacyAdded[__legacyIndex++]);"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "continue;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "if (!map.TryGetValue(__ok, out var __oitem)) throw new global::System.InvalidOperationException(\"Order lists an unknown key.\");"
        );
        code.AppendLineAt(6, "result.Add(__oitem);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (__orderTemp is not null && __addedTempMap.Count > 0) throw new global::System.InvalidOperationException(\"Order must list exactly the final keys.\");"
        );
    }

    /// <summary>Emits temporary edits application for patch Apply.</summary>
    internal static void AppendApplyTempEdits(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        bool hasPatch,
        string runtime
    )
    {
        var id = member.Id;
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        var tempOf = SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member);
        code.AppendLineAt(4, "if (__editedTemp is not null) foreach (var __tkv in __editedTemp)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!__baselineTemp"
                + id
                + ".TryGetValue(__tkv.Key, out var __texisting)) throw new global::System.InvalidOperationException(\"Cannot edit a missing element.\");"
        );
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLineAt(
                5,
                "var __tapplied = __tkv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(__texisting)));"
            );
            code.AppendLineAt(
                5,
                "if (!__tapplied.IsPresent || __tapplied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(5, "var __tupdated = __tapplied.Value!.ToModel();");
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        keyOf + "(__tupdated)"
                    )
                    + " || "
                    + tempOf
                    + "(__tupdated) != __tkv.Key) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(5, "__baselineTemp" + id + "[__tkv.Key] = __tupdated;");
        }
        else
        {
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        keyOf + "(__tkv.Value)"
                    )
                    + " || "
                    + tempOf
                    + "(__tkv.Value) != __tkv.Key) throw new global::System.InvalidOperationException(\"Changing an element's identity through an update is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(5, "__baselineTemp" + id + "[__tkv.Key] = __tkv.Value;");
        }
        code.AppendLineAt(4, "}");
    }
}
