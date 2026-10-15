using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the keyed and dictionary item validation for payloads.</summary>
/// <remarks>Item checks mirror the mixed-partition keyed case without throwing.</remarks>
internal static class SparseChangeSetPayloadValidateKeyedEmitter
{
    internal static void AppendKeyedValidate(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        string path,
        string runtime,
        string error,
        string kind
    )
    {
        _ = modelType;
        _ = dialect;
        var id = member.Id;
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var isModelValue = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        code.AppendLineAt(
            6,
            "if (item.Items is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + path
                + ", Message = \"Payload item changes must not be null.\" }); break; }"
        );
        code.AppendLineAt(6, "if (item.Before is not null || item.After is not null)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (item.Before is null || item.After is null || item.Items.Count != 0) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + path
                + ", Message = \"A whole collection payload must contain both endpoints and no item changes.\" }); break; }"
        );
        SparseChangeSetPayloadValidateEmitter.AppendEndpointValidate(
            code,
            7,
            "item.Before",
            path,
            runtime,
            error,
            kind,
            "errors",
            "__validateWholeBeforeBad" + id
        );
        SparseChangeSetPayloadValidateEmitter.AppendEndpointValidate(
            code,
            7,
            "item.After",
            path,
            runtime,
            error,
            kind,
            "errors",
            "__validateWholeAfterBad" + id
        );
        code.AppendLineAt(
            7,
            "if (__validateWholeBeforeBad" + id + " || __validateWholeAfterBad" + id + ") break;"
        );
        code.AppendLineAt(
            7,
            "if (item.After.IsRedacted) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = \"The payload contains a redacted after-state for '\" + "
                + path
                + " + \"'. After-states must stay concrete (value, null, or missing).\" }); break; }"
        );
        code.AppendLineAt(7, "if (item.Before.IsRedacted)");
        code.AppendLineAt(7, "{");
        // A blind whole-collection set is valid except for the missing-after
        // removal, which has no patch projection.
        code.AppendLineAt(7, "try");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(8, "var __validateWholeAfter" + id + " = item.After.ToOptional();");
        code.AppendLineAt(
            8,
            "if (!__validateWholeAfter"
                + id
                + ".IsPresent) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Changes, Path = "
                + path
                + ", Message = \"The payload redacts the before-state of whole collection member '\" + "
                + path
                + " + \"' with a missing after-state. Blind whole-collection removal has no patch projection; express the removal with an explicit patch.\" }); }"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "catch (global::System.InvalidOperationException __validateWholeBlindEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateWholeBlindEx"
                + id
                + ".Message }); }"
        );
        code.AppendLineAt(7, "break;");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(7, "try { item.Before.ToOptional(); item.After.ToOptional(); }");
        code.AppendLineAt(
            7,
            "catch (global::System.InvalidOperationException __validateWholeOrdinaryEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateWholeOrdinaryEx"
                + id
                + ".Message }); }"
        );
        code.AppendLineAt(6, "break;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "var __validateSeenKeys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">();"
        );
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLineAt(
                6,
                "var __validateSeenTemps"
                    + id
                    + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
            );
        }
        code.AppendLineAt(6, "foreach (var changeItem in item.Items)");
        code.AppendLineAt(6, "{");
        AppendItemValidate(
            code,
            member,
            dialect,
            path,
            runtime,
            error,
            kind,
            isKeyed,
            isModelValue
        );
        code.AppendLineAt(6, "}");
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary orders restore parallel to the key orders; items
            // without them are malformed, never position-guessed.
            code.AppendLineAt(
                6,
                "var __validateHasTempItems"
                    + id
                    + " = false; foreach (var __validateTempProbe"
                    + id
                    + " in item.Items) if (__validateTempProbe"
                    + id
                    + " is not null && __validateTempProbe"
                    + id
                    + ".TemporaryKey.HasValue) { __validateHasTempItems"
                    + id
                    + " = true; break; }"
            );
            code.AppendLineAt(
                6,
                "if (item.TempBeforeOrder is null && __validateHasTempItems"
                    + id
                    + ") errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Order, Path = "
                    + path
                    + ", Message = \"A temporary-keyed payload must contain temporary orders.\" });"
            );
            code.AppendLineAt(
                6,
                "if (item.TempAfterOrder is null && __validateHasTempItems"
                    + id
                    + ") errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Order, Path = "
                    + path
                    + ", Message = \"A temporary-keyed payload must contain temporary orders.\" });"
            );
        }
        code.AppendLineAt(6, "break;");
    }

    private static void AppendItemValidate(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string path,
        string runtime,
        string error,
        string kind,
        bool isKeyed,
        bool isModelValue
    )
    {
        _ = dialect;
        var id = member.Id;
        var itemKind = runtime + "ChangePayloadItemKind";
        code.AppendLineAt(
            7,
            "if (changeItem is null) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Item, Path = "
                + path
                + ", Message = \"Payload item is required.\" }); continue; }"
        );
        // Value-free endpoints must not smuggle values; reuse the endpoint
        // check so validation and conversion cannot disagree.
        code.AppendLineAt(7, "bool __validateItemEndpointBad" + id + " = false;");
        code.AppendLineAt(7, "try { changeItem.Before?.Validate(); }");
        code.AppendLineAt(
            7,
            "catch (global::System.InvalidOperationException __validateItemBeforeEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateItemBeforeEx"
                + id
                + ".Message }); __validateItemEndpointBad"
                + id
                + " = true; }"
        );
        code.AppendLineAt(7, "try { changeItem.After?.Validate(); }");
        code.AppendLineAt(
            7,
            "catch (global::System.InvalidOperationException __validateItemAfterEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + path
                + ", Message = __validateItemAfterEx"
                + id
                + ".Message }); __validateItemEndpointBad"
                + id
                + " = true; }"
        );
        code.AppendLineAt(7, "if (__validateItemEndpointBad" + id + ") continue;");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                7,
                "if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "changeItem.Key")
                    + " && !__validateSeenKeys"
                    + id
                    + ".Add(changeItem.Key!)) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Duplicate, Path = "
                    + path
                    + ", Message = \"A payload cannot contain duplicate keyed changes.\" }); continue; }"
            );
        }
        else
        {
            code.AppendLineAt(
                7,
                "if (!__validateSeenKeys"
                    + id
                    + ".Add(changeItem.Key!)) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Duplicate, Path = "
                    + path
                    + ", Message = \"A payload cannot contain duplicate keyed changes.\" }); continue; }"
            );
        }
        if (isKeyed && SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary identities must be unique per collection, like keys.
            code.AppendLineAt(
                7,
                "if (changeItem.TemporaryKey.HasValue && !__validateSeenTemps"
                    + id
                    + ".Add(changeItem.TemporaryKey.Value)) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Duplicate, Path = "
                    + path
                    + ", Message = \"A payload cannot contain duplicate temporary keyed changes.\" }); continue; }"
            );
        }
        code.AppendLineAt(
            7,
            "var __validateItemPath"
                + id
                + " = "
                + path
                + " + \"[\" + (((object?)changeItem.Key is null) ? \"?\" : (object?)changeItem.Key) + \"]\";"
        );
        var itemPath = "__validateItemPath" + id;
        if (!member.RedactBefore)
        {
            code.AppendLineAt(
                7,
                "if (changeItem.Before is not null && changeItem.Before.IsRedacted) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + itemPath
                    + ", Message = \"The payload contains a redacted item before-state for '\" + "
                    + itemPath
                    + " + \"'. Per-item redacted endpoints are not supported; send a whole-member blind set.\" }); continue; }"
            );
        }
        code.AppendLineAt(
            7,
            "if (changeItem.After is not null && changeItem.After.IsRedacted) { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + itemPath
                + ", Message = \"The payload contains a redacted item after-state for '\" + "
                + itemPath
                + " + \"'. After-states must stay concrete (value, null, or missing).\" }); continue; }"
        );
        if (isModelValue && member.RedactBefore)
        {
            // Removals may omit undisclosed before-values; edits must omit
            // endpoints and carry a payload.
            code.AppendLineAt(
                7,
                "if (changeItem.Before is not null && changeItem.Before.IsRedacted)"
            );
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                8,
                "if (changeItem.Kind == "
                    + itemKind
                    + ".Remove && changeItem.Edit is null) continue;"
            );
            code.AppendLineAt(
                8,
                "errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + itemPath
                    + ", Message = \"An edited model payload item must omit 'before' and 'after'.\" }); continue;"
            );
            code.AppendLineAt(7, "}");
        }
        if (!isModelValue && member.RedactBefore)
        {
            code.AppendLineAt(
                7,
                "if (changeItem.Before is not null && changeItem.Before.IsRedacted)"
            );
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                8,
                "if (changeItem.Kind != "
                    + itemKind
                    + ".Edit && changeItem.Kind != "
                    + itemKind
                    + ".Remove) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Item, Path = "
                    + itemPath
                    + ", Message = \"A payload item with a redacted before-state must be an edit or a removal.\" }); continue; }"
            );
            code.AppendLineAt(
                8,
                "if (changeItem.Kind == "
                    + itemKind
                    + ".Edit && (changeItem.After is null || changeItem.After.IsRedacted)) { errors.Add(new "
                    + error
                    + " { Kind = "
                    + kind
                    + ".Endpoint, Path = "
                    + itemPath
                    + ", Message = \"A blind edit must contain an observable after-state.\" }); continue; }"
            );
            code.AppendLineAt(7, "continue;");
            code.AppendLineAt(7, "}");
        }
        AppendItemShapeValidate(
            code,
            member,
            itemPath,
            runtime,
            error,
            kind,
            isKeyed,
            isModelValue
        );
    }

    private static void AppendItemShapeValidate(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string itemPath,
        string runtime,
        string error,
        string kind,
        bool isKeyed,
        bool isModelValue
    )
    {
        var id = member.Id;
        var itemKind = runtime + "ChangePayloadItemKind";
        var added = "changeItem.Kind == " + itemKind + ".Add";
        var removed = "changeItem.Kind == " + itemKind + ".Remove";
        var edited = "changeItem.Kind == " + itemKind + ".Edit";
        // Kind/endpooint consistency mirrors the item conversion; the edit
        // core itself recurses so nested problems stay path-addressable.
        code.AppendLineAt(7, "try");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (changeItem.Kind != "
                + itemKind
                + ".Add && changeItem.Kind != "
                + itemKind
                + ".Remove && changeItem.Kind != "
                + itemKind
                + ".Edit && changeItem.Kind != "
                + itemKind
                + ".Reorder) throw new global::System.ArgumentException(\"Unsupported payload item kind.\");"
        );
        if (!isKeyed)
            code.AppendLineAt(
                8,
                "if (changeItem.Kind == "
                    + itemKind
                    + ".Reorder) throw new global::System.ArgumentException(\"Reorder is not supported for dictionary payload items.\");"
            );
        code.AppendLineAt(
            8,
            "var __validateItemBefore"
                + id
                + " = changeItem.Before is null ? "
                + runtime
                + "Optional<"
                + ItemValueType(member)
                + ">.Missing : changeItem.Before.ToOptional();"
        );
        code.AppendLineAt(
            8,
            "var __validateItemAfter"
                + id
                + " = changeItem.After is null ? "
                + runtime
                + "Optional<"
                + ItemValueType(member)
                + ">.Missing : changeItem.After.ToOptional();"
        );
        code.AppendLineAt(8, "if (" + added + ")");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (__validateItemBefore"
                + id
                + ".IsPresent || !__validateItemAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"An added payload item must omit 'before' and contain 'after'.\");"
        );
        if (isModelValue)
            code.AppendLineAt(
                9,
                "if (changeItem.Edit is not null) throw new global::System.ArgumentException(\"An added payload item must not contain 'edit'.\");"
            );
        if (isKeyed)
            code.AppendLineAt(
                9,
                "if (changeItem.BeforeIndex != -1 || changeItem.AfterIndex < 0 || changeItem.IsReordered) throw new global::System.ArgumentException(\"An added keyed payload item has invalid indices.\");"
            );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else if (" + removed + ")");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (!__validateItemBefore"
                + id
                + ".IsPresent || __validateItemAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"A removed payload item must contain 'before' and omit 'after'.\");"
        );
        if (isModelValue)
            code.AppendLineAt(
                9,
                "if (changeItem.Edit is not null) throw new global::System.ArgumentException(\"A removed payload item must not contain 'edit'.\");"
            );
        if (isKeyed)
            code.AppendLineAt(
                9,
                "if (changeItem.BeforeIndex < 0 || changeItem.AfterIndex != -1 || changeItem.IsReordered) throw new global::System.ArgumentException(\"A removed keyed payload item has invalid indices.\");"
            );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else if (" + edited + ")");
        code.AppendLineAt(8, "{");
        if (isModelValue)
        {
            code.AppendLineAt(
                9,
                "if (__validateItemBefore"
                    + id
                    + ".IsPresent || __validateItemAfter"
                    + id
                    + ".IsPresent) throw new global::System.ArgumentException(\"An edited model payload item must omit 'before' and 'after'.\");"
            );
            code.AppendLineAt(
                9,
                "if (changeItem.Edit is null) throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\");"
            );
            if (isKeyed)
                code.AppendLineAt(
                    9,
                    "if (changeItem.BeforeIndex < 0 || changeItem.AfterIndex < 0) throw new global::System.ArgumentException(\"An edited keyed payload item has invalid indices.\");"
                );
            code.AppendLineAt(
                9,
                "if (changeItem.Edit is not null) changeItem.Edit.__SparseValidateInto(errors, "
                    + itemPath
                    + " + \".\");"
            );
        }
        else
        {
            code.AppendLineAt(
                9,
                "if (!__validateItemBefore"
                    + id
                    + ".IsPresent || !__validateItemAfter"
                    + id
                    + ".IsPresent) throw new global::System.ArgumentException(\"An edited scalar payload item must contain 'before' and 'after'.\");"
            );
            if (isKeyed)
                code.AppendLineAt(
                    9,
                    "if (changeItem.BeforeIndex < 0 || changeItem.AfterIndex < 0) throw new global::System.ArgumentException(\"An edited keyed payload item has invalid indices.\");"
                );
        }
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (__validateItemBefore"
                + id
                + ".IsPresent || __validateItemAfter"
                + id
                + ".IsPresent) throw new global::System.ArgumentException(\"A reordered payload item must omit 'before' and 'after'.\");"
        );
        if (isModelValue)
            code.AppendLineAt(
                9,
                "if (changeItem.Edit is not null) throw new global::System.ArgumentException(\"A reordered payload item must not contain 'edit'.\");"
            );
        if (isKeyed)
            code.AppendLineAt(
                9,
                "if (!changeItem.IsReordered || changeItem.BeforeIndex < 0 || changeItem.AfterIndex < 0) throw new global::System.ArgumentException(\"A reordered keyed payload item has invalid indices.\");"
            );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "catch (global::System.ArgumentException __validateItemShapeEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Item, Path = "
                + itemPath
                + ", Message = __validateItemShapeEx"
                + id
                + ".Message }); }"
        );
        code.AppendLineAt(
            7,
            "catch (global::System.InvalidOperationException __validateItemOptionalEx"
                + id
                + ") { errors.Add(new "
                + error
                + " { Kind = "
                + kind
                + ".Endpoint, Path = "
                + itemPath
                + ", Message = __validateItemOptionalEx"
                + id
                + ".Message }); }"
        );
    }

    private static string ItemValueType(SparseMemberModel member) =>
        SparseChangeSetBasicsEmitter.IsKeyed(member)
            ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
            : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
}
