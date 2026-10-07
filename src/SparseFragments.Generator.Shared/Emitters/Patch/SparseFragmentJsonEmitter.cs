using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared fragment JSON converter emission.</summary>
internal static class SparseFragmentJsonEmitter
{
    private static string FragmentValueType(SparseMemberModel member) =>
        member.ChildModel is null ? member.Property.Type.Name : member.ChildFragmentType + "?";

    private static string WireName(SparseMemberModel member) =>
        member.Property.JsonPropertyName ?? member.Property.Name;

    /// <summary>Emits the standalone fragment JSON converter and JsonConverter accessor.</summary>
    public static void AppendStandaloneFragmentJson(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optional
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "public static global::System.Text.Json.Serialization.JsonConverter<Fragment> JsonConverter { get; } = new FragmentJsonConverter();"
        );
        code.AppendLine();
        AppendConverter(code, members, optional, isStandalone: true);
        code.AppendLine();
    }

    /// <summary>Emits the fragment JSON converter used for standard System.Text.Json serialization.</summary>
    public static void AppendConverter(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optional,
        bool isStandalone
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "/// <summary>Reads and writes sparse fragment properties without materializing absent values.</summary>"
        );
        code.AppendLineAt(
            2,
            "public sealed class FragmentJsonConverter : global::System.Text.Json.Serialization.JsonConverter<Fragment>"
        );
        code.AppendLineAt(2, "{");
        // Keep linear UTF-8 dispatch limited to small models; wider converters retain string dispatch.
        var utf8Dispatch = members.Length > 0 && members.Length <= 3;
        if (utf8Dispatch)
        {
            for (var i = 0; i < members.Length; i++)
            {
                code.AppendIndent(3)
                    .Append("private static readonly byte[] __jsonName")
                    .Append(i.ToString())
                    .Append(" = new byte[] { ")
                    .Append(
                        string.Join(", ", System.Text.Encoding.UTF8.GetBytes(WireName(members[i])))
                    )
                    .AppendLine(" };");
            }
        }
        code.AppendLineAt(
            3,
            "private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember> GetMemberTypeInfo<TMember>(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "try");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "return (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<TMember>)options.GetTypeInfo(typeof(TMember));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "catch (global::System.NotSupportedException exception)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "throw new global::System.InvalidOperationException(\"The generated fragment converter requires JsonTypeInfo metadata for member type '\" + typeof(TMember) + \"'. Add the model/member types to a source-generated JsonSerializerContext and set it as JsonSerializerOptions.TypeInfoResolver.\", exception);"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "public override Fragment Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "ValidateJsonNames(options);");
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) { throw new global::System.Text.Json.JsonException(\"A fragment must be a JSON object.\"); }"
        );
        code.AppendLineAt(4, "var builder = new FragmentBuilder();");
        if (utf8Dispatch)
            code.AppendLineAt(
                4,
                "var useUtf8Names = options.PropertyNamingPolicy is null && !options.PropertyNameCaseInsensitive;"
            );
        code.AppendLineAt(4, "while (reader.Read())");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) { return builder.Build(); }"
        );
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) { throw new global::System.Text.Json.JsonException(\"Expected a fragment property name.\"); }"
        );
        if (utf8Dispatch)
        {
            code.AppendIndent(5).Append("var propertyIndex = !useUtf8Names ? -2 : ");
            // Match visible members before ignored members, as in the string dispatch below.
            foreach (
                var member in members
                    .Where(static m => !m.Property.IsJsonIgnored)
                    .Concat(members.Where(static m => m.Property.IsJsonIgnored))
            )
            {
                var index = members.IndexOf(member);
                code.Append("reader.ValueTextEquals(__jsonName")
                    .Append(index.ToString())
                    .Append(") ? ")
                    .Append(index.ToString())
                    .Append(" : ");
            }
            code.AppendLine("-1;");
            code.AppendLineAt(
                5,
                "var propertyName = propertyIndex < 0 ? reader.GetString() : null;"
            );
        }
        else
            code.AppendLineAt(5, "var propertyName = reader.GetString();");
        code.AppendLineAt(
            5,
            "if (!reader.Read()) { throw new global::System.Text.Json.JsonException(\"Unexpected end of fragment.\"); }"
        );
        var jsonMembers = members
            .Where(static member => !member.Property.IsJsonIgnored)
            .ToImmutableArray();
        var ignoredMembers = members
            .Where(static member => member.Property.IsJsonIgnored)
            .ToImmutableArray();
        if (jsonMembers.Length + ignoredMembers.Length > 0)
        {
            var first = true;
            foreach (var member in jsonMembers)
            {
                var property = SparseNaming.EscapeIdentifier(member.Property.Name);
                var wireName = WireName(member);
                var explicitName = member.Property.HasExplicitJsonPropertyName;
                code.AppendIndent(5).Append(first ? "if (" : "else if (");
                if (utf8Dispatch)
                    code.Append("propertyIndex == ")
                        .Append(members.IndexOf(member).ToString())
                        .Append(" || (propertyIndex == -2 && ");
                code.Append("Matches(propertyName, ")
                    .Append(SymbolDisplay.FormatLiteral(wireName, true))
                    .Append(", ")
                    .Append(explicitName ? "false" : "true")
                    .AppendLine(utf8Dispatch ? ", options)))" : ", options))");
                if (member.ChildModel is null)
                {
                    code.AppendIndent(6)
                        .Append("builder.")
                        .Append(property)
                        .Append(" = " + optional + "<")
                        .Append(FragmentValueType(member))
                        .Append(
                            ">.Present(global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                        )
                        .Append(FragmentValueType(member))
                        .AppendLine(">(options)));");
                }
                else
                {
                    var childFragment = member.ChildFragmentType!;
                    code.AppendIndent(6)
                        .Append("builder.")
                        .Append(property)
                        .Append(" = " + optional + "<")
                        .Append(FragmentValueType(member))
                        .Append(
                            ">.Present(reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : "
                        )
                        .Append(childFragment)
                        .AppendLine(
                            ".JsonConverter.Read(ref reader, typeof("
                                + childFragment
                                + "), options));"
                        );
                }
                first = false;
            }

            foreach (var ignoredMember in ignoredMembers)
            {
                var ignored = ignoredMember.Property;
                var ignoredWireName = ignored.JsonPropertyName ?? ignored.Name;
                code.AppendIndent(5).Append(first ? "if (" : "else if (");
                if (utf8Dispatch)
                    code.Append("propertyIndex == ")
                        .Append(members.IndexOf(ignoredMember).ToString())
                        .Append(" || (propertyIndex == -2 && ");
                code.Append("Matches(propertyName, ")
                    .Append(SymbolDisplay.FormatLiteral(ignoredWireName, true))
                    .Append(", ")
                    .Append(ignored.HasExplicitJsonPropertyName ? "false" : "true")
                    .AppendLine(utf8Dispatch ? ", options)))" : ", options))");
                code.AppendLineAt(6, "{ reader.Skip(); }");
                first = false;
            }

            code.AppendLineAt(
                5,
                "else HandleUnknownFragmentProperty(ref reader, options, propertyName);"
            );
        }
        else
        {
            code.AppendLineAt(
                5,
                "HandleUnknownFragmentProperty(ref reader, options, propertyName);"
            );
        }

        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "throw new global::System.Text.Json.JsonException(\"Unexpected end of fragment.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, Fragment value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "writer.WriteStartObject();");
        foreach (var member in jsonMembers)
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            var wireName = WireName(member);
            var explicitName = member.Property.HasExplicitJsonPropertyName;
            code.AppendIndent(4).Append("if (value.").Append(property).AppendLine(".IsPresent)");
            code.AppendLineAt(4, "{");
            var conditionalIndent = 5;
            if (member.Property.IsJsonIgnoreWhenWritingNull)
            {
                code.AppendIndent(5)
                    .Append("if (value.")
                    .Append(property)
                    .AppendLine(".Value is not null)");
                code.AppendLineAt(5, "{");
                conditionalIndent = 6;
            }
            else if (member.Property.IsJsonIgnoreWhenWritingDefault)
            {
                code.AppendIndent(5)
                    .Append(
                        "if (!global::System.Collections.Generic.EqualityComparer<"
                            + FragmentValueType(member)
                            + ">.Default.Equals(value."
                    )
                    .Append(property)
                    .AppendLine(".Value!, default))");
                code.AppendLineAt(5, "{");
                conditionalIndent = 6;
            }
            code.AppendIndent(conditionalIndent)
                .Append("var jsonPropertyName = ")
                .Append(
                    explicitName
                        ? SymbolDisplay.FormatLiteral(wireName, true)
                        : "options.PropertyNamingPolicy?.ConvertName("
                            + SymbolDisplay.FormatLiteral(wireName, true)
                            + ") ?? "
                            + SymbolDisplay.FormatLiteral(wireName, true)
                )
                .AppendLine(";");
            code.AppendLineAt(conditionalIndent, "writer.WritePropertyName(jsonPropertyName);");

            if (member.ChildModel is null)
            {
                code.AppendIndent(conditionalIndent)
                    .Append("global::System.Text.Json.JsonSerializer.Serialize<")
                    .Append(FragmentValueType(member))
                    .Append(">(writer, value.")
                    .Append(property)
                    .Append(".Value!, GetMemberTypeInfo<")
                    .Append(FragmentValueType(member))
                    .AppendLine(">(options));");
            }
            else
            {
                var childFragment = member.ChildFragmentType!;
                code.AppendIndent(conditionalIndent)
                    .Append("if (value.")
                    .Append(property)
                    .AppendLine(".Value is null)");
                code.AppendLineAt(conditionalIndent, "{ writer.WriteNullValue(); }");
                code.AppendIndent(conditionalIndent).AppendLine("else");
                code.AppendLineAt(conditionalIndent, "{");
                code.AppendIndent(conditionalIndent + 1)
                    .Append(childFragment)
                    .Append(".JsonConverter.Write(writer, value.")
                    .Append(property)
                    .AppendLine(".Value, options);");
                code.AppendLineAt(conditionalIndent, "}");
            }
            if (conditionalIndent > 5)
            {
                code.AppendLineAt(5, "}");
            }
            code.AppendLineAt(4, "}");
        }

        code.AppendLineAt(4, "writer.WriteEndObject();");
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "private static bool Matches(string? actual, string propertyName, bool useNamingPolicy, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (actual is null) { return false; }");
        code.AppendLineAt(
            4,
            "var expected = useNamingPolicy ? options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName : propertyName;"
        );
        code.AppendLineAt(
            4,
            "return global::System.String.Equals(actual, expected, options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLine();
        code.AppendLineAt(
            3,
            "private static void HandleUnknownFragmentProperty(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options, string? propertyName)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (options.UnmappedMemberHandling == global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow) { throw new global::System.Text.Json.JsonException(\"The JSON property '\" + propertyName + \"' could not be mapped to fragment '\" + typeof(Fragment) + \"'.\"); }"
        );
        code.AppendLineAt(4, "reader.Skip();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private static void ValidateJsonNames(global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(3, "{");
        var comparePairs = jsonMembers.Length <= 3;
        if (!comparePairs)
        {
            code.AppendLineAt(
                4,
                "var names = new global::System.Collections.Generic.HashSet<string>(options.PropertyNameCaseInsensitive ? global::System.StringComparer.OrdinalIgnoreCase : global::System.StringComparer.Ordinal);"
            );
        }
        else if (jsonMembers.Length > 1)
        {
            code.AppendLineAt(
                4,
                "var comparison = options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal;"
            );
        }
        else
        {
            // Preserve the options requirement even with no names to compare.
            code.AppendLineAt(4, "_ = options.PropertyNameCaseInsensitive;");
        }
        var nameIndex = 0;
        foreach (var property in jsonMembers.Select(static member => member.Property))
        {
            var literal = SymbolDisplay.FormatLiteral(
                property.JsonPropertyName ?? property.Name,
                true
            );
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            if (!comparePairs)
            {
                code.AppendLineAt(
                    4,
                    "if (!names.Add("
                        + expression
                        + ")) { throw new global::System.Text.Json.JsonException(\"Multiple fragment members map to the same JSON property name.\"); }"
                );
            }
            else if (jsonMembers.Length == 1)
            {
                // Retain naming-policy evaluation even when collisions are impossible.
                code.AppendLineAt(4, "_ = " + expression + ";");
            }
            else
            {
                code.AppendLineAt(4, "var __name_" + nameIndex + " = " + expression + ";");
                for (var previous = 0; previous < nameIndex; previous++)
                {
                    code.AppendLineAt(
                        4,
                        "if (global::System.String.Equals(__name_"
                            + previous
                            + ", __name_"
                            + nameIndex
                            + ", comparison)) { throw new global::System.Text.Json.JsonException(\"Multiple fragment members map to the same JSON property name.\"); }"
                    );
                }
                nameIndex++;
            }
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }
}
