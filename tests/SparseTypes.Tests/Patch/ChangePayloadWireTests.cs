using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SparseTypes.Tests.Patch;

/// <summary>Certifies the optional-JSON split.</summary>
/// <remarks>The base runtime carries no System.Text.Json reference; the v1 wire shape is implemented by generated bridge converters.</remarks>
public sealed class ChangePayloadWireTests
{
    [Test]
    public void RuntimeCarriesNoSystemTextJsonReference()
    {
        typeof(ChangePayloadEndpoint<>)
            .Assembly.GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ShouldNotContain("System.Text.Json");
    }

    [Test]
    public void EndpointAndEnumsCarryNoSerializerAttributes()
    {
        foreach (
            var attribute in typeof(ChangePayloadEndpoint<>)
                .GetCustomAttributes()
                .Concat(typeof(ChangePayloadEndpoint<string>).GetCustomAttributes())
                .Concat(typeof(ChangePayloadState).GetCustomAttributes())
                .Concat(typeof(ChangePayloadItemKind).GetCustomAttributes())
        )
        {
            (attribute.GetType().FullName ?? string.Empty).ShouldNotStartWith(
                "System.Text.Json",
                Case.Sensitive
            );
        }
    }

    [Test]
    public void WireTokensMatchVersion1()
    {
        ChangePayloadWire.Version.ShouldBe("0.1");
        ChangePayloadWire.ToToken(ChangePayloadState.Missing).ShouldBe("missing");
        ChangePayloadWire.ToToken(ChangePayloadState.Null).ShouldBe("null");
        ChangePayloadWire.ToToken(ChangePayloadState.Value).ShouldBe("value");
        ChangePayloadWire.ToToken(ChangePayloadState.Redacted).ShouldBe("redacted");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Add).ShouldBe("add");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Remove).ShouldBe("remove");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Edit).ShouldBe("edit");
        ChangePayloadWire.ToToken(ChangePayloadItemKind.Reorder).ShouldBe("reorder");

        foreach (var state in Enum.GetValues<ChangePayloadState>())
        {
            ChangePayloadWire
                .TryParseState(ChangePayloadWire.ToToken(state), out var parsedState)
                .ShouldBeTrue();
            parsedState.ShouldBe(state);
        }
        foreach (var kind in Enum.GetValues<ChangePayloadItemKind>())
        {
            ChangePayloadWire
                .TryParseKind(ChangePayloadWire.ToToken(kind), out var parsedKind)
                .ShouldBeTrue();
            parsedKind.ShouldBe(kind);
        }
        ChangePayloadWire.TryParseState("nope", out _).ShouldBeFalse();
        ChangePayloadWire.TryParseKind("nope", out _).ShouldBeFalse();
    }

    [Test]
    public void GeneratedBridgeWritesStateBeforeValue()
    {
        var before = Settings.Fragment.From(new Settings { Label = "before", RetryCount = 1 });
        var after = Settings.Fragment.From(new Settings { Label = "after", RetryCount = 1 });
        var payload = Settings
            .ChangeSet.Between(
                Optional<Settings.Fragment?>.Present(before),
                Optional<Settings.Fragment?>.Present(after)
            )
            .ToPayload();
        var json = JsonSerializer.Serialize(payload);

        // v1 order: state(0) precedes value(1) inside every endpoint.
        var state = json.IndexOf("\"state\":\"value\"", StringComparison.Ordinal);
        var value = json.IndexOf("\"value\":\"after\"", StringComparison.Ordinal);
        state.ShouldBeGreaterThanOrEqualTo(0);
        value.ShouldBeGreaterThan(state);
    }

    [Test]
    public void GeneratedPayloadContainerOwnsEndpointAndKindConverters()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var endpointConverter = container
            .GetNestedTypes()
            .SingleOrDefault(static type =>
                type
                    is {
                        IsGenericTypeDefinition: true,
                        Name: "ChangePayloadEndpointJsonConverter`1"
                    }
            );
        endpointConverter.ShouldNotBeNull();
        var kindConverter = container
            .GetNestedTypes()
            .SingleOrDefault(static type => type.Name == "ChangePayloadItemKindJsonConverter");
        kindConverter.ShouldNotBeNull();

        // Every endpoint-typed DTO property routes through the bridge, so the
        // wire no longer depends on runtime attributes.
        var endpointProperties = container
            .GetNestedTypes()
            .SelectMany(static type => type.GetProperties())
            .Where(static property =>
                property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition()
                    == typeof(ChangePayloadEndpoint<>)
            )
            .ToArray();
        endpointProperties.ShouldNotBeEmpty();
        foreach (var property in endpointProperties)
            property
                .GetCustomAttribute<JsonConverterAttribute>()
                .ShouldNotBeNull(property.DeclaringType!.Name + "." + property.Name);
    }

    [Test]
    public void BridgeRejectsUnknownStateTokens()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
            """{"state":"value","value":"kept"}""",
            options
        )!;
        endpoint.State.ShouldBe(ChangePayloadState.Value);
        endpoint.Value.ShouldBe("kept");

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
                """{"state":"nope","value":null}""",
                options
            )
        );
    }

    [Test]
    public void BridgeRoundTripsEveryEndpointState()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        // Scalar, keyed, dictionary, and nested round-trips live in
        // ChangePayloadTests; redaction round-trips live in
        foreach (var state in Enum.GetValues<ChangePayloadState>())
        {
            var token = ChangePayloadWire.ToToken(state);
            var endpoint = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>(
                "{\"state\":\"" + token + "\",\"value\":null}",
                options
            )!;
            endpoint.State.ShouldBe(state);

            var json = JsonSerializer.Serialize(endpoint, options);
            json.ShouldContain("\"state\":\"" + token + "\"");
        }
    }

    [Test]
    public void BridgeDefaultsMissingEndpointMembers()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadEndpointJsonConverter`1")
            .MakeGenericType(typeof(string));
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        var empty = JsonSerializer.Deserialize<ChangePayloadEndpoint<string>>("{}", options)!;
        empty.State.ShouldBe(ChangePayloadState.Missing);
        empty.Value.ShouldBeNull();
    }

    [Test]
    public void BridgeRoundTripsEveryItemKindAndRejectsUnknownTokens()
    {
        var container = typeof(Settings.ChangePayload).BaseType!.DeclaringType!;
        var converterType = container
            .GetNestedTypes()
            .Single(static type => type.Name == "ChangePayloadItemKindJsonConverter");
        var options = new JsonSerializerOptions();
        options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        foreach (var kind in Enum.GetValues<ChangePayloadItemKind>())
        {
            var token = ChangePayloadWire.ToToken(kind);
            var json = JsonSerializer.Serialize(kind, options);
            json.ShouldBe("\"" + token + "\"");
            JsonSerializer.Deserialize<ChangePayloadItemKind>(json, options).ShouldBe(kind);
        }

        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadItemKind>("\"bogus\"", options)
        );
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<ChangePayloadItemKind>("null", options)
        );
    }
}
