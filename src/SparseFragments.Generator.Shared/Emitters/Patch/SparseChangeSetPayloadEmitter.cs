using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the typed, serializer-facing payload DTOs for one model.</summary>
internal static class SparseChangeSetPayloadEmitter
{
    internal static void AppendPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames = default
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadRoot = PayloadName(modelType, "Root");
        var payloadChange = PayloadName(modelType, "Change");
        var variants = members.Where(static member => !member.Property.IsJsonIgnored).ToArray();
        // Write-only members never appear in the read projection; their
        // transport variants below still carry the command after-state.
        var readable = variants
            .Where(member =>
                dialect.GetTransport(member.Property.Name) != SparseMemberTransport.WriteOnly
            )
            .ToArray();

        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonUnmappedMemberHandling(global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]"
        );
        code.AppendLineAt(1, "public class " + payloadCore);
        code.AppendLineAt(1, "{");
        AppendJsonProperty(code, 2, "Changes", 1);
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + ">? Changes { get; set; }"
        );
        if (modelType is not null)
            code.AppendLineAt(
                2,
                "internal "
                    + modelType
                    + ".ChangeSet ToChangeSetCore() => "
                    + modelType
                    + ".ChangeSet.FromPayloadCore(this);"
            );
        if (modelType is not null && !SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
            code.AppendLineAt(
                2,
                "internal "
                    + modelType
                    + ".Patch ToPatchCore() => "
                    + modelType
                    + ".ChangeSet.PatchFromPayloadCore(this);"
            );
        if (modelType is not null)
            code.AppendLineAt(
                2,
                "internal static "
                    + payloadCore
                    + " FromPatchCore("
                    + modelType
                    + ".Patch patch) => patch.ToChangePayloadCore();"
            );
        SparseChangeSetMixedEmitter.AppendMixedPayloadSurface(
            code,
            members,
            dialect,
            modelType,
            ignoredSettablePropertyNames
        );
        if (modelType is not null && SparseDownstreamPolicy.HasAnyNonFullPolicy(dialect))
        {
            SparseChangeSetPayloadProjectionEmitter.AppendCoreToPatch(
                code,
                members,
                dialect,
                modelType
            );
        }
        code.AppendLineAt(1, "}");
        code.AppendLine();
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonUnmappedMemberHandling(global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]"
        );
        code.AppendLineAt(1, "public sealed class ChangePayload : " + payloadCore);
        code.AppendLineAt(1, "{");
        AppendJsonProperty(code, 2, "Version", 0);
        code.AppendLineAt(
            2,
            "/// <summary>Gets or sets the provisional wire version token.</summary>"
        );
        code.AppendLineAt(2, "public string? Version { get; set; }");
        if (modelType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Converts this validated envelope to a complete change set.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Redacted or otherwise incomplete histories are rejected; project them with <see cref=\"ToPatch\"/> instead.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + ".ChangeSet ToChangeSet() => "
                    + modelType
                    + ".ChangeSet.FromPayload(this);"
            );
            // Baseline-discarding projection is owned by the payload core (mixed
            // partition) so ordinary transitions and redacted-before blind sets
            // route identically; the envelope re-declares it to keep the
            // conversion discoverable on the validated envelope type. When member
            // transport policies apply the downstream projection owns ToPatch
            // instead (strict-aware), so the mixed re-declaration is skipped to
            // keep a single seam with no duplicate member.
            if (!SparseDownstreamPolicy.HasOwnNonFullPolicy(members, dialect))
            {
                code.AppendLineAt(
                    2,
                    "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
                );
                code.AppendLineAt(
                    2,
                    "/// <remarks>Redacted before-states project to their requested after-state without historical comparison; ordinary members project their after-state too. The result is baseline-free and can no longer rebase or report conflicts.</remarks>"
                );
                code.AppendLineAt(
                    2,
                    "public new " + modelType + ".Patch ToPatch() => base.ToPatch();"
                );
            }
            code.AppendLineAt(
                2,
                "/// <summary>Builds a baseline-free command envelope from a patch.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Before-states are redacted by construction; the result only supports <see cref=\"ToPatch\"/>.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public static ChangePayload FromPatch(" + modelType + ".Patch patch)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
            );
            code.AppendLineAt(
                3,
                "return new ChangePayload { Version = "
                    + SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true)
                    + ", Changes = "
                    + payloadCore
                    + ".FromPatchCore(patch).Changes };"
            );
            code.AppendLineAt(2, "}");
            SparseChangeSetPayloadProjectionEmitter.AppendRootToPatch(
                code,
                members,
                dialect,
                modelType
            );
        }
        code.AppendLineAt(1, "}");
        code.AppendLine();

        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(1, "public sealed class " + payloadRoot);
        code.AppendLineAt(1, "{");
        AppendJsonProperty(code, 2, "Members", 0);
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + "> Members { get; set; } = new();"
        );
        code.AppendLineAt(2, "public static " + payloadRoot + " FromFragment(Fragment value)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var result = new " + payloadRoot + "();");
        AppendFromFragmentMembers(
            code,
            System.Collections.Immutable.ImmutableArray.CreateRange(readable),
            endpoint,
            runtime,
            payloadChange,
            false
        );
        code.AppendLineAt(3, "return result;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static " + payloadRoot + " FromFragment(Fragment value, bool redactBefores)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var result = new " + payloadRoot + "();");
        AppendFromFragmentMembers(
            code,
            System.Collections.Immutable.ImmutableArray.CreateRange(readable),
            endpoint,
            runtime,
            payloadChange,
            true
        );
        code.AppendLineAt(3, "return result;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "public Fragment ToFragment()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (Members is null) throw new global::System.ArgumentException(\"Root members must not be null.\", nameof(Members));"
        );
        foreach (var member in readable)
        {
            var valueType = SparseChangeSetBasicsEmitter.IsNested(member)
                ? member.ChildFragmentType + "?"
                : SparseChangeSetBasicsEmitter.FragmentValueType(member);
            code.AppendLineAt(
                3,
                runtime
                    + "Optional<"
                    + valueType
                    + "> rootMember"
                    + member.Id
                    + " = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Missing;"
            );
        }
        foreach (var member in readable)
            code.AppendLineAt(3, "bool seen" + member.Id + " = false;");
        code.AppendLineAt(3, "foreach (var member in Members)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (member)");
        code.AppendLineAt(4, "{");
        foreach (var member in readable)
        {
            code.AppendLineAt(5, "case " + payloadChange + member.Id + " item:");
            code.AppendLineAt(
                6,
                "if (seen"
                    + member.Id
                    + ") throw new global::System.ArgumentException(\"Root contains duplicate member values.\", nameof(Members));"
            );
            code.AppendLineAt(6, "seen" + member.Id + " = true;");
            code.AppendLineAt(
                6,
                "if (item.Value is null) throw new global::System.ArgumentException(\"Root member value is required.\", nameof(Members));"
            );
            code.AppendLineAt(
                6,
                "if (item.Value.State == "
                    + runtime
                    + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted root member cannot convert to a fragment.\", nameof(Members));"
            );
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childFragment = member.ChildFragmentType + "?";
                code.AppendLineAt(6, "var member" + member.Id + " = item.Value.ToOptional();");
                code.AppendLineAt(
                    6,
                    "rootMember"
                        + member.Id
                        + " = member"
                        + member.Id
                        + ".IsPresent ? "
                        + runtime
                        + "Optional<"
                        + childFragment
                        + ">.Present(member"
                        + member.Id
                        + ".Value is null ? null : member"
                        + member.Id
                        + ".Value.ToFragment()) : "
                        + runtime
                        + "Optional<"
                        + childFragment
                        + ">.Missing;"
                );
            }
            else
            {
                code.AppendLineAt(6, "rootMember" + member.Id + " = item.Value.ToOptional();");
            }
            code.AppendLineAt(6, "break;");
        }
        code.AppendLineAt(
            5,
            "default: throw new global::System.ArgumentException(\"Root contains an unknown member value.\", nameof(Members));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return new Fragment");
        code.AppendLineAt(3, "{");
        foreach (var member in readable)
            code.AppendLineAt(
                4,
                SparseNaming.EscapeIdentifier(member.Property.Name)
                    + " = rootMember"
                    + member.Id
                    + ","
            );
        code.AppendLineAt(3, "};");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(1, "}");
        code.AppendLine();

        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"member\")]"
        );
        var rootDiscriminatorBuilder = new System.Text.StringBuilder("$root");
        while (variants.Any(member => member.Property.Name == rootDiscriminatorBuilder.ToString()))
            rootDiscriminatorBuilder.Append('$');
        var rootDiscriminator = rootDiscriminatorBuilder.ToString();
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonDerivedType(typeof("
                + PayloadName(modelType, "RootChange")
                + "), "
                + SymbolDisplay.FormatLiteral(rootDiscriminator, true)
                + ")]"
        );
        foreach (var member in variants)
        {
            code.AppendLineAt(
                1,
                "[global::System.Text.Json.Serialization.JsonDerivedType(typeof("
                    + payloadChange
                    + member.Id
                    + "), "
                    + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                    + ")]"
            );
        }
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(1, "public abstract class " + payloadChange + " { }");
        code.AppendLine();

        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class " + PayloadName(modelType, "RootChange") + " : " + payloadChange
        );
        code.AppendLineAt(1, "{");
        AppendIgnoreNull(code, 2);
        AppendJsonProperty(code, 2, "Before", 0);
        code.AppendLineAt(2, "public " + endpoint + "<" + payloadRoot + ">? Before { get; set; }");
        AppendIgnoreNull(code, 2);
        AppendJsonProperty(code, 2, "After", 1);
        code.AppendLineAt(2, "public " + endpoint + "<" + payloadRoot + ">? After { get; set; }");
        code.AppendLineAt(1, "}");
        code.AppendLine();

        foreach (var member in variants)
        {
            SparseChangeSetPayloadItemEmitter.AppendMemberPayload(
                code,
                member,
                endpoint,
                dialect,
                modelType
            );
            code.AppendLine();
        }
    }

    internal static void AppendToPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadRoot = PayloadName(modelType, "Root");
        var payloadChange = PayloadName(modelType, "Change");
        var rootChange = PayloadName(modelType, "RootChange");
        var versionLiteral = SymbolDisplay.FormatLiteral(dialect.ChangePayloadVersion, true);
        code.AppendLineAt(2, "public ChangePayload ToPayload()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "return new ChangePayload { Version = "
                + versionLiteral
                + ", Changes = ToPayloadCore(false).Changes };"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "internal " + payloadCore + " ToPayloadCore(bool redactBefores)");
        code.AppendLineAt(2, "{");
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
            var variant = payloadChange + id;
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
                        + PayloadName(modelType, "Item")
                        + id
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

    /// <summary>Emits the per-member snapshot loop shared by honest and redacting roots.</summary>
    /// <remarks>Honest snapshots never redact; before snapshots redact flagged members plus everything under an ambient subtree flag.</remarks>
    private static void AppendFromFragmentMembers(
        SharedIndentedBuilder code,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        string endpoint,
        string runtime,
        string payloadChange,
        bool redactFlagged
    )
    {
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var redact = redactFlagged && member.RedactBefore ? "true" : "redactBefores";
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childModelType = member.ChildModel!.Value.NonNullableName;
                var childRoot = childModelType + "." + PayloadName(childModelType, "Root");
                var childCall = redactFlagged
                    ? childRoot + ".FromFragment(member" + member.Id + ".Value!, " + redact + ")"
                    : childRoot + ".FromFragment(member" + member.Id + ".Value!)";
                code.AppendLineAt(3, "var member" + member.Id + " = value." + property + ";");
                var valueExpression = redactFlagged
                    ? "("
                        + redact
                        + ") ? "
                        + endpoint
                        + "<"
                        + childRoot
                        + "?>.Redacted() : "
                        + SnapshotValueExpression(
                            endpoint,
                            runtime,
                            childRoot + "?",
                            "member" + member.Id,
                            childCall
                        )
                    : SnapshotValueExpression(
                        endpoint,
                        runtime,
                        childRoot + "?",
                        "member" + member.Id,
                        childCall
                    );
                code.AppendLineAt(
                    3,
                    "if (member"
                        + member.Id
                        + ".IsPresent) result.Members.Add(new "
                        + payloadChange
                        + member.Id
                        + " { Value = "
                        + valueExpression
                        + " });"
                );
            }
            else
            {
                var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
                var valueExpression = redactFlagged
                    ? "("
                        + redact
                        + ") ? "
                        + endpoint
                        + "<"
                        + valueType
                        + ">.Redacted() : "
                        + endpoint
                        + "<"
                        + valueType
                        + ">.FromOptional(value."
                        + property
                        + ")"
                    : endpoint + "<" + valueType + ">.FromOptional(value." + property + ")";
                code.AppendLineAt(
                    3,
                    "if (value."
                        + property
                        + ".IsPresent) result.Members.Add(new "
                        + payloadChange
                        + member.Id
                        + " { Value = "
                        + valueExpression
                        + " });"
                );
            }
        }
    }

    private static string SnapshotValueExpression(
        string endpoint,
        string runtime,
        string childRoot,
        string holder,
        string childCall
    ) =>
        endpoint
        + "<"
        + childRoot
        + ">.FromOptional("
        + runtime
        + "Optional<"
        + childRoot
        + ">.Present("
        + holder
        + ".Value is null ? null : "
        + childCall
        + "))";

    internal static string PayloadName(string? modelType, string suffix)
    {
        var source = (modelType ?? "SparseGeneratedModel").Replace("global::", string.Empty);
        var name = new System.Text.StringBuilder(source.Length);
        foreach (var character in source)
            name.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        if (name.Length == 0 || char.IsDigit(name[0]))
            name.Insert(0, '_');
        return name + "ChangePayload" + suffix;
    }

    internal static void AppendIgnoreNull(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]"
        );

    internal static void AppendJsonProperty(
        SharedIndentedBuilder code,
        int indent,
        string name,
        int order
    )
    {
        var jsonName = char.ToLowerInvariant(name[0]) + name.Substring(1);
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonPropertyName(\"" + jsonName + "\")]"
        );
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonPropertyOrder(" + order + ")]"
        );
    }

    internal static void AppendIgnoreDefault(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]"
        );
}
