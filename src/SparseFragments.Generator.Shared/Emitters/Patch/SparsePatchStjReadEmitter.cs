using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits Patch STJ read.</summary>
internal static class SparsePatchStjReadEmitter
{
    internal static void AppendPatchRead(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
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
            var lit = SparseStjKeyHelpers.Lit(member.Property.Name);
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
            if (SparseStjKeyHelpers.IsScalar(member))
            {
                var field = dialect.MemberField(member);
                code.AppendLineAt(
                    5,
                    "result."
                        + field
                        + " = __SparseReadScalar_"
                        + member.Id
                        + "(ref reader, options);"
                );
            }
            else if (SparseStjKeyHelpers.IsNested(member))
            {
                var field = dialect.MemberField(member);
                var child = SparseStjKeyHelpers.ChildPatchType(member, dialect);
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
                var field = dialect.MemberField(member);
                var coll = SparseStjKeyHelpers.CollectionPatchType(member, dialect);
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
            "__kind = reader.ValueTextEquals(\"remove\") ? (byte)1 : reader.ValueTextEquals(\"set\") ? (byte)2 : (byte)3;"
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
            "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Remove whole must not have a value.\");"
        );
        code.AppendLineAt(4, "return " + runtime + "FragmentOperation<Fragment?>.Remove;");
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
            if (!SparseStjKeyHelpers.IsScalar(member))
                continue;
            var vt = SparseStjKeyHelpers.ScalarValueType(member, dialect);
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
                "__kind = reader.ValueTextEquals(\"remove\") ? (byte)1 : reader.ValueTextEquals(\"set\") ? (byte)2 : (byte)3;"
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
                "if (__hasValue) throw new global::System.Text.Json.JsonException(\"Remove must not have a value.\");"
            );
            code.AppendLineAt(4, "return " + runtime + "FragmentOperation<" + vt + ">.Remove;");
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
}
