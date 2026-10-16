using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator.Shared;

/// <summary>Probes the consuming compilation for usable System.Text.Json APIs.</summary>
/// <remarks>Symbol-based detection: every answer comes from <see cref="Compilation.GetTypeByMetadataName"/> plus constructor.</remarks>
internal static class SparseStjCapabilityDetector
{
    /// <summary>Probes one compilation for the JSON surface usable by generated code.</summary>
    /// <param name="compilation">The consuming compilation.</param>
    /// <returns>The capability snapshot for this compilation.</returns>
    public static SparseStjCapabilities Probe(Compilation compilation)
    {
        var serializer = SafeGet(compilation, "System.Text.Json.JsonSerializer");
        var converter = SafeGet(compilation, "System.Text.Json.Serialization.JsonConverter`1");
        var hasSystemTextJson =
            serializer is not null
            && converter is not null
            && IsUsableType(serializer, compilation)
            && IsUsableType(converter, compilation);
        if (!hasSystemTextJson)
            return SparseStjCapabilities.None;

        var reader = SafeGet(compilation, "System.Text.Json.Utf8JsonReader");
        var writer = SafeGet(compilation, "System.Text.Json.Utf8JsonWriter");
        var options = SafeGet(compilation, "System.Text.Json.JsonSerializerOptions");
        var genericTypeInfo = SafeGet(
            compilation,
            "System.Text.Json.Serialization.Metadata.JsonTypeInfo`1"
        );
        var typeInfo = SafeGet(compilation, "System.Text.Json.Serialization.Metadata.JsonTypeInfo");
        var hasTypeInfo =
            serializer is not null
            && options is not null
            && reader is not null
            && writer is not null
            && genericTypeInfo is not null
            && typeInfo is not null
            && genericTypeInfo.TypeParameters.Length == 1
            && genericTypeInfo.IsAccessibleTo(compilation)
            && typeInfo.IsAccessibleTo(compilation)
            && HasBaseType(genericTypeInfo, typeInfo)
            && HasJsonTypeInfoLookup(compilation, options, typeInfo)
            && HasSerializerTypeInfoOverloads(
                compilation,
                serializer,
                reader,
                writer,
                genericTypeInfo,
                typeInfo
            );

        return new SparseStjCapabilities(
            HasSystemTextJson: true,
            HasJsonPropertyName: HasAttributeWithStringCtor(
                compilation,
                "System.Text.Json.Serialization.JsonPropertyNameAttribute"
            ),
            HasJsonPropertyOrder: HasAttributeWithIntCtor(
                compilation,
                "System.Text.Json.Serialization.JsonPropertyOrderAttribute"
            ),
            HasJsonIgnore: HasJsonIgnore(compilation),
            HasJsonPolymorphic: HasPolymorphic(compilation),
            HasJsonDerivedType: HasDerivedType(compilation),
            HasUnmappedMemberHandling: HasUnmappedMemberHandling(compilation),
            HasJsonConverter: HasJsonConverter(compilation, converter),
            HasUtf8JsonReaderWriter: HasUtf8Surface(compilation, reader, writer, options),
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

    private static bool IsUsableType(INamedTypeSymbol symbol, Compilation compilation) =>
        symbol.TypeKind == TypeKind.Class && symbol.IsAccessibleTo(compilation);

    private static bool HasAttributeWithStringCtor(Compilation compilation, string metadataName) =>
        HasAttributeCtor(
            compilation,
            metadataName,
            [compilation.GetSpecialType(SpecialType.System_String)]
        );

    private static bool HasAttributeWithIntCtor(Compilation compilation, string metadataName) =>
        HasAttributeCtor(
            compilation,
            metadataName,
            [compilation.GetSpecialType(SpecialType.System_Int32)]
        );

    private static bool HasAttributeCtor(
        Compilation compilation,
        string metadataName,
        ITypeSymbol[] parameterTypes
    )
    {
        var attribute = SafeGet(compilation, metadataName);
        if (!IsUsableAttribute(attribute, compilation))
            return false;
        foreach (var ctor in attribute!.InstanceConstructors)
        {
            if (
                ctor.IsAccessibleTo(compilation)
                && ctor.Parameters.Length == parameterTypes.Length
                && ParametersMatch(ctor.Parameters, parameterTypes)
            )
                return true;
        }

        return false;
    }

    private static bool HasJsonIgnore(Compilation compilation)
    {
        var attribute = SafeGet(compilation, "System.Text.Json.Serialization.JsonIgnoreAttribute");
        var condition = SafeGet(compilation, "System.Text.Json.Serialization.JsonIgnoreCondition");
        if (
            !IsUsableAttribute(attribute, compilation)
            || condition is null
            || condition.TypeKind != TypeKind.Enum
            || !condition.IsAccessibleTo(compilation)
        )
            return false;
        if (
            !HasEnumMember(condition, "WhenWritingNull", compilation)
            || !HasEnumMember(condition, "WhenWritingDefault", compilation)
        )
            return false;
        return HasAttributeCtor(
                compilation,
                "System.Text.Json.Serialization.JsonIgnoreAttribute",
                []
            ) && HasWritableAttributeProperty(attribute!, compilation, "Condition", condition);
    }

    private static bool HasPolymorphic(Compilation compilation)
    {
        var attribute = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonPolymorphicAttribute"
        );
        return IsUsableAttribute(attribute, compilation)
            && HasAttributeCtor(
                compilation,
                "System.Text.Json.Serialization.JsonPolymorphicAttribute",
                []
            )
            && HasWritableAttributeProperty(
                attribute!,
                compilation,
                "TypeDiscriminatorPropertyName",
                compilation.GetSpecialType(SpecialType.System_String)
            );
    }

    private static bool HasDerivedType(Compilation compilation)
    {
        var attribute = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonDerivedTypeAttribute"
        );
        if (!IsUsableAttribute(attribute, compilation))
            return false;

        var systemType = SafeGet(compilation, "System.Type");
        return systemType is not null
            && HasAttributeCtor(
                compilation,
                "System.Text.Json.Serialization.JsonDerivedTypeAttribute",
                [systemType, compilation.GetSpecialType(SpecialType.System_String)]
            );
    }

