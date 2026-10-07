using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits keyed-sequence STJ support.</summary>
internal static class SparseKeyedStjEmitter
{
    internal static void AppendKeyedStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
        var keyType = SparseStjKeyHelpers.KeyTypeOf(member);
        var elementType = SparseStjKeyHelpers.ElementTypeOf(member);
        var listType = SparseStjKeyHelpers.ListTypeOf(member);
        var hasPatch = SparseStjKeyHelpers.HasElementPatch(member);
        var elementPatch = hasPatch ? SparseStjKeyHelpers.ElementPatchType(member) : elementType;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        SparseStjKeyHelpers.AppendTypeInfoHelper(code, 3);
        SparseStjKeyHelpers.AppendKeyHelpers(code, member, keyType);
        code.AppendLine();
        // Write.
        code.AppendLineAt(
            3,
            "internal void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "if (__whole.Kind != " + runtime + "FragmentOperationKind.Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(5, "writer.WriteStartObject();");
        code.AppendLineAt(5, "if (__whole.Kind == " + runtime + "FragmentOperationKind.Unset)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WriteString(\"kind\", \"unset\");");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WriteString(\"kind\", \"set\");");
        code.AppendLineAt(6, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(
            6,
            "global::System.Text.Json.JsonSerializer.Serialize<"
                + listType
                + ">(writer, __whole.Value!, GetMemberTypeInfo<"
                + listType
                + ">(options));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "writer.WriteEndObject();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__added is not null && __added.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"added\");");
        code.AppendLineAt(5, "writer.WriteStartArray();");
        code.AppendLineAt(5, "foreach (var __e in __added)");
        code.AppendLineAt(
            6,
            "global::System.Text.Json.JsonSerializer.Serialize<"
                + elementType
                + ">(writer, __e, GetMemberTypeInfo<"
                + elementType
                + ">(options));"
        );
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
            code.AppendLineAt(6, elementPatch + ".__SparseWriteStj(writer, __kv.Value, options);");
        }
        else
        {
            code.AppendLineAt(6, "writer.WritePropertyName(\"value\");");
            code.AppendLineAt(
                6,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + elementType
                    + ">(writer, __kv.Value, GetMemberTypeInfo<"
                    + elementType
                    + ">(options));"
            );
        }
        code.AppendLineAt(6, "writer.WriteEndObject();");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "writer.WriteEndArray();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__order is not null && __order.Count != 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"order\");");
        code.AppendLineAt(5, "writer.WriteStartArray();");
        code.AppendLineAt(5, "foreach (var __k in __order)");
        code.AppendLineAt(6, "__SparseWriteKey_" + member.Id + "(writer, __k, options);");
        code.AppendLineAt(5, "writer.WriteEndArray();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
        code.AppendLine();
        // Read.
        var patchName = SparseStjKeyHelpers.CollectionPatchType(member);
        code.AppendLineAt(
            3,
            "internal static "
                + patchName
                + " __SparseReadStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A keyed patch must be a JSON object.\");"
        );
        code.AppendLineAt(4, "bool __hasWhole = false;");
        code.AppendLineAt(4, runtime + "FragmentOperation<" + listType + "> __whole = default;");
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.List<" + elementType + ">? __added = null;"
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
                    + elementPatch
                    + ">? __edited = null;"
            );
        else
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">? __edited = null;"
            );
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.List<" + keyType + ">? __order = null;"
        );
        code.AppendLineAt(
            4,
            "bool __seenWhole = false; bool __seenAdded = false; bool __seenRemoved = false; bool __seenEdited = false; bool __seenOrder = false;"
        );
        code.AppendLineAt(4, "while (reader.Read())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a keyed-patch property name.\");"
        );
        code.AppendLineAt(5, "var __p = reader.GetString();");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of keyed patch.\");"
        );
        // $whole
        code.AppendLineAt(5, "if (__p == \"$whole\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Duplicate keyed property '$whole'.\");"
        );
        code.AppendLineAt(6, "__seenWhole = true;");
        code.AppendLineAt(
            6,
            "__whole = __SparseReadWhole_" + member.Id + "(ref reader, options); __hasWhole = true;"
        );
        code.AppendLineAt(5, "}");
        // added
        code.AppendLineAt(5, "else if (__p == \"added\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenAdded) throw new global::System.Text.Json.JsonException(\"Duplicate keyed property 'added'.\");"
        );
        code.AppendLineAt(6, "__seenAdded = true;");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Added must be an array.\");"
        );
        code.AppendLineAt(
            6,
            "__added = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(
            7,
            "var __e = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + elementType
                + ">(options));"
        );
        code.AppendLineAt(
            7,
            "if ((object?)__e is null) throw new global::System.Text.Json.JsonException(\"Added element must not be null.\");"
        );
        code.AppendLineAt(7, "__added.Add(__e!);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        // removed
        code.AppendLineAt(5, "else if (__p == \"removed\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenRemoved) throw new global::System.Text.Json.JsonException(\"Duplicate keyed property 'removed'.\");"
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
        // edited
        code.AppendLineAt(5, "else if (__p == \"edited\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenEdited) throw new global::System.Text.Json.JsonException(\"Duplicate keyed property 'edited'.\");"
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
                + elementPatch
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
        code.AppendLineAt(7, "bool __hasKey = false; bool __hasPayload = false;");
        code.AppendLineAt(7, keyType + " __ek = default!;");
        if (hasPatch)
            code.AppendLineAt(7, elementPatch + "? __ep = null;");
        else
            code.AppendLineAt(7, elementType + " __ev = default!; bool __evSet = false;");
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
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An element patch must be an object.\");"
            );
            code.AppendLineAt(
                9,
                "__ep = " + elementPatch + ".__SparseReadStj(ref reader, options);"
            );
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
                    + elementType
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
        // order
        code.AppendLineAt(5, "else if (__p == \"order\")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__seenOrder) throw new global::System.Text.Json.JsonException(\"Duplicate keyed property 'order'.\");"
        );
        code.AppendLineAt(6, "__seenOrder = true;");
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Order must be an array.\");"
        );
        code.AppendLineAt(
            6,
            "__order = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(6, "while (reader.Read())");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(7, "var __k = __SparseReadKey_" + member.Id + "(ref reader, options);");
        code.AppendLineAt(7, "__order.Add(__k);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "else throw new global::System.Text.Json.JsonException(\"Unknown keyed property '\" + __p + \"'.\");"
        );
        code.AppendLineAt(4, "}");
        // Build result with validation.
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(4, "if (__hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ((__added is not null && __added.Count != 0) || (__removed is not null && __removed.Count != 0) || (__edited is not null && __edited.Count != 0) || (__order is not null && __order.Count != 0)) throw new global::System.Text.Json.JsonException(\"Whole and granular keyed operations cannot coexist.\");"
        );
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + runtime
                + "FragmentOperationKind.Unset) { result.Unset(); return result; }"
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
        // Validate duplicates/disjointness.
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __seen = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "if (__added is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "foreach (var __e in __added)");
        code.AppendLineAt(6, "{");
        // KeyOf method name: __SparseKeyOf_<id>
        code.AppendLineAt(7, "var __k = __SparseKeyOf_" + member.Id + "(__e);");
        code.AppendLineAt(
            7,
            "if (!__seen.Add(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate added key.\");"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "var __removedSet = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "if (__removed is not null) foreach (var __k in __removed)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!__removedSet.Add(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate removed key.\");"
        );
        code.AppendLineAt(
            6,
            "if (!__seen.Add(__k)) throw new global::System.Text.Json.JsonException(\"Overlapping keyed operations.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__edited is not null) foreach (var __k in __edited.Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!__seen.Add(__k)) throw new global::System.Text.Json.JsonException(\"Overlapping keyed operations.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (__order is not null && __order.Count != 0) { var __o = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + "); foreach (var __k in __order) if (!__o.Add(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate order key.\"); }"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (__added is not null && __added.Count != 0) result.__added = __added;"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null && __removed.Count != 0) result.__removed = __removed;"
        );
        code.AppendLineAt(
            4,
            "if (__edited is not null && __edited.Count != 0) result.__edited = __edited;"
        );
        code.AppendLineAt(
            4,
            "if (__order is not null && __order.Count != 0) result.__order = __order;"
        );
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        // Whole helper for list type.
        code.AppendLineAt(
            3,
            "private static "
                + runtime
                + "FragmentOperation<"
                + listType
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
            "string? __kind = null; bool __hasValue = false; " + listType + " __v = default!;"
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
                + listType
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
        code.AppendLineAt(4, "if (__kind == \"unset\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Unset whole must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + listType + ">.Unset;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__kind == \"set\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing whole value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + listType + ">.Set(__v);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.Text.Json.JsonException(\"Unknown whole kind '\" + __kind + \"'.\");"
        );
        code.AppendLineAt(3, "}");
    }
}
