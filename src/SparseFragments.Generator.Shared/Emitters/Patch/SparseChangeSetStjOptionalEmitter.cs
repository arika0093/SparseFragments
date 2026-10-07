using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits ChangeSet STJ optional helpers.</summary>
internal static class SparseChangeSetStjOptionalEmitter
{
    internal static void AppendChangeSetOptionalHelpers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
        var vt = SparseStjKeyHelpers.ChangeSetValueType(member);
        var allowsNull =
            member.Property.Type.IsReferenceType || vt.EndsWith("?", StringComparison.Ordinal);
        code.AppendLineAt(
            2,
            "private static void __SparseWriteOpt_"
                + member.Id
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + runtime
                + "Optional<"
                + vt
                + "> optional, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "if (!optional.IsPresent)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteString(\"state\", \"missing\");");
        code.AppendLineAt(3, "}");
        if (allowsNull)
        {
            code.AppendLineAt(3, "else if ((object?)optional.Value is null)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "writer.WriteString(\"state\", \"null\");");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "else");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "writer.WriteString(\"state\", \"value\");");
            code.AppendLineAt(4, "writer.WritePropertyName(\"value\");");
            code.AppendLineAt(
                4,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + vt
                    + ">(writer, optional.Value!, GetMemberTypeInfo<"
                    + vt
                    + ">(options));"
            );
            code.AppendLineAt(3, "}");
        }
        else
        {
            code.AppendLineAt(3, "else");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "writer.WriteString(\"state\", \"value\");");
            code.AppendLineAt(4, "writer.WritePropertyName(\"value\");");
            code.AppendLineAt(
                4,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + vt
                    + ">(writer, optional.Value!, GetMemberTypeInfo<"
                    + vt
                    + ">(options));"
            );
            code.AppendLineAt(3, "}");
        }
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<"
                + vt
                + "> __SparseReadOpt_"
                + member.Id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A member value must be a JSON object.\");"
        );
        code.AppendLineAt(3, "byte __state = 0;");
        code.AppendLineAt(3, "string? __unknownState = null;");
        code.AppendLineAt(3, "bool __hasValue = false;");
        code.AppendLineAt(3, vt + " __v = default!;");
        code.AppendLineAt(3, "bool __valueWasNull = false;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a member-value property name.\");"
        );
        code.AppendLineAt(4, "var __isState = reader.ValueTextEquals(\"state\");");
        code.AppendLineAt(4, "var __isValue = !__isState && reader.ValueTextEquals(\"value\");");
        code.AppendLineAt(4, "var __unknown = __isState || __isValue ? null : reader.GetString();");
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of member value.\");"
        );
        code.AppendLineAt(4, "if (__isState)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__state != 0) throw new global::System.Text.Json.JsonException(\"Duplicate member state.\");"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Member state must be a string.\");"
        );
        code.AppendLineAt(
            5,
            "__state = reader.ValueTextEquals(\"missing\") ? (byte)1 : reader.ValueTextEquals(\"null\") ? (byte)2 : reader.ValueTextEquals(\"value\") ? (byte)3 : (byte)4;"
        );
        code.AppendLineAt(5, "if (__state == 4) __unknownState = reader.GetString();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__isValue)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Duplicate member value.\");"
        );
        code.AppendLineAt(5, "__hasValue = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { __v = default!; __valueWasNull = true; }"
        );
        code.AppendLineAt(
            5,
            "else { __v = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + vt
                + ">(options))!; __valueWasNull = false; }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown member property '\" + __unknown + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (__state == 0) throw new global::System.Text.Json.JsonException(\"Missing member state.\");"
        );
        code.AppendLineAt(3, "if (__state == 1)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Missing state must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "Optional<" + vt + ">.Missing;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__state == 2)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Null state must not have a value.\");"
        );
        if (!allowsNull)
        {
            code.AppendLineAt(
                4,
                "throw new global::System.Text.Json.JsonException(\"Null state is not valid for this member type.\");"
            );
        }
        else
        {
            code.AppendLineAt(4, "return " + runtime + "Optional<" + vt + ">.Present(default!);");
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__state == 3)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing member value.\");"
        );
        code.AppendLineAt(
            4,
            "if (__valueWasNull) throw new global::System.Text.Json.JsonException(\"Value state must have a non-null value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "Optional<" + vt + ">.Present(__v!);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "throw new global::System.Text.Json.JsonException(\"Unknown member state '\" + __unknownState + \"'.\");"
        );
        code.AppendLineAt(2, "}");
    }

    internal static void AppendChangeSetOptionalFragment(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
        code.AppendLineAt(
            2,
            "private static void __SparseWriteOptionalFragment(global::System.Text.Json.Utf8JsonWriter writer, "
                + runtime
                + "Optional<Fragment?> optional, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "if (!optional.IsPresent)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteString(\"state\", \"missing\");");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else if (optional.Value is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteString(\"state\", \"null\");");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteString(\"state\", \"value\");");
        code.AppendLineAt(4, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(
            4,
            "((Fragment.FragmentJsonConverter)Fragment.JsonConverter).Write(writer, optional.Value, options);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<Fragment?> __SparseReadOptionalFragment(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An optional fragment must be a JSON object.\");"
        );
        code.AppendLineAt(3, "byte __state = 0;");
        code.AppendLineAt(3, "string? __unknownState = null;");
        code.AppendLineAt(3, "bool __hasValue = false;");
        code.AppendLineAt(3, "Fragment? __frag = null;");
        code.AppendLineAt(3, "bool __valueWasNull = false;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected an optional-fragment property name.\");"
        );
        code.AppendLineAt(4, "var __isState = reader.ValueTextEquals(\"state\");");
        code.AppendLineAt(4, "var __isValue = !__isState && reader.ValueTextEquals(\"value\");");
        code.AppendLineAt(4, "var __unknown = __isState || __isValue ? null : reader.GetString();");
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of optional fragment.\");"
        );
        code.AppendLineAt(4, "if (__isState)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__state != 0) throw new global::System.Text.Json.JsonException(\"Duplicate optional state.\");"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Optional state must be a string.\");"
        );
        code.AppendLineAt(
            5,
            "__state = reader.ValueTextEquals(\"missing\") ? (byte)1 : reader.ValueTextEquals(\"null\") ? (byte)2 : reader.ValueTextEquals(\"value\") ? (byte)3 : (byte)4;"
        );
        code.AppendLineAt(5, "if (__state == 4) __unknownState = reader.GetString();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__isValue)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Duplicate optional value.\");"
        );
        code.AppendLineAt(5, "__hasValue = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { __frag = null; __valueWasNull = true; }"
        );
        code.AppendLineAt(
            5,
            "else if (reader.TokenType == global::System.Text.Json.JsonTokenType.StartObject) { var __r = ((Fragment.FragmentJsonConverter)Fragment.JsonConverter).Read(ref reader, typeof(Fragment), options); __frag = __r; __valueWasNull = false; }"
        );
        code.AppendLineAt(
            5,
            "else throw new global::System.Text.Json.JsonException(\"Optional value must be an object or null.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown optional property '\" + __unknown + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (__state == 0) throw new global::System.Text.Json.JsonException(\"Missing optional state.\");"
        );
        code.AppendLineAt(3, "if (__state == 1)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Missing state must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "Optional<Fragment?>.Missing;");
        code.AppendLine();

        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__state == 2)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Null state must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "Optional<Fragment?>.Present(null);");
        code.AppendLine();

        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__state == 3)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing optional value.\");"
        );
        code.AppendLineAt(
            4,
            "if (__valueWasNull) throw new global::System.Text.Json.JsonException(\"Value state must have an object value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "Optional<Fragment?>.Present(__frag);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "throw new global::System.Text.Json.JsonException(\"Unknown optional state '\" + __unknownState + \"'.\");"
        );
        code.AppendLineAt(2, "}");
    }
}
