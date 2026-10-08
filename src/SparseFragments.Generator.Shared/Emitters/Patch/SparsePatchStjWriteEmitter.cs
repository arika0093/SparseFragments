using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits Patch STJ write.</summary>
internal static class SparsePatchStjWriteEmitter
{
    internal static void AppendPatchWrite(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
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
            var field = dialect.MemberField(member);
            var lit = SparseStjKeyHelpers.Lit(name);
            if (SparseStjKeyHelpers.IsScalar(member))
            {
                var vt = SparseStjKeyHelpers.ScalarValueType(member, dialect);
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
            else if (SparseStjKeyHelpers.IsNested(member))
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
                    SparseStjKeyHelpers.ChildPatchType(member, dialect)
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
}
