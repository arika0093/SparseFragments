using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits dictionary STJ support.</summary>
internal static class SparseDictionaryStjEmitter
{
    internal static void AppendDictionaryStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
        var keyType = SparseStjKeyHelpers.KeyTypeOf(member);
        var valueType = SparseStjKeyHelpers.ValueTypeOf(member);
        var dictType = SparseStjKeyHelpers.DictTypeOf(member);
        var hasPatch = SparseStjKeyHelpers.HasValuePatch(member);
        var valuePatch = hasPatch ? SparseStjKeyHelpers.ValuePatchType(member) : valueType;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        SparseStjKeyHelpers.AppendTypeInfoHelper(code, 3);
        SparseStjKeyHelpers.AppendKeyHelpers(code, member, keyType);
        code.AppendLine();
        code.AppendLineAt(
            3,
            "internal void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "if (__whole.Kind != " + runtime + "FragmentOperationKind.Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(5, "writer.WriteStartObject();");
        code.AppendLineAt(5, "if (__whole.Kind == " + runtime + "FragmentOperationKind.Remove)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WriteString(\"kind\", \"remove\");");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WriteString(\"kind\", \"set\");");
        code.AppendLineAt(6, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(
            6,
            "global::System.Text.Json.JsonSerializer.Serialize<"
                + dictType
                + ">(writer, __whole.Value!, GetMemberTypeInfo<"
                + dictType
                + ">(options));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "writer.WriteEndObject();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__set is not null && __set.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"set\");");
        code.AppendLineAt(5, "writer.WriteStartArray();");
        code.AppendLineAt(5, "foreach (var __kv in __set)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WriteStartObject();");
        code.AppendLineAt(6, "writer.WritePropertyName(\"key\");");
        code.AppendLineAt(6, "__SparseWriteKey_" + member.Id + "(writer, __kv.Key, options);");
        code.AppendLineAt(6, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(
            6,
            "global::System.Text.Json.JsonSerializer.Serialize<"
                + valueType
                + ">(writer, __kv.Value, GetMemberTypeInfo<"
                + valueType
                + ">(options));"
        );
        code.AppendLineAt(6, "writer.WriteEndObject();");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "writer.WriteEndArray();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__removed is not null && __removed.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"removed\");");
        code.AppendLineAt(5, "writer.WriteStartArray();");
        code.AppendLineAt(5, "foreach (var __k in __removed)");
        code.AppendLineAt(6, "__SparseWriteKey_" + member.Id + "(writer, __k, options);");
        code.AppendLineAt(5, "writer.WriteEndArray();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__edited is not null && __edited.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"edited\");");
        code.AppendLineAt(5, "writer.WriteStartArray();");
        code.AppendLineAt(5, "foreach (var __kv in __edited)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WriteStartObject();");
        code.AppendLineAt(6, "writer.WritePropertyName(\"key\");");
        code.AppendLineAt(6, "__SparseWriteKey_" + member.Id + "(writer, __kv.Key, options);");
        if (hasPatch)
        {
            code.AppendLineAt(6, "writer.WritePropertyName(\"patch\");");
            code.AppendLineAt(6, valuePatch + ".__SparseWriteStj(writer, __kv.Value, options);");
        }
        else
        {
            code.AppendLineAt(6, "writer.WritePropertyName(\"value\");");
            code.AppendLineAt(
                6,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + valueType
                    + ">(writer, __kv.Value, GetMemberTypeInfo<"
                    + valueType
                    + ">(options));"
            );
        }
        code.AppendLineAt(6, "writer.WriteEndObject();");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "writer.WriteEndArray();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
        code.AppendLine();
        var patchName = SparseStjKeyHelpers.CollectionPatchType(member, dialect);
        code.AppendLineAt(
            3,
            "internal static "
                + patchName
                + " __SparseReadStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A dictionary patch must be a JSON object.\");"
        );
        code.AppendLineAt(4, "bool __hasWhole = false;");
        code.AppendLineAt(4, runtime + "FragmentOperation<" + dictType + "> __whole = default;");
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">? __set = null;"
        );
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.List<" + keyType + ">? __removed = null;"
        );
        if (hasPatch)
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">? __edited = null;"
            );
        else
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">? __edited = null;"
            );
        code.AppendLineAt(
            4,
            "bool __seenWhole = false; bool __seenSet = false; bool __seenRemoved = false; bool __seenEdited = false;"
        );
        code.AppendLineAt(4, "while (reader.Read())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a dictionary-patch property name.\");"
        );
        code.AppendLineAt(5, "var __p = reader.GetString();");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of dictionary patch.\");"
        );
        code.AppendLineAt(5, "if (__p == \"$whole\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Duplicate dictionary property '$whole'.\");"
        );
        code.AppendLineAt(6, "__seenWhole = true;");
        code.AppendLineAt(
            6,
            "__whole = __SparseReadWhole_" + member.Id + "(ref reader, options); __hasWhole = true;"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__p == \"set\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenSet) throw new global::System.Text.Json.JsonException(\"Duplicate dictionary property 'set'.\");"
        );
        code.AppendLineAt(6, "__seenSet = true;");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Set must be an array.\");"
        );
        code.AppendLineAt(
            6,
            "__set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(
            7,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A set entry must be an object.\");"
        );
        code.AppendLineAt(
            7,
            "bool __hk = false; bool __hv = false; "
                + keyType
                + " __k = default!; "
                + valueType
                + " __v = default!;"
        );
        code.AppendLineAt(7, "while (reader.Read())");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            8,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a set-entry property name.\");"
        );
        code.AppendLineAt(8, "var __e = reader.GetString();");
        code.AppendLineAt(
            8,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of set entry.\");"
        );
        code.AppendLineAt(8, "if (__e == \"key\")");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (__hk) throw new global::System.Text.Json.JsonException(\"Duplicate set key.\");"
        );
        code.AppendLineAt(9, "__hk = true;");
        code.AppendLineAt(9, "__k = __SparseReadKey_" + member.Id + "(ref reader, options);");
        code.AppendLineAt(8, "}");
        code.AppendLineAt(8, "else if (__e == \"value\")");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (__hv) throw new global::System.Text.Json.JsonException(\"Duplicate set value.\");"
        );
        code.AppendLineAt(9, "__hv = true;");
        code.AppendLineAt(
            9,
            "__v = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + valueType
                + ">(options))!;"
        );
        code.AppendLineAt(8, "}");
        code.AppendLineAt(
            8,
            "else throw new global::System.Text.Json.JsonException(\"Unknown set property '\" + __e + \"'.\");"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "if (!__hk) throw new global::System.Text.Json.JsonException(\"Missing set key.\");"
        );
        code.AppendLineAt(
            7,
            "if (!__hv) throw new global::System.Text.Json.JsonException(\"Missing set value.\");"
        );
        code.AppendLineAt(
            7,
            "if (__set.ContainsKey(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate set key value.\");"
        );
        code.AppendLineAt(7, "__set[__k] = __v;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__p == \"removed\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenRemoved) throw new global::System.Text.Json.JsonException(\"Duplicate dictionary property 'removed'.\");"
        );
        code.AppendLineAt(6, "__seenRemoved = true;");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Removed must be an array.\");"
        );
        code.AppendLineAt(
            6,
            "__removed = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(7, "var __k = __SparseReadKey_" + member.Id + "(ref reader, options);");
        code.AppendLineAt(7, "__removed.Add(__k);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__p == \"edited\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenEdited) throw new global::System.Text.Json.JsonException(\"Duplicate dictionary property 'edited'.\");"
        );
        code.AppendLineAt(6, "__seenEdited = true;");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Edited must be an array.\");"
        );
        code.AppendLineAt(
            6,
            "__edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valuePatch
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(
            7,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An edited entry must be an object.\");"
        );
        code.AppendLineAt(
            7,
            "bool __hasKey = false; bool __hasPayload = false; " + keyType + " __ek = default!;"
        );
        if (hasPatch)
            code.AppendLineAt(7, valuePatch + "? __ep = null;");
        else
            code.AppendLineAt(7, valueType + " __ev = default!; bool __evSet = false;");
        code.AppendLineAt(7, "while (reader.Read())");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            8,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected an edited-entry property name.\");"
        );
        code.AppendLineAt(8, "var __ep2 = reader.GetString();");
        code.AppendLineAt(
            8,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of edited entry.\");"
        );
        code.AppendLineAt(8, "if (__ep2 == \"key\")");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (__hasKey) throw new global::System.Text.Json.JsonException(\"Duplicate edited key.\");"
        );
        code.AppendLineAt(9, "__hasKey = true;");
        code.AppendLineAt(9, "__ek = __SparseReadKey_" + member.Id + "(ref reader, options);");
        code.AppendLineAt(8, "}");
        if (hasPatch)
        {
            code.AppendLineAt(8, "else if (__ep2 == \"patch\")");
            code.AppendLineAt(8, "{");
            code.AppendLineAt(
                9,
                "if (__hasPayload) throw new global::System.Text.Json.JsonException(\"Duplicate edited patch.\");"
            );
            code.AppendLineAt(9, "__hasPayload = true;");
            code.AppendLineAt(
                9,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An entry patch must be an object.\");"
            );
            code.AppendLineAt(9, "__ep = " + valuePatch + ".__SparseReadStj(ref reader, options);");
            code.AppendLineAt(8, "}");
        }
        else
        {
            code.AppendLineAt(8, "else if (__ep2 == \"value\")");
            code.AppendLineAt(8, "{");
            code.AppendLineAt(
                9,
                "if (__hasPayload) throw new global::System.Text.Json.JsonException(\"Duplicate edited value.\");"
            );
            code.AppendLineAt(9, "__hasPayload = true;");
            code.AppendLineAt(
                9,
                "__ev = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                    + valueType
                    + ">(options))!; __evSet = true;"
            );
            code.AppendLineAt(8, "}");
        }
        code.AppendLineAt(
            8,
            "else throw new global::System.Text.Json.JsonException(\"Unknown edited property '\" + __ep2 + \"'.\");"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "if (!__hasKey) throw new global::System.Text.Json.JsonException(\"Missing edited key.\");"
        );
        code.AppendLineAt(
            7,
            "if (!__hasPayload) throw new global::System.Text.Json.JsonException(\"Missing edited payload.\");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                7,
                "if (__ep is null || __ep.__SparseIsEmpty()) throw new global::System.Text.Json.JsonException(\"Edited patch must not be empty.\");"
            );
            code.AppendLineAt(
                7,
                "if (__edited.ContainsKey(__ek)) throw new global::System.Text.Json.JsonException(\"Duplicate edited key value.\");"
            );
            code.AppendLineAt(7, "__edited[__ek] = __ep;");
        }
        else
        {
            code.AppendLineAt(
                7,
                "if (!__evSet) throw new global::System.Text.Json.JsonException(\"Missing edited value.\");"
            );
            code.AppendLineAt(
                7,
                "if (__edited.ContainsKey(__ek)) throw new global::System.Text.Json.JsonException(\"Duplicate edited key value.\");"
            );
            code.AppendLineAt(7, "__edited[__ek] = __ev;");
        }
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "else throw new global::System.Text.Json.JsonException(\"Unknown dictionary property '\" + __p + \"'.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(4, "if (__hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ((__set is not null && __set.Count != 0) || (__removed is not null && __removed.Count != 0) || (__edited is not null && __edited.Count != 0)) throw new global::System.Text.Json.JsonException(\"Whole and granular dictionary operations cannot coexist.\");"
        );
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + runtime
                + "FragmentOperationKind.Remove) { result.Remove(); return result; }"
        );
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + runtime
                + "FragmentOperationKind.Set) { result.Set(__whole.Value!); return result; }"
        );
        code.AppendLineAt(
            5,
            "throw new global::System.Text.Json.JsonException(\"Invalid whole kind.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __seen = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "if (__set is not null) foreach (var __k in __set.Keys) if (!__seen.Add(__k)) throw new global::System.Text.Json.JsonException(\"Overlapping dictionary operations.\");"
        );
        code.AppendLineAt(
            5,
            "var __rset = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + "); if (__removed is not null) foreach (var __k in __removed) { if (!__rset.Add(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate removed key.\"); if (!__seen.Add(__k)) throw new global::System.Text.Json.JsonException(\"Overlapping dictionary operations.\"); }"
        );
        code.AppendLineAt(
            5,
            "if (__edited is not null) foreach (var __k in __edited.Keys) if (!__seen.Add(__k)) throw new global::System.Text.Json.JsonException(\"Overlapping dictionary operations.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__set is not null && __set.Count != 0) result.__set = __set;");
        code.AppendLineAt(
            4,
            "if (__removed is not null && __removed.Count != 0) result.__removed = __removed;"
        );
        code.AppendLineAt(
            4,
            "if (__edited is not null && __edited.Count != 0) result.__edited = __edited;"
        );
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private static "
                + runtime
                + "FragmentOperation<"
                + dictType
                + "> __SparseReadWhole_"
                + member.Id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A whole operation must be a JSON object.\");"
        );
        code.AppendLineAt(
            4,
            "string? __kind = null; bool __hasValue = false; " + dictType + " __v = default!;"
        );
        code.AppendLineAt(4, "while (reader.Read())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a whole-operation property name.\");"
        );
        code.AppendLineAt(5, "var __p = reader.GetString();");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of whole operation.\");"
        );
        code.AppendLineAt(5, "if (__p == \"kind\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__kind is not null) throw new global::System.Text.Json.JsonException(\"Duplicate whole kind.\");"
        );
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Whole kind must be a string.\");"
        );
        code.AppendLineAt(6, "__kind = reader.GetString();");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__p == \"value\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Duplicate whole value.\");"
        );
        code.AppendLineAt(6, "__hasValue = true;");
        code.AppendLineAt(
            6,
            "__v = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + dictType
                + ">(options))!;"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "else throw new global::System.Text.Json.JsonException(\"Unknown whole property '\" + __p + \"'.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (__kind is null) throw new global::System.Text.Json.JsonException(\"Missing whole kind.\");"
        );
        code.AppendLineAt(4, "if (__kind == \"remove\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Remove whole must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + dictType + ">.Remove;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__kind == \"set\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing whole value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + dictType + ">.Set(__v);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.Text.Json.JsonException(\"Unknown whole kind '\" + __kind + \"'.\");"
        );
        code.AppendLineAt(3, "}");
    }
}
