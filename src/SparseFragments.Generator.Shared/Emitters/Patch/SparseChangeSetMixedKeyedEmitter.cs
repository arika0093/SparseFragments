namespace SparseFragments.Generator.Shared;

/// <summary>Emits the mixed-partition case for keyed and dictionary payload members.</summary>
/// <remarks>Collection members route per item: ordinary items keep baseline-aware conversion while undisclosed befores on a redacted-before member pass their requested after-state through without comparison. Hand-crafted redacted items on ordinary members stay malformed.</remarks>
internal static class SparseChangeSetMixedKeyedEmitter
{
    internal static void AppendKeyedPartitionCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        int id,
        string esc,
        string path
    )
    {
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var collectionPatch =
            "Patch." + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member);
        code.AppendLineAt(6, "__payloadHas" + id + " = true;");
        code.AppendLineAt(
            6,
            "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
        );
        // Per-item redacted endpoints cannot form item transitions; fail before
        // the shared item helper converts endpoints without naming the path.
        // A redacted-before collection member legitimately emits granular items
        // with undisclosed befores, so those route blind per item below instead.
        code.AppendLineAt(6, "foreach (var __raw" + id + " in item.Items)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__raw"
                + id
                + " is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
        );
        // Value-free endpoints must not smuggle values past the redacted
        // branch below (issue #164).
        code.AppendLineAt(
            7,
            "__raw" + id + ".Before?.Validate(); __raw" + id + ".After?.Validate();"
        );
        if (!member.RedactBefore)
        {
            code.AppendLineAt(
                7,
                "if (__raw"
                    + id
                    + ".Before is not null && __raw"
                    + id
                    + ".Before.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted item before-state for '\" + "
                    + path
                    + " + \"[\" + (((object?)__raw"
                    + id
                    + ".Key is null) ? \"?\" : (object?)__raw"
                    + id
                    + ".Key) + \"]\" + \"'. Per-item redacted endpoints are not supported; send a whole-member blind set.\", nameof(payload));"
            );
        }
        code.AppendLineAt(
            7,
            "if (__raw"
                + id
                + ".After is not null && __raw"
                + id
                + ".After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted item after-state for '\" + "
                + path
                + " + \"[\" + (((object?)__raw"
                + id
                + ".Key is null) ? \"?\" : (object?)__raw"
                + id
                + ".Key) + \"]\" + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "if (item.After is not null && item.After.IsRedacted) throw new global::System.ArgumentException(\"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\", nameof(payload));"
        );
        code.AppendLineAt(6, "if (item.Before is not null || item.After is not null)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
        );
        code.AppendLineAt(7, "item.Before.Validate(); item.After.Validate();");
        code.AppendLineAt(7, "if (item.Before.IsRedacted)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(7, "var __wholeAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(
            8,
            "if (!__wholeAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"The payload redacts the before-state of whole collection member '\" + "
                + path
                + " + \"' with a missing after-state. Blind whole-collection removal has no patch projection; express the removal with an explicit patch.\", nameof(payload));"
        );
        code.AppendLineAt(8, "var __wholeBlind" + id + " = new " + collectionPatch + "();");
        code.AppendLineAt(8, "__wholeBlind" + id + ".Set(__wholeAfter" + id + ".Value!);");
        code.AppendLineAt(8, "blindSets." + esc + " = __wholeBlind" + id + ";");
        code.AppendLineAt(8, "blindPaths.Add(" + path + ");");
        code.AppendLineAt(8, "__payloadHas" + id + " = false;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "else");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(8, "__payloadWhole" + id + " = true;");
        code.AppendLineAt(8, "__payloadBefore" + id + " = item.Before.ToOptional();");
        code.AppendLineAt(8, "__payloadAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "var __payloadSeenKeys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">();"
        );
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLineAt(
                7,
                "var __payloadSeenTemps"
                    + id
                    + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
            );
        }
        // Blind per-item sets for undisclosed befores. A redacted-before member
        // legitimately emits granular items without observable befores; those
        // pass their requested after-state through without comparison, while
        // ordinary items keep baseline-aware conversion. Non-flagged members
        // keep the strict pre-check above, so hand-crafted redacted items fail.
        var isModelValue = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        var canContainBlindItems = isModelValue || member.RedactBefore;
        if (!canContainBlindItems)
        {
            code.AppendLineAt(7, "__payloadItems" + id + ".Capacity = item.Items.Count;");
        }
        if (canContainBlindItems)
        {
            code.AppendLineAt(7, "var __blindColl" + id + " = new " + collectionPatch + "();");
            code.AppendLineAt(7, "bool __hasBlindColl" + id + " = false;");
        }
        code.AppendLineAt(7, "foreach (var changeItem in item.Items)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (changeItem is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
        );
        code.AppendLineAt(8, "changeItem.Before?.Validate(); changeItem.After?.Validate();");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                8,
                "if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "changeItem.Key")
                    + " && !__payloadSeenKeys"
                    + id
                    + ".Add(changeItem.Key!)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
            );
        }
        else
        {
            code.AppendLineAt(
                8,
                "if (!__payloadSeenKeys"
                    + id
                    + ".Add(changeItem.Key!)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
            );
        }
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary identities must be unique per collection, like keys.
            code.AppendLineAt(
                8,
                "if (changeItem.TemporaryKey.HasValue && !__payloadSeenTemps"
                    + id
                    + ".Add(changeItem.TemporaryKey.Value)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate temporary keyed changes.\", nameof(payload));"
            );
        }
        if (canContainBlindItems)
        {
            code.AppendLineAt(
                8,
                "var __itemPath"
                    + id
                    + " = "
                    + path
                    + " + \"[\" + (((object?)changeItem.Key is null) ? \"?\" : (object?)changeItem.Key) + \"]\";"
            );
        }
        if (isModelValue)
        {
            // Edited model items carry a nested core instead of endpoints.
            // Partition it: blind leaves route to the blind collection patch,
            // clean edits keep baseline-aware item conversion.
            var valueChangeSet = isKeyed
                ? SparseChangeSetBasicsEmitter.ElementChangeSetOf(member)
                : SparseChangeSetBasicsEmitter.ValueChangeSetOf(member);
            code.AppendLineAt(8, valueChangeSet + "? __restoredEdit" + id + " = null;");
            code.AppendLineAt(8, "if (changeItem.Edit is not null)");
            code.AppendLineAt(8, "{");
            code.AppendLineAt(
                9,
                "__restoredEdit"
                    + id
                    + " = "
                    + valueChangeSet
                    + ".__SparseMixedPartition(changeItem.Edit, __itemPath"
                    + id
                    + " + \".\", out var __editBlind"
                    + id
                    + ", out var __editPaths"
                    + id
                    + ");"
            );
            code.AppendLineAt(9, "if (!__editBlind" + id + ".__SparseIsEmpty())");
            code.AppendLineAt(9, "{");
            if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                code.AppendLineAt(
                    10,
                    "if (changeItem.TemporaryKey.HasValue) __blindColl"
                        + id
                        + ".__SparseSetEditedByTemporaryKey(changeItem.TemporaryKey.Value, changeItem.Edit.ToPatchCore()); else __blindColl"
                        + id
                        + ".__SparseSetEdited(changeItem.Key!, changeItem.Edit.ToPatchCore());"
                );
            }
            else
            {
                code.AppendLineAt(
                    10,
                    "__blindColl"
                        + id
                        + ".__SparseSetEdited(changeItem.Key!, changeItem.Edit.ToPatchCore());"
                );
            }
            code.AppendLineAt(10, "blindPaths.AddRange(__editPaths" + id + ");");
            code.AppendLineAt(10, "__hasBlindColl" + id + " = true;");
            code.AppendLineAt(10, "continue;");
            code.AppendLineAt(9, "}");
            code.AppendLineAt(8, "}");
        }
        if (member.RedactBefore)
        {
            var itemKind = dialect.RuntimeNamespace + "ChangePayloadItemKind";
            if (isModelValue)
            {
                // Removed model items legitimately carry an undisclosed before
                // without an edit payload; pass the removal through blind.
                // Any other endpoint-bearing model item is malformed: edited
                // model items must omit endpoints and carry an edit payload.
                // Scalar collection operations are not referenced here so
                // model keyed patches (which expose no Update) still compile.
                code.AppendLineAt(
                    8,
                    "if (changeItem.Before is not null && changeItem.Before.IsRedacted)"
                );
                code.AppendLineAt(8, "{");
                code.AppendLineAt(
                    9,
                    "if (changeItem.Kind == " + itemKind + ".Remove && changeItem.Edit is null)"
                );
                code.AppendLineAt(9, "{");
                if (isKeyed)
                {
                    code.AppendLineAt(10, "__blindColl" + id + ".Remove(changeItem.Key!);");
                }
                else
                {
                    code.AppendLineAt(10, "__blindColl" + id + ".RemoveEntry(changeItem.Key!);");
                }
                code.AppendLineAt(10, "blindPaths.Add(" + path + ");");
                code.AppendLineAt(10, "__hasBlindColl" + id + " = true;");
                code.AppendLineAt(10, "continue;");
                code.AppendLineAt(9, "}");
                code.AppendLineAt(
                    9,
                    "throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' edited model payload item must omit 'before' and 'after'.\", nameof(payload));"
                );
                code.AppendLineAt(8, "}");
            }
            else
            {
                var itemValueType = isKeyed
                    ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                    : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
                var itemOptional = dialect.RuntimeNamespace + "Optional<" + itemValueType + ">";
                code.AppendLineAt(
                    8,
                    "if (changeItem.Before is not null && changeItem.Before.IsRedacted)"
                );
                code.AppendLineAt(8, "{");
                code.AppendLineAt(
                    9,
                    "if (changeItem.Kind != "
                        + itemKind
                        + ".Edit && changeItem.Kind != "
                        + itemKind
                        + ".Remove) throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' payload item with a redacted before-state must be an edit or a removal.\", nameof(payload));"
                );
                code.AppendLineAt(
                    9,
                    "if (changeItem.Kind == "
                        + itemKind
                        + ".Edit && (changeItem.After is null || changeItem.After.IsRedacted)) throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' blind edit must contain an observable after-state.\", nameof(payload));"
                );
                code.AppendLineAt(
                    9,
                    "var __blindAfter"
                        + id
                        + " = changeItem.After is null ? "
                        + itemOptional
                        + ".Missing : changeItem.After.ToOptional();"
                );
                code.AppendLineAt(
                    9,
                    "if (changeItem.Kind == "
                        + itemKind
                        + ".Edit && !__blindAfter"
                        + id
                        + ".IsPresent) throw new global::System.ArgumentException(\"A '\" + __itemPath"
                        + id
                        + " + \"' blind edit must contain an after-state.\", nameof(payload));"
                );
                if (isKeyed)
                {
                    if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
                    {
                        code.AppendLineAt(
                            9,
                            "if (changeItem.Kind == "
                                + itemKind
                                + ".Edit) __blindColl"
                                + id
                                + ".Update(__blindAfter"
                                + id
                                + ".Value!); else if (changeItem.TemporaryKey.HasValue) __blindColl"
                                + id
                                + ".RemoveByTemporaryKey(changeItem.TemporaryKey.Value); else __blindColl"
                                + id
                                + ".Remove(changeItem.Key!);"
                        );
                    }
                    else
                    {
                        code.AppendLineAt(
                            9,
                            "if (changeItem.Kind == "
                                + itemKind
                                + ".Edit) __blindColl"
                                + id
                                + ".Update(__blindAfter"
                                + id
                                + ".Value!); else __blindColl"
                                + id
                                + ".Remove(changeItem.Key!);"
                        );
                    }
                }
                else
                {
                    code.AppendLineAt(
                        9,
                        "if (changeItem.Kind == "
                            + itemKind
                            + ".Edit) __blindColl"
                            + id
                            + ".SetEntry(changeItem.Key!, __blindAfter"
                            + id
                            + ".Value!); else __blindColl"
                            + id
                            + ".RemoveEntry(changeItem.Key!);"
                    );
                }
                code.AppendLineAt(9, "blindPaths.Add(" + path + ");");
                code.AppendLineAt(9, "__hasBlindColl" + id + " = true;");
                code.AppendLineAt(9, "continue;");
                code.AppendLineAt(8, "}");
            }
        }
        code.AppendLineAt(
            8,
            "__payloadItems"
                + id
                + ".Add(__SparsePayloadItem"
                + id
                + "(changeItem"
                + (isModelValue ? ", __restoredEdit" + id : string.Empty)
                + "));"
        );
        code.AppendLineAt(7, "}");
        if (canContainBlindItems)
        {
            code.AppendLineAt(
                7,
                "if (__hasBlindColl" + id + ") blindSets." + esc + " = __blindColl" + id + ";"
            );
        }
        if (isKeyed)
        {
            code.AppendLineAt(7, "__payloadBeforeOrder" + id + " = item.BeforeOrder;");
            code.AppendLineAt(7, "__payloadAfterOrder" + id + " = item.AfterOrder;");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                // Temporary orders restore parallel to the key orders; items
                // without them are malformed, never position-guessed.
                code.AppendLineAt(
                    7,
                    "var __hasTempItems"
                        + id
                        + " = false; foreach (var __ci in item.Items) if (__ci.TemporaryKey.HasValue) { __hasTempItems"
                        + id
                        + " = true; break; }"
                );
                code.AppendLineAt(
                    7,
                    "if (item.TempBeforeOrder is null && __hasTempItems"
                        + id
                        + ") throw new global::System.ArgumentException(\"A temporary-keyed payload must contain temporary orders.\", nameof(payload));"
                );
                code.AppendLineAt(
                    7,
                    "__payloadTempBeforeOrder"
                        + id
                        + " = item.TempBeforeOrder ?? new global::System.Collections.Generic.List<global::System.Guid>();"
                );
                code.AppendLineAt(
                    7,
                    "if (item.TempAfterOrder is null && __hasTempItems"
                        + id
                        + ") throw new global::System.ArgumentException(\"A temporary-keyed payload must contain temporary orders.\", nameof(payload));"
                );
                code.AppendLineAt(
                    7,
                    "__payloadTempAfterOrder"
                        + id
                        + " = item.TempAfterOrder ?? new global::System.Collections.Generic.List<global::System.Guid>();"
                );
            }
        }
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "break;");
    }
}
