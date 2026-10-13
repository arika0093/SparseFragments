using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Probes the consuming compilation for usable System.Text.Json APIs.</summary>
/// <remarks>
/// Symbol-based detection (issue #243): every answer comes from
/// <see cref="Compilation.GetTypeByMetadataName"/> plus constructor, property,
/// and enum-member checks against the consumer's referenced assemblies. The
/// target-framework name and the generator host's dependencies are never
/// consulted, and no assemblies are loaded. Ambiguous or broken resolution is
/// treated as unavailable so generation degrades to omission plus a diagnostic
/// instead of emitting uncompilable code.
/// </remarks>
internal static class SparseStjCapabilityDetector
{
    /// <summary>Probes one compilation for the JSON surface usable by generated code.</summary>
    /// <param name="compilation">The consuming compilation.</param>
    /// <returns>The capability snapshot for this compilation.</returns>
    public static SparseStjCapabilities Probe(Compilation compilation)
    {
        // Core presence: the serializer entry point plus the open converter
        // base. Either missing means no JSON support at all.
        var serializer = SafeGet(compilation, "System.Text.Json.JsonSerializer");
        var converter = SafeGet(compilation, "System.Text.Json.Serialization.JsonConverter`1");
        var hasSystemTextJson =
            serializer is not null && converter is not null && IsAccessible(serializer);
        if (!hasSystemTextJson)
            return SparseStjCapabilities.None;

        var hasPropertyName = HasAttributeWithStringCtor(
            compilation,
            "System.Text.Json.Serialization.JsonPropertyNameAttribute"
        );
        var hasPropertyOrder = HasAttributeWithIntCtor(
            compilation,
            "System.Text.Json.Serialization.JsonPropertyOrderAttribute"
        );
        var hasIgnore = HasJsonIgnore(compilation);
        var hasPolymorphic = HasPolymorphic(compilation);
        var hasDerivedType = HasDerivedType(compilation);
        var hasUnmapped = HasUnmappedMemberHandling(compilation);
        var hasConverter =
            converter is not null && IsAccessible(converter) && HasReadWriteOverrides(converter);
        var hasUtf8 = HasUtf8Surface(compilation);
        var hasTypeInfo = HasJsonTypeInfo(compilation);

        return new SparseStjCapabilities(
            HasSystemTextJson: true,
            HasJsonPropertyName: hasPropertyName,
            HasJsonPropertyOrder: hasPropertyOrder,
            HasJsonIgnore: hasIgnore,
            HasJsonPolymorphic: hasPolymorphic,
            HasJsonDerivedType: hasDerivedType,
            HasUnmappedMemberHandling: hasUnmapped,
            HasJsonConverter: hasConverter,
            HasUtf8JsonReaderWriter: hasUtf8,
            HasJsonTypeInfo: hasTypeInfo
        );
    }

    private static INamedTypeSymbol? SafeGet(Compilation compilation, string metadataName)
    {
        try
        {
            return compilation.GetTypeByMetadataName(metadataName);
        }
        catch
        {
            // Broken or ambiguous references must read as unavailable.
            return null;
        }
    }

    private static bool IsAccessible(INamedTypeSymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.NotApplicable;

    private static bool HasAttributeWithStringCtor(Compilation compilation, string metadataName)
    {
        var attribute = SafeGet(compilation, metadataName);
        if (attribute is null || !IsAccessible(attribute))
            return false;
        foreach (var ctor in attribute.Constructors)
        {
            if (!ctor.IsAccessibleTo(compilation))
                continue;
            var parameters = ctor.Parameters;
            if (
                parameters.Length == 1
                && parameters[0].Type.SpecialType == SpecialType.System_String
            )
                return true;
        }

        return false;
    }

    private static bool HasAttributeWithIntCtor(Compilation compilation, string metadataName)
    {
        var attribute = SafeGet(compilation, metadataName);
        if (attribute is null || !IsAccessible(attribute))
            return false;
        foreach (var ctor in attribute.Constructors)
        {
            if (!ctor.IsAccessibleTo(compilation))
                continue;
            var parameters = ctor.Parameters;
            if (
                parameters.Length == 1
                && parameters[0].Type.SpecialType == SpecialType.System_Int32
            )
                return true;
        }

        return false;
    }

    private static bool HasJsonIgnore(Compilation compilation)
    {
        // [JsonIgnore] needs both the bare form (transition projections) and
        // the conditioned form (WhenWritingNull/Default omission).
        var attribute = SafeGet(compilation, "System.Text.Json.Serialization.JsonIgnoreAttribute");
        var condition = SafeGet(compilation, "System.Text.Json.Serialization.JsonIgnoreCondition");
        if (attribute is null || condition is null)
            return false;
        if (!IsAccessible(attribute) || !IsAccessible(condition))
            return false;
        if (!HasEnumMember(condition, "WhenWritingNull"))
            return false;
        if (!HasEnumMember(condition, "WhenWritingDefault"))
            return false;
        foreach (var member in attribute.GetMembers("Condition"))
        {
            if (
                member is IPropertySymbol property
                && property.IsAccessibleTo(compilation)
                && SymbolEqualityComparer.Default.Equals(property.Type, condition)
            )
                return true;
        }

        return false;
    }

    private static bool HasPolymorphic(Compilation compilation)
    {
        // Polymorphic dispatch needs the discriminator property used by the
        // generated Change base.
        var attribute = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonPolymorphicAttribute"
        );
        if (attribute is null || !IsAccessible(attribute))
            return false;
        foreach (var member in attribute.GetMembers("TypeDiscriminatorPropertyName"))
        {
            if (member is IPropertySymbol property && property.IsAccessibleTo(compilation))
                return true;
        }

        return false;
    }

