using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Restores one member change from its payload variant.</summary>
/// <remarks>
/// Keeps the per-member restore case beside the payload reader so the DTO
/// emitter stays focused on shapes and the forward projection.
/// </remarks>
internal static class SparseChangeSetPayloadRestoreEmitter
{
    internal static void AppendFromPayloadCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var id = member.Id;
        var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        code.AppendLineAt(5, "case " + variant + " item:");
        code.AppendLineAt(
            6,
            "if (__payloadSeen"
                + id
                + ") throw new global::System.ArgumentException(\"A payload cannot contain duplicate member changes.\", nameof(payload));"
        );
        code.AppendLineAt(6, "__payloadSeen" + id + " = true;");
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            code.AppendLineAt(
                6,
                "if (item.Nested is null) throw new global::System.ArgumentException(\"Nested payload is required.\", nameof(payload));"
            );
            code.AppendLineAt(6, "__payloadNested" + id + " = item.Nested.ToChangeSetCore();");
            code.AppendLineAt(6, "break;");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
            code.AppendLineAt(6, "__payloadHas" + id + " = true;");
            code.AppendLineAt(
                6,
                "if (item.Items is null) throw new global::System.ArgumentException(\"Payload item changes must not be null.\", nameof(payload));"
            );
            code.AppendLineAt(6, "if (item.Before is not null || item.After is not null)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (item.Before is null || item.After is null || item.Items.Count != 0) throw new global::System.ArgumentException(\"A whole collection payload must contain both endpoints and no item changes.\", nameof(payload));"
            );
            code.AppendLineAt(7, "__payloadWhole" + id + " = true;");
            code.AppendLineAt(7, "__payloadBefore" + id + " = item.Before.ToOptional();");
            code.AppendLineAt(7, "__payloadAfter" + id + " = item.After.ToOptional();");
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
            var hasTempRestore = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
            if (hasTempRestore)
            {
                code.AppendLineAt(
                    7,
                    "var __payloadSeenTemps"
                        + id
                        + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
                );
            }
            code.AppendLineAt(7, "foreach (var changeItem in item.Items)");
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                7,
                "if (changeItem is null) throw new global::System.ArgumentException(\"Payload item is required.\", nameof(payload));"
            );
            if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            {
                code.AppendLineAt(
                    8,
                    "if (!"
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                            member,
                            "changeItem.Key"
                        )
                        + " && !__payloadSeenKeys"
                        + id
                        + ".Add(changeItem.Key)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
                );
            }
            else
            {
                code.AppendLineAt(
                    8,
                    "if (!__payloadSeenKeys"
                        + id
                        + ".Add(changeItem.Key)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate keyed changes.\", nameof(payload));"
                );
            }
            code.AppendLineAt(
                8,
                "__payloadItems" + id + ".Add(__SparsePayloadItem" + id + "(changeItem));"
            );
            if (hasTempRestore)
            {
                // Temporary identities must be unique per collection; assigned
                // entries never carry them after restore enforcement, so any
                // duplicate here is malformed transport.
                code.AppendLineAt(
                    8,
                    "if (changeItem.TemporaryKey.HasValue && !__payloadSeenTemps"
                        + id
                        + ".Add(changeItem.TemporaryKey.Value)) throw new global::System.ArgumentException(\"A payload cannot contain duplicate temporary keyed changes.\", nameof(payload));"
                );
            }
            code.AppendLineAt(7, "}");
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                code.AppendLineAt(7, "__payloadBeforeOrder" + id + " = item.BeforeOrder;");
                code.AppendLineAt(7, "__payloadAfterOrder" + id + " = item.AfterOrder;");
                if (hasTempRestore)
                {
                    // Temporary orders are explicit transport: temp-carrying
                    // items without them are malformed, never position-guessed.
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
        else
        {
            if (dialect.GetTransport(member.Property.Name) == SparseMemberTransport.Full)
            {
                code.AppendLineAt(
                    6,
                    "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
                );
                code.AppendLineAt(6, "__payloadHas" + id + " = true;");
                code.AppendLineAt(6, "__payloadBefore" + id + " = item.Before.ToOptional();");
                code.AppendLineAt(6, "__payloadAfter" + id + " = item.After.ToOptional();");
                code.AppendLineAt(6, "break;");
            }
            else
            {
                // Transport policy: an undisclosed before-state can never rebuild
                // a complete ChangeSet. The baseline-free projection stays explicit.
                code.AppendLineAt(
                    6,
                    "if (item.Before is not null) throw new global::System.ArgumentException(\"A redacted member must omit its before-state.\", nameof(payload));"
                );
                code.AppendLineAt(
                    6,
                    "if (item.After is null) throw new global::System.ArgumentException(\"A redacted member requires an after-state.\", nameof(payload));"
                );
                code.AppendLineAt(
                    6,
                    "throw new global::System.ArgumentException(\"Member '"
                        + member.Property.Name
                        + "' is redacted and cannot be converted to a complete ChangeSet. Use ToPatch() for the baseline-free projection.\", nameof(payload));"
                );
            }
        }
    }
}
