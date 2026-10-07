using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the generated <c>Sparse</c> runtime-metadata holder for a model.</summary>
internal static class SparseMetadataEmitter
{
    internal static void AppendSparseMetadata(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            1,
            "/// <summary>Generated SparseFragments runtime metadata for this model.</summary>"
        );
        code.AppendLineAt(1, "public static class Sparse");
        code.AppendLineAt(1, "{");
        const string propertiesType =
            "global::System.Collections.Generic.IReadOnlyList<global::SparseFragments.SparsePropertyInfo>";
        const string arrayType = "new global::SparseFragments.SparsePropertyInfo[]";
        if (members.IsDefaultOrEmpty)
        {
            code.AppendLineAt(
                2,
                "public static " + propertiesType + " Properties { get; } = " + arrayType + " { };"
            );
        }
        else
        {
            code.AppendLineAt(
                2,
                "public static " + propertiesType + " Properties { get; } = " + arrayType
            );
            code.AppendLineAt(2, "{");
            foreach (var member in members)
            {
                code.CancellationToken.ThrowIfCancellationRequested();
                code.AppendIndent(3).Append(FormatMember(member)).AppendLine(",");
            }

            code.AppendLineAt(2, "};");
        }

        code.AppendLineAt(1, "}");
    }

    private static string FormatMember(SparseMemberModel member)
    {
        var isNullable = member.Property.IsNullable;
        var isNestedModel = member.ChildModel is not null;
        var nestedModelType = member.ChildModel is null
            ? "null"
            : "typeof(" + member.ChildModel.Value.NonNullableName + ")";
        var mergeStrategyType = member.MergeStrategyType is null
            ? "null"
            : "typeof(" + member.MergeStrategyType.Value.NonNullableName + ")";
        var keyType = string.IsNullOrEmpty(member.Collection.KeyTypeName)
            ? "null"
            : "typeof(" + member.Collection.KeyTypeName + ")";
        var jsonName = member.Property.JsonPropertyName ?? member.Property.Name;
        return "new global::SparseFragments.SparsePropertyInfo("
            + Quote(member.Property.Name)
            + ", typeof("
            + member.Property.Type.NonNullableName
            + "), "
            + Bool(isNullable)
            + ", "
            + Bool(isNestedModel)
            + ", "
            + nestedModelType
            + ", global::SparseFragments.MergeMode."
            + SparseNaming.MergeModeName(member.MergeMode)
            + ", "
            + mergeStrategyType
            + ", global::SparseFragments.SparseCollectionKind."
            + CollectionKindName(member.Collection.CloneKind)
            + ", global::SparseFragments.SparseCollectionSemantic."
            + member.Collection.Semantic
            + ", global::SparseFragments.SparseKeyKind."
            + member.Collection.KeyKind
            + ", "
            + FormatKeyPropertyNames(member.Collection.KeyPropertyNames)
            + ", "
            + keyType
            + ", "
            + Quote(jsonName)
            + ", "
            + Bool(member.Property.IsRequired)
            + ", "
            + Bool(member.Property.IsInitOnly)
            + ", "
            + Bool(member.Property.IsReadOnly)
            + ")";
    }

    private static string CollectionKindName(SparseCloneCollectionKind cloneKind) =>
        cloneKind switch
        {
            SparseCloneCollectionKind.Array => "Array",
            SparseCloneCollectionKind.List => "List",
            SparseCloneCollectionKind.Set => "Set",
            SparseCloneCollectionKind.Dictionary => "Dictionary",
            _ => "None",
        };

    private static string FormatKeyPropertyNames(ImmutableArray<string> names)
    {
        if (names.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<string>()";
        }

        var quoted = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            quoted[i] = Quote(names[i]);
        }

        return "new string[] { " + string.Join(", ", quoted) + " }";
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Quote(string value)
    {
        return "\"" + Escape(value) + "\"";
    }

    private static string Escape(string value)
    {
        // String literals (not identifiers): escape backslashes and quotes.
        // Also escape control characters so arbitrary JSON names stay valid.
        var result = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        result = result.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return result;
    }
}
