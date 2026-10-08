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
        code.AppendLineAt(
            2,
            "private static "
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
        var beforeIsPresent = itemValueIsReference
            ? "before.IsPresent && before.Value is not null"
            : "before.IsPresent";
        var afterIsPresent = itemValueIsReference
            ? "after.IsPresent && after.Value is not null"
            : "after.IsPresent";
        var beforeValue = "before.Value" + (itemValueIsReference ? "!" : string.Empty);
        var afterValue = "after.Value" + (itemValueIsReference ? "!" : string.Empty);
        var added = "item.Kind == " + runtime + "ChangeSetPayloadItemKind.Add";
        var removed = "item.Kind == " + runtime + "ChangeSetPayloadItemKind.Remove";
        var edited = "item.Kind == " + runtime + "ChangeSetPayloadItemKind.Edit";
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
}
