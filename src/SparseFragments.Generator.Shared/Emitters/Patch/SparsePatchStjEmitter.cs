using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits System.Text.Json round-trip support for Patch and ChangeSet.</summary>
/// <remarks>Uses explicit Utf8JsonReader/Writer calls (AOT-safe); user values go through JsonSerializer options. Facade delegating to focused emitters.</remarks>
internal static class SparsePatchStjEmitter
{
    internal static string RuntimeFor(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        SparseStjKeyHelpers.RuntimeFor(dialect);

    public static void AppendPatchConverterAttribute(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof(PatchJsonConverter))]"
        );
    }

    public static void AppendPatchStj(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        SparseStjKeyHelpers.AppendTypeInfoHelper(code, 2);
        code.AppendLine();
        SparsePatchStjWriteEmitter.AppendPatchWrite(code, members, dialect);
        code.AppendLine();
        SparsePatchStjReadEmitter.AppendPatchRead(code, members, dialect);
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

    public static void AppendKeyedStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => SparseKeyedStjEmitter.AppendKeyedStj(code, member, dialect);

    public static void AppendDictionaryStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => SparseDictionaryStjEmitter.AppendDictionaryStj(code, member, dialect);
}