    private static bool HasJsonConverter(Compilation compilation, INamedTypeSymbol? converter)
    {
        if (
            converter is null
            || converter.TypeParameters.Length != 1
            || !IsUsableType(converter, compilation)
        )
            return false;

        var attributeUsable = HasAttributeCtor(
            compilation,
            "System.Text.Json.Serialization.JsonConverterAttribute",
            [SafeGet(compilation, "System.Type")!]
        );
        var reader = SafeGet(compilation, "System.Text.Json.Utf8JsonReader");
        var writer = SafeGet(compilation, "System.Text.Json.Utf8JsonWriter");
        var options = SafeGet(compilation, "System.Text.Json.JsonSerializerOptions");
        return attributeUsable
            && reader is not null
            && writer is not null
            && options is not null
            && HasReadWriteOverrides(compilation, converter, reader, writer, options);
    }

    private static bool HasUnmappedMemberHandling(Compilation compilation)
    {
        var handling = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonUnmappedMemberHandling"
        );
        return handling is not null
            && handling.IsAccessibleTo(compilation)
            && HasEnumMember(handling, "Disallow", compilation);
    }

    private static bool HasReadWriteOverrides(
        Compilation compilation,
        INamedTypeSymbol converter,
        INamedTypeSymbol reader,
        INamedTypeSymbol writer,
        INamedTypeSymbol options
    )
    {
        var valueType = converter.TypeParameters[0];
        var foundRead = false;
        var foundWrite = false;
        foreach (var member in converter.GetMembers("Read"))
        {
            if (
                member is IMethodSymbol method
                && IsUsableMethod(method, compilation, isStatic: false, genericArity: 0)
                && (method.IsVirtual || method.IsAbstract)
                && !method.IsSealed
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, valueType)
                && method.Parameters.Length == 3
                && ParameterMatches(method.Parameters[0], reader, RefKind.Ref)
                && ParameterMatches(
                    method.Parameters[1],
                    SafeGet(compilation, "System.Type"),
                    RefKind.None
                )
                && ParameterMatches(method.Parameters[2], options, RefKind.None)
            )
                foundRead = true;
        }

        foreach (var member in converter.GetMembers("Write"))
        {
            if (
                member is IMethodSymbol method
                && IsUsableMethod(method, compilation, isStatic: false, genericArity: 0)
                && (method.IsVirtual || method.IsAbstract)
                && !method.IsSealed
                && method.ReturnsVoid
                && method.Parameters.Length == 3
                && ParameterMatches(method.Parameters[0], writer, RefKind.None)
                && ParameterMatches(method.Parameters[1], valueType, RefKind.None)
                && ParameterMatches(method.Parameters[2], options, RefKind.None)
            )
                foundWrite = true;
        }

        return foundRead && foundWrite;
    }

    private static bool HasUtf8Surface(
        Compilation compilation,
        INamedTypeSymbol? reader,
        INamedTypeSymbol? writer,
        INamedTypeSymbol? options
    )
    {
        var tokens = SafeGet(compilation, "System.Text.Json.JsonTokenType");
        var exception = SafeGet(compilation, "System.Text.Json.JsonException");
        var namingPolicy = SafeGet(compilation, "System.Text.Json.JsonNamingPolicy");
        var unmapped = SafeGet(
            compilation,
            "System.Text.Json.Serialization.JsonUnmappedMemberHandling"
        );
        if (
            reader is null
            || writer is null
            || options is null
            || tokens is null
            || exception is null
            || namingPolicy is null
            || unmapped is null
            || !reader.IsAccessibleTo(compilation)
            || !writer.IsAccessibleTo(compilation)
            || !tokens.IsAccessibleTo(compilation)
            || !options.IsAccessibleTo(compilation)
            || !exception.IsAccessibleTo(compilation)
            || !namingPolicy.IsAccessibleTo(compilation)
            || !unmapped.IsAccessibleTo(compilation)
            || tokens.TypeKind != TypeKind.Enum
            || unmapped.TypeKind != TypeKind.Enum
        )
            return false;

        var readOnlySpan = SafeGet(compilation, "System.ReadOnlySpan`1");
        var byteType = compilation.GetSpecialType(SpecialType.System_Byte);
        return readOnlySpan is not null
            && HasEnumMember(tokens, "StartObject", compilation)
            && HasEnumMember(tokens, "Null", compilation)
            && HasInstanceProperty(reader, "TokenType", tokens, compilation)
            && HasInstanceMethod(
                reader,
                "Read",
                compilation.GetSpecialType(SpecialType.System_Boolean),
                compilation,
                []
            )
            && HasInstanceMethod(
                reader,
                "GetString",
                compilation.GetSpecialType(SpecialType.System_String),
                compilation,
                []
            )
            && HasInstanceMethod(
                reader,
                "ValueTextEquals",
                compilation.GetSpecialType(SpecialType.System_Boolean),
                compilation,
                [readOnlySpan.Construct(byteType)]
            )
            && HasInstanceMethod(
                reader,
                "Skip",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                []
            )
            && HasInstanceMethod(
                writer,
                "WriteStartObject",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                []
            )
            && HasInstanceMethod(
                writer,
                "WriteEndObject",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                []
            )
            && HasInstanceMethod(
                writer,
                "WritePropertyName",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                [compilation.GetSpecialType(SpecialType.System_String)]
            )
            && HasInstanceMethod(
                writer,
                "WriteNullValue",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                []
            )
            && HasInstanceMethod(
                writer,
                "WriteString",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                [
                    compilation.GetSpecialType(SpecialType.System_String),
                    compilation.GetSpecialType(SpecialType.System_String),
                ]
            )
            && HasInstanceMethod(
                writer,
                "WriteStringValue",
                compilation.GetSpecialType(SpecialType.System_Void),
                compilation,
                [compilation.GetSpecialType(SpecialType.System_String)]
            )
            && HasInstanceProperty(options, "PropertyNamingPolicy", namingPolicy, compilation)
            && HasInstanceProperty(
                options,
                "PropertyNameCaseInsensitive",
                compilation.GetSpecialType(SpecialType.System_Boolean),
                compilation
            )
            && HasInstanceProperty(options, "UnmappedMemberHandling", unmapped, compilation)
            && HasInstanceMethod(
                namingPolicy,
                "ConvertName",
                compilation.GetSpecialType(SpecialType.System_String),
                compilation,
                [compilation.GetSpecialType(SpecialType.System_String)]
            )
            && HasConstructor(exception, compilation, [])
            && HasConstructor(
                exception,
                compilation,
                [compilation.GetSpecialType(SpecialType.System_String)]
            );
    }

    /// <summary>Whether trim-safe metadata lookup and all emitted serializer overloads are available.</summary>
    /// <remarks>Uses declared-type metadata when available.</remarks>
    private static bool HasJsonTypeInfoLookup(
        Compilation compilation,
        INamedTypeSymbol options,
        INamedTypeSymbol typeInfo
    )
    {
        var found = false;
        foreach (var member in options.GetMembers("GetTypeInfo"))
        {
            if (
                member is IMethodSymbol method
                && IsUsableMethod(method, compilation, isStatic: false, genericArity: 0)
                && method.Parameters.Length == 1
                && ParameterMatches(
                    method.Parameters[0],
                    SafeGet(compilation, "System.Type"),
                    RefKind.None
                )
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, typeInfo)
            )
                found = true;
        }

        return found;
    }

    private static bool HasSerializerTypeInfoOverloads(
        Compilation compilation,
        INamedTypeSymbol serializer,
        INamedTypeSymbol reader,
        INamedTypeSymbol writer,
        INamedTypeSymbol genericTypeInfo,
        INamedTypeSymbol typeInfo
    )
    {
        var hasGenericDeserialize = false;
        var hasGenericSerialize = false;
        var hasDeserialize = false;
        var hasSerialize = false;
        foreach (var member in serializer.GetMembers("Deserialize"))
        {
            if (member is not IMethodSymbol method || !IsUsableMethod(method, compilation, true, 1))
                continue;
            var valueType = method.TypeParameters[0];
            if (
                method.Parameters.Length == 2
                && ParameterMatches(method.Parameters[0], reader, RefKind.Ref)
                && ParameterMatches(
                    method.Parameters[1],
                    genericTypeInfo.Construct(valueType),
                    RefKind.None
                )
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, valueType)
            )
                hasGenericDeserialize = true;
        }

        foreach (var member in serializer.GetMembers("Serialize"))
        {
            if (member is not IMethodSymbol method || !IsUsableMethod(method, compilation, true, 1))
                continue;
            var valueType = method.TypeParameters[0];
            if (
                method.Parameters.Length == 3
                && ParameterMatches(method.Parameters[0], writer, RefKind.None)
                && ParameterMatches(method.Parameters[1], valueType, RefKind.None)
                && ParameterMatches(
                    method.Parameters[2],
                    genericTypeInfo.Construct(valueType),
                    RefKind.None
                )
                && method.ReturnsVoid
            )
                hasGenericSerialize = true;
        }

        foreach (var member in serializer.GetMembers("Deserialize"))
        {
            if (
                member is IMethodSymbol method
                && IsUsableMethod(method, compilation, true, 0)
                && method.Parameters.Length == 2
                && ParameterMatches(method.Parameters[0], reader, RefKind.Ref)
                && ParameterMatches(method.Parameters[1], typeInfo, RefKind.None)
                && SymbolEqualityComparer.Default.Equals(
                    method.ReturnType,
                    compilation.GetSpecialType(SpecialType.System_Object)
                )
            )
                hasDeserialize = true;
        }

        foreach (var member in serializer.GetMembers("Serialize"))
        {
            if (
                member is IMethodSymbol method
                && IsUsableMethod(method, compilation, true, 0)
                && method.Parameters.Length == 3
                && ParameterMatches(method.Parameters[0], writer, RefKind.None)
                && ParameterMatches(
                    method.Parameters[1],
                    compilation.GetSpecialType(SpecialType.System_Object),
                    RefKind.None
                )
                && ParameterMatches(method.Parameters[2], typeInfo, RefKind.None)
                && method.ReturnsVoid
            )
                hasSerialize = true;
        }

        return hasGenericDeserialize && hasGenericSerialize && hasDeserialize && hasSerialize;
    }

    private static bool HasInstanceProperty(
        INamedTypeSymbol type,
        string name,
        ITypeSymbol propertyType,
        Compilation compilation
    )
    {
        foreach (var member in type.GetMembers(name))
        {
            if (
                member is IPropertySymbol property
                && !property.IsStatic
                && property.Parameters.Length == 0
                && property.GetMethod is not null
                && property.GetMethod.IsAccessibleTo(compilation)
                && SymbolEqualityComparer.Default.Equals(property.Type, propertyType)
            )
                return true;
        }

        return false;
    }

    private static bool HasWritableAttributeProperty(
        INamedTypeSymbol attribute,
        Compilation compilation,
        string name,
        ITypeSymbol propertyType
    )
    {
        foreach (var member in attribute.GetMembers(name))
        {
            if (
                member is IPropertySymbol property
                && !property.IsStatic
                && property.Parameters.Length == 0
                && property.SetMethod is not null
                && property.SetMethod.IsAccessibleTo(compilation)
                && SymbolEqualityComparer.Default.Equals(property.Type, propertyType)
            )
                return true;
        }

        return false;
    }

    private static bool HasInstanceMethod(
        INamedTypeSymbol type,
        string name,
        ITypeSymbol returnType,
        Compilation compilation,
        ITypeSymbol[] parameterTypes
    )
    {
        foreach (var member in type.GetMembers(name))
        {
            if (
                member is IMethodSymbol method
                && IsUsableMethod(method, compilation, isStatic: false, genericArity: 0)
                && !method.ReturnsByRef
                && !method.ReturnsByRefReadonly
                && SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType)
                && ParametersMatch(method.Parameters, parameterTypes)
            )
                return true;
        }

        return false;
    }

    private static bool HasConstructor(
        INamedTypeSymbol type,
        Compilation compilation,
        ITypeSymbol[] parameterTypes
    )
    {
        foreach (var ctor in type.InstanceConstructors)
        {
            if (
                ctor.IsAccessibleTo(compilation)
                && ctor.Parameters.Length == parameterTypes.Length
                && ParametersMatch(ctor.Parameters, parameterTypes)
            )
                return true;
        }

        return false;
    }

    private static bool ParametersMatch(
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters,
        ITypeSymbol[] parameterTypes
    )
    {
        if (parameters.Length != parameterTypes.Length)
            return false;
        for (var index = 0; index < parameters.Length; index++)
        {
            if (!ParameterMatches(parameters[index], parameterTypes[index], RefKind.None))
                return false;
        }

        return true;
    }

    private static bool ParameterMatches(
        IParameterSymbol parameter,
        ITypeSymbol? type,
        RefKind refKind
    ) =>
        type is not null
        && parameter.RefKind == refKind
        && SymbolEqualityComparer.Default.Equals(parameter.Type, type);

    private static bool IsUsableMethod(
        IMethodSymbol method,
        Compilation compilation,
        bool isStatic,
        int genericArity
    ) =>
        method.MethodKind == MethodKind.Ordinary
        && method.IsStatic == isStatic
        && method.Arity == genericArity
        && method.IsAccessibleTo(compilation);

    private static bool IsUsableAttribute(INamedTypeSymbol? type, Compilation compilation)
    {
        if (
            type is null
            || type.TypeKind != TypeKind.Class
            || type.IsAbstract
            || !type.IsAccessibleTo(compilation)
        )
            return false;

        var attributeBase = SafeGet(compilation, "System.Attribute");
        if (attributeBase is null)
            return false;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, attributeBase))
                return true;
        }

        return false;
    }

    private static bool HasEnumMember(
        INamedTypeSymbol enumType,
        string name,
        Compilation compilation
    )
    {
        foreach (var member in enumType.GetMembers(name))
        {
            if (
                member is IFieldSymbol field
                && field.HasConstantValue
                && field.IsAccessibleTo(compilation)
            )
                return true;
        }

        return false;
    }

    private static bool HasBaseType(INamedTypeSymbol type, INamedTypeSymbol expectedBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBase))
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
