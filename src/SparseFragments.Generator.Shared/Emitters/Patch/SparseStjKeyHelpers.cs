using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared STJ helpers (runtime, predicates, key/tuple helpers).</summary>
internal static class SparseStjKeyHelpers
{
    internal static string RuntimeFor(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.RuntimeNamespace;

    internal static string ChildPatchType(
        SparseMemberModel m,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => dialect.ChildPatchName(m);

    internal static string ChangeSetChildChangeSet(
        SparseMemberModel m,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => dialect.ChildChangeSetName(m);

    internal static string Lit(string value) => SymbolDisplay.FormatLiteral(value, true);

    internal static void AppendTypeInfoHelper(SharedIndentedBuilder code, int indent)
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

    internal static bool IsScalar(SparseMemberModel m) =>
        m.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    internal static bool IsNested(SparseMemberModel m) =>
        m.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(m);

    internal static bool IsDict(SparseMemberModel m) =>
        SparseKeyedCollectionEmitter.IsDictionary(m);

    internal static string ScalarValueType(SparseMemberModel m) =>
        SparseFragmentPatchEmitter.ValueType(m);

    internal static string CollectionPatchType(SparseMemberModel m) =>
        SparseFragmentPatchEmitter.CollectionPatch(m);

    internal static string KeyTypeOf(SparseMemberModel m)
    {
        if (IsDict(m))
            return m.Collection.ElementType.Name;
        return m.Collection.KeyTypeName ?? "object?";
    }

    internal static string ElementTypeOf(SparseMemberModel m) => m.Collection.ElementType.Name;

    internal static string ListTypeOf(SparseMemberModel m) => m.Property.Type.Name;

    internal static string DictTypeOf(SparseMemberModel m) => m.Property.Type.Name;

    internal static string ValueTypeOf(SparseMemberModel m) =>
        m.Collection.ValueType?.Name ?? "object?";

    internal static bool HasElementPatch(SparseMemberModel m) =>
        m.Collection.ElementType.IsFragmentModel;

    internal static bool HasValuePatch(SparseMemberModel m) =>
        m.Collection.ValueType?.IsFragmentModel == true;

    internal static string ElementPatchType(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".Patch";

    internal static string ValuePatchType(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".Patch";

    internal static bool IsTupleKey(string keyType) => keyType.TrimStart().StartsWith("(");

    internal static string[] SplitTupleComponents(string keyType)
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

    internal static void AppendKeyHelpers(
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

    internal static string ChangeSetWireName(SparseMemberModel member) =>
        member.Property.JsonPropertyName ?? member.Property.Name;

    internal static bool ChangeSetIsNested(SparseMemberModel member) =>
        member.ChildModel is not null && !SparseFragmentPatchEmitter.IsCollectionPatch(member);

    internal static string ChangeSetValueType(SparseMemberModel member) =>
        SparseFragmentEmitHelpers.FragmentValueType(member);

    internal static string SparseTransName(
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member
    )
    {
        SparseChangeSetEmitter.ComputePublicNames(members, out _, out var transNames);
        return transNames[member.Id];
    }

    internal static string SparseElementChangeSet(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".ChangeSet";

    internal static string SparseElementFragment(SparseMemberModel m) =>
        m.Collection.ElementType.NonNullableName + ".Fragment";

    internal static string SparseValueChangeSet(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".ChangeSet";

    internal static string SparseValueFragment(SparseMemberModel m) =>
        m.Collection.ValueType!.Value.NonNullableName + ".Fragment";
}
