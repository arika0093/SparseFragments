using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the ChangeSet-to-payload transfer: envelope construction and root snapshots.</summary>
/// <remarks>
/// Separated from the typed DTO definitions
/// (<see cref="SparseChangeSetPayloadEmitter"/>): this type owns the
/// <c>ChangeSet</c>-side conversion bodies (<c>ToPayload</c>,
/// <c>ToPayloadCore</c>, whole-root snapshot helpers) while the DTO hierarchy
/// itself relocates to the per-model implementation source (issue #192).
/// </remarks>
internal static class SparseChangeSetPayloadTransferEmitter
{
    internal static void AppendToPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        SparseOperationTarget? target = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Core");
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadTypeName(dialect, modelType, "Root");
        var payloadChange = SparseChangeSetPayloadEmitter.PayloadTypeName(
            dialect,
            modelType,
            "Change"
        );
        var rootChange = SparseChangeSetPayloadEmitter.PayloadTypeName(
            dialect,
            modelType,
            "RootChange"
        );
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Converts this change set into its serializable payload envelope.</summary>"
            );
            code.AppendLineAt(
                2,
                "public ChangePayload ToPayload() => "
                    + target.ChangeSetOperationsType
                    + ".ToPayload(this);"
            );
            // The internal core projection stays callable in value form:
            // nested change-set values project through the facade bridge.
            code.AppendLineAt(
                2,
                "/// <summary>Builds the transport core for this change set.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>ChangeSet payloads are lossless: members excluded from JSON transport (STJ <c>JsonIgnore</c>) throw instead of silently dropping their changes. Ordinary <c>Fragment</c> JSON still honors <c>JsonIgnore</c>.</remarks>"
            );
            code.AppendLineAt(
                2,
                "internal "
                    + payloadCore
                    + " ToPayloadCore(bool redactBefores) => "
                    + target.ChangeSetOperationsType
                    + ".ToPayloadCore(this, redactBefores);"
            );
            code = target.ChangeSetOperations;
            code.AppendLineAt(
                2,
                "/// <summary>Converts a change set into its serializable payload envelope.</summary>"
            );
            code.AppendLineAt(2, "internal static ChangePayload ToPayload(ChangeSet self)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "return new ChangePayload { Version = "
                    + versionLiteral
                    + ", Changes = ToPayloadCore(self, false).Changes };"
            );
            code.AppendLineAt(2, "}");
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Converts this change set into its serializable payload envelope.</summary>"
            );
            code.AppendLineAt(2, "public ChangePayload ToPayload()");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "return new ChangePayload { Version = "
                    + versionLiteral
                    + ", Changes = ToPayloadCore(false).Changes };"
            );
            code.AppendLineAt(2, "}");
        }
        code.AppendLineAt(
            2,
            "/// <summary>Builds the transport core for this change set.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>ChangeSet payloads are lossless: members excluded from JSON transport (STJ <c>JsonIgnore</c>) throw instead of silently dropping their changes. Ordinary <c>Fragment</c> JSON still honors <c>JsonIgnore</c>.</remarks>"
        );
        code.AppendLineAt(
            2,
            (target is null ? "internal " : "internal static ")
                + payloadCore
                + " ToPayloadCore("
                + (target is null ? string.Empty : "ChangeSet self, ")
                + "bool redactBefores)"
        );
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            AppendSelfAliases(code, members);
        }
        SparseChangePayloadPatchSyncEmitter.AppendIgnoredTransportGuard(code, members);
        code.AppendLineAt(
            3,
            "var changes = new global::System.Collections.Generic.List<" + payloadChange + ">();"
        );
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        if (SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            // Whole-root snapshots would leak undisclosed before-state through
            // a different path, so they are refused while policies apply.
            code.AppendLineAt(
                4,
                "throw new global::System.InvalidOperationException(\"A whole-root transition cannot be serialized while member transport policies apply.\");"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "changes.Add(new "
                    + rootChange
                    + " { Before = __SparsePayloadRoot(__sparse_wholeBefore, redactBefores), After = __SparsePayloadRootAfter(__sparse_wholeAfter) });"
            );
            code.AppendLineAt(4, "return new " + payloadCore + " { Changes = changes };");
        }
        code.AppendLineAt(3, "}");

        SparseDownstreamPolicy.ThrowOnInvalidTransport(members, dialect);
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            var id = member.Id;
            var variant = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
                dialect,
                modelType,
                "Change",
                id
            );
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var redact = member.RedactBefore ? "true" : "redactBefores";
                code.AppendLineAt(
                    3,
                    "if (" + SparseChangeSetBasicsEmitter.NestedField(member) + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    "changes.Add(new "
                        + variant
                        + " { Nested = "
                        + SparseChangeSetBasicsEmitter.NestedField(member)
                        + "!.ToPayloadCore("
                        + redact
                        + ") });"
                );
                code.AppendLineAt(3, "}");
            }
            else if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                || SparseChangeSetBasicsEmitter.IsDict(member)
            )
            {
                code.AppendLineAt(3, "if (" + SparseChangeSetBasicsEmitter.HasField(member) + ")");
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "var entry = new " + variant + "();");
                // A redacted before-state stays undisclosed; the after-state
                // still travels so the change can be projected with ToPatch.
                var redactBefore = member.RedactBefore ? "true" : "redactBefores";
                code.AppendLineAt(
                    4,
                    "if (" + SparseChangeSetBasicsEmitter.KeyedWholeFlag(member) + ")"
                );
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    5,
                    "entry.Before = ("
                        + SparseChangeSetBasicsEmitter.KeyedWholeBefore(member)
                        + ".IsPresent && ("
                        + redactBefore
                        + ")) ? "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.Redacted() : "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.FromOptional("
                        + SparseChangeSetBasicsEmitter.KeyedWholeBefore(member)
                        + ");"
                );
                code.AppendLineAt(
                    5,
                    "entry.After = "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.FromOptional("
                        + SparseChangeSetBasicsEmitter.KeyedWholeAfter(member)
                        + ");"
                );
                code.AppendLineAt(4, "}");
                code.AppendLineAt(4, "else");
                code.AppendLineAt(4, "{");
                var itemField = SparseChangeSetBasicsEmitter.KeyedItems(member);
                var itemValueType = SparseChangeSetBasicsEmitter.IsKeyed(member)
                    ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                    : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
                var isModelValue = SparseChangeSetBasicsEmitter.IsKeyed(member)
                    ? member.Collection.ElementType.IsFragmentModel
                    : member.Collection.ValueType?.IsFragmentModel == true;
                var omitItemEndpoints = "";
                if (isModelValue)
                    omitItemEndpoints = SparseChangeSetBasicsEmitter.IsKeyed(member)
                        ? "item.IsEdited || item.IsReordered || "
                        : "item.IsEdited || ";
                var beforeEndpoint =
                    "("
                    + omitItemEndpoints
                    + "!item.Before.IsPresent ? null : (("
                    + redactBefore
                    + ") ? "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.Redacted() : "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.FromOptional(item.Before)))";
                var afterEndpoint =
                    "("
                    + omitItemEndpoints
                    + "!item.After.IsPresent ? null : "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.FromOptional(item.After))";
                code.AppendLineAt(
                    5,
                    "if (" + itemField + " is not null) foreach (var item in " + itemField + ")"
                );
                code.AppendLineAt(5, "{");
                code.AppendLineAt(
                    6,
                    "var mapped = new "
                        + SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
                            dialect,
                            modelType,
                            "Item",
                            id
                        )
                        + " { Key = item.Key, Before = "
                        + beforeEndpoint
                        + ", After = "
                        + afterEndpoint
                        + ", Kind = item.IsAdded ? "
                        + runtime
                        + "ChangePayloadItemKind.Add : item.IsRemoved ? "
                        + runtime
                        + "ChangePayloadItemKind.Remove : item.IsEdited ? "
                        + runtime
                        + "ChangePayloadItemKind.Edit : "
                        + runtime
                        + "ChangePayloadItemKind.Reorder };"
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    code.AppendLineAt(6, "mapped.BeforeIndex = item.BeforeIndex;");
                    code.AppendLineAt(6, "mapped.AfterIndex = item.AfterIndex;");
                    code.AppendLineAt(6, "mapped.IsReordered = item.IsReordered;");
                }
                if (isModelValue)
                    code.AppendLineAt(
                        6,
                        "if (item.IsEdited) mapped.Edit = item.Edit.ToPayloadCore("
                            + redactBefore
                            + ");"
                    );
                code.AppendLineAt(6, "entry.Items.Add(mapped);");
                code.AppendLineAt(5, "}");
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    code.AppendLineAt(
                        5,
                        "entry.BeforeOrder = "
                            + SparseChangeSetBasicsEmitter.KeyedBeforeOrder(member)
                            + ";"
                    );
                    code.AppendLineAt(
                        5,
                        "entry.AfterOrder = "
                            + SparseChangeSetBasicsEmitter.KeyedAfterOrder(member)
                            + ";"
                    );
                }
                code.AppendLineAt(4, "}");
                code.AppendLineAt(4, "changes.Add(entry);");
                code.AppendLineAt(3, "}");
            }
            else
            {
                code.AppendLineAt(3, "if (" + SparseChangeSetBasicsEmitter.HasField(member) + ")");
                code.AppendLineAt(3, "{");
                var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
                var scalarRedact = member.RedactBefore ? "true" : "redactBefores";
                if (dialect.GetTransport(member.Property.Name) == SparseMemberTransport.Full)
                {
                    code.AppendLineAt(
                        4,
                        "changes.Add(new "
                            + variant
                            + " { Before = ("
                            + SparseChangeSetBasicsEmitter.BeforeField(member)
                            + ".IsPresent && ("
                            + scalarRedact
                            + ")) ? "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.Redacted() : "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.FromOptional("
                            + SparseChangeSetBasicsEmitter.BeforeField(member)
                            + "), After = "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.FromOptional("
                            + SparseChangeSetBasicsEmitter.AfterField(member)
                            + ") });"
                    );
                }
                else
                {
                    // Transport policy: the before-state stays undisclosed and
                    // the after-state travels alone. Never fabricate a missing.
                    code.AppendLineAt(
                        4,
                        "changes.Add(new "
                            + variant
                            + " { After = "
                            + endpoint
                            + "<"
                            + valueType
                            + ">.FromOptional("
                            + SparseChangeSetBasicsEmitter.AfterField(member)
                            + ") });"
                    );
                }
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(3, "return new " + payloadCore + " { Changes = changes };");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + endpoint
                + "<"
                + payloadRoot
                + "> __SparsePayloadRoot("
                + runtime
                + "Optional<Fragment?> value, bool redactBefores)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (!value.IsPresent) return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Missing);"
        );
        code.AppendLineAt(
            3,
            "if (value.Value is null) return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present(null));"
        );
        // A whole-root before snapshot that hides member values cannot travel
        // as an observable root: endpoint-redact it so the mixed partition
        // routes the whole change blind instead of failing fragment conversion.
        // Known absence (missing) and explicit null stay observable.
        var wholeBeforeRedacted = members.Any(static member =>
            !member.Property.IsJsonIgnored && member.RedactBefore
        )
            ? "true"
            : "false";
        code.AppendLineAt(
            3,
            "if (redactBefores || "
                + wholeBeforeRedacted
                + ") return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.Redacted();"
        );
        code.AppendLineAt(
            3,
            "return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present("
                + payloadRoot
                + ".FromFragment(value.Value, redactBefores)));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "/// <summary>Builds an undisclosed-free after snapshot for a whole-root change.</summary>"
        );
        code.AppendLineAt(
            2,
            "private static "
                + endpoint
                + "<"
                + payloadRoot
                + "> __SparsePayloadRootAfter("
                + runtime
                + "Optional<Fragment?> value)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (!value.IsPresent) return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Missing);"
        );
        code.AppendLineAt(
            3,
            "if (value.Value is null) return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present(null));"
        );
        code.AppendLineAt(
            3,
            "return "
                + endpoint
                + "<"
                + payloadRoot
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + payloadRoot
                + ">.Present("
                + payloadRoot
                + ".FromFragment(value.Value)));"
        );
        code.AppendLineAt(2, "}");
    }
}
