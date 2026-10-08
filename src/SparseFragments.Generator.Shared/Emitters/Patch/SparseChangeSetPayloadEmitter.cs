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
        string? modelType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangeSetPayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadRoot = PayloadName(modelType, "Root");
        var payloadChange = PayloadName(modelType, "Change");
        var variants = members.Where(static member => !member.Property.IsJsonIgnored).ToArray();

        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(1, "public class " + payloadCore);
        code.AppendLineAt(1, "{");
        AppendJsonProperty(code, 2, "Changes", 1);
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.List<"
                + payloadChange
                + "> Changes { get; set; } = new();"
        );
        if (modelType is not null)
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + ".ChangeSet ToChangeSet() => "
                    + modelType
                    + ".ChangeSet.FromPayload(this);"
            );
        code.AppendLineAt(1, "}");
        code.AppendLine();
        code.AppendLineAt(1, "public sealed class ChangeSetPayload : " + payloadCore);
        code.AppendLineAt(1, "{");
        AppendJsonProperty(code, 2, "Version", 0);
        code.AppendLineAt(2, "public int Version { get; set; } = 1;");
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
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                var childModelType = member.ChildModel!.Value.NonNullableName;
                var childRoot = childModelType + "." + PayloadName(childModelType, "Root");
                code.AppendLineAt(3, "var member" + member.Id + " = value." + property + ";");
                code.AppendLineAt(
                    3,
                    "if (member"
                        + member.Id
                        + ".IsPresent) result.Members.Add(new "
                        + payloadChange
                        + member.Id
                        + " { Value = "
                        + endpoint
                        + "<"
                        + childRoot
                        + "?>.FromOptional("
                        + runtime
                        + "Optional<"
                        + childRoot
                        + "?>.Present(member"
                        + member.Id
                        + ".Value is null ? null : "
                        + childRoot
                        + ".FromFragment(member"
                        + member.Id
                        + ".Value!))) });"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "if (value."
                        + property
                        + ".IsPresent) result.Members.Add(new "
                        + payloadChange
                        + member.Id
                        + " { Value = "
                        + endpoint
                        + "<"
                        + SparseChangeSetBasicsEmitter.FragmentValueType(member)
                        + ">.FromOptional(value."
                        + property
                        + ") });"
                );
            }
        }
        code.AppendLineAt(3, "return result;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "public Fragment ToFragment()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (Members is null) throw new global::System.ArgumentException(\"Root members must not be null.\", nameof(Members));"
        );
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
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
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
            code.AppendLineAt(3, "bool seen" + member.Id + " = false;");
        code.AppendLineAt(3, "foreach (var member in Members)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "switch (member)");
        code.AppendLineAt(4, "{");
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
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
        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
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
            AppendMemberPayload(code, member, endpoint, dialect, modelType);
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
        var endpoint = runtime + "ChangeSetPayloadEndpoint";
        var payloadRoot = PayloadName(modelType, "Root");
        var payloadChange = PayloadName(modelType, "Change");
        var rootChange = PayloadName(modelType, "RootChange");
        code.AppendLineAt(2, "public ChangeSetPayload ToPayload()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var payload = new ChangeSetPayload();");
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "payload.Changes.Add(new "
                + rootChange
                + " { Before = __SparsePayloadRoot(__sparse_wholeBefore), After = __SparsePayloadRoot(__sparse_wholeAfter) });"
        );
        code.AppendLineAt(4, "return payload;");
        code.AppendLineAt(3, "}");

        foreach (var member in members.Where(static member => !member.Property.IsJsonIgnored))
        {
            var id = member.Id;
            var variant = payloadChange + id;
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    "if (" + SparseChangeSetBasicsEmitter.NestedField(member) + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    "payload.Changes.Add(new "
                        + variant
                        + " { Nested = "
                        + SparseChangeSetBasicsEmitter.NestedField(member)
                        + "!.ToPayload() });"
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
                code.AppendLineAt(
                    4,
                    "if (" + SparseChangeSetBasicsEmitter.KeyedWholeFlag(member) + ")"
                );
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    5,
                    "entry.Before = "
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
                    + "!item.Before.IsPresent ? null : "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.FromOptional(item.Before))";
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
                        + "ChangeSetPayloadItemKind.Add : item.IsRemoved ? "
                        + runtime
                        + "ChangeSetPayloadItemKind.Remove : item.IsEdited ? "
                        + runtime
                        + "ChangeSetPayloadItemKind.Edit : "
                        + runtime
                        + "ChangeSetPayloadItemKind.Reorder };"
                );
                if (SparseChangeSetBasicsEmitter.IsKeyed(member))
                {
                    code.AppendLineAt(6, "mapped.BeforeIndex = item.BeforeIndex;");
                    code.AppendLineAt(6, "mapped.AfterIndex = item.AfterIndex;");
                    code.AppendLineAt(6, "mapped.IsReordered = item.IsReordered;");
                }
                if (isModelValue)
                    code.AppendLineAt(6, "if (item.IsEdited) mapped.Edit = item.Edit.ToPayload();");
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
                code.AppendLineAt(4, "payload.Changes.Add(entry);");
                code.AppendLineAt(3, "}");
            }
            else
            {
                code.AppendLineAt(3, "if (" + SparseChangeSetBasicsEmitter.HasField(member) + ")");
                code.AppendLineAt(3, "{");
                var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
                code.AppendLineAt(
                    4,
                    "payload.Changes.Add(new "
                        + variant
                        + " { Before = "
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
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(3, "return payload;");
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

    internal static void AppendFromPayload(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangeSetPayloadEndpoint";
        var payloadCore = PayloadName(modelType, "Core");
        var payloadRoot = PayloadName(modelType, "Root");
        var rootChange = PayloadName(modelType, "RootChange");
        code.AppendLineAt(2, "internal static ChangeSet FromPayload(" + payloadCore + " payload)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (payload is null) throw new global::System.ArgumentNullException(nameof(payload));"
        );
        code.AppendLineAt(
            3,
            "if (payload is ChangeSetPayload rootPayload && rootPayload.Version != 1) throw new global::System.ArgumentException(\"Unsupported ChangeSet payload version.\", nameof(payload));"
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
            AppendFromPayloadCase(code, member, modelType);
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

    private static void AppendFromPayloadCase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string? modelType
    )
    {
        var id = member.Id;
        var variant = PayloadName(modelType, "Change") + id;
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
            code.AppendLineAt(6, "__payloadNested" + id + " = item.Nested.ToChangeSet();");
            code.AppendLineAt(6, "break;");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
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
            code.AppendLineAt(7, "foreach (var changeItem in item.Items)");
            code.AppendLineAt(7, "{");
            code.AppendLineAt(
                8,
                "__payloadItems" + id + ".Add(__SparsePayloadItem" + id + "(changeItem));"
            );
            code.AppendLineAt(7, "}");
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
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
            code.AppendLineAt(6, "__payloadHas" + id + " = true;");
            code.AppendLineAt(6, "__payloadBefore" + id + " = item.Before.ToOptional();");
            code.AppendLineAt(6, "__payloadAfter" + id + " = item.After.ToOptional();");
            code.AppendLineAt(6, "break;");
        }
    }

    private static void AppendMemberPayload(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string endpoint,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var id = member.Id;
        var runtime = dialect.RuntimeNamespace;
        var payloadChange = PayloadName(modelType, "Change");
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(1, "public sealed class " + payloadChange + id + " : " + payloadChange);
        code.AppendLineAt(1, "{");
        var memberValueType = SparseChangeSetBasicsEmitter.IsNested(member)
            ? member.ChildModel!.Value.NonNullableName
                + "."
                + PayloadName(member.ChildModel.Value.NonNullableName, "Root")
                + "?"
            : SparseChangeSetBasicsEmitter.FragmentValueType(member);
        AppendIgnoreNull(code, 2);
        AppendJsonProperty(code, 2, "Value", 0);
        code.AppendLineAt(
            2,
            "public " + endpoint + "<" + memberValueType + ">? Value { get; set; }"
        );
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childModelType = member.ChildModel!.Value.NonNullableName;
            var childPayload = childModelType + "." + PayloadName(childModelType, "Core");
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "Nested", 1);
            code.AppendLineAt(2, "public " + childPayload + "? Nested { get; set; }");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "Before", 2);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + valueType + ">? Before { get; set; }"
            );
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "After", 3);
            code.AppendLineAt(2, "public " + endpoint + "<" + valueType + ">? After { get; set; }");
            AppendJsonProperty(code, 2, "Items", 4);
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.List<"
                    + PayloadName(modelType, "Item")
                    + id
                    + "> Items { get; set; } = new();"
            );
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                AppendIgnoreNull(code, 2);
                AppendJsonProperty(code, 2, "BeforeOrder", 5);
                code.AppendLineAt(
                    2,
                    "public global::System.Collections.Generic.List<"
                        + keyType
                        + ">? BeforeOrder { get; set; }"
                );
                AppendIgnoreNull(code, 2);
                AppendJsonProperty(code, 2, "AfterOrder", 6);
                code.AppendLineAt(
                    2,
                    "public global::System.Collections.Generic.List<"
                        + keyType
                        + ">? AfterOrder { get; set; }"
                );
            }
        }
        else
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "Before", 0);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + valueType + ">? Before { get; set; }"
            );
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "After", 1);
            code.AppendLineAt(2, "public " + endpoint + "<" + valueType + ">? After { get; set; }");
        }
        code.AppendLineAt(1, "}");

        if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
            var itemValueType = SparseChangeSetBasicsEmitter.IsKeyed(member)
                ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
                : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
            var isModelValue = SparseChangeSetBasicsEmitter.IsKeyed(member)
                ? member.Collection.ElementType.IsFragmentModel
                : member.Collection.ValueType?.IsFragmentModel == true;
            var childName = SparseChangeSetBasicsEmitter.IsKeyed(member)
                ? member.Collection.ElementType.NonNullableName
                    + "."
                    + PayloadName(member.Collection.ElementType.NonNullableName, "Core")
                : member.Collection.ValueType?.NonNullableName
                    + "."
                    + PayloadName(member.Collection.ValueType!.Value.NonNullableName, "Core");
            code.AppendLineAt(
                1,
                "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
            );
            code.AppendLineAt(1, "public sealed class " + PayloadName(modelType, "Item") + id);
            code.AppendLineAt(1, "{");
            AppendJsonProperty(code, 2, "Key", 0);
            code.AppendLineAt(2, "public " + keyType + " Key { get; set; } = default!;");
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "Before", 4);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + itemValueType + ">? Before { get; set; }"
            );
            AppendIgnoreNull(code, 2);
            AppendJsonProperty(code, 2, "After", 5);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + itemValueType + ">? After { get; set; }"
            );
            AppendJsonProperty(code, 2, "Kind", 1);
            code.AppendLineAt(
                2,
                "public " + runtime + "ChangeSetPayloadItemKind Kind { get; set; }"
            );
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                AppendJsonProperty(code, 2, "BeforeIndex", 2);
                code.AppendLineAt(2, "public int BeforeIndex { get; set; } = -1;");
                AppendJsonProperty(code, 2, "AfterIndex", 3);
                code.AppendLineAt(2, "public int AfterIndex { get; set; } = -1;");
                AppendIgnoreDefault(code, 2);
                AppendJsonProperty(code, 2, "IsReordered", 6);
                code.AppendLineAt(2, "public bool IsReordered { get; set; }");
            }
            if (isModelValue)
            {
                AppendIgnoreNull(code, 2);
                AppendJsonProperty(code, 2, "Edit", 7);
                code.AppendLineAt(2, "public " + childName + "? Edit { get; set; }");
            }
            code.AppendLineAt(1, "}");
        }
    }

    internal static string PayloadName(string? modelType, string suffix)
    {
        var source = (modelType ?? "SparseGeneratedModel").Replace("global::", string.Empty);
        var name = new System.Text.StringBuilder(source.Length);
        foreach (var character in source)
            name.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        if (name.Length == 0 || char.IsDigit(name[0]))
            name.Insert(0, '_');
        return name + "ChangeSetPayload" + suffix;
    }

    private static void AppendIgnoreNull(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]"
        );

    private static void AppendJsonProperty(
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

    private static void AppendIgnoreDefault(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]"
        );
}
