using System;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

internal static class SparseChangeSetPayloadItemEmitter
{
    internal static void AppendPayloadItemHelper(
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
        var itemValueIsReference = isKeyed
            ? member.Collection.ElementType.IsReferenceType
            : member.Collection.ValueType?.IsReferenceType == true;
        var hasEdit = isKeyed || member.Collection.ValueType?.IsFragmentModel == true;
        var isModelValue = isKeyed
            ? member.Collection.ElementType.IsFragmentModel
            : member.Collection.ValueType?.IsFragmentModel == true;
        // Internal so the payload baseline-free projection reuses the same
        // validation and conversion instead of duplicating it.
        code.AppendLineAt(
            2,
            "internal static "
                + trans
                + ".Item __SparsePayloadItem"
                + id
                + "("
                + SparseChangeSetPayloadEmitter.PayloadName(modelType, "Item")
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
            "if (item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Add && item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Remove && item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Edit && item.Kind != "
                + runtime
                + "ChangePayloadItemKind.Reorder) throw new global::System.ArgumentException(\"Unsupported payload item kind.\");"
        );
        if (!isKeyed)
            code.AppendLineAt(
                3,
                "if (item.Kind == "
                    + runtime
                    + "ChangePayloadItemKind.Reorder) throw new global::System.ArgumentException(\"Reorder is not supported for dictionary payload items.\");"
            );
        // Redacted item histories only support the baseline-discarding
        // projection; check states before ToOptional so the error names the
        // conversion rather than the endpoint read.
        code.AppendLineAt(
            3,
            "if (item.Before is not null && item.Before.State == "
                + runtime
                + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A redacted payload cannot convert to a ChangeSet. Project it with ToPatch instead.\");"
        );
        code.AppendLineAt(
            3,
            "if (item.After is not null && item.After.State == "
                + runtime
                + "ChangePayloadState.Redacted) throw new global::System.ArgumentException(\"A payload after-state must be observable.\");"
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
        var added = "item.Kind == " + runtime + "ChangePayloadItemKind.Add";
        var removed = "item.Kind == " + runtime + "ChangePayloadItemKind.Remove";
        var edited = "item.Kind == " + runtime + "ChangePayloadItemKind.Edit";
        // Kind/endpooint consistency: reject invalid payloads before producing an unsafe ChangeSet.
        code.AppendLineAt(3, "if (" + added + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (before.IsPresent || !after.IsPresent) throw new global::System.ArgumentException(\"An added payload item must omit 'before' and contain 'after'.\");"
        );
        if (isModelValue)
            code.AppendLineAt(
                4,
                "if (item.Edit is not null) throw new global::System.ArgumentException(\"An added payload item must not contain 'edit'.\");"
            );
        if (isKeyed)
            code.AppendLineAt(
                4,
                "if (item.BeforeIndex != -1 || item.AfterIndex < 0 || item.IsReordered) throw new global::System.ArgumentException(\"An added keyed payload item has invalid indices.\");"
            );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else if (" + removed + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!before.IsPresent || after.IsPresent) throw new global::System.ArgumentException(\"A removed payload item must contain 'before' and omit 'after'.\");"
        );
        if (isModelValue)
            code.AppendLineAt(
                4,
                "if (item.Edit is not null) throw new global::System.ArgumentException(\"A removed payload item must not contain 'edit'.\");"
            );
        if (isKeyed)
            code.AppendLineAt(
                4,
                "if (item.BeforeIndex < 0 || item.AfterIndex != -1 || item.IsReordered) throw new global::System.ArgumentException(\"A removed keyed payload item has invalid indices.\");"
            );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else if (" + edited + ")");
        code.AppendLineAt(3, "{");
        if (isModelValue)
        {
            code.AppendLineAt(
                4,
                "if (before.IsPresent || after.IsPresent) throw new global::System.ArgumentException(\"An edited model payload item must omit 'before' and 'after'.\");"
            );
            code.AppendLineAt(
                4,
                "if (item.Edit is null) throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\");"
            );
            if (isKeyed)
                code.AppendLineAt(
                    4,
                    "if (item.BeforeIndex < 0 || item.AfterIndex < 0) throw new global::System.ArgumentException(\"An edited keyed payload item has invalid indices.\");"
                );
        }
        else
        {
            code.AppendLineAt(
                4,
                "if (!before.IsPresent || !after.IsPresent) throw new global::System.ArgumentException(\"An edited scalar payload item must contain 'before' and 'after'.\");"
            );
            if (isKeyed)
                code.AppendLineAt(
                    4,
                    "if (item.BeforeIndex < 0 || item.AfterIndex < 0) throw new global::System.ArgumentException(\"An edited keyed payload item has invalid indices.\");"
                );
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        // Reorder (keyed only; dictionary reorder rejected above).
        code.AppendLineAt(
            4,
            "if (before.IsPresent || after.IsPresent) throw new global::System.ArgumentException(\"A reordered payload item must omit 'before' and 'after'.\");"
        );
        if (isModelValue)
            code.AppendLineAt(
                4,
                "if (item.Edit is not null) throw new global::System.ArgumentException(\"A reordered payload item must not contain 'edit'.\");"
            );
        if (isKeyed)
            code.AppendLineAt(
                4,
                "if (!item.IsReordered || item.BeforeIndex < 0 || item.AfterIndex < 0) throw new global::System.ArgumentException(\"A reordered keyed payload item has invalid indices.\");"
            );
        code.AppendLineAt(3, "}");
        var beforeIsPresent = itemValueIsReference
            ? "before.IsPresent && before.Value is not null"
            : "before.IsPresent";
        var afterIsPresent = itemValueIsReference
            ? "after.IsPresent && after.Value is not null"
            : "after.IsPresent";
        var beforeValue = "before.Value" + (itemValueIsReference ? "!" : string.Empty);
        var afterValue = "after.Value" + (itemValueIsReference ? "!" : string.Empty);
        if (isKeyed)
        {
            var beforeFragment =
                "("
                + beforeIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + beforeValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var afterFragment =
                "("
                + afterIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + afterValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var derivedEdit = valueCs + ".Between(" + beforeFragment + ", " + afterFragment + ")";
            var edit = hasEdit
                ? "("
                    + edited
                    + " ? item.Edit?.ToChangeSetCore() ?? throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\") : "
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
                + beforeIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + beforeValue
                + ")) : "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Missing)";
            var afterFragment =
                "("
                + afterIsPresent
                + " ? "
                + runtime
                + "Optional<"
                + valueFrag
                + "?>.Present("
                + valueFrag
                + ".From("
                + afterValue
                + ")) : "
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
                    + " ? item.Edit?.ToChangeSetCore() ?? throw new global::System.ArgumentException(\"Edited payload items require an edit payload.\") : "
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

    internal static void AppendMemberPayload(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string endpoint,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        var id = member.Id;
        var runtime = dialect.RuntimeNamespace;
        var payloadChange = SparseChangeSetPayloadEmitter.PayloadName(modelType, "Change");
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
        );
        code.AppendLineAt(1, "public sealed class " + payloadChange + id + " : " + payloadChange);
        code.AppendLineAt(1, "{");
        var memberValueType = SparseChangeSetBasicsEmitter.IsNested(member)
            ? member.ChildModel!.Value.NonNullableName
                + "."
                + SparseChangeSetPayloadEmitter.PayloadName(
                    member.ChildModel.Value.NonNullableName,
                    "Root"
                )
                + "?"
            : SparseChangeSetBasicsEmitter.FragmentValueType(member);
        SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
        SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Value", 0);
        code.AppendLineAt(
            2,
            "public " + endpoint + "<" + memberValueType + ">? Value { get; set; }"
        );
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var childModelType = member.ChildModel!.Value.NonNullableName;
            var childPayload =
                childModelType
                + "."
                + SparseChangeSetPayloadEmitter.PayloadName(childModelType, "Core");
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Nested", 1);
            code.AppendLineAt(2, "public " + childPayload + "? Nested { get; set; }");
        }
        else if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
        )
        {
            var valueType = SparseChangeSetBasicsEmitter.FragmentValueType(member);
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Before", 2);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + valueType + ">? Before { get; set; }"
            );
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "After", 3);
            code.AppendLineAt(2, "public " + endpoint + "<" + valueType + ">? After { get; set; }");
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Items", 4);
            code.AppendLineAt(
                2,
                "public global::System.Collections.Generic.List<"
                    + SparseChangeSetPayloadEmitter.PayloadName(modelType, "Item")
                    + id
                    + "> Items { get; set; } = new();"
            );
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
                SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "BeforeOrder", 5);
                code.AppendLineAt(
                    2,
                    "public global::System.Collections.Generic.List<"
                        + keyType
                        + ">? BeforeOrder { get; set; }"
                );
                SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "AfterOrder", 6);
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
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Before", 0);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + valueType + ">? Before { get; set; }"
            );
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "After", 1);
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
                    + SparseChangeSetPayloadEmitter.PayloadName(
                        member.Collection.ElementType.NonNullableName,
                        "Core"
                    )
                : member.Collection.ValueType?.NonNullableName
                    + "."
                    + SparseChangeSetPayloadEmitter.PayloadName(
                        member.Collection.ValueType!.Value.NonNullableName,
                        "Core"
                    );
            code.AppendLineAt(
                1,
                "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Advanced)]"
            );
            code.AppendLineAt(
                1,
                "public sealed class "
                    + SparseChangeSetPayloadEmitter.PayloadName(modelType, "Item")
                    + id
            );
            code.AppendLineAt(1, "{");
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Key", 0);
            code.AppendLineAt(2, "public " + keyType + " Key { get; set; } = default!;");
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Before", 4);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + itemValueType + ">? Before { get; set; }"
            );
            SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "After", 5);
            code.AppendLineAt(
                2,
                "public " + endpoint + "<" + itemValueType + ">? After { get; set; }"
            );
            SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Kind", 1);
            code.AppendLineAt(2, "public " + runtime + "ChangePayloadItemKind Kind { get; set; }");
            if (SparseChangeSetBasicsEmitter.IsKeyed(member))
            {
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "BeforeIndex", 2);
                code.AppendLineAt(2, "public int BeforeIndex { get; set; } = -1;");
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "AfterIndex", 3);
                code.AppendLineAt(2, "public int AfterIndex { get; set; } = -1;");
                SparseChangeSetPayloadEmitter.AppendIgnoreDefault(code, 2);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "IsReordered", 6);
                code.AppendLineAt(2, "public bool IsReordered { get; set; }");
            }
            if (isModelValue)
            {
                SparseChangeSetPayloadEmitter.AppendIgnoreNull(code, 2);
                SparseChangeSetPayloadEmitter.AppendJsonProperty(code, 2, "Edit", 7);
                code.AppendLineAt(2, "public " + childName + "? Edit { get; set; }");
            }
            code.AppendLineAt(1, "}");
        }
    }
}
