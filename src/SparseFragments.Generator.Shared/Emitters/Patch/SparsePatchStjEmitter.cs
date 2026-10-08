using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

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

    public static void AppendChangeSetConverterAttribute(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            1,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof(ChangeSetJsonConverter))]"
        );
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
        SparseStjKeyHelpers.AppendTypeInfoHelper(code, 2);
        code.AppendLine();
        SparseChangeSetStjWriteEmitter.AppendChangeSetWireHelpers(code, members);
        code.AppendLine();
        foreach (
            var member in members.Where(static m =>
                !m.Property.IsJsonIgnored
                && (
                    SparseStjKeyHelpers.IsDict(m) || SparseKeyedCollectionEmitter.IsKeyedSequence(m)
                )
            )
        )
        {
            SparseStjKeyHelpers.AppendKeyHelpers(
                code,
                member,
                SparseStjKeyHelpers.KeyTypeOf(member)
            );
            code.AppendLine();
        }
        SparseChangeSetStjWriteEmitter.AppendSparseEndpointWrite(code, members, dialect);
        code.AppendLine();
        SparseChangeSetStjWriteEmitter.AppendChangeSetWrite(code, members, dialect);
        code.AppendLine();
        SparseChangeSetStjReadEmitter.AppendChangeSetRead(code, members, dialect);
        code.AppendLine();
        foreach (var member in members)
        {
            if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
            )
                continue;
            SparseChangeSetStjOptionalEmitter.AppendChangeSetOptionalHelpers(code, member, dialect);
            code.AppendLine();
        }
        SparseChangeSetStjOptionalEmitter.AppendChangeSetOptionalFragment(code, dialect);
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

    public static void AppendKeyedStj(SharedIndentedBuilder code, SparseMemberModel member) =>
        AppendKeyedStj(code, member, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendKeyedStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => SparseKeyedStjEmitter.AppendKeyedStj(code, member, dialect);

    public static void AppendDictionaryStj(SharedIndentedBuilder code, SparseMemberModel member) =>
        AppendDictionaryStj(code, member, SparseFragmentPatchEmitter.StandaloneDialect());

    public static void AppendDictionaryStj(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) => SparseDictionaryStjEmitter.AppendDictionaryStj(code, member, dialect);
}
