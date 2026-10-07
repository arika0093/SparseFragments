using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits System.Text.Json round-trip support for Patch and ChangeSet.</summary>
/// <remarks>Uses explicit Utf8JsonReader/Writer calls (AOT-safe); user values go through JsonSerializer options.</remarks>
internal static class SparsePatchStjEmitter
{
    internal static string RuntimeFor(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace;

    private static string ChildPatchType(
        SparseMemberModel m,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => dialect.ChildPatchName(m);

    private static string ChangeSetChildChangeSet(
        SparseMemberModel m,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => dialect.ChildChangeSetName(m);

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
    ) => AppendPatchStj(code, members, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendPatchStj(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        AppendTypeInfoHelper(code, 2);
        code.AppendLine();
        AppendPatchWrite(code, members, dialect);
        code.AppendLine();
        AppendPatchRead(code, members, dialect);
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
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
        code.AppendLineAt(
            2,
            "internal static void __SparseWriteStj(global::System.Text.Json.Utf8JsonWriter writer, Patch value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        // Whole.
        code.AppendLineAt(
            3,
            "if (value.__sparse_whole.Kind != " + runtime + "FragmentOperationKind.Unchanged)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WritePropertyName(\"$whole\");");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        code.AppendLineAt(
            4,
            "if (value.__sparse_whole.Kind == " + runtime + "FragmentOperationKind.Unset)"
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
            "((Fragment.FragmentJsonConverter)Fragment.JsonConverter).Write(writer, value.__sparse_whole.Value, options);"
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
                        + runtime
                        + "FragmentOperationKind.Unchanged)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "writer.WritePropertyName(" + lit + ");");
                code.AppendLineAt(4, "writer.WriteStartObject();");
                code.AppendLineAt(
                    4,
                    "if (value." + field + ".Kind == " + runtime + "FragmentOperationKind.Unset)"
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
                    ChildPatchType(member, dialect)
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
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
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
                var child = ChildPatchType(member, dialect);
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
                + runtime
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
            "else if (reader.TokenType == global::System.Text.Json.JsonTokenType.StartObject) { __fragValue = ((Fragment.FragmentJsonConverter)Fragment.JsonConverter).Read(ref reader, typeof(Fragment), options); }"
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
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<Fragment?>.Unset;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__kind == 2)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing whole value.\");"
        );
        code.AppendLineAt(
            4,
            "return " + runtime + "FragmentOperation<Fragment?>.Set(__fragValue);"
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
                    + runtime
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
            code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + vt + ">.Unset;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "if (__kind == 2)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__hasValue) throw new global::System.Text.Json.JsonException(\"Missing scalar value.\");"
            );
            code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + vt + ">.Set(__sv);");
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
    ) => AppendChangeSetStj(code, members, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendChangeSetStj(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        AppendTypeInfoHelper(code, 2);
        code.AppendLine();
        AppendChangeSetWireHelpers(code, members);
        code.AppendLine();
        foreach (
            var member in members.Where(static m =>
                !m.Property.IsJsonIgnored
                && (IsDict(m) || SparseKeyedCollectionEmitter.IsKeyedSequence(m))
            )
        )
        {
            AppendKeyHelpers(code, member, KeyTypeOf(member));
            code.AppendLine();
        }
        AppendChangeSetWrite(code, members, dialect);
        code.AppendLine();
        AppendChangeSetRead(code, members, dialect);
        code.AppendLine();
        foreach (var member in members)
        {
            if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
            )
                continue;
            AppendChangeSetOptionalHelpers(code, member, dialect);
            code.AppendLine();
        }
        AppendChangeSetOptionalFragment(code, dialect);
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

    private static string ChangeSetWireName(SparseMemberModel member) =>
        member.Property.JsonPropertyName ?? member.Property.Name;

    private static bool ChangeSetIsNested(SparseMemberModel member) =>
        member.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(member);

    private static string ChangeSetValueType(SparseMemberModel member) =>
        SparseFragmentEmitHelpers.FragmentValueType(member);

    private static void AppendChangeSetWireHelpers(
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

    private static void AppendChangeSetWrite(
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
            var wire = ChangeSetWireName(member);
            var lit = Lit(wire);
            var explicitName = member.Property.HasExplicitJsonPropertyName;
            var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
            _ = esc;
            if (ChangeSetIsNested(member))
            {
                var child = ChangeSetChildChangeSet(member, dialect);
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
            else if (IsDict(member) || SparseKeyedCollectionEmitter.IsKeyedSequence(member))
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
    private static void AppendChangeSetSparseMemberWrite(
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
        var elementType = isKeyed ? ElementTypeOf(member) : ValueTypeOf(member);
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
        code.AppendLineAt(
            6,
            "global::System.Text.Json.JsonSerializer.Serialize<"
                + elementType
                + ">(writer, __it.Before.Value!, GetMemberTypeInfo<"
                + elementType
                + ">(options));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__it.After.IsPresent)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "writer.WritePropertyName(\"after\");");
        code.AppendLineAt(
            6,
            "global::System.Text.Json.JsonSerializer.Serialize<"
                + elementType
                + ">(writer, __it.After.Value!, GetMemberTypeInfo<"
                + elementType
                + ">(options));"
        );
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

    private static string SparseTransName(
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member
    )
    {
        SparseChangeSetEmitter.ComputePublicNames(members, out _, out var transNames);
        return transNames[member.Id];
    }

    private static string SparseElementChangeSet(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".ChangeSet";

    private static string SparseElementFragment(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".Fragment";

    private static string SparseValueChangeSet(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".ChangeSet";

    private static string SparseValueFragment(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".Fragment";

    /// <summary>Emits sparse keyed/dict member ChangeSet JSON read.</summary>
    private static void AppendChangeSetSparseMemberRead(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var runtime = RuntimeFor(dialect);
        var isKeyed = SparseKeyedCollectionEmitter.IsKeyedSequence(member);
        var keyType = KeyTypeOf(member);
        var elementType = isKeyed ? ElementTypeOf(member) : ValueTypeOf(member);
        var trans = SparseTransName(members, member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = isKeyed ? HasElementPatch(member) : HasValuePatch(member);
        string? elemCs;
        string? elemFrag;
        if (isKeyed)
        {
            elemCs = SparseElementChangeSet(member);
            elemFrag = SparseElementFragment(member);
        }
        else if (hasPatch)
        {
            elemCs = SparseValueChangeSet(member);
            elemFrag = SparseValueFragment(member);
        }
        else
        {
            elemCs = null;
            elemFrag = null;
        }
        var optElem = runtime + "Optional<" + elementType + ">";
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A member transition must be a JSON object.\");"
        );
        // Per-member sparse locals are predeclared at method scope
        // (__wb/__wa/__hb/__ha/__whole/__hasItems/__items[/__bO/__aO]).
        if (isKeyed)
        {
            code.AppendLineAt(5, "bool __hbO" + id + " = false; bool __haO" + id + " = false;");
        }
        code.AppendLineAt(5, "bool __hasIt" + id + " = false;");
        code.AppendLineAt(5, "while (reader.Read())");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a member-transition property name.\");"
        );
        code.AppendLineAt(6, "var __isB = reader.ValueTextEquals(\"before\");");
        code.AppendLineAt(6, "var __isA = !__isB && reader.ValueTextEquals(\"after\");");
        code.AppendLineAt(
            6,
            "var __isItems = !__isB && !__isA && reader.ValueTextEquals(\"items\");"
        );
        string orderDecl = isKeyed
            ? "var __isBO = !__isB && !__isA && !__isItems && reader.ValueTextEquals(\"beforeOrder\"); var __isAO = !__isB && !__isA && !__isItems && !__isBO && reader.ValueTextEquals(\"afterOrder\"); var __mUnknown = __isB || __isA || __isItems || __isBO || __isAO ? null : reader.GetString();"
            : "var __mUnknown = __isB || __isA || __isItems ? null : reader.GetString();";
        code.AppendLineAt(6, orderDecl);
        code.AppendLineAt(
            6,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of member transition.\");"
        );
        // before (whole).
        code.AppendLineAt(6, "if (__isB)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hb_"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Duplicate member before.\");"
        );
        code.AppendLineAt(7, "__hb_" + id + " = true;");
        code.AppendLineAt(7, "__wb_" + id + " = __SparseReadOpt_" + id + "(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__isA)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__ha_"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Duplicate member after.\");"
        );
        code.AppendLineAt(7, "__ha_" + id + " = true;");
        code.AppendLineAt(7, "__wa_" + id + " = __SparseReadOpt_" + id + "(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__isItems)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hasIt"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Duplicate member items.\");"
        );
        code.AppendLineAt(7, "__hasIt" + id + " = true;");
        code.AppendLineAt(
            7,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Items must be an array.\");"
        );
        code.AppendLineAt(
            7,
            "__items" + id + " = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        code.AppendLineAt(
            7,
            "var __seenKeys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(7, "while (reader.Read())");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(
            8,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An item must be a JSON object.\");"
        );
        // Item fields (indexes/reorder only for keyed sequences).
        if (isKeyed)
        {
            code.AppendLineAt(
                8,
                "bool __hK = false; bool __hKind = false; bool __hB = false; bool __hA = false; bool __hBi = false; bool __hAi = false; bool __hRe = false;"
            );
            code.AppendLineAt(8, keyType + " __k = default!;");
            code.AppendLineAt(8, "byte __kind = 0;");
            code.AppendLineAt(8, elementType + " __bv = default!;");
            code.AppendLineAt(8, elementType + " __av = default!;");
            code.AppendLineAt(8, "int __bi = -1; int __ai = -1; bool __re = false;");
        }
        else
        {
            code.AppendLineAt(
                8,
                "bool __hK = false; bool __hKind = false; bool __hB = false; bool __hA = false;"
            );
            code.AppendLineAt(8, keyType + " __k = default!;");
            code.AppendLineAt(8, "byte __kind = 0;");
            code.AppendLineAt(8, elementType + " __bv = default!;");
            code.AppendLineAt(8, elementType + " __av = default!;");
        }
        code.AppendLineAt(8, "while (reader.Read())");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            9,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected an item property name.\");"
        );
        code.AppendLineAt(9, "var __isK = reader.ValueTextEquals(\"key\");");
        code.AppendLineAt(9, "var __isKind = !__isK && reader.ValueTextEquals(\"kind\");");
        code.AppendLineAt(
            9,
            "var __isBf = !__isK && !__isKind && reader.ValueTextEquals(\"before\");"
        );
        code.AppendLineAt(
            9,
            "var __isAf = !__isK && !__isKind && !__isBf && reader.ValueTextEquals(\"after\");"
        );
        if (isKeyed)
        {
            code.AppendLineAt(
                9,
                "var __isBi = !__isK && !__isKind && !__isBf && !__isAf && reader.ValueTextEquals(\"beforeIndex\");"
            );
            code.AppendLineAt(
                9,
                "var __isAi = !__isK && !__isKind && !__isBf && !__isAf && !__isBi && reader.ValueTextEquals(\"afterIndex\");"
            );
            code.AppendLineAt(
                9,
                "var __isRe = !__isK && !__isKind && !__isBf && !__isAf && !__isBi && !__isAi && reader.ValueTextEquals(\"reordered\");"
            );
            code.AppendLineAt(
                9,
                "var __iu = __isK || __isKind || __isBf || __isAf || __isBi || __isAi || __isRe ? null : reader.GetString();"
            );
        }
        else
        {
            code.AppendLineAt(
                9,
                "var __iu = __isK || __isKind || __isBf || __isAf ? null : reader.GetString();"
            );
        }
        code.AppendLineAt(
            9,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of item.\");"
        );
        code.AppendLineAt(
            9,
            "if (__isK) { if (__hK) throw new global::System.Text.Json.JsonException(\"Duplicate item key.\"); __hK = true; __k = __SparseReadKey_"
                + id
                + "(ref reader, options); }"
        );
        code.AppendLineAt(9, "else if (__isKind)");
        code.AppendLineAt(9, "{");
        code.AppendLineAt(
            10,
            "if (__hKind) throw new global::System.Text.Json.JsonException(\"Duplicate item kind.\"); __hKind = true;"
        );
        code.AppendLineAt(
            10,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Item kind must be a string.\");"
        );
        code.AppendLineAt(
            10,
            "__kind = reader.ValueTextEquals(\"add\") ? (byte)1 : reader.ValueTextEquals(\"remove\") ? (byte)2 : reader.ValueTextEquals(\"edit\") ? (byte)3 : reader.ValueTextEquals(\"reorder\") ? (byte)4 : (byte)5;"
        );
        code.AppendLineAt(
            10,
            "if (__kind == 5) throw new global::System.Text.Json.JsonException(\"Unknown item kind.\");"
        );
        code.AppendLineAt(9, "}");
        code.AppendLineAt(
            9,
            "else if (__isBf) { if (__hB) throw new global::System.Text.Json.JsonException(\"Duplicate item before.\"); __hB = true; __bv = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + elementType
                + ">(options))!; }"
        );
        code.AppendLineAt(
            9,
            "else if (__isAf) { if (__hA) throw new global::System.Text.Json.JsonException(\"Duplicate item after.\"); __hA = true; __av = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + elementType
                + ">(options))!; }"
        );
        if (isKeyed)
        {
            code.AppendLineAt(
                9,
                "else if (__isBi) { if (__hBi) throw new global::System.Text.Json.JsonException(\"Duplicate item beforeIndex.\"); __hBi = true; if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number || !reader.TryGetInt32(out __bi)) throw new global::System.Text.Json.JsonException(\"Item beforeIndex must be an integer.\"); }"
            );
            code.AppendLineAt(
                9,
                "else if (__isAi) { if (__hAi) throw new global::System.Text.Json.JsonException(\"Duplicate item afterIndex.\"); __hAi = true; if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number || !reader.TryGetInt32(out __ai)) throw new global::System.Text.Json.JsonException(\"Item afterIndex must be an integer.\"); }"
            );
            code.AppendLineAt(
                9,
                "else if (__isRe) { if (__hRe) throw new global::System.Text.Json.JsonException(\"Duplicate item reordered.\"); __hRe = true; if (reader.TokenType != global::System.Text.Json.JsonTokenType.True && reader.TokenType != global::System.Text.Json.JsonTokenType.False) throw new global::System.Text.Json.JsonException(\"Item reordered must be a boolean.\"); __re = reader.GetBoolean(); if (!__re) throw new global::System.Text.Json.JsonException(\"Item reordered must be true when present.\"); }"
            );
        }
        code.AppendLineAt(
            9,
            "else throw new global::System.Text.Json.JsonException(\"Unknown item property '\" + __iu + \"'.\");"
        );
        code.AppendLineAt(8, "}");
        // Validate item.
        code.AppendLineAt(
            8,
            "if (!__hK) throw new global::System.Text.Json.JsonException(\"Missing item key.\");"
        );
        code.AppendLineAt(
            8,
            "if (__kind == 0) throw new global::System.Text.Json.JsonException(\"Missing item kind.\");"
        );
        code.AppendLineAt(
            8,
            "if (!__seenKeys"
                + id
                + ".Add(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate item key.\");"
        );
        if (isKeyed)
        {
            code.AppendLineAt(
                8,
                "if (!__hBi) throw new global::System.Text.Json.JsonException(\"Missing item beforeIndex.\");"
            );
            code.AppendLineAt(
                8,
                "if (!__hAi) throw new global::System.Text.Json.JsonException(\"Missing item afterIndex.\");"
            );
            // kind/endpoint/index consistency.
            code.AppendLineAt(
                8,
                "if (__kind == 1) { if (__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Added item must have after only.\"); if (__bi != -1 || __ai < 0) throw new global::System.Text.Json.JsonException(\"Added item has invalid indexes.\"); }"
            );
            code.AppendLineAt(
                8,
                "else if (__kind == 2) { if (!__hB || __hA) throw new global::System.Text.Json.JsonException(\"Removed item must have before only.\"); if (__bi < 0 || __ai != -1) throw new global::System.Text.Json.JsonException(\"Removed item has invalid indexes.\"); }"
            );
            code.AppendLineAt(
                8,
                "else { if (!__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Edited/reordered item must have before and after.\"); if (__bi < 0 || __ai < 0) throw new global::System.Text.Json.JsonException(\"Edited/reordered item has invalid indexes.\"); }"
            );
            code.AppendLineAt(
                8,
                "if (__kind == 4 && __re) throw new global::System.Text.Json.JsonException(\"Reordered item must not set reordered flag.\");"
            );
            code.AppendLineAt(
                8,
                "if ((__kind == 1 || __kind == 2) && __re) throw new global::System.Text.Json.JsonException(\"Added/removed item must not be reordered.\");"
            );
            // Reconstruct nested edit and validate.
            code.AppendLineAt(
                8,
                runtime
                    + "Optional<"
                    + elemFrag
                    + "?> __eb = __hB ? "
                    + runtime
                    + "Optional<"
                    + elemFrag
                    + "?>.Present("
                    + elemFrag
                    + ".From(__bv!)) : default;"
            );
            code.AppendLineAt(
                8,
                runtime
                    + "Optional<"
                    + elemFrag
                    + "?> __ea = __hA ? "
                    + runtime
                    + "Optional<"
                    + elemFrag
                    + "?>.Present("
                    + elemFrag
                    + ".From(__av!)) : default;"
            );
            code.AppendLineAt(8, "var __edit = " + elemCs + ".Between(__eb, __ea);");
            code.AppendLineAt(
                8,
                "if (__kind == 3 && __edit.IsEmpty) throw new global::System.Text.Json.JsonException(\"Edited item has empty edit.\");"
            );
            code.AppendLineAt(
                8,
                "if (__kind == 4 && !__edit.IsEmpty) throw new global::System.Text.Json.JsonException(\"Reordered item must have equal endpoints.\");"
            );
            code.AppendLineAt(
                8,
                "bool __isEd = __kind == 3; bool __isRe2 = __kind == 4 || (__kind == 3 && __re); bool __isAd = __kind == 1; bool __isRm = __kind == 2;"
            );
            code.AppendLineAt(
                8,
                optElem
                    + " __ob = __hB ? "
                    + runtime
                    + "Optional<"
                    + elementType
                    + ">.Present(__bv!) : default; "
                    + optElem
                    + " __oa = __hA ? "
                    + runtime
                    + "Optional<"
                    + elementType
                    + ">.Present(__av!) : default;"
            );
            code.AppendLineAt(
                8,
                "__items"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__k, __ob, __oa, __bi, __ai, __isAd, __isRm, __isEd, __isRe2, __edit, false));"
            );
        }
        else
        {
            // Dict: kind/endpoint consistency, no indexes.
            code.AppendLineAt(
                8,
                "if (__kind == 1) { if (__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Added entry must have after only.\"); }"
            );
            code.AppendLineAt(
                8,
                "else if (__kind == 2) { if (!__hB || __hA) throw new global::System.Text.Json.JsonException(\"Removed entry must have before only.\"); }"
            );
            code.AppendLineAt(
                8,
                "else { if (!__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Edited entry must have before and after.\"); }"
            );
            if (hasPatch)
            {
                code.AppendLineAt(
                    8,
                    runtime
                        + "Optional<"
                        + elemFrag
                        + "?> __eb = __hB ? "
                        + runtime
                        + "Optional<"
                        + elemFrag
                        + "?>.Present("
                        + elemFrag
                        + ".From(__bv!)) : default;"
                );
                code.AppendLineAt(
                    8,
                    runtime
                        + "Optional<"
                        + elemFrag
                        + "?> __ea = __hA ? "
                        + runtime
                        + "Optional<"
                        + elemFrag
                        + "?>.Present("
                        + elemFrag
                        + ".From(__av!)) : default;"
                );
                code.AppendLineAt(8, "var __edit = " + elemCs + ".Between(__eb, __ea);");
                code.AppendLineAt(
                    8,
                    "if (__kind == 3 && __edit.IsEmpty) throw new global::System.Text.Json.JsonException(\"Edited entry has empty edit.\");"
                );
                code.AppendLineAt(8, "if ((__kind == 1 || __kind == 2) && !__edit.IsEmpty) { }");
                code.AppendLineAt(
                    8,
                    "bool __isAd = __kind == 1; bool __isRm = __kind == 2; bool __isEd = __kind == 3;"
                );
                code.AppendLineAt(
                    8,
                    optElem
                        + " __ob = __hB ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__bv!) : default; "
                        + optElem
                        + " __oa = __hA ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__av!) : default;"
                );
                code.AppendLineAt(
                    8,
                    "__items"
                        + id
                        + ".Add(new "
                        + trans
                        + ".Item(__k, __ob, __oa, __isAd, __isRm, __isEd, __edit, false));"
                );
            }
            else
            {
                code.AppendLineAt(
                    8,
                    "if (__kind == 3 && global::System.Collections.Generic.EqualityComparer<"
                        + elementType
                        + ">.Default.Equals(__bv!, __av!)) throw new global::System.Text.Json.JsonException(\"Edited entry must change the value.\");"
                );
                code.AppendLineAt(
                    8,
                    "bool __isAd = __kind == 1; bool __isRm = __kind == 2; bool __isEd = __kind == 3;"
                );
                code.AppendLineAt(
                    8,
                    optElem
                        + " __ob = __hB ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__bv!) : default; "
                        + optElem
                        + " __oa = __hA ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__av!) : default;"
                );
                code.AppendLineAt(
                    8,
                    "__items"
                        + id
                        + ".Add(new "
                        + trans
                        + ".Item(__k, __ob, __oa, __isAd, __isRm, __isEd, false));"
                );
            }
        }
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "if (__items"
                + id
                + ".Count == 0) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
        );
        code.AppendLineAt(6, "}");
        if (isKeyed)
        {
            code.AppendLineAt(6, "else if (__isBO)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (__hbO"
                    + id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate member beforeOrder.\"); __hbO"
                    + id
                    + " = true;"
            );
            code.AppendLineAt(
                7,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"beforeOrder must be an array.\");"
            );
            code.AppendLineAt(
                7,
                "__bO" + id + " = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLineAt(
                7,
                "while (reader.Read()) { if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break; __bO"
                    + id
                    + ".Add(__SparseReadKey_"
                    + id
                    + "(ref reader, options)); }"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else if (__isAO)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (__haO"
                    + id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate member afterOrder.\"); __haO"
                    + id
                    + " = true;"
            );
            code.AppendLineAt(
                7,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"afterOrder must be an array.\");"
            );
            code.AppendLineAt(
                7,
                "__aO" + id + " = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLineAt(
                7,
                "while (reader.Read()) { if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break; __aO"
                    + id
                    + ".Add(__SparseReadKey_"
                    + id
                    + "(ref reader, options)); }"
            );
            code.AppendLineAt(6, "}");
        }
        code.AppendLineAt(
            6,
            "else throw new global::System.Text.Json.JsonException(\"Unknown member property.\");"
        );
        code.AppendLineAt(5, "}");
        // Whole vs granular exclusivity and non-empty validation.
        code.AppendLineAt(5, "bool __isWhole" + id + " = __hb_" + id + " || __ha_" + id + ";");
        code.AppendLineAt(
            5,
            "if (__isWhole"
                + id
                + " && __hasIt"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Whole and granular transitions cannot coexist.\");"
        );
        if (isKeyed)
            code.AppendLineAt(
                5,
                "if (__isWhole"
                    + id
                    + " && (__hbO"
                    + id
                    + " || __haO"
                    + id
                    + ")) throw new global::System.Text.Json.JsonException(\"Whole and order transitions cannot coexist.\");"
            );
        code.AppendLineAt(5, "if (__isWhole" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            5,
            "if (!__hb_"
                + id
                + " || !__ha_"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Missing whole before/after.\");"
        );
        code.AppendLineAt(
            5,
            "if (!__wb_"
                + id
                + ".IsPresent && !__wa_"
                + id
                + ".IsPresent) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
        );
        code.AppendLineAt(
            5,
            "if (Fragment.__SparseEqual_"
                + id
                + "(__wb_"
                + id
                + ", __wa_"
                + id
                + ")) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
        );
        code.AppendLineAt(5, "__whole_" + id + " = true;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            5,
            "if (!__hasIt"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Missing member items.\");"
        );
        if (isKeyed)
            code.AppendLineAt(
                5,
                "if (__hbO"
                    + id
                    + " != __haO"
                    + id
                    + ") throw new global::System.Text.Json.JsonException(\"beforeOrder and afterOrder must coexist.\");"
            );
        code.AppendLineAt(5, "}");
    }

    private static void AppendChangeSetRead(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
        code.AppendLineAt(
            2,
            "internal static ChangeSet __SparseReadBodyStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "__SparseValidateJsonNames(options);");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change set must be a JSON object.\");"
        );
        code.AppendLineAt(3, "bool __seenWhole = false;");
        code.AppendLineAt(3, runtime + "Optional<Fragment?> __wholeBefore = default;");
        code.AppendLineAt(3, runtime + "Optional<Fragment?> __wholeAfter = default;");
        code.AppendLineAt(3, "bool __hasWholeBefore = false; bool __hasWholeAfter = false;");
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            if (ChangeSetIsNested(member))
            {
                code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
                code.AppendLineAt(
                    3,
                    ChangeSetChildChangeSet(member, dialect) + "? __n_" + member.Id + " = null;"
                );
            }
            else if (IsDict(member) || SparseKeyedCollectionEmitter.IsKeyedSequence(member))
            {
                var vt = ChangeSetValueType(member);
                code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
                code.AppendLineAt(3, "bool __whole_" + member.Id + " = false;");
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __wb_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __wa_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    "bool __hb_" + member.Id + " = false; bool __ha_" + member.Id + " = false;"
                );
                // Item/order accumulators are assigned in the member branch and
                // consumed by final ChangeSet construction below. Trans names use
                // the full member list (same collision scheme as ChangeSet).
                var __trans = SparseTransName(members, member);
                code.AppendLineAt(
                    3,
                    "global::System.Collections.Generic.List<"
                        + __trans
                        + ".Item>? __items"
                        + member.Id
                        + " = null;"
                );
                if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                {
                    var __kt = KeyTypeOf(member);
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + __kt
                            + ">? __bO"
                            + member.Id
                            + " = null; global::System.Collections.Generic.List<"
                            + __kt
                            + ">? __aO"
                            + member.Id
                            + " = null;"
                    );
                }
            }
            else
            {
                var vt = ChangeSetValueType(member);
                code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __b_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __a_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    "bool __hb_" + member.Id + " = false; bool __ha_" + member.Id + " = false;"
                );
            }
        }
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
        code.AppendLineAt(4, "var __prop = reader.GetString();");
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of change set.\");"
        );
        code.AppendLineAt(4, "if (__prop == \"$whole\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Duplicate change-set property '$whole'.\");"
        );
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            code.AppendLineAt(
                5,
                "if (__seen_"
                    + member.Id
                    + ") throw new global::System.Text.Json.JsonException(\"Whole and member transitions cannot coexist.\");"
            );
        }
        code.AppendLineAt(5, "__seenWhole = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A whole transition must be a JSON object.\");"
        );
        code.AppendLineAt(5, "while (reader.Read())");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a whole-transition property name.\");"
        );
        code.AppendLineAt(6, "var __isWb = reader.ValueTextEquals(\"before\");");
        code.AppendLineAt(6, "var __isWa = !__isWb && reader.ValueTextEquals(\"after\");");
        code.AppendLineAt(6, "var __wUnknown = __isWb || __isWa ? null : reader.GetString();");
        code.AppendLineAt(
            6,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of whole transition.\");"
        );
        code.AppendLineAt(6, "if (__isWb)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hasWholeBefore) throw new global::System.Text.Json.JsonException(\"Duplicate whole before.\");"
        );
        code.AppendLineAt(7, "__hasWholeBefore = true;");
        code.AppendLineAt(7, "__wholeBefore = __SparseReadOptionalFragment(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__isWa)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hasWholeAfter) throw new global::System.Text.Json.JsonException(\"Duplicate whole after.\");"
        );
        code.AppendLineAt(7, "__hasWholeAfter = true;");
        code.AppendLineAt(7, "__wholeAfter = __SparseReadOptionalFragment(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else throw new global::System.Text.Json.JsonException(\"Unknown whole property '\" + __wUnknown + \"'.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (!__hasWholeBefore) throw new global::System.Text.Json.JsonException(\"Missing whole before.\");"
        );
        code.AppendLineAt(
            5,
            "if (!__hasWholeAfter) throw new global::System.Text.Json.JsonException(\"Missing whole after.\");"
        );
        code.AppendLineAt(4, "}");
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            var wire = ChangeSetWireName(member);
            var lit = Lit(wire);
            var usePolicy = member.Property.HasExplicitJsonPropertyName ? "false" : "true";
            code.AppendLineAt(
                4,
                "else if" + " (__SparseMatches(__prop, " + lit + ", " + usePolicy + ", options))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Whole and member transitions cannot coexist.\");"
            );
            code.AppendLineAt(
                5,
                "if (__seen_"
                    + member.Id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate change-set property.\");"
            );
            code.AppendLineAt(5, "__seen_" + member.Id + " = true;");
            if (ChangeSetIsNested(member))
            {
                var child = ChangeSetChildChangeSet(member, dialect);
                code.AppendLineAt(
                    5,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A nested change set must be a JSON object.\");"
                );
                code.AppendLineAt(
                    5,
                    "var __nn"
                        + member.Id
                        + " = "
                        + child
                        + ".__SparseReadBodyStj(ref reader, options);"
                );
                code.AppendLineAt(
                    5,
                    "if (__nn"
                        + member.Id
                        + ".IsEmpty) throw new global::System.Text.Json.JsonException(\"Empty nested change must not be serialized.\");"
                );
                code.AppendLineAt(5, "__n_" + member.Id + " = __nn" + member.Id + ";");
            }
            else if (IsDict(member) || SparseKeyedCollectionEmitter.IsKeyedSequence(member))
            {
                AppendChangeSetSparseMemberRead(code, member, members, dialect);
            }
            else
            {
                code.AppendLineAt(
                    5,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A member transition must be a JSON object.\");"
                );
                code.AppendLineAt(5, "while (reader.Read())");
                code.AppendLineAt(5, "{");
                code.AppendLineAt(
                    6,
                    "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
                );
                code.AppendLineAt(
                    6,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a member-transition property name.\");"
                );
                code.AppendLineAt(6, "var __isB = reader.ValueTextEquals(\"before\");");
                code.AppendLineAt(6, "var __isA = !__isB && reader.ValueTextEquals(\"after\");");
                code.AppendLineAt(
                    6,
                    "var __mUnknown = __isB || __isA ? null : reader.GetString();"
                );
                code.AppendLineAt(
                    6,
                    "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of member transition.\");"
                );
                code.AppendLineAt(6, "if (__isB)");
                code.AppendLineAt(6, "{");
                code.AppendLineAt(
                    7,
                    "if (__hb_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Duplicate member before.\");"
                );
                code.AppendLineAt(7, "__hb_" + member.Id + " = true;");
                code.AppendLineAt(
                    7,
                    "__b_"
                        + member.Id
                        + " = __SparseReadOpt_"
                        + member.Id
                        + "(ref reader, options);"
                );
                code.AppendLineAt(6, "}");
                code.AppendLineAt(6, "else if (__isA)");
                code.AppendLineAt(6, "{");
                code.AppendLineAt(
                    7,
                    "if (__ha_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Duplicate member after.\");"
                );
                code.AppendLineAt(7, "__ha_" + member.Id + " = true;");
                code.AppendLineAt(
                    7,
                    "__a_"
                        + member.Id
                        + " = __SparseReadOpt_"
                        + member.Id
                        + "(ref reader, options);"
                );
                code.AppendLineAt(6, "}");
                code.AppendLineAt(
                    6,
                    "else throw new global::System.Text.Json.JsonException(\"Unknown member property '\" + __mUnknown + \"'.\");"
                );
                code.AppendLineAt(5, "}");
                code.AppendLineAt(
                    5,
                    "if (!__hb_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Missing member before.\");"
                );
                code.AppendLineAt(
                    5,
                    "if (!__ha_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Missing member after.\");"
                );
                code.AppendLineAt(
                    5,
                    "if (!__b_"
                        + member.Id
                        + ".IsPresent && !__a_"
                        + member.Id
                        + ".IsPresent) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
                );
                code.AppendLineAt(
                    5,
                    "if (Fragment.__SparseEqual_"
                        + member.Id
                        + "(__b_"
                        + member.Id
                        + ", __a_"
                        + member.Id
                        + ")) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
                );
            }
            code.AppendLineAt(4, "}");
        }
        // Ignored members: skip.
        foreach (var member in members.Where(static m => m.Property.IsJsonIgnored))
        {
            var wire = ChangeSetWireName(member);
            var lit = Lit(wire);
            var usePolicy = member.Property.HasExplicitJsonPropertyName ? "false" : "true";
            code.AppendLineAt(
                4,
                "else if (__SparseMatches(__prop, " + lit + ", " + usePolicy + ", options))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "reader.Skip();");
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown change-set property '\" + __prop + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__seenWhole)");
        code.AppendLineAt(3, "{");
        var tail = new System.Text.StringBuilder();
        var sep = string.Empty;
        foreach (var member in members)
        {
            tail.Append(sep);
            if (ChangeSetIsNested(member))
                tail.Append("null");
            else if (IsDict(member) || SparseKeyedCollectionEmitter.IsKeyedSequence(member))
            {
                tail.Append("false, false, default, default, null");
                if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                    tail.Append(", null, null");
            }
            else
                tail.Append("default, default, false");
            sep = ", ";
        }
        if (tail.Length == 0)
            code.AppendLineAt(4, "return Between(__wholeBefore, __wholeAfter);");
        else
            code.AppendLineAt(
                4,
                "if ("
                    + string.Join(
                        " || ",
                        members.Where(m => !m.Property.IsJsonIgnored).Select(m => "__seen_" + m.Id)
                    )
                    + (members.Any(m => !m.Property.IsJsonIgnored) ? ") " : string.Empty)
                    + (
                        members.Any(m => !m.Property.IsJsonIgnored)
                            ? "throw new global::System.Text.Json.JsonException(\"Whole and member transitions cannot coexist.\");"
                            : string.Empty
                    )
            );
        if (tail.Length != 0)
            code.AppendLineAt(
                4,
                "return new ChangeSet(true, __wholeBefore, __wholeAfter, " + tail.ToString() + ");"
            );
        code.AppendLineAt(3, "}");
        var cargs = new System.Collections.Generic.List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            var __isSparse = IsDict(member) || SparseKeyedCollectionEmitter.IsKeyedSequence(member);
            if (member.Property.IsJsonIgnored)
            {
                if (ChangeSetIsNested(member))
                    cargs.Add("null");
                else if (__isSparse)
                {
                    cargs.AddRange(new[] { "false", "false", "default", "default", "null" });
                    if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                        cargs.AddRange(new[] { "null", "null" });
                }
                else
                    cargs.AddRange(new[] { "default", "default", "false" });
                continue;
            }
            if (ChangeSetIsNested(member))
                cargs.Add("__n_" + member.Id);
            else if (__isSparse)
            {
                cargs.AddRange(
                    new[]
                    {
                        "__seen_" + member.Id,
                        "__whole_" + member.Id,
                        "__wb_" + member.Id,
                        "__wa_" + member.Id,
                        "__items" + member.Id,
                    }
                );
                if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                    cargs.AddRange(new[] { "__bO" + member.Id, "__aO" + member.Id });
            }
            else
                cargs.AddRange(
                    new[] { "__b_" + member.Id, "__a_" + member.Id, "__seen_" + member.Id }
                );
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", cargs) + ");");
        code.AppendLineAt(2, "}");
        // v1 document envelope reader (order-independent, strict).
        code.AppendLine();
        code.AppendLineAt(
            2,
            "internal static ChangeSet __SparseReadStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change-set document must be a JSON object.\");"
        );
        code.AppendLineAt(3, "bool __seenVersion = false;");
        code.AppendLineAt(3, "bool __seenChanges = false;");
        code.AppendLineAt(3, "ChangeSet? __docBody = null;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a change-set document property name.\");"
        );
        code.AppendLineAt(4, "var __isVersion = reader.ValueTextEquals(\"version\");");
        code.AppendLineAt(4, "var __isChanges = !__isVersion && reader.ValueTextEquals(\"changes\");");
        code.AppendLineAt(
            4,
            "var __docUnknown = __isVersion || __isChanges ? null : reader.GetString();"
        );
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of change-set document.\");"
        );
        code.AppendLineAt(4, "if (__isVersion)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenVersion) throw new global::System.Text.Json.JsonException(\"Duplicate change-set version.\");"
        );
        code.AppendLineAt(5, "__seenVersion = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number || !reader.TryGetInt32(out var __v)) throw new global::System.Text.Json.JsonException(\"Change-set version must be the integer 1.\");"
        );
        code.AppendLineAt(
            5,
            "if (__v != 1) throw new global::System.Text.Json.JsonException(\"Unsupported change-set version '\" + __v + \"'. Expected version 1.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__isChanges)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenChanges) throw new global::System.Text.Json.JsonException(\"Duplicate change-set changes.\");"
        );
        code.AppendLineAt(5, "__seenChanges = true;");
        code.AppendLineAt(5, "__docBody = __SparseReadBodyStj(ref reader, options);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown change-set document property '\" + __docUnknown + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!__seenVersion) throw new global::System.Text.Json.JsonException(\"Missing change-set version.\");"
        );
        code.AppendLineAt(
            3,
            "if (!__seenChanges) throw new global::System.Text.Json.JsonException(\"Missing change-set changes.\");"
        );
        code.AppendLineAt(3, "return __docBody!;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendChangeSetOptionalHelpers(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
        var vt = ChangeSetValueType(member);
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

    private static void AppendChangeSetOptionalFragment(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
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

    public static void AppendKeyedStj(SharedIndentedBuilder code, SparseMemberModel member) =>
        AppendKeyedStj(code, member, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendKeyedStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
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

    public static void AppendDictionaryStj(SharedIndentedBuilder code, SparseMemberModel member) =>
        AppendDictionaryStj(code, member, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendDictionaryStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = RuntimeFor(dialect);
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
        code.AppendLineAt(4, "if (__kind == \"unset\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Unset whole must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + dictType + ">.Unset;");
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
