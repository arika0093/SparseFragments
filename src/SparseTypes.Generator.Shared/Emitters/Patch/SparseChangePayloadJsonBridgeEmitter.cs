namespace SparseTypes.Generator.Shared;

/// <summary>Emits the optional System.Text.Json bridge for ChangePayload DTOs.</summary>
/// <remarks>
/// The base runtime is serializer-agnostic (issue #242): endpoint and enum
/// types carry no JSON attributes. When the consuming compilation references a
/// compatible API set, each payload container gains closed-generic endpoint
/// converters plus an item-kind converter that preserve the v1 wire shape —
/// <c>state</c>/<c>value</c> order, explicit null values, lower-case tokens
/// from the runtime <c>ChangePayloadWire</c> constants, and strict rejection
/// of unknown states, kinds, and missing members. The converters are
/// closed-generic attribute arguments (no reflection factory) so trimming and
/// NativeAOT keep working.
/// </remarks>
internal static class SparseChangePayloadJsonBridgeEmitter
{
    /// <summary>Nested generic endpoint converter name within one payload container.</summary>
    internal const string EndpointConverterName = "ChangePayloadEndpointJsonConverter";

    /// <summary>Nested item-kind converter name within one payload container.</summary>
    internal const string ItemKindConverterName = "ChangePayloadItemKindJsonConverter";

    /// <summary>Emits the bridge converter types into the payload container.</summary>
    /// <param name="code">Container target builder (indented one level inside the container).</param>
    /// <param name="dialect">Owning patch dialect for runtime type names.</param>
    internal static void AppendBridgeConverters(
        SharedIndentedBuilder code,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var wire = runtime + "ChangePayloadWire";
        var endpoint = runtime + "ChangePayloadEndpoint";
        var kind = runtime + "ChangePayloadItemKind";

        code.AppendLineAt(
            1,
            "/// <summary>Preserves the v1 endpoint wire shape for one value type.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>State tokens and names come from the runtime wire constants so the contract stays single-sourced.</remarks>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + EndpointConverterName
                + "<TValue> : global::System.Text.Json.Serialization.JsonConverter<"
                + endpoint
                + "<TValue>>"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override "
                + endpoint
                + "<TValue>? Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.Null) return null;"
        );
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An endpoint must be an object.\");"
        );
        code.AppendLineAt(3, "var state = default(" + runtime + "ChangePayloadState);");
        code.AppendLineAt(3, "TValue? value = default;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLineAt(4, "var name = reader.GetString();");
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Malformed endpoint.\");"
        );
        code.AppendLineAt(4, "if (name == " + wire + ".StateProperty)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "string? token = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : reader.GetString();"
        );
        code.AppendLineAt(
            5,
            "if (!"
                + wire
                + ".TryParseState(token, out state)) throw new global::System.Text.Json.JsonException(\"Unknown endpoint state.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (name == " + wire + ".ValueProperty)");
        code.AppendLineAt(4, "{");
        // Trim-safe value codec: the non-generic JsonTypeInfo overloads carry
        // no RequiresUnreferencedCode/RequiresDynamicCode annotations, unlike
        // the options-based generics, so trimming/AOT builds stay clean.
        // Metadata resolves the declared value type exactly like the previous
        // attribute-driven envelopes; source-generated contexts supply it.
        code.AppendLineAt(
            5,
            "value = (TValue?)global::System.Text.Json.JsonSerializer.Deserialize(ref reader, options.GetTypeInfo(typeof(TValue)));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // The endpoint itself stays permissive (DTO-level Disallow guards
        // members); only state/value presence and token validity are strict.
        code.AppendLineAt(5, "reader.Skip();");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        // Missing members stay defaulted exactly like the previous
        // attribute-driven envelopes: absent state reads as Missing and an
        // absent value reads as default. Unknown tokens still fail here;
        // value-carrying Missing/Null/Redacted states are rejected later by
        // endpoint validation during ToChangeSet/ToPatch conversion.
        code.AppendLineAt(
            3,
            "return new " + endpoint + "<TValue> { State = state, Value = value };"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + endpoint
                + "<TValue>? value, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (value is null) { writer.WriteNullValue(); return; }");
        code.AppendLineAt(3, "writer.WriteStartObject();");
        code.AppendLineAt(
            3,
            "writer.WriteString(" + wire + ".StateProperty, " + wire + ".ToToken(value.State));"
        );
        code.AppendLineAt(3, "writer.WritePropertyName(" + wire + ".ValueProperty);");
        // Explicit null keeps value-free envelopes byte-identical; otherwise
        // the declared-type metadata serializes exactly like before.
        code.AppendLineAt(3, "if (value.Value is null) writer.WriteNullValue();");
        code.AppendLineAt(
            3,
            "else global::System.Text.Json.JsonSerializer.Serialize(writer, (object?)value.Value!, options.GetTypeInfo(typeof(TValue)));"
        );
        code.AppendLineAt(3, "writer.WriteEndObject();");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(1, "}");
        code.AppendLine();

        code.AppendLineAt(
            1,
            "/// <summary>Preserves the lower-case v1 item-kind tokens.</summary>"
        );
        code.AppendLineAt(
            1,
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + ItemKindConverterName
                + " : global::System.Text.Json.Serialization.JsonConverter<"
                + kind
                + ">"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override "
                + kind
                + " Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "string? token = reader.TokenType == global::System.Text.Json.JsonTokenType.Null ? null : reader.GetString();"
        );
        code.AppendLineAt(
            3,
            "if (!"
                + wire
                + ".TryParseKind(token, out var kind)) throw new global::System.Text.Json.JsonException(\"Unknown item kind.\");"
        );
        code.AppendLineAt(3, "return kind;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <inheritdoc />");
        code.AppendLineAt(
            2,
            "public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
                + kind
                + " value, global::System.Text.Json.JsonSerializerOptions options) => writer.WriteStringValue("
                + wire
                + ".ToToken(value));"
        );
        code.AppendLineAt(1, "}");
        code.AppendLine();
    }

    /// <summary>Emits a bare exclude-from-JSON marker when JSON is enabled.</summary>
    /// <remarks>
    /// Computed transition projections must not serialize; the marker is
    /// omitted entirely when the probed surface lacks System.Text.Json so
    /// serializer-free compilations keep building.
    /// </remarks>
    /// <param name="code">Target builder.</param>
    /// <param name="indent">Member indent.</param>
    /// <param name="emitJson">Whether the v1 JSON surface is enabled.</param>
    internal static void AppendBareJsonIgnore(SharedIndentedBuilder code, int indent, bool emitJson)
    {
        if (!emitJson)
            return;
        code.AppendLineAt(indent, "[global::System.Text.Json.Serialization.JsonIgnore]");
    }

    /// <summary>Emits the endpoint converter attribute for one endpoint property.</summary>
    /// <param name="code">DTO target builder.</param>
    /// <param name="indent">Member indent.</param>
    /// <param name="valueType">The endpoint value type name.</param>
    internal static void AppendEndpointConverterAttribute(
        SharedIndentedBuilder code,
        int indent,
        string valueType
    ) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof("
                + EndpointConverterName
                + "<"
                + valueType
                + ">))]"
        );

    /// <summary>Emits the item-kind converter attribute for one kind property.</summary>
    /// <param name="code">DTO target builder.</param>
    /// <param name="indent">Member indent.</param>
    internal static void AppendItemKindConverterAttribute(SharedIndentedBuilder code, int indent) =>
        code.AppendLineAt(
            indent,
            "[global::System.Text.Json.Serialization.JsonConverter(typeof("
                + ItemKindConverterName
                + "))]"
        );
}