    private static bool HasDerivedType(Compilation compilation)
    {
        // Each member variant registers a (Type, discriminator) mapping.
        var attribute = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonDerivedTypeAttribute"
        );
        if (attribute is null || !IsAccessible(attribute))
            return false;
        foreach (var ctor in attribute.Constructors)
        {
            if (!ctor.IsAccessibleTo(compilation))
                continue;
            var parameters = ctor.Parameters;
            if (
                parameters.Length == 2
                && IsSystemType(parameters[0].Type)
                && parameters[1].Type.SpecialType == SpecialType.System_String
            )
                return true;
        }

        return false;
    }

    private static bool IsSystemType(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.Name == "Type"
        && named.ContainingNamespace?.ToDisplayString() == "System";

    private static bool HasUnmappedMemberHandling(Compilation compilation)
    {
        // Strict payloads reject unknown members via Disallow.
        var attribute = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute"
        );
        var handling = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonUnmappedMemberHandling"
        );
        if (attribute is null || handling is null)
            return false;
        if (!IsAccessible(attribute) || !IsAccessible(handling))
            return false;
        if (!HasEnumMember(handling, "Disallow"))
            return false;
        foreach (var ctor in attribute.Constructors)
        {
            if (!ctor.IsAccessibleTo(compilation))
                continue;
            var parameters = ctor.Parameters;
            if (
                parameters.Length == 1
                && SymbolEqualityComparer.Default.Equals(parameters[0].Type, handling)
            )
                return true;
        }

        return false;
    }

    private static bool HasReadWriteOverrides(INamedTypeSymbol converter)
    {
        // A usable JsonConverter<T> exposes overridable Read/Write seams for
        // the generated endpoint and kind converters. Roslyn reports abstract
        // seams with IsAbstract (not IsVirtual), so both count.
        var foundRead = false;
        var foundWrite = false;
        foreach (var member in converter.GetMembers("Read"))
        {
            if (member is IMethodSymbol method && (method.IsVirtual || method.IsAbstract))
                foundRead = true;
        }

        foreach (var member in converter.GetMembers("Write"))
        {
            if (member is IMethodSymbol method && (method.IsVirtual || method.IsAbstract))
                foundWrite = true;
        }

        return foundRead && foundWrite;
    }

    private static bool HasUtf8Surface(Compilation compilation)
    {
        // Hand-written converters use the reader/writer, token kinds, options,
        // and JsonException; all must resolve.
        var reader = SafeGet(compilation, "System.Text.Json.Utf8JsonReader");
        var writer = SafeGet(compilation, "System.Text.Json.Utf8JsonWriter");
        var tokens = SafeGet(compilation, "System.Text.Json.JsonTokenType");
        var options = SafeGet(compilation, "System.Text.Json.JsonSerializerOptions");
        var exception = SafeGet(compilation, "System.Text.Json.JsonException");
        if (
            reader is null
            || writer is null
            || tokens is null
            || options is null
            || exception is null
        )
            return false;
        if (
            !IsAccessible(reader)
            || !IsAccessible(writer)
            || !IsAccessible(tokens)
            || !IsAccessible(options)
            || !IsAccessible(exception)
        )
            return false;
        return HasEnumMember(tokens, "StartObject") && HasEnumMember(tokens, "Null");
    }

    /// <summary>Whether trim-safe metadata lookup is available for endpoint values.</summary>
    /// <remarks>
    /// The generated endpoint bridge resolves declared-type metadata through
    /// <c>JsonSerializerOptions.GetTypeInfo(Type)</c> so trimming and NativeAOT
    /// builds stay free of reflection fallback warnings.
    /// </remarks>
    private static bool HasJsonTypeInfo(Compilation compilation)
    {
        var generic = SafeGet(
            compilation,
            "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1"
        );
        var nonGeneric = SafeGet(
            compilation,
            "System.Text.Json.Serialization.Metadata.JsonTypeInfo"
        );
        var options = SafeGet(compilation, "System.Text.Json.JsonSerializerOptions");
        if (generic is null || nonGeneric is null || options is null)
            return false;
        if (!IsAccessible(generic) || !IsAccessible(nonGeneric) || !IsAccessible(options))
            return false;
        foreach (var member in options.GetMembers("GetTypeInfo"))
        {
            if (
                member is IMethodSymbol method
                && !method.IsGenericMethod
                && method.IsAccessibleTo(compilation)
                && method.Parameters.Length == 1
                && IsSystemType(method.Parameters[0].Type)
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, nonGeneric)
            )
                return true;
        }

        return false;
    }

    private static bool HasEnumMember(INamedTypeSymbol enumType, string name)
    {
        foreach (var member in enumType.GetMembers(name))
        {
            if (member is IFieldSymbol field && field.HasConstantValue)
                return true;
        }

        return false;
    }

    private static bool IsAccessibleTo(this ISymbol symbol, Compilation compilation)
    {
        try
        {
            return compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);
        }
        catch
        {
            return false;
        }
    }
}
