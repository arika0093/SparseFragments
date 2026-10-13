namespace SparseTypes.Generator.Shared;

/// <summary>Product-neutral System.Text.Json API surface available to generated code.</summary>
/// <remarks>
/// Probed from the consuming compilation's referenced symbols (issue #243),
/// never from the target-framework name or the generator host's own
/// dependencies. Each flag maps to emitted syntax that requires the matching
/// symbol or member signature; <see cref="SupportsV1Wire"/> gates the
/// ChangePayload version-1 contract as a whole so generation never silently
/// weakens wire validation or drifts field order, casing, discriminators, or
/// unknown-member rejection when an API is unavailable.
/// </remarks>
internal readonly record struct SparseStjCapabilities(
    bool HasSystemTextJson,
    bool HasJsonPropertyName,
    bool HasJsonPropertyOrder,
    bool HasJsonIgnore,
    bool HasJsonPolymorphic,
    bool HasJsonDerivedType,
    bool HasUnmappedMemberHandling,
    bool HasJsonConverter,
    bool HasUtf8JsonReaderWriter,
    bool HasJsonTypeInfo
)
{
    /// <summary>Full surface: every probe succeeded.</summary>
    public static SparseStjCapabilities All { get; } =
        new(
            HasSystemTextJson: true,
            HasJsonPropertyName: true,
            HasJsonPropertyOrder: true,
            HasJsonIgnore: true,
            HasJsonPolymorphic: true,
            HasJsonDerivedType: true,
            HasUnmappedMemberHandling: true,
            HasJsonConverter: true,
            HasUtf8JsonReaderWriter: true,
            HasJsonTypeInfo: true
        );

    /// <summary>No surface: no System.Text.Json reference.</summary>
    public static SparseStjCapabilities None { get; } = default;

    /// <summary>Whether the ChangePayload v1 wire contract can be emitted as specified.</summary>
    /// <remarks>
    /// V1 needs every flag: named/ordered properties, null/default omission,
    /// polymorphic discriminators, strict unknown-member rejection, endpoint
    /// converters, and the UTF-8 reader/writer surface. A partial surface must
    /// omit the JSON artifacts (with a diagnostic when explicitly requested)
    /// rather than emit code that fails to compile or silently changes the
    /// contract.
    /// </remarks>
    public bool SupportsV1Wire =>
        HasSystemTextJson
        && HasJsonPropertyName
        && HasJsonPropertyOrder
        && HasJsonIgnore
        && HasJsonPolymorphic
        && HasJsonDerivedType
        && HasUnmappedMemberHandling
        && HasJsonConverter
        && HasUtf8JsonReaderWriter
        && HasJsonTypeInfo;

    /// <summary>Names the v1-required surface missing from this snapshot.</summary>
    /// <remarks>Used for the actionable partial-surface diagnostic.</remarks>
    /// <returns>Comma-separated API names, or empty when complete.</returns>
    public string DescribeMissing()
    {
        if (SupportsV1Wire)
            return string.Empty;
        var missing = new System.Collections.Generic.List<string>();
        if (!HasSystemTextJson)
            missing.Add("System.Text.Json");
        if (!HasJsonPropertyName)
            missing.Add("JsonPropertyNameAttribute");
        if (!HasJsonPropertyOrder)
            missing.Add("JsonPropertyOrderAttribute");
        if (!HasJsonIgnore)
            missing.Add("JsonIgnoreAttribute/Condition");
        if (!HasJsonPolymorphic)
            missing.Add("JsonPolymorphicAttribute");
        if (!HasJsonDerivedType)
            missing.Add("JsonDerivedTypeAttribute");
        if (!HasUnmappedMemberHandling)
            missing.Add("JsonUnmappedMemberHandling.Disallow");
        if (!HasJsonConverter)
            missing.Add("JsonConverter<T>");
        if (!HasUtf8JsonReaderWriter)
            missing.Add("Utf8JsonReader/Writer");
        if (!HasJsonTypeInfo)
            missing.Add("JsonTypeInfo/GetTypeInfo");
        return string.Join(", ", missing);
    }
}
