using System.Collections.Immutable;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Dictionary branches of the resolution fragment applier (issue #203).</summary>
internal static class SparseResolutionDictEmitter
{
    internal static void AppendDictBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        string runtime
    )
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        var keyType = KeyTypeOf(member);
        var valueValueType = ValueTypeOf(member);
        var valueIsModel = member.Collection.ValueType?.IsFragmentModel == true;
        var valueChangeSet = valueIsModel ? ValueChangeSetOf(member) : string.Empty;
        var valueFragment = valueIsModel ? ValueFragmentOf(member) : string.Empty;
        var id = member.Id;
        var segmentKind = runtime + "SparsePathSegmentKind";
        var dictType =
            "global::System.Collections.Generic.Dictionary<"
            + keyType
            + ", "
            + valueValueType
            + ">";
        var genericDictType =
            "global::System.Collections.Generic.IDictionary<"
            + keyType
            + ", "
            + valueValueType
            + ">";

        body.AppendLineAt(4, "if (offset + 1 == path.Depth)");
        body.AppendLineAt(4, "{");
        SparseResolutionApplyEmitter.AppendCheckedSlot(
            body,
            "builder." + esc,
            optional,
            valueType,
            name,
            id
        );
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "if (offset + 2 > path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"The resolution path is too short for member '" + name + "'.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "var dictSegment" + id + " = path.Segments[offset + 1];");
        body.AppendLineAt(4, "var rawDictKey" + id + " = dictSegment" + id + ".Key;");
        body.AppendLineAt(
            4,
            "if (dictSegment"
                + id
                + ".Kind != "
                + segmentKind
                + ".Key || rawDictKey"
                + id
                + " is null || !typeof("
                + keyType
                + ").IsInstanceOfType(rawDictKey"
                + id
                + "))"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"The key segment for member '" + name + "' has an unexpected type.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "var dictKey" + id + " = (" + keyType + ")rawDictKey" + id + ";");
        if (!valueIsModel)
        {
            body.AppendLineAt(4, "if (offset + 2 != path.Depth)");
            body.AppendLineAt(4, "{");
            body.AppendLineAt(
                5,
                "error = \"Member '" + name + "' has no resolvable entry leaves.\";"
            );
            body.AppendLineAt(5, "return false;");
            body.AppendLineAt(4, "}");
        }

        body.AppendLineAt(
            4,
            "if (!builder." + esc + ".IsPresent || builder." + esc + ".Value is null)"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "if (!desired.IsPresent)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "updated = builder.Build();");
        body.AppendLineAt(6, "return true;");
        body.AppendLineAt(5, "}");
        AppendDictAbsentAdd(
            body,
            esc,
            optional,
            dictType,
            keyType,
            valueValueType,
            valueFragment,
            valueIsModel,
            id,
            name,
            runtime
        );
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, dictType + "? editable" + id + " = null;");
        body.AppendLineAt(
            4,
            "if (builder." + esc + ".Value is " + dictType + " concrete" + id + ")"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "editable"
                + id
                + " = new "
                + dictType
                + "(concrete"
                + id
                + ", concrete"
                + id
                + ".Comparer);"
        );
        body.AppendLineAt(4, "}");
        body.AppendLineAt(
            4,
            "else if (builder." + esc + ".Value is " + genericDictType + " generic" + id + ")"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "editable"
                + id
                + " = new "
                + dictType
                + "(generic"
                + id
                + ", global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default);"
        );
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"Member '" + name + "' has an unsupported dictionary shape.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        // Typed lookup honors the dictionary comparer; no display-text fallback.
        body.AppendLineAt(
            4,
            "var found" + id + " = editable" + id + ".TryGetValue(dictKey" + id + ", out _);"
        );
        if (valueIsModel)
        {
            AppendDictStructuralEntry(
                body,
                esc,
                optional,
                name,
                keyType,
                valueValueType,
                valueFragment,
                valueChangeSet,
                id,
                member.Collection.ValueType?.IsReferenceType == true
                    || member.Collection.ValueType?.Name.EndsWith("?", StringComparison.Ordinal)
                        == true,
                runtime
            );
        }
        else
        {
            AppendDictScalarEntry(body, esc, optional, name, valueValueType, id);
        }
    }

    private static void AppendDictAbsentAdd(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string dictType,
        string keyType,
        string valueValueType,
        string valueFragment,
        bool valueIsModel,
        int id,
        string name,
        string runtime
    )
    {
        var checkValue = SparseResolutionApplyEmitter.NonNullable(valueValueType);
        body.AppendLineAt(5, "if (desired.Value is " + checkValue + " absentValue" + id + ")");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "builder."
                + esc
                + " = "
                + optional
                + ".Present(new "
                + dictType
                + " { [dictKey"
                + id
                + "] = absentValue"
                + id
                + " });"
        );
        body.AppendLineAt(6, "updated = builder.Build();");
        body.AppendLineAt(6, "return true;");
        body.AppendLineAt(5, "}");
        if (valueIsModel)
        {
            body.AppendLineAt(
                5,
                "if (desired.Value is " + valueFragment + " absentFragment" + id + ")"
            );
            body.AppendLineAt(5, "{");
            body.AppendLineAt(
                6,
                "builder."
                    + esc
                    + " = "
                    + optional
                    + ".Present(new "
                    + dictType
                    + " { [dictKey"
                    + id
                    + "] = absentFragment"
                    + id
                    + ".ToModel() });"
            );
            body.AppendLineAt(6, "updated = builder.Build();");
            body.AppendLineAt(6, "return true;");
            body.AppendLineAt(5, "}");
            var valueModel = valueFragment.EndsWith(".Fragment", System.StringComparison.Ordinal)
                ? valueFragment.Substring(0, valueFragment.Length - ".Fragment".Length)
                : valueValueType;
            body.AppendLineAt(5, "if (desired.Value is " + valueModel + " absentModel" + id + ")");
            body.AppendLineAt(5, "{");
            body.AppendLineAt(
                6,
                "builder."
                    + esc
                    + " = "
                    + optional
                    + ".Present(new "
                    + dictType
                    + " { [dictKey"
                    + id
                    + "] = absentModel"
                    + id
                    + " });"
            );
            body.AppendLineAt(6, "updated = builder.Build();");
            body.AppendLineAt(6, "return true;");
            body.AppendLineAt(5, "}");
        }

        body.AppendLineAt(
            5,
            "error = \"Member '" + name + "' is absent and the resolution value cannot seed it.\";"
        );
        body.AppendLineAt(5, "return false;");
        _ = runtime;
        _ = keyType;
    }

    private static void AppendDictScalarEntry(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string name,
        string valueValueType,
        int id
    )
    {
        var checkValue = SparseResolutionApplyEmitter.NonNullable(valueValueType);
        body.AppendLineAt(4, "if (!desired.IsPresent)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "if (found" + id + ") editable" + id + ".Remove(dictKey" + id + ");");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else if (desired.Value is null)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "if (typeof("
                + checkValue
                + ").IsValueType && global::System.Nullable.GetUnderlyingType(typeof("
                + checkValue
                + ")) is null)"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "error = \"Member '" + name + "' does not accept null values.\";");
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "editable" + id + "[dictKey" + id + "] = default(" + checkValue + ")!;"
        );
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else if (desired.Value is " + checkValue + " entry" + id + ")");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "editable" + id + "[dictKey" + id + "] = entry" + id + ";");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "else");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"The resolution value for member '" + name + "' has an unsupported type.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "builder." + esc + " = " + optional + ".Present(editable" + id + ");");
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
    }

    private static void AppendDictStructuralEntry(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string name,
        string keyType,
        string valueValueType,
        string valueFragment,
        string valueChangeSet,
        int id,
        bool valueMayBeNull,
        string runtime
    )
    {
        _ = runtime;
        _ = keyType;
        var valueModel = valueFragment.EndsWith(".Fragment", System.StringComparison.Ordinal)
            ? valueFragment.Substring(0, valueFragment.Length - ".Fragment".Length)
            : valueValueType;
        body.AppendLineAt(4, "if (offset + 2 == path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "if (!desired.IsPresent)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "if (found" + id + ") editable" + id + ".Remove(dictKey" + id + ");");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else if (desired.Value is null)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "error = \"Null entries are not supported for member '" + name + "'.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "else if (desired.Value is " + valueValueType + " structEntry" + id + ")"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "editable" + id + "[dictKey" + id + "] = structEntry" + id + ";");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "else if (desired.Value is " + valueFragment + " structFragment" + id + ")"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "editable" + id + "[dictKey" + id + "] = structFragment" + id + ".ToModel();"
        );
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else if (desired.Value is " + valueModel + " structModel" + id + ")");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "editable" + id + "[dictKey" + id + "] = structModel" + id + ";");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "error = \"The resolution value for member '" + name + "' has an unsupported type.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "builder." + esc + " = " + optional + ".Present(editable" + id + ");");
        body.AppendLineAt(5, "updated = builder.Build();");
        body.AppendLineAt(5, "return true;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "if (!found" + id + ")");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"No entry with the key was found in member '" + name + "'.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        if (valueMayBeNull)
        {
            body.AppendLineAt(4, "if (editable" + id + "[dictKey" + id + "] is null)");
            body.AppendLineAt(4, "{");
            body.AppendLineAt(5, "error = \"The entry for member '" + name + "' is missing.\";");
            body.AppendLineAt(5, "return false;");
            body.AppendLineAt(4, "}");
        }
        body.AppendLineAt(
            4,
            "var valueFragment"
                + id
                + " = "
                + valueFragment
                + ".From(editable"
                + id
                + "[dictKey"
                + id
                + "]);"
        );
        body.AppendLineAt(
            4,
            "if (!"
                + valueChangeSet
                + ".__SparseTryApplyResolution(valueFragment"
                + id
                + ", path, offset + 2, desired, out var updatedValue"
                + id
                + ", out error)) return false;"
        );
        body.AppendLineAt(4, "if (updatedValue" + id + " is null)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "error = \"The entry resolution produced no fragment.\";");
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(
            4,
            "editable" + id + "[dictKey" + id + "] = updatedValue" + id + ".ToModel();"
        );
        body.AppendLineAt(4, "builder." + esc + " = " + optional + ".Present(editable" + id + ");");
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
    }
}
