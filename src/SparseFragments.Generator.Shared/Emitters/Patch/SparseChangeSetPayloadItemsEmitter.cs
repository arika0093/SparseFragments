using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

internal static partial class SparseChangeSetPayloadEmitter
{
    private static void AppendPayloadItemHelper(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string? modelType
    )
    {
        var id = member.Id;
        var trans = SparseChangeSetBasicsEmitter.TransNameFor(members, member);
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var valueCs = isKeyed
            ? SparseChangeSetBasicsEmitter.ElementChangeSetOf(member)
            : SparseChangeSetBasicsEmitter.ValueChangeSetOf(member);
        var valueFrag = isKeyed
            ? SparseChangeSetBasicsEmitter.ElementFragmentOf(member)
            : SparseChangeSetBasicsEmitter.ValueFragmentOf(member);
        var itemValueType = isKeyed
            ? SparseChangeSetBasicsEmitter.ElementTypeOf(member)
            : SparseChangeSetBasicsEmitter.ValueTypeOf(member);
        var hasEdit = isKeyed || member.Collection.ValueType?.IsFragmentModel == true;
        code.AppendLineAt(
            2,
            "private static "
                + trans
                + ".Item __SparsePayloadItem"
                + id
                + "("
                + PayloadName(modelType, "Item")
                + id
                + " item)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (item is null) throw new global::System.ArgumentException(\"Payload item is required.\");"
        );
        code.AppendLineAt(
            3,
            "var before = item.Before is null ? "
                + runtime
                + "Optional<"
                + itemValueType
                + ">.Missing : item.Before.ToOptional();"
        );
        code.AppendLineAt(
            3,
            "var after = item.After is null ? "
                + runtime
                + "Optional<"
                + itemValueType
                + ">.Missing : item.After.ToOptional();"
        );
        var added = "item.Kind == " + runtime + "ChangeSetPayloadItemKind.Add";
        var removed = "item.Kind == " + runtime + "ChangeSetPayloadItemKind.Remove";
        var edited = "item.Kind == " + runtime + "ChangeSetPayloadItemKind.Edit";
        if (isKeyed)
        {
            var beforeFragment =
                "("
                + "before.IsPresent && before.Value is not null ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From(before.Value!)) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var afterFragment =
                "("
                + "after.IsPresent && after.Value is not null ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From(after.Value!)) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var derivedEdit = valueCs + ".Between(" + beforeFragment + ", " + afterFragment + ")";
            var edit = hasEdit
                ? "("
                    + edited
                    + " ? item.Edit?.ToChangeSet() ?? throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\") : "
                    + derivedEdit
                    + ")"
                : derivedEdit;
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + ".Item(item.Key, before, after, item.BeforeIndex, item.AfterIndex, "
                    + added
                    + ", "
                    + removed
                    + ", "
                    + edited
                    + ", item.IsReordered, "
                    + edit
                    + ", false);"
            );
        }
        else if (hasEdit)
        {
            var beforeFragment =
                "("
                + "before.IsPresent && before.Value is not null ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From(before.Value!)) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var afterFragment =
                "("
                + "after.IsPresent && after.Value is not null ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From(after.Value!)) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var derivedEdit = valueCs + ".Between(" + beforeFragment + ", " + afterFragment + ")";
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + ".Item(item.Key, before, after, "
                    + added
                    + ", "
                    + removed
                    + ", "
                    + edited
                    + ", "
                    + "("
                    + edited
                    + " ? item.Edit?.ToChangeSet() ?? throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\") : "
                    + derivedEdit
                    + "), false);"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + ".Item(item.Key, before, after, "
                    + added
                    + ", "
                    + removed
                    + ", "
                    + edited
                    + ", false);"
            );
        }
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
}
