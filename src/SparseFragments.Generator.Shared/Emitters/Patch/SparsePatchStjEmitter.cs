using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits System.Text.Json round-trip support for Patch and ChangeSet (issue #86).</summary>
/// <remarks>
/// Generates an internal sealed JsonConverter per Patch/ChangeSet applied via [JsonConverter],
/// encoding semantic state (presence tags, nested, keyed identity/order) with explicit
/// Utf8JsonReader/Writer calls (AOT-safe). User values are (de)serialized via
/// JsonSerializer with JsonTypeInfo from options, so application source-gen covers user types.
/// No runtime reflection or MakeGenericType is emitted. No public Serialize/Deserialize API.
/// Malformed JSON throws JsonException; ChangeSet is rebuilt via Between(before, after)
/// so no invalid baseline can be produced.
/// </remarks>
internal static class SparsePatchStjEmitter
{
    private const string Runtime = "global::SparseFragments.";

    private static string Lit(string value) => SymbolDisplay.FormatLiteral(value, true);

    public static void AppendPatchConverterAttribute(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof(PatchJsonConverter))]"
        );
    }

    public static void AppendChangeSetConverterAttribute(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof(ChangeSetJsonConverter))]"
        );
    }

    private static void AppendTypeInfoHelper(SharedIndentedBuilder code, int indent)
    {
        code.AppendLineAt(
            indent,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember> GetMemberTypeInfo<TMember>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "try");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>)options.GetTypeInfo(typeof(TMember));"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "catch (global::System.NotSupportedException exception)");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "throw new global::System.InvalidOperationException(\"The generated patch converter requires JsonTypeInfo metadata for member type '\" + typeof(TMember) + \"'. Add the model/member types to a source-generated JsonSerializerContext and set it as JsonSerializerOptions.TypeInfoResolver.\", exception);"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    private static bool IsScalar(SparseMemberModel m) =>
        m.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    private static bool IsNested(SparseMemberModel m) =>
        m.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    private static bool IsDict(SparseMemberModel m) => SparseKeyedCollectionEmitter.IsDictionary(m);

    private static string ScalarValueType(SparseMemberModel m) =>
        SparseFragmentPatchEmitter.ValueType(m);

    private static string ChildPatchType(SparseMemberModel m) =>
        SparseFragmentPatchEmitter.ChildPatch(m);

    private static string CollectionPatchType(SparseMemberModel m) =>
        SparseFragmentPatchEmitter.CollectionPatch(m);

    private static string KeyTypeOf(SparseMemberModel m)
    {
        if (IsDict(m))
            return m.Collection.ElementType.Name;
        return m.Collection.KeyTypeName ?? "object?";
    }

    private static string ElementTypeOf(SparseMemberModel m) => m.Collection.ElementType.Name;

    private static string ListTypeOf(SparseMemberModel m) => m.Property.Type.Name;

    private static string DictTypeOf(SparseMemberModel m) => m.Property.Type.Name;

    private static string ValueTypeOf(SparseMemberModel m) =>
        m.Collection.ValueType?.Name ?? "object?";

    private static bool HasElementPatch(SparseMemberModel m) =>
        m.Collection.ElementType.IsFragmentModel;

    private static bool HasValuePatch(SparseMemberModel m) =>
        m.Collection.ValueType?.IsFragmentModel == true;

    private static string ElementPatchType(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".Patch";

    private static string ValuePatchType(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".Patch";

    private static bool IsTupleKey(string keyType) => keyType.TrimStart().StartsWith("(");

    private static string[] SplitTupleComponents(string keyType)
    {
        var t = keyType.Trim();
        if (t.StartsWith("(") && t.EndsWith(")"))
            t = t.Substring(1, t.Length - 2);
        var parts = new System.Collections.Generic.List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var ch in t)
        {
            if (ch == '<' || ch == '(' || ch == '[')
                depth++;
            if (ch == '>' || ch == ')' || ch == ']')
                depth--;
            if (ch == ',' && depth == 0)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        parts.Add(current.ToString().Trim());
        return parts.ToArray();
    }

    private static void AppendKeyHelpers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string keyType
    )
    {
        var id = member.Id;
        if (!IsTupleKey(keyType))
        {
            code.AppendLineAt(
                3,
                "private static void __SparseWriteKey_"
                    + id
                    + "(global::System.Text.Json.Utf8JsonWriter writer, "
                    + keyType
                    + " key, global::System.Text.Json.JsonSerializerOptions options) => global::System.Text.Json.JsonSerializer.Serialize<"
                    + keyType
                    + ">(writer, key, GetMemberTypeInfo<"
                    + keyType
                    + ">(options));"
            );
            code.AppendLineAt(
                3,
                "private static "
                    + keyType
                    + " __SparseReadKey_"
                    + id
                    + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options) => global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                    + keyType
                    + ">(options))!;"
            );
            return;
        }

        var comps = SplitTupleComponents(keyType);
        code.AppendLineAt(
            3,
            "private static void __SparseWriteKey_"
                + id
                + "(global::System.Text.Json.Utf8JsonWriter writer, "
                + keyType
                + " key, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartArray();");
        for (var i = 0; i < comps.Length; i++)
        {
            code.AppendLineAt(
                4,
                "global::System.Text.Json.JsonSerializer.Serialize<"
                    + comps[i]
                    + ">(writer, key.Item"
                    + (i + 1)
                    + ", GetMemberTypeInfo<"
                    + comps[i]
                    + ">(options));"
            );
        }
        code.AppendLineAt(4, "writer.WriteEndArray();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private static "
                + keyType
                + " __SparseReadKey_"
                + id
                + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"A tuple key must be an array.\");"
        );
        for (var i = 0; i < comps.Length; i++)
        {
            code.AppendLineAt(4, comps[i] + " __c" + i + " = default!; bool __h" + i + " = false;");
        }
        code.AppendLineAt(4, "var __idx = 0;");
        code.AppendLineAt(4, "while (reader.Read())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        for (var i = 0; i < comps.Length; i++)
        {
            code.AppendLineAt(5, (i == 0 ? "if" : "else if") + " (__idx == " + i + ")");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "__c"
                    + i
                    + " = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                    + comps[i]
                    + ">(options))!; __h"
                    + i
                    + " = true; __idx++;"
            );
            code.AppendLineAt(5, "}");
        }
        code.AppendLineAt(
            5,
            "else throw new global::System.Text.Json.JsonException(\"Too many tuple key components.\");"
        );
        code.AppendLineAt(4, "}");
        for (var i = 0; i < comps.Length; i++)
        {
            code.AppendLineAt(
                4,
                "if (!__h"
                    + i
                    + ") throw new global::System.Text.Json.JsonException(\"Missing tuple key component.\");"
            );
        }
        code.AppendLineAt(
            4,
            "return (" + string.Join(", ", comps.Select((_, i) => "__c" + i)) + ");"
        );
        code.AppendLineAt(3, "}");
    }

    public static void AppendPatchStj(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        AppendTypeInfoHelper(code, 2);
        code.AppendLine();
        AppendPatchWrite(code, members);
        code.AppendLine();
        AppendPatchRead(code, members);
        code.AppendLine();
        code.AppendLineAt(
            2,
            "public sealed class PatchJsonConverter : global::System.Text.Json.Serialization.JsonConverter<Patch>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "public override Patch Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options) => Patch.__SparseReadStj(ref reader, options);"
        );
        code.AppendLineAt(
            3,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, Patch value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(4, "Patch.__SparseWriteStj(writer, value, options);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendPatchWrite(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLineAt(
            2,
            "internal static void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, Patch value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        // Whole.
        code.AppendLineAt(
            3,
            "if (value.__sparse_whole.Kind != " + Runtime + "FragmentOperationKind.Unchanged)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(
            4,
            "if (value.__sparse_whole.Kind == " + Runtime + "FragmentOperationKind.Unset)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WriteString(\"kind\", \"unset\");");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WriteString(\"kind\", \"set\");");
        code.AppendLineAt(5, "writer.WritePropertyName(\"value\");");
        code.AppendLineAt(5, "if (value.__sparse_whole.Value is null)");
        code.AppendLineAt(5, "{ writer.WriteNullValue(); }");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "new Fragment.FragmentJsonConverter().Write(writer, value.__sparse_whole.Value, options);"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
        foreach (var member in members)
        {
            var name = member.Property.Name;
            var field = SparseFragmentPatchEmitter.Field(member);
            var lit = Lit(name);
            if (IsScalar(member))
            {
                var vt = ScalarValueType(member);
                code.AppendLineAt(
                    3,
                    "if (value."
                        + field
                        + ".Kind != "
                        + Runtime
                        + "FragmentOperationKind.Unchanged)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "writer.WritePropertyName(" + lit + ");");
                code.AppendLineAt(4, "writer.WriteStartObject();");
                code.AppendLineAt(
                    4,
                    "if (value." + field + ".Kind == " + Runtime + "FragmentOperationKind.Unset)"
                );
                code.AppendLineAt(4, "{");
                code.AppendLineAt(5, "writer.WriteString(\"kind\", \"unset\");");
                code.AppendLineAt(4, "}");
                code.AppendLineAt(4, "else");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(5, "writer.WriteString(\"kind\", \"set\");");
                code.AppendLineAt(5, "writer.WritePropertyName(\"value\");");
                code.AppendLineAt(
                    5,
                    "global::System.Text.Json.JsonSerializer.Serialize<"
                        + vt
                        + ">(writer, value."
                        + field
                        + ".Value!, GetMemberTypeInfo<"
                        + vt
                        + ">(options));"
                );
                code.AppendLineAt(4, "}");
                code.AppendLineAt(4, "writer.WriteEndObject();");
                code.AppendLineAt(3, "}");
            }
            else if (IsNested(member))
            {
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "var __nested_" + member.Id + " = value." + field + ";");
                code.AppendLineAt(
                    4,
                    "if (__nested_"
                        + member.Id
                        + " is not null && !__nested_"
                        + member.Id
                        + ".__SparseIsEmpty())"
                );
                code.AppendLineAt(4, "{");
                code.AppendLineAt(5, "writer.WritePropertyName(" + lit + ");");
                code.AppendLineAt(
                    5,
                    ChildPatchType(member)
                        + ".__SparseWriteStj(writer, __nested_"
                        + member.Id
                        + ", options);"
                );
                code.AppendLineAt(4, "}");
                code.AppendLineAt(3, "}");
            }
            else
            {
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "var __coll_" + member.Id + " = value." + field + ";");
                code.AppendLineAt(
                    4,
                    "if (__coll_"
                        + member.Id
                        + " is not null && !__coll_"
                        + member.Id
                        + ".__SparseIsEmpty())"
                );
                code.AppendLineAt(4, "{");
                code.AppendLineAt(5, "writer.WritePropertyName(" + lit + ");");
                code.AppendLineAt(5, "__coll_" + member.Id + ".__SparseWriteStj(writer, options);");
                code.AppendLineAt(4, "}");
                code.AppendLineAt(3, "}");
            }
        }
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
    }

    private static void AppendPatchRead(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        // Bound the number of UTF-8 comparisons for small models.
        // Raising this to eight reduced allocation but slowed dense reads in
        // PatchJsonWidthBenchmarks; keep wider models on string dispatch.
        var useUtf8Names = members.Length <= 4;
        var propertyNamesType = "__SparseJsonPropertyNames";
        var propertyNamesSuffix = 0;
        while (members.Any(member => member.Property.Name == propertyNamesType))
            propertyNamesType = "__SparseJsonPropertyNames" + ++propertyNamesSuffix;
        if (useUtf8Names)
        {
            code.AppendLineAt(2, "private static class " + propertyNamesType);
            code.AppendLineAt(2, "{");
            var names = new[] { "$whole" }.Concat(members.Select(member => member.Property.Name));
            var nameIndex = 0;
            foreach (var name in names)
            {
                code.AppendLineAt(
                    3,
                    "internal static readonly byte[] Name"
                        + nameIndex++
                        + " = new byte[] { "
                        + string.Join(", ", System.Text.Encoding.UTF8.GetBytes(name))
                        + " };"
                );
            }
            code.AppendLineAt(2, "}");
        }
        code.AppendLineAt(
            2,
            "internal static Patch __SparseReadStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A patch must be a JSON object.\");"
        );
        code.AppendLineAt(3, "var result = new Patch();");
        code.AppendLineAt(3, "bool __seenWhole = false;");
        foreach (var member in members)
        {
            code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
        }
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) return result;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a patch property name.\");"
        );
        if (useUtf8Names)
        {
            code.AppendLineAt(
                4,
                "var __property = reader.ValueTextEquals("
                    + propertyNamesType
                    + ".Name0) ? 0 : "
                    + string.Concat(
                        members.Select(
                            (member, index) =>
                                "reader.ValueTextEquals("
                                + propertyNamesType
                                + ".Name"
                                + (index + 1)
                                + ") ? "
                                + (index + 1)
                                + " : "
                        )
                    )
                    + "-1;"
            );
            code.AppendLineAt(4, "var __prop = __property < 0 ? reader.GetString() : null;");
        }
        else
        {
            code.AppendLineAt(4, "var __prop = reader.GetString();");
        }
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of patch.\");"
        );
        code.AppendLineAt(4, useUtf8Names ? "if (__property == 0)" : "if (__prop == \"$whole\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Duplicate patch property '$whole'.\");"
        );
        code.AppendLineAt(5, "__seenWhole = true;");
        code.AppendLineAt(
            5,
            "result.__sparse_whole = __SparseReadWholeFragment(ref reader, options);"
        );
        code.AppendLineAt(4, "}");
        var propertyIndex = 1;
        foreach (var member in members)
        {
            var lit = Lit(member.Property.Name);
            code.AppendLineAt(
                4,
                useUtf8Names
                    ? "else if (__property == " + propertyIndex++ + ")"
                    : "else if (__prop == " + lit + ")"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (__seen_"
                    + member.Id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate patch property.\");"
            );
            code.AppendLineAt(5, "__seen_" + member.Id + " = true;");
            if (IsScalar(member))
            {
                var field = SparseFragmentPatchEmitter.Field(member);
                code.AppendLineAt(
                    5,
                    "result."
                        + field
                        + " = __SparseReadScalar_"
                        + member.Id
                        + "(ref reader, options);"
                );
            }
            else if (IsNested(member))
            {
                var field = SparseFragmentPatchEmitter.Field(member);
                var child = ChildPatchType(member);
                code.AppendLineAt(
                    5,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A nested patch must be a JSON object.\");"
                );
                code.AppendLineAt(
                    5,
                    "var __np_"
                        + member.Id
                        + " = "
                        + child
                        + ".__SparseReadStj(ref reader, options);"
                );
                code.AppendLineAt(
                    5,
                    "if (!__np_"
                        + member.Id
                        + ".__SparseIsEmpty()) result."
                        + field
                        + " = __np_"
                        + member.Id
                        + ";"
                );
            }
            else
            {
                var field = SparseFragmentPatchEmitter.Field(member);
                var coll = CollectionPatchType(member);
                code.AppendLineAt(
                    5,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A collection patch must be a JSON object.\");"
                );
                code.AppendLineAt(
                    5,
                    "var __cp_"
                        + member.Id
                        + " = "
                        + coll
                        + ".__SparseReadStj(ref reader, options);"
                );
                code.AppendLineAt(
                    5,
                    "if (!__cp_"
                        + member.Id
                        + ".__SparseIsEmpty()) result."
                        + field
                        + " = __cp_"
                        + member.Id
                        + ";"
                );
            }
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown patch property '\" + __prop + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "throw new global::System.Text.Json.JsonException(\"Unexpected end of patch.\");"
        );
        code.AppendLineAt(2, "}");
        // Whole helper.
        code.AppendLineAt(
            2,
            "private static "
                + Runtime
                + "FragmentOperation<Fragment?> __SparseReadWholeFragment(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A whole operation must be a JSON object.\");"
        );
        code.AppendLineAt(3, "byte __kind = 0;");
        code.AppendLineAt(3, "string? __unknownKind = null;");
        code.AppendLineAt(3, "bool __hasValue = false;");
        code.AppendLineAt(3, "Fragment? __fragValue = null;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a whole-operation property name.\");"
        );
        code.AppendLineAt(4, "var __isKind = reader.ValueTextEquals(\"kind\");");
        code.AppendLineAt(4, "var __isValue = !__isKind && reader.ValueTextEquals(\"value\");");
        code.AppendLineAt(4, "var __unknown = __isKind || __isValue ? null : reader.GetString();");
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of whole operation.\");"
        );
        code.AppendLineAt(4, "if (__isKind)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__kind != 0) throw new global::System.Text.Json.JsonException(\"Duplicate whole kind.\");"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Whole kind must be a string.\");"
        );
        code.AppendLineAt(
            5,
            "__kind = reader.ValueTextEquals(\"unset\") ? (byte)1 : reader.ValueTextEquals(\"set\") ? (byte)2 : (byte)3;"
        );
        code.AppendLineAt(5, "if (__kind == 3) __unknownKind = reader.GetString();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__isValue)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Duplicate whole value.\");"
        );
        code.AppendLineAt(5, "__hasValue = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) { __fragValue = null; }"
        );
        code.AppendLineAt(
            5,
            "else if (reader.TokenType == global::System.Text.Json.JsonTokenType.StartObject) { __fragValue = new Fragment.FragmentJsonConverter().Read(ref reader, typeof(Fragment), options); }"
        );
        code.AppendLineAt(
            5,
            "else throw new global::System.Text.Json.JsonException(\"Whole value must be an object or null.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown whole property '\" + __unknown + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (__kind == 0) throw new global::System.Text.Json.JsonException(\"Missing whole kind.\");"
        );
        code.AppendLineAt(3, "if (__kind == 1)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Unset whole must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<Fragment?>.Unset;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__kind == 2)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing whole value.\");"
        );
        code.AppendLineAt(
            4,
            "return " + Runtime + "FragmentOperation<Fragment?>.Set(__fragValue);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "throw new global::System.Text.Json.JsonException(\"Unknown whole kind '\" + __unknownKind + \"'.\");"
        );
        code.AppendLineAt(2, "}");
        // Scalar helpers per member.
        foreach (var member in members)
        {
            if (!IsScalar(member))
                continue;
            var vt = ScalarValueType(member);
            code.AppendLineAt(
                2,
                "private static "
                    + Runtime
                    + "FragmentOperation<"
                    + vt
                    + "> __SparseReadScalar_"
                    + member.Id
                    + "(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A scalar operation must be a JSON object.\");"
            );
            code.AppendLineAt(3, "byte __kind = 0;");
            code.AppendLineAt(3, "string? __unknownKind = null;");
            code.AppendLineAt(3, "bool __hasValue = false;");
            code.AppendLineAt(3, vt + " __sv = default!;");
            code.AppendLineAt(3, "while (reader.Read())");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
            );
            code.AppendLineAt(
                4,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a scalar-operation property name.\");"
            );
            code.AppendLineAt(4, "var __isKind = reader.ValueTextEquals(\"kind\");");
            code.AppendLineAt(4, "var __isValue = !__isKind && reader.ValueTextEquals(\"value\");");
            code.AppendLineAt(
                4,
                "var __unknown = __isKind || __isValue ? null : reader.GetString();"
            );
            code.AppendLineAt(
                4,
                "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of scalar operation.\");"
            );
            code.AppendLineAt(4, "if (__isKind)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (__kind != 0) throw new global::System.Text.Json.JsonException(\"Duplicate scalar kind.\");"
            );
            code.AppendLineAt(
                5,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Scalar kind must be a string.\");"
            );
            code.AppendLineAt(
                5,
                "__kind = reader.ValueTextEquals(\"unset\") ? (byte)1 : reader.ValueTextEquals(\"set\") ? (byte)2 : (byte)3;"
            );
            code.AppendLineAt(5, "if (__kind == 3) __unknownKind = reader.GetString();");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else if (__isValue)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Duplicate scalar value.\");"
            );
            code.AppendLineAt(5, "__hasValue = true;");
            code.AppendLineAt(
                5,
                "__sv = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                    + vt
                    + ">(options))!;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "else throw new global::System.Text.Json.JsonException(\"Unknown scalar property '\" + __unknown + \"'.\");"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "if (__kind == 0) throw new global::System.Text.Json.JsonException(\"Missing scalar kind.\");"
            );
            code.AppendLineAt(3, "if (__kind == 1)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Unset must not have a value.\");"
            );
            code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<" + vt + ">.Unset;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "if (__kind == 2)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing scalar value.\");"
            );
            code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<" + vt + ">.Set(__sv);");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "throw new global::System.Text.Json.JsonException(\"Unknown scalar kind '\" + __unknownKind + \"'.\");"
            );
            code.AppendLineAt(2, "}");
        }
    }

    public static void AppendChangeSetStj(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        _ = members;
        code.AppendLine();
        code.AppendLineAt(
            2,
            "internal static void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, ChangeSet value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(3, "writer.WritePropertyName(\"before\");");
        code.AppendLineAt(3, "__SparseWriteOptionalFragment(writer, value._before, options);");
        code.AppendLineAt(3, "writer.WritePropertyName(\"after\");");
        code.AppendLineAt(3, "__SparseWriteOptionalFragment(writer, value._after, options);");
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static void __SparseWriteOptionalFragment(global::System.Text.Json.Utf8JsonWriter writer, "
                + Runtime
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
            "new Fragment.FragmentJsonConverter().Write(writer, optional.Value, options);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "internal static ChangeSet __SparseReadStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change set must be a JSON object.\");"
        );
        code.AppendLineAt(3, "bool __hasBefore = false;");
        code.AppendLineAt(3, "bool __hasAfter = false;");
        code.AppendLineAt(3, Runtime + "Optional<Fragment?> __before = default;");
        code.AppendLineAt(3, Runtime + "Optional<Fragment?> __after = default;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a change-set property name.\");"
        );
        code.AppendLineAt(4, "var __isBefore = reader.ValueTextEquals(\"before\");");
        code.AppendLineAt(4, "var __isAfter = !__isBefore && reader.ValueTextEquals(\"after\");");
        code.AppendLineAt(
            4,
            "var __unknown = __isBefore || __isAfter ? null : reader.GetString();"
        );
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of change set.\");"
        );
        code.AppendLineAt(4, "if (__isBefore)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasBefore) throw new global::System.Text.Json.JsonException(\"Duplicate change-set property 'before'.\");"
        );
        code.AppendLineAt(5, "__hasBefore = true;");
        code.AppendLineAt(5, "__before = __SparseReadOptionalFragment(ref reader, options);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__isAfter)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasAfter) throw new global::System.Text.Json.JsonException(\"Duplicate change-set property 'after'.\");"
        );
        code.AppendLineAt(5, "__hasAfter = true;");
        code.AppendLineAt(5, "__after = __SparseReadOptionalFragment(ref reader, options);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown change-set property '\" + __unknown + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!__hasBefore) throw new global::System.Text.Json.JsonException(\"Missing change-set property 'before'.\");"
        );
        code.AppendLineAt(
            3,
            "if (!__hasAfter) throw new global::System.Text.Json.JsonException(\"Missing change-set property 'after'.\");"
        );
        code.AppendLineAt(3, "return ChangeSet.Between(__before, __after);");
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "private static "
                + Runtime
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
            "else if (reader.TokenType == global::System.Text.Json.JsonTokenType.StartObject) { var __r = new Fragment.FragmentJsonConverter().Read(ref reader, typeof(Fragment), options); __frag = __r; __valueWasNull = false; }"
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
        code.AppendLineAt(4, "return " + Runtime + "Optional<Fragment?>.Missing;");
        code.AppendLine();
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__state == 2)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Null state must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + Runtime + "Optional<Fragment?>.Present(null);");
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
        code.AppendLineAt(4, "return " + Runtime + "Optional<Fragment?>.Present(__frag);");
        code.AppendLine();
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "throw new global::System.Text.Json.JsonException(\"Unknown optional state '\" + __unknownState + \"'.\");"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "public sealed class ChangeSetJsonConverter : global::System.Text.Json.Serialization.JsonConverter<ChangeSet>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "public override ChangeSet Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options) => ChangeSet.__SparseReadStj(ref reader, options);"
        );
        code.AppendLineAt(
            3,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, ChangeSet value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(4, "ChangeSet.__SparseWriteStj(writer, value, options);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    public static void AppendKeyedStj(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var listType = ListTypeOf(member);
        var hasPatch = HasElementPatch(member);
        var elementPatch = hasPatch ? ElementPatchType(member) : elementType;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        AppendTypeInfoHelper(code, 3);
        AppendKeyHelpers(code, member, keyType);
        code.AppendLine();
        // Write.
        code.AppendLineAt(
            3,
            "internal void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "if (__whole.Kind != " + Runtime + "FragmentOperationKind.Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(5, "writer.WriteStartObject();");
        code.AppendLineAt(5, "if (__whole.Kind == " + Runtime + "FragmentOperationKind.Unset)");
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
        var patchName = CollectionPatchType(member);
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
        code.AppendLineAt(4, Runtime + "FragmentOperation<" + listType + "> __whole = default;");
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
                + Runtime
                + "FragmentOperationKind.Unset) { result.Unset(); return result; }"
        );
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + Runtime
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
                + Runtime
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
        code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<" + listType + ">.Unset;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__kind == \"set\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing whole value.\");"
        );
        code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<" + listType + ">.Set(__v);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.Text.Json.JsonException(\"Unknown whole kind '\" + __kind + \"'.\");"
        );
        code.AppendLineAt(3, "}");
    }

    public static void AppendDictionaryStj(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dictType = DictTypeOf(member);
        var hasPatch = HasValuePatch(member);
        var valuePatch = hasPatch ? ValuePatchType(member) : valueType;
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        AppendTypeInfoHelper(code, 3);
        AppendKeyHelpers(code, member, keyType);
        code.AppendLine();
        code.AppendLineAt(
            3,
            "internal void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(4, "if (__whole.Kind != " + Runtime + "FragmentOperationKind.Unchanged)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(5, "writer.WriteStartObject();");
        code.AppendLineAt(5, "if (__whole.Kind == " + Runtime + "FragmentOperationKind.Unset)");
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
        var patchName = CollectionPatchType(member);
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
        code.AppendLineAt(4, Runtime + "FragmentOperation<" + dictType + "> __whole = default;");
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
                + Runtime
                + "FragmentOperationKind.Unset) { result.Unset(); return result; }"
        );
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + Runtime
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
                + Runtime
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
        code.AppendLineAt(4, "if (__kind == \"unset\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Unset whole must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<" + dictType + ">.Unset;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__kind == \"set\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing whole value.\");"
        );
        code.AppendLineAt(4, "return " + Runtime + "FragmentOperation<" + dictType + ">.Set(__v);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.Text.Json.JsonException(\"Unknown whole kind '\" + __kind + \"'.\");"
        );
        code.AppendLineAt(3, "}");
    }
}
