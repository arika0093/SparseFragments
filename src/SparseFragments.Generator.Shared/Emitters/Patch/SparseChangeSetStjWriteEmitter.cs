using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits ChangeSet STJ write path.</summary>
internal static class SparseChangeSetStjWriteEmitter
{
    internal static void AppendSparseEndpointWrite(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLineAt(
            2,
            "internal static void __SparseWriteSparseEndpointStj(global::System.Text.Json.Utf8JsonWriter writer, Fragment endpoint, ChangeSet changes, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (changes.__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "((Fragment.FragmentJsonConverter)Fragment.JsonConverter).Write(writer, endpoint, options);"
        );
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var wire = SparseStjKeyHelpers.Lit(SparseStjKeyHelpers.ChangeSetWireName(member));
            var include = SparseStjKeyHelpers.ChangeSetIsNested(member)
                ? "changes.__sparse_nested_" + member.Id + " is not null"
                : "changes.__sparse_has_" + member.Id;
            code.AppendLineAt(3, "if ((" + include + ") && endpoint." + property + ".IsPresent)");
            code.AppendLineAt(3, "{");
            var indent = 4;
            if (member.Property.IsJsonIgnoreWhenWritingNull)
            {
                code.AppendLineAt(4, "if ((object?)endpoint." + property + ".Value is not null)");
                code.AppendLineAt(4, "{");
                indent = 5;
            }
            else if (member.Property.IsJsonIgnoreWhenWritingDefault)
            {
                var valueType = SparseStjKeyHelpers.ChangeSetValueType(member);
                code.AppendLineAt(
                    4,
                    "if (!global::System.Collections.Generic.EqualityComparer<"
                        + valueType
                        + ">.Default.Equals(endpoint."
                        + property
                        + ".Value!, default))"
                );
                code.AppendLineAt(4, "{");
                indent = 5;
            }
            code.AppendLineAt(
                indent,
                "writer.WritePropertyName("
                    + (
                        member.Property.HasExplicitJsonPropertyName
                            ? wire
                            : "options.PropertyNamingPolicy?.ConvertName(" + wire + ") ?? " + wire
                    )
                    + ");"
            );
            if (SparseStjKeyHelpers.ChangeSetIsNested(member))
            {
                var childCs = SparseStjKeyHelpers.ChangeSetChildChangeSet(member, dialect);
                var childFragment = member.ChildFragmentType!;
                code.AppendLineAt(indent, "if (endpoint." + property + ".Value is null)");
                code.AppendLineAt(indent, "{");
                code.AppendLineAt(indent + 1, "writer.WriteNullValue();");
                code.AppendLineAt(indent, "}");
                code.AppendLineAt(
                    indent,
                    "else if (changes.__sparse_nested_" + member.Id + " is null)"
                );
                code.AppendLineAt(indent, "{");
                code.AppendLineAt(
                    indent + 1,
                    childFragment
                        + ".JsonConverter.Write(writer, endpoint."
                        + property
                        + ".Value, options);"
                );
                code.AppendLineAt(indent, "}");
                code.AppendLineAt(indent, "else");
                code.AppendLineAt(indent, "{");
                code.AppendLineAt(
                    indent + 1,
                    childCs
                        + ".__SparseWriteSparseEndpointStj(writer, endpoint."
                        + property
                        + ".Value, changes.__sparse_nested_"
                        + member.Id
                        + "!, options);"
                );
                code.AppendLineAt(indent, "}");
            }
            else
            {
                var valueType = SparseStjKeyHelpers.ChangeSetValueType(member);
                code.AppendLineAt(
                    indent,
                    "global::System.Text.Json.JsonSerializer.Serialize<"
                        + valueType
                        + ">(writer, endpoint."
                        + property
                        + ".Value!, GetMemberTypeInfo<"
                        + valueType
                        + ">(options));"
                );
            }
            if (indent > 4)
            {
                code.AppendLineAt(4, "}");
            }
            code.AppendLineAt(3, "}");
        }
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendChangeSetWireHelpers(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLineAt(
            2,
            "private static bool __SparseMatches(string? actual, string propertyName, bool useNamingPolicy, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (actual is null) { return false; }");
        code.AppendLineAt(
            3,
            "var expected = useNamingPolicy ? options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName : propertyName;"
        );
        code.AppendLineAt(
            3,
            "return global::System.String.Equals(actual, expected, options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void __SparseValidateJsonNames(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var names = new global::System.Collections.Generic.HashSet<string>(options.PropertyNameCaseInsensitive ? global::System.StringComparer.OrdinalIgnoreCase : global::System.StringComparer.Ordinal);"
        );
        foreach (
            var property in members
                .Where(static m => !m.Property.IsJsonIgnored)
                .Select(static m => m.Property)
        )
        {
            var literal = SymbolDisplay.FormatLiteral(
                property.JsonPropertyName ?? property.Name,
                true
            );
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            code.AppendLineAt(
                3,
                "if (!names.Add("
                    + expression
                    + ")) { throw new global::System.Text.Json.JsonException(\"Multiple change-set members map to the same JSON property name.\"); }"
            );
        }
        code.AppendLineAt(2, "}");
    }

