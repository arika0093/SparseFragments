using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the ChangePayload read path: validated conversion to a complete ChangeSet.</summary>
/// <remarks>Redacted or otherwise incomplete histories are rejected here rather than fabricated; baseline-discarding projection lives in <see cref="SparseChangePayloadPatchSyncEmitter"/>.</remarks>
internal static class SparseChangePayloadReaderEmitter
{
    internal static void AppendFromPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Core");
        var payloadRoot = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Root");
        var rootChange = SparseChangeSetPayloadEmitter.PayloadName(modelType, "RootChange");
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLineAt(
            2,
            "/// <summary>Converts a validated envelope to a complete change set.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ChangePayload.ToPatch\"/> instead.</remarks>"
        );
        code.AppendLineAt(2, "public static ChangeSet FromPayload(ChangePayload payload)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (!global::System.String.Equals(payload.Version, "
                + versionLiteral
                + ", global::System.StringComparison.Ordinal)) throw new global::System.ArgumentException(\"Unsupported ChangePayload version.\", nameof(payload));"
        );
        code.AppendLineAt(3, "return FromPayloadCore(payload);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static ChangeSet FromPayloadCore(" + payloadCore + " payload)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (payload.Changes is null) throw new global::System.ArgumentException(\"Payload changes must not be null.\", nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (payload.Changes.Count == 1 && payload.Changes[0] is " + rootChange + " whole)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (whole.Before is null || whole.After is null) throw new global::System.ArgumentException(\"A whole-root payload must contain both endpoints.\", nameof(payload));"
        );
        code.AppendLineAt(
            4,
            "return new ChangeSet(true, __SparsePayloadFragment(whole.Before), __SparsePayloadFragment(whole.After), "
                + SparseChangeSetBasicsEmitter.MemberEmptyTail(members)
                + ");"
        );
        code.AppendLineAt(3, "}");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            var id = member.Id;
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    SparseChangeSetBasicsEmitter.ChildChangeSet(member, dialect)
                        + "? __payloadNested"
                        + id
                        + " = null;"
                );
            }
            else if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                || SparseChangeSetBasicsEmitter.IsDict(member)
            )
            {
                var opt =
                    runtime
                    + "Optional<"
                    + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                    + ">";
                var trans = SparseChangeSetBasicsEmitter.TransNameFor(members, member);
                code.AppendLineAt(3, "bool __payloadHas" + id + " = false;");
                code.AppendLineAt(3, "bool __payloadWhole" + id + " = false;");
                code.AppendLineAt(3, opt + " __payloadBefore" + id + " = default;");
                code.AppendLineAt(3, opt + " __payloadAfter" + id + " = default;");
                code.AppendLineAt(
                    3,
                    "var __payloadItems"
                        + id
                        + " = new global::System.Collections.Generic.List<"
                        + trans
                        + ".Item>();"
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? __payloadBeforeOrder"
                            + id
                            + " = null;"
                    );
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? __payloadAfterOrder"
                            + id
                            + " = null;"
                    );
                }
            }
            else
            {
                var opt =
                    runtime
                    + "Optional<"
                    + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                    + ">";
                code.AppendLineAt(3, "bool __payloadHas" + id + " = false;");
                code.AppendLineAt(3, opt + " __payloadBefore" + id + " = default;");
                code.AppendLineAt(3, opt + " __payloadAfter" + id + " = default;");
            }
            code.AppendLineAt(3, "bool __payloadSeen" + id + " = false;");
        }

        code.AppendLineAt(3, "foreach (var change in payload.Changes)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (change)");
        code.AppendLineAt(4, "{");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            AppendFromPayloadCase(code, member, modelType, runtime);
        }
        code.AppendLineAt(
            5,
            "default: throw new global::System.ArgumentException(\"Payload contains a null or unknown change variant.\", nameof(payload));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        var args = new global::System.Collections.Generic.List<string>
        {
            "false",
            "default",
            "default",
        };
        foreach (var member in members)
        {
            var id = member.Id;
            if (member.Property.IsJsonIgnored)
            {
                args.AddRange(SparseChangeSetBasicsEmitter.EmptyMemberArgs(member));
                continue;
            }
            if (SparseChangeSetBasicsEmitter.IsNested(member))
                args.Add("__payloadNested" + id);
            else if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                || SparseChangeSetBasicsEmitter.IsDict(member)
            )
            {
                args.AddRange(
                    new[]
                    {
                        "__payloadHas" + id,
                        "__payloadWhole" + id,
                        "__payloadBefore" + id,
                        "__payloadAfter" + id,
                        "__payloadItems" + id,
                    }
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                    args.AddRange(
                        new[] { "__payloadBeforeOrder" + id, "__payloadAfterOrder" + id }
                    );
            }
            else
                args.AddRange(
                    new[] { "__payloadBefore" + id, "__payloadAfter" + id, "__payloadHas" + id }
                );
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
        code.AppendLineAt(2, "}");
        foreach (
            var member in members.Where(static member =>
                !member.Property.IsJsonIgnored
                && (
                    SparseChangeSetBasicsEmitter.IsKeyed(member)
                    || SparseChangeSetBasicsEmitter.IsDict(member)
                )
            )
        )
        {
            SparseChangeSetPayloadItemEmitter.AppendPayloadItemHelper(
                code,
                member,
                members,
                runtime,
                modelType
            );
        }
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<Fragment?> __SparsePayloadFragment("
                + endpoint
                + "<"
                + payloadRoot
                + "> endpoint)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (endpoint is null) throw new global::System.ArgumentException(\"A root endpoint is required.\", nameof(endpoint));"
        );
        code.AppendLineAt(3, "var root = endpoint.ToOptional();");
        code.AppendLineAt(
            3,
            "return !root.IsPresent ? "
                + runtime
                + "Optional<Fragment?>.Missing : "
                + runtime
                + "Optional<Fragment?>.Present(root.Value?.ToFragment());"
        );
        code.AppendLineAt(2, "}");
    }

    internal static void AppendFromPayloadCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string? modelType,
        string runtime
    )
    {
        var id = member.Id;
        var variant = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Change") + id;
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
            code.AppendLineAt(
                7,
                "if (item.Before.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted payload cannot convert to a ChangeSet. Project it with ToPatch instead.\", nameof(payload));"
            );
            code.AppendLineAt(
                7,
                "if (item.After.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
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
            code.AppendLineAt(7, "foreach (var changeItem in item.Items)");
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                8,
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
            code.AppendLineAt(7, "}");
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                // Granular transitions retain full key orders; an asymmetric
                // order history only carries the desired order.
                code.AppendLineAt(
                    7,
                    "if (item.BeforeOrder is null != item.AfterOrder is null) throw new global::System.ArgumentException(\"A keyed payload with an incomplete order history cannot convert to a ChangeSet. Project it with ToPatch instead.\", nameof(payload));"
                );
                code.AppendLineAt(7, "__payloadBeforeOrder" + id + " = item.BeforeOrder;");
                code.AppendLineAt(7, "__payloadAfterOrder" + id + " = item.AfterOrder;");
            }
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "break;");
        }
        else
        {
            code.AppendLineAt(
                6,
                "if (item.Before is null || item.After is null) throw new global::System.ArgumentException(\"Both transition endpoints are required.\", nameof(payload));"
            );
            // A redacted before-state is undisclosed, not absent: only a
            // baseline-discarding projection may consume it.
            code.AppendLineAt(
                6,
                "if (item.Before.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted payload cannot convert to a ChangeSet. Project it with ToPatch instead.\", nameof(payload));"
            );
            code.AppendLineAt(
                6,
                "if (item.After.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\", nameof(payload));"
            );
            code.AppendLineAt(6, "__payloadHas" + id + " = true;");
            code.AppendLineAt(6, "__payloadBefore" + id + " = item.Before.ToOptional();");
            code.AppendLineAt(6, "__payloadAfter" + id + " = item.After.ToOptional();");
            code.AppendLineAt(6, "break;");
        }
    }
}
