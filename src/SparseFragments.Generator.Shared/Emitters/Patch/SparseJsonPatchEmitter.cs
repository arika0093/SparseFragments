using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared JSON converter and JSON Patch bridge emission.</summary>
internal static class SparseJsonPatchEmitter
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

    /// <summary>Emits the fragment JSON converter used by the standalone interop bridge.</summary>
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
                code.AppendIndent(5)
                    .Append(first ? "if (" : "else if (")
                    .Append("Matches(propertyName, ")
                    .Append(SymbolDisplay.FormatLiteral(wireName, true))
                    .Append(", ")
                    .Append(explicitName ? "false" : "true")
                    .AppendLine(", options))");
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

            foreach (var ignored in ignoredMembers.Select(static member => member.Property))
            {
                var ignoredWireName = ignored.JsonPropertyName ?? ignored.Name;
                code.AppendIndent(5)
                    .Append(first ? "if (" : "else if (")
                    .Append("Matches(propertyName, ")
                    .Append(SymbolDisplay.FormatLiteral(ignoredWireName, true))
                    .Append(", ")
                    .Append(ignored.HasExplicitJsonPropertyName ? "false" : "true")
                    .AppendLine(", options))");
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
        code.AppendLineAt(
            4,
            "var names = new global::System.Collections.Generic.HashSet<string>(options.PropertyNameCaseInsensitive ? global::System.StringComparer.OrdinalIgnoreCase : global::System.StringComparer.Ordinal);"
        );
        foreach (var property in jsonMembers.Select(static member => member.Property))
        {
            var literal = SymbolDisplay.FormatLiteral(
                property.JsonPropertyName ?? property.Name,
                true
            );
            var expression = property.HasExplicitJsonPropertyName
                ? literal
                : "options.PropertyNamingPolicy?.ConvertName(" + literal + ") ?? " + literal;
            code.AppendLineAt(
                4,
                "if (!names.Add("
                    + expression
                    + ")) { throw new global::System.Text.Json.JsonException(\"Multiple fragment members map to the same JSON property name.\"); }"
            );
        }
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits private fragment/JSON helpers used by the Patch bridge.</summary>
    /// <remarks>
    /// Conversion goes through the generated fragment converter directly so no
    /// <c>JsonTypeInfo</c> metadata is ever required for the fragment itself;
    /// scalar and collection members still use the supplied options resolver.
    /// Fragment JSON is written to a UTF-8 buffer and parsed into a
    /// <c>JsonNode</c> without materializing an intermediate string, and nodes
    /// are written back to UTF-8 consumed directly by <c>Utf8JsonReader</c>
    /// rather than round-tripping through <c>ToJsonString()</c>, so no
    /// UTF-8/string re-encoding occurs on either path.
    /// The reflection fallback is suppressed for trimming and NativeAOT and is
    /// guarded by <c>JsonSerializer.IsReflectionEnabledByDefault</c> (available
    /// in System.Text.Json 8 and later, which the JsonPatch runtime requires),
    /// so NativeAOT applications fail fast with a clear message instead of
    /// reaching runtime code generation.
    /// </remarks>
    public static void AppendFragmentJsonHelpers(
        SharedIndentedBuilder code,
        string runtime,
        string optional
    )
    {
        // UnconditionalSuppressMessageAttribute only exists on modern TFMs
        // (netstandard2.0/netstandard2.1/net48 consumers cannot resolve it, and
        // trim/AOT analysis only gates modern publishes), so guard the emission.
        code.AppendLineAt(2, "#if NET5_0_OR_GREATER");
        code.AppendLineAt(
            2,
            "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\", \"IL2026\", Justification = \"The reflection resolver is created only when reflection-based serialization is enabled. NativeAOT applications must supply a source-generated resolver, which bypasses this branch.\")]"
        );
        code.AppendLineAt(
            2,
            "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Aot\", \"IL3050\", Justification = \"The reflection resolver is created only when reflection-based serialization is enabled. NativeAOT applications must supply a source-generated resolver, which bypasses this branch.\")]"
        );
        code.AppendLineAt(2, "#endif");
        code.AppendLineAt(
            2,
            "private static global::System.Text.Json.JsonSerializerOptions __EffectiveOptions(global::System.Text.Json.JsonSerializerOptions? options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var effective = options is null ? new global::System.Text.Json.JsonSerializerOptions() : new global::System.Text.Json.JsonSerializerOptions(options);"
        );
        code.AppendLineAt(3, "if (effective.TypeInfoResolver is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!global::System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "throw new global::System.InvalidOperationException(\"Patch JSON serialization requires JsonSerializerOptions.TypeInfoResolver when reflection is disabled. Supply a source-generated JsonSerializerContext covering the scalar/collection member types used by the generated fragment converter.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "effective.TypeInfoResolver = new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return effective;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static global::System.Text.Json.Nodes.JsonNode? __SerializeFragmentToNode("
                + optional
                + "<Fragment?> fragment, global::System.Text.Json.JsonSerializerOptions options, out bool isAbsent)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (!fragment.IsPresent) { isAbsent = true; return null; }");
        code.AppendLineAt(3, "isAbsent = false;");
        code.AppendLineAt(3, "if (fragment.Value is null) return null;");
        code.AppendLineAt(3, "using var stream = new global::System.IO.MemoryStream();");
        code.AppendLineAt(
            3,
            "using (var writer = new global::System.Text.Json.Utf8JsonWriter(stream))"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "new Fragment.FragmentJsonConverter().Write(writer, fragment.Value, options);"
        );
        code.AppendLineAt(4, "writer.Flush();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "stream.Position = 0;");
        code.AppendLineAt(
            3,
            "return global::System.Text.Json.Nodes.JsonNode.Parse(stream);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static Fragment? __DeserializeFragmentNode(global::System.Text.Json.Nodes.JsonNode node, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var strict = new global::System.Text.Json.JsonSerializerOptions(options) { UnmappedMemberHandling = global::System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };"
        );
        code.AppendLineAt(
            3,
            "using var stream = new global::System.IO.MemoryStream();"
        );
        code.AppendLineAt(
            3,
            "using (var writer = new global::System.Text.Json.Utf8JsonWriter(stream))"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "node.WriteTo(writer);");
        code.AppendLineAt(4, "writer.Flush();");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "global::System.ArraySegment<byte> buffer;"
        );
        code.AppendLineAt(
            3,
            "if (!stream.TryGetBuffer(out buffer)) { buffer = new global::System.ArraySegment<byte>(stream.ToArray()); }"
        );
        code.AppendLineAt(
            3,
            "var reader = new global::System.Text.Json.Utf8JsonReader(new global::System.ReadOnlySpan<byte>(buffer.Array!, buffer.Offset, buffer.Count));"
        );
        code.AppendLineAt(
            3,
            "if (!reader.Read()) { throw new "
                + runtime
                + ".JsonPatchException("
                + runtime
                + ".JsonPatchErrorKind.DeserializationFailed, \"The patched JSON could not be read as a fragment.\"); }"
        );
        code.AppendLineAt(3, "try");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "return new Fragment.FragmentJsonConverter().Read(ref reader, typeof(Fragment), strict);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "catch (global::System.Text.Json.JsonException exception)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var kind = exception.Message.Contains(\"could not be mapped\") ? "
                + runtime
                + ".JsonPatchErrorKind.UnmappedProperty : "
                + runtime
                + ".JsonPatchErrorKind.DeserializationFailed;"
        );
        code.AppendLineAt(
            4,
            "throw new " + runtime + ".JsonPatchException(kind, exception.Message, exception);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static global::System.StringComparison __PropertyNameComparison(global::System.Text.Json.JsonSerializerOptions options) => options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal;"
        );
    }

    /// <summary>Emits Patch.FromJsonPatch overloads against the generated semantic patch.</summary>
    /// <remarks>Single bridge for both generators; pass the product Between helper as <paramref name="betweenCall"/>.</remarks>
    public static void AppendFromJsonPatch(
        SharedIndentedBuilder code,
        string runtime,
        string facade,
        string optional,
        string jsonPrefix,
        string betweenCall
    )
    {
        var baselineType = optional + "<Fragment?>";
        var fromJsonPatch = jsonPrefix + "FromJsonPatch";
        code.AppendLineAt(
            2,
            "/// <summary>Imports an RFC 6902 JSON Patch document relative to a sparse baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static Patch "
                + fromJsonPatch
                + "("
                + baselineType
                + " baseline, System.ReadOnlyMemory<byte> jsonPatch, global::System.Text.Json.JsonSerializerOptions? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var effective = __EffectiveOptions(options);");
        code.AppendLineAt(
            3,
            "var baselineNode = __SerializeFragmentToNode(baseline, effective, out var baselineIsAbsent);"
        );
        code.AppendLineAt(
            3,
            "var appliedNode = "
                + SparseWellKnownNames.JsonPatchBridgeType
                + ".Apply(baselineNode, baselineIsAbsent, jsonPatch, __PropertyNameComparison(effective), out var appliedIsAbsent);"
        );
        code.AppendLineAt(3, baselineType + " result;");
        code.AppendLineAt(
            3,
            "if (appliedIsAbsent) { result = " + optional + "<Fragment?>.Missing; }"
        );
        code.AppendLineAt(
            3,
            "else if (appliedNode is null) { result = " + optional + "<Fragment?>.Present(null); }"
        );
        code.AppendLineAt(
            3,
            "else { result = "
                + optional
                + "<Fragment?>.Present(__DeserializeFragmentNode(appliedNode, effective)); }"
        );
        code.AppendLineAt(3, "return " + betweenCall + "(baseline, result);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Imports an RFC 6902 JSON Patch document relative to a present baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static Patch "
                + fromJsonPatch
                + "(Fragment baseline, System.ReadOnlyMemory<byte> jsonPatch, global::System.Text.Json.JsonSerializerOptions? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (baseline is null) throw new global::System.ArgumentNullException(nameof(baseline));"
        );
        code.AppendLineAt(
            3,
            "return "
                + fromJsonPatch
                + "("
                + optional
                + "<Fragment?>.Present(baseline), jsonPatch, options);"
        );
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits Patch.ToJsonPatch overloads exporting a semantically equivalent document.</summary>
    /// <remarks>Single bridge for both generators; pass the product apply expression as <paramref name="applyExpression"/>.</remarks>
    public static void AppendToJsonPatch(
        SharedIndentedBuilder code,
        string runtime,
        string facade,
        string optional,
        string jsonPrefix,
        string applyExpression
    )
    {
        var baselineType = optional + "<Fragment?>";
        var toJsonPatch = jsonPrefix + "ToJsonPatch";
        code.AppendLineAt(
            2,
            "private static readonly global::System.ReadOnlyMemory<byte> __sparse_empty_json_patch = new byte[] { 91, 93 };"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Exports a semantically equivalent RFC 6902 JSON Patch document.</summary>"
        );
        code.AppendLineAt(
            2,
            "public System.ReadOnlyMemory<byte> "
                + toJsonPatch
                + "("
                + baselineType
                + " baseline, global::System.Text.Json.JsonSerializerOptions? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (__SparseIsEmpty()) return __sparse_empty_json_patch;");
        code.AppendLineAt(3, "var effective = __EffectiveOptions(options);");
        code.AppendLineAt(3, "var result = " + applyExpression + ";");
        code.AppendLineAt(
            3,
            "var beforeNode = __SerializeFragmentToNode(baseline, effective, out var beforeIsAbsent);"
        );
        code.AppendLineAt(
            3,
            "var afterNode = __SerializeFragmentToNode(result, effective, out var afterIsAbsent);"
        );
        code.AppendLineAt(
            3,
            "return "
                + SparseWellKnownNames.JsonPatchBridgeType
                + ".Diff(beforeNode, beforeIsAbsent, afterNode, afterIsAbsent);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Exports a semantically equivalent RFC 6902 JSON Patch document.</summary>"
        );
        code.AppendLineAt(
            2,
            "public System.ReadOnlyMemory<byte> "
                + toJsonPatch
                + "(Fragment baseline, global::System.Text.Json.JsonSerializerOptions? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (baseline is null) throw new global::System.ArgumentNullException(nameof(baseline));"
        );
        code.AppendLineAt(
            3,
            "return " + toJsonPatch + "(" + optional + "<Fragment?>.Present(baseline), options);"
        );
        code.AppendLineAt(2, "}");
    }
}