    internal static void AppendChangeSetWrite(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        // v1 document envelope: {"version":1,"changes":{...body...}}.
        // Body grammar lives in __SparseWriteBodyStj so nested ChangeSets can reuse it
        // without nested envelopes. version/changes are exact wire names (policy-independent).
        code.AppendLineAt(
            2,
            "internal static void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, ChangeSet value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "writer.WritePropertyName(\"version\");");
        code.AppendLineAt(3, "writer.WriteNumberValue(1);");
        code.AppendLineAt(3, "writer.WritePropertyName(\"changes\");");
        code.AppendLineAt(3, "__SparseWriteBodyStj(writer, value, options);");
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "internal static void __SparseWriteBodyStj(global::System.Text.Json.Utf8JsonWriter writer, ChangeSet value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "if (value.__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "writer.WritePropertyName(\"before\");");
        code.AppendLineAt(
            4,
            "__SparseWriteOptionalFragment(writer, value.__sparse_wholeBefore, options);"
        );
        code.AppendLineAt(4, "writer.WritePropertyName(\"after\");");
        code.AppendLineAt(
            4,
            "__SparseWriteOptionalFragment(writer, value.__sparse_wholeAfter, options);"
        );
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            var wire = SparseStjKeyHelpers.ChangeSetWireName(member);
            var lit = SparseStjKeyHelpers.Lit(wire);
            var explicitName = member.Property.HasExplicitJsonPropertyName;
            var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
            _ = esc;
            if (SparseStjKeyHelpers.ChangeSetIsNested(member))
            {
                var child = SparseStjKeyHelpers.ChangeSetChildChangeSet(member, dialect);
                code.AppendLineAt(3, "if (value.__sparse_nested_" + member.Id + " is not null)");
                code.AppendLineAt(3, "{");
                if (explicitName)
                    code.AppendLineAt(4, "writer.WritePropertyName(" + lit + ");");
                else
                    code.AppendLineAt(
                        4,
                        "writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName("
                            + lit
                            + ") ?? "
                            + lit
                            + ");"
                    );
                code.AppendLineAt(
                    4,
                    child
                        + ".__SparseWriteBodyStj(writer, value.__sparse_nested_"
                        + member.Id
                        + ", options);"
                );
                code.AppendLineAt(3, "}");
            }
            else if (
                SparseStjKeyHelpers.IsDict(member)
                || SparseKeyedCollectionEmitter.IsKeyedSequence(member)
            )
            {
                AppendChangeSetSparseMemberWrite(code, member, lit, explicitName, dialect);
            }
            else
            {
                code.AppendLineAt(3, "if (value.__sparse_has_" + member.Id + ")");
                code.AppendLineAt(3, "{");
                if (explicitName)
                    code.AppendLineAt(4, "writer.WritePropertyName(" + lit + ");");
                else
                    code.AppendLineAt(
                        4,
                        "writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName("
                            + lit
                            + ") ?? "
                            + lit
                            + ");"
                    );
                code.AppendLineAt(4, "writer.WriteStartObject();");
                code.AppendLineAt(4, "writer.WritePropertyName(\"before\");");
                code.AppendLineAt(
                    4,
                    "__SparseWriteOpt_"
                        + member.Id
                        + "(writer, value.__sparse_before_"
                        + member.Id
                        + ", options);"
                );
                code.AppendLineAt(4, "writer.WritePropertyName(\"after\");");
                code.AppendLineAt(
                    4,
                    "__SparseWriteOpt_"
                        + member.Id
                        + "(writer, value.__sparse_after_"
                        + member.Id
                        + ", options);"
                );
                code.AppendLineAt(4, "writer.WriteEndObject();");
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits sparse keyed/dictionary member ChangeSet JSON.</summary>
    /// <remarks>
    /// Canonical transition only: whole presence transitions write full before/after
    /// optionals; granular transitions write per-changed-key items (key, kind,
    /// endpoint values, indexes, reorder flag) plus key-only orders when changed.
    /// Nested element edits are reconstructed via canonical Between, never wired
    /// separately. Unchanged element values are never serialized.
    /// </remarks>
    internal static void AppendChangeSetSparseMemberWrite(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        bool explicitName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = dialect;
        var id = member.Id;
        var isKeyed = SparseKeyedCollectionEmitter.IsKeyedSequence(member);
        var elementType = isKeyed
            ? SparseStjKeyHelpers.ElementTypeOf(member)
            : SparseStjKeyHelpers.ValueTypeOf(member);
        var hasFragmentElement = isKeyed || SparseStjKeyHelpers.HasValuePatch(member);
        string? elementChangeSet = null;
        string? elementFragment = null;
        if (isKeyed)
        {
            elementChangeSet = SparseStjKeyHelpers.SparseElementChangeSet(member);
            elementFragment = SparseStjKeyHelpers.SparseElementFragment(member);
        }
        else if (hasFragmentElement)
        {
            elementChangeSet = SparseStjKeyHelpers.SparseValueChangeSet(member);
            elementFragment = SparseStjKeyHelpers.SparseValueFragment(member);
        }
        code.AppendLineAt(3, "if (value.__sparse_has_" + id + ")");
        code.AppendLineAt(3, "{");
        if (explicitName)
            code.AppendLineAt(4, "writer.WritePropertyName(" + lit + ");");
        else
            code.AppendLineAt(
                4,
                "writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName("
                    + lit
                    + ") ?? "
                    + lit
                    + ");"
            );
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "if (value.__sparse_kwhole_" + id + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "writer.WritePropertyName(\"before\");");
        code.AppendLineAt(
            4,
            "__SparseWriteOpt_" + id + "(writer, value.__sparse_kwholeBefore_" + id + ", options);"
        );
        code.AppendLineAt(4, "writer.WritePropertyName(\"after\");");
        code.AppendLineAt(
            4,
            "__SparseWriteOpt_" + id + "(writer, value.__sparse_kwholeAfter_" + id + ", options);"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "writer.WritePropertyName(\"items\");");
        code.AppendLineAt(4, "writer.WriteStartArray();");
        code.AppendLineAt(
            4,
            "if (value.__sparse_kitems_"
                + id
                + " is not null) foreach (var __it in value.__sparse_kitems_"
                + id
                + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WriteStartObject();");
        code.AppendLineAt(5, "writer.WritePropertyName(\"key\");");
        code.AppendLineAt(5, "__SparseWriteKey_" + id + "(writer, __it.Key, options);");
        code.AppendLineAt(5, "writer.WritePropertyName(\"kind\");");
        // Short kind tags avoid colliding (even case-insensitively) with the typed
        // projection names (Added/Removed/Edited) which must stay out of the wire.
        code.AppendLineAt(
            5,
            "writer.WriteStringValue(__it.IsAdded ? \"add\" : __it.IsRemoved ? \"remove\" : __it.IsEdited ? \"edit\" : \"reorder\");"
        );
        code.AppendLineAt(5, "if (__it.Before.IsPresent)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WritePropertyName(\"before\");");
        if (hasFragmentElement)
        {
            code.AppendLineAt(6, "if (__it.IsEdited)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                elementChangeSet
                    + ".__SparseWriteSparseEndpointStj(writer, "
                    + elementFragment
                    + ".From(__it.Before.Value!), __it.Edit, options);"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + elementType
                    + ">(writer, __it.Before.Value!, GetMemberTypeInfo<"
                    + elementType
                    + ">(options));"
            );
            code.AppendLineAt(6, "}");
        }
        else
        {
            code.AppendLineAt(
                6,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + elementType
                    + ">(writer, __it.Before.Value!, GetMemberTypeInfo<"
                    + elementType
                    + ">(options));"
            );
        }
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__it.After.IsPresent)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WritePropertyName(\"after\");");
        if (hasFragmentElement)
        {
            code.AppendLineAt(6, "if (__it.IsEdited)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                elementChangeSet
                    + ".__SparseWriteSparseEndpointStj(writer, "
                    + elementFragment
                    + ".From(__it.After.Value!), __it.Edit, options);"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + elementType
                    + ">(writer, __it.After.Value!, GetMemberTypeInfo<"
                    + elementType
                    + ">(options));"
            );
            code.AppendLineAt(6, "}");
        }
        else
        {
            code.AppendLineAt(
                6,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + elementType
                    + ">(writer, __it.After.Value!, GetMemberTypeInfo<"
                    + elementType
                    + ">(options));"
            );
        }
        code.AppendLineAt(5, "}");
        if (isKeyed)
        {
            code.AppendLineAt(5, "writer.WritePropertyName(\"beforeIndex\");");
            code.AppendLineAt(5, "writer.WriteNumberValue(__it.BeforeIndex);");
            code.AppendLineAt(5, "writer.WritePropertyName(\"afterIndex\");");
            code.AppendLineAt(5, "writer.WriteNumberValue(__it.AfterIndex);");
            code.AppendLineAt(5, "if (__it.IsEdited && __it.IsReordered)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "writer.WritePropertyName(\"reordered\");");
            code.AppendLineAt(6, "writer.WriteBooleanValue(true);");
            code.AppendLineAt(5, "}");
        }
        code.AppendLineAt(5, "writer.WriteEndObject();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "writer.WriteEndArray();");
        if (isKeyed)
        {
            code.AppendLineAt(4, "if (value.__sparse_kbeforeOrder_" + id + " is not null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "writer.WritePropertyName(\"beforeOrder\");");
            code.AppendLineAt(5, "writer.WriteStartArray();");
            code.AppendLineAt(
                5,
                "foreach (var __k in value.__sparse_kbeforeOrder_"
                    + id
                    + ") __SparseWriteKey_"
                    + id
                    + "(writer, __k, options);"
            );
            code.AppendLineAt(5, "writer.WriteEndArray();");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "if (value.__sparse_kafterOrder_" + id + " is not null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "writer.WritePropertyName(\"afterOrder\");");
            code.AppendLineAt(5, "writer.WriteStartArray();");
            code.AppendLineAt(
                5,
                "foreach (var __k in value.__sparse_kafterOrder_"
                    + id
                    + ") __SparseWriteKey_"
                    + id
                    + "(writer, __k, options);"
            );
            code.AppendLineAt(5, "writer.WriteEndArray();");
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
    }
}
