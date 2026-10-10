using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Key shape of a discovered structural key.</summary>
internal enum SparseKeyKind
{
    None,

    /// <summary>One property marked with parameterless <c>[SparseKey]</c>.</summary>
    Property,
}

/// <summary>Validated key metadata for a structural element type.</summary>
/// <remarks>
/// <see cref="PropertyNames"/> holds the single key property and
/// <see cref="KeyTypeName"/> is the declared property type. The key type may
/// itself be a tuple or value object for composite identity; comparison uses
/// <c>EqualityComparer&lt;TKey&gt;.Default</c> on the whole key value.
/// <see cref="TemporaryKeyProperty"/> holds the <c>Guid?</c> temporary-identity
/// property when the element opts in, or null otherwise.
/// </remarks>
internal sealed record SparseKeyInfo(
    SparseKeyKind Kind,
    ImmutableArray<string> PropertyNames,
    string KeyTypeName,
    string? UnassignedKeyExpression = null,
    string? TemporaryKeyProperty = null
);

/// <summary>Discovers and validates SparseFragments key metadata on element types.</summary>
/// <remarks>
/// Exactly one property marked with parameterless <c>[SparseKey]</c> declares
/// the identity of a structural element type. Type-level <c>[SparseKey]</c>
/// usage is invalid and reported as <c>SPF014</c>; the C# compiler additionally
/// rejects it because the attribute only targets properties. Validation is
/// strict: any invalid shape yields diagnostics and no key (callers must fail
/// generation for the affected model rather than silently selecting another
/// key source).
/// </remarks>
internal static class SparseKeyAnalyzer
{
    /// <summary>Determines whether an element type declares any key metadata.</summary>
    public static bool HasKeyDeclaration(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return FindKeyProperties(element, config, cancellationToken).Count > 0
            || HasTypeLevelAttribute(element, config, cancellationToken);
    }

    /// <summary>Strictly discovers the usable key of an element type, if any.</summary>
    /// <remarks>
    /// Returns <c>false</c> when no valid single-property key applies or when
    /// the declaration is invalid (multiple marks, type-level usage, bad
    /// shapes, unusable key types). Use <see cref="CollectDiagnostics"/> to
    /// report the corresponding diagnostics.
    /// </remarks>
    public static bool TryGetKeyInfo(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out SparseKeyInfo? info
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        info = null;
        if (HasTypeLevelAttribute(element, config, cancellationToken))
        {
            return false;
        }

        var marks = FindKeyProperties(element, config, cancellationToken);
        if (marks.Count != 1)
        {
            return false;
        }

        var (property, attribute) = marks[0];
        if (HasAttributeArguments(attribute))
        {
            return false;
        }

        if (!IsPubliclyReadableInstance(property))
        {
            return false;
        }

        if (IsCollectionShaped(property.Type, cancellationToken))
        {
            return false;
        }

        if (!TryGetUnassignedExpression(attribute, property.Type, out var sentinel, out _))
        {
            return false;
        }

        info = new SparseKeyInfo(
            SparseKeyKind.Property,
            ImmutableArray.Create(property.Name),
            NonNullableTypeName(property.Type),
            sentinel,
            TemporaryKeyProperty(element, config, cancellationToken)
        );
        return true;
    }

    /// <summary>Collects key-shape diagnostics for an element type.</summary>
    /// <remarks>
    /// Returns an empty array when the element has no key declaration at all
    /// (callers then report the configured unkeyed-sequence diagnostic for
    /// structural sequences) or when its single property key is fully valid.
    /// Any invalid shape yields one or more configured key-shape diagnostics
    /// and no key.
    /// </remarks>
    public static ImmutableArray<SparseGeneratorDiagnostic> CollectDiagnostics(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var marks = FindKeyProperties(element, config, cancellationToken);
        var typeAttributes = FindTypeLevelAttributes(element, config, cancellationToken);
        if (marks.Count == 0 && typeAttributes.Count == 0)
        {
            return ImmutableArray<SparseGeneratorDiagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        foreach (var attribute in typeAttributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.InvalidKeyAttributeShape,
                    AttributeLocation(attribute, element),
                    element.Name
                )
            );
        }

        if (marks.Count > 1)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.MultiplePropertyKeys,
                    element.Locations.FirstOrDefault(),
                    element.Name
                )
            );
        }

        for (var index = 0; index < marks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (property, attribute) = marks[index];
            if (SparseModelDiscovery.IsSparseIgnored(property, config))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.SparseIgnoreOnKey,
                        property.Locations.FirstOrDefault(),
                        property.Name
                    )
                );
            }

            if (HasAttributeArguments(attribute))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidKeyAttributeShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
                continue;
            }

            if (!TryGetUnassignedExpression(attribute, property.Type, out _, out var sentinelError))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        "SPF024",
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name + ": " + sentinelError
                    )
                );
                continue;
            }

            // Only validate the property itself when it is the single
            // declaration; multiple marks already explain the failure.
            if (marks.Count != 1 || typeAttributes.Count != 0)
            {
                continue;
            }

            if (!IsPubliclyReadableInstance(property))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InaccessibleKeyProperty,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
                continue;
            }

            if (IsCollectionShaped(property.Type, cancellationToken))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.UnsupportedKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                );
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// Validates keys for keyed-sequence candidates reachable from root members (root
    /// members plus transitively promoted nested models) and returns diagnostics that
    /// must fail generation of the analyzed root.
    /// </summary>
    /// <remarks>
    /// The generic unkeyed-sequence diagnostic is still owned by
    /// <see cref="SparseModelDiagnostics"/> for root members; this helper only reports
    /// specific key-shape errors so callers can prefer them.
    /// </remarks>
    public static ImmutableArray<SparseGeneratorDiagnostic> CollectNestedKeyDiagnostics(
        ImmutableArray<SparseSymbolMemberModel> rootMembers,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var promoted in SparsePromotedDiscovery.CollectPromotedTypes(
                rootMembers,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (
                var member in SparseModelDiscovery.GetMembers(promoted, config, cancellationToken)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMemberKeyDiagnostics(member, config, seen, diagnostics, cancellationToken);
            }
        }

        return diagnostics.ToImmutable();
    }

    internal static void AddMemberKeyDiagnostics(
        SparseSymbolMemberModel member,
        SparseGeneratorConfig config,
        HashSet<string> seen,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RequiresKeyedSemantics(member, config, cancellationToken))
        {
            return;
        }

        if (member.Collection.ElementType is not INamedTypeSymbol element)
        {
            return;
        }

        foreach (var diagnostic in CollectDiagnostics(element, config, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key =
                diagnostic.DescriptorId
                + "|"
                + diagnostic.Argument1
                + "|"
                + diagnostic.Location?.SourceSpan.ToString();
            if (seen.Add(key))
            {
                diagnostics.Add(diagnostic);
            }
        }

        // Temporary-identity declarations on the same element validate together
        // with the key so promoted nested models fail the root precisely.
        foreach (
            var diagnostic in SparseTemporaryKeyAnalyzer.CollectDiagnostics(
                element,
                config,
                cancellationToken
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key =
                diagnostic.DescriptorId
                + "|"
                + diagnostic.Argument1
                + "|"
                + diagnostic.Location?.SourceSpan.ToString();
            if (seen.Add(key))
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    /// <summary>Determines whether a member is a keyed-sequence candidate needing a key.</summary>
    /// <remarks>
    /// Mirrors the escape-hatch rule of
    /// <see cref="SparseCollectionAnalyzer.IsUnkeyedStructuralSequence"/>: explicit
    /// <c>Append</c>/<c>SetUnion</c>/<c>Custom</c> merge modes and explicitly
    /// selected <c>Replace</c> keep whole-collection semantics without a key.
    /// </remarks>
    public static bool RequiresKeyedSemantics(
        SparseSymbolMemberModel member,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
            || member.MergeMode == SparseMergeModes.Custom
            || (member.HasExplicitMergeMode && member.MergeMode == SparseMergeModes.Replace)
        )
        {
            return false;
        }

        return SparseCollectionAnalyzer.ClassifySemantic(
                member.Collection,
                config,
                cancellationToken
            ) == SparseCollectionSemantic.KeyedSequence;
    }

    /// <summary>Discovers the unassigned sentinel of a single key declaration, if any.</summary>
    /// <remarks>
    /// Returns <c>false</c> when the element lacks exactly one key mark or when
    /// its sentinel is invalid (key-shape diagnostics cover those cases).
    /// A <c>true</c> result carries the sentinel expression, or null when the
    /// key has no unassigned semantics. Temporary-identity validation uses this
    /// without recursing into full key analysis.
    /// </remarks>
    internal static bool TryGetSingleKeyUnassignedExpression(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out string? expression
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        expression = null;
        var marks = FindKeyProperties(element, config, cancellationToken);
        if (marks.Count != 1)
        {
            return false;
        }

        var (property, attribute) = marks[0];
        if (attribute.ConstructorArguments.Length > 0)
        {
            return false;
        }

        if (!TryGetUnassignedExpression(attribute, property.Type, out expression, out _))
        {
            expression = null;
            return false;
        }

        return true;
    }

    private static List<(IPropertySymbol Property, AttributeData Attribute)> FindKeyProperties(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var marks = new List<(IPropertySymbol, AttributeData)>();
        foreach (var property in GetAllProperties(element, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in property.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attribute.AttributeClass?.ToDisplayString() == config.KeyAttributeMetadataName)
                {
                    marks.Add((property, attribute));
                    break;
                }
            }
        }

        return marks;
    }

    private static List<AttributeData> FindTypeLevelAttributes(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var attributes = new List<AttributeData>();
        foreach (var attribute in element.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() == config.KeyAttributeMetadataName)
            {
                attributes.Add(attribute);
            }
        }

        return attributes;
    }

    private static bool HasTypeLevelAttribute(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => FindTypeLevelAttributes(element, config, cancellationToken).Count > 0;

    private static IEnumerable<IPropertySymbol> GetAllProperties(
        INamedTypeSymbol model,
        CancellationToken cancellationToken
    )
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (
            var current = model;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            hierarchy.Push(current);
        }

        // Most-derived declaration wins per name (base-first overwrite order).
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.Ordinal);
        while (hierarchy.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var property in hierarchy.Pop().GetMembers().OfType<IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                properties[property.Name] = property;
            }
        }

        return properties.Values;
    }

    private static bool TryGetUnassignedExpression(
        AttributeData attribute,
        ITypeSymbol keyType,
        out string? expression,
        out string? error
    )
    {
        expression = null;
        error = null;
        var named = attribute.NamedArguments.FirstOrDefault(static argument =>
            argument.Key == "Unassigned"
        );
        if (named.Key is null)
        {
            if (IsNullableKeyType(keyType))
            {
                expression = "null";
            }
            return true;
        }

        var constant = named.Value;
        var sourceType = constant.Type;
        if (constant.IsNull || constant.Value is null)
        {
            if (keyType.IsReferenceType || IsNullableValueType(keyType))
            {
                expression = "null";
                return true;
            }

            error = "Unassigned null requires a nullable key type.";
            return false;
        }

        if (
            sourceType is null
            || constant.Kind is not (TypedConstantKind.Primitive or TypedConstantKind.Enum)
        )
        {
            error = "Unassigned must be a constant convertible to the key property type.";
            return false;
        }

        var conversionTarget = NullableUnderlyingType(keyType) ?? keyType;
        if (
            sourceType.SpecialType != conversionTarget.SpecialType
            && !SymbolEqualityComparer.Default.Equals(sourceType, conversionTarget)
            && !CanConvertNumericConstant(
                constant.Value,
                sourceType.SpecialType,
                conversionTarget.SpecialType
            )
        )
        {
            error = "Unassigned is not convertible to the key property type.";
            return false;
        }

        var literal = constant.Value switch
        {
            string text => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, true),
            char character => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
                character,
                true
            ),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(
                constant.Value,
                System.Globalization.CultureInfo.InvariantCulture
            )!,
        };
        if (constant.Value is long)
            literal += "L";
        else if (constant.Value is ulong)
            literal += "UL";
        else if (constant.Value is uint)
            literal += "U";
        else if (constant.Value is float)
            literal += "F";
        else if (constant.Value is decimal)
            literal += "M";
        expression = "(" + NonNullableTypeName(keyType) + ")(" + literal + ")";
        return true;
    }

    private static bool CanConvertNumericConstant(
        object value,
        SpecialType sourceType,
        SpecialType targetType
    )
    {
        var runtimeType = targetType switch
        {
            SpecialType.System_SByte => typeof(sbyte),
            SpecialType.System_Byte => typeof(byte),
            SpecialType.System_Int16 => typeof(short),
            SpecialType.System_UInt16 => typeof(ushort),
            SpecialType.System_Int32 => typeof(int),
            SpecialType.System_UInt32 => typeof(uint),
            SpecialType.System_Int64 => typeof(long),
            SpecialType.System_UInt64 => typeof(ulong),
            SpecialType.System_Single => typeof(float),
            SpecialType.System_Double => typeof(double),
            SpecialType.System_Decimal => typeof(decimal),
            _ => null,
        };
        if (runtimeType is null || !IsNumeric(sourceType))
            return false;

        try
        {
            _ = Convert.ChangeType(
                value,
                runtimeType,
                System.Globalization.CultureInfo.InvariantCulture
            );
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsNumeric(SpecialType type) =>
        type
            is SpecialType.System_SByte
                or SpecialType.System_Byte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Decimal;

    private static bool IsCollectionShaped(ITypeSymbol type, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        return type is INamedTypeSymbol named
            && SparseCollectionAnalyzer.GetCollectionInfo(named).CloneKind
                != SparseCloneCollectionKind.Unsupported;
    }

    private static bool IsNullableKeyType(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValueType(type);

    private static bool IsNullableValueType(ITypeSymbol type) =>
        NullableUnderlyingType(type) is not null;

    private static ITypeSymbol? NullableUnderlyingType(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : null;

    private static bool HasAttributeArguments(AttributeData attribute) =>
        attribute.ConstructorArguments.Length > 0;

    private static bool IsPubliclyReadableInstance(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsIndexer
        && property.DeclaredAccessibility == Accessibility.Public
        && property.GetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static string? TemporaryKeyProperty(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        // Temporary identity is advisory here: shape diagnostics belong to
        // SparseTemporaryKeyAnalyzer. Only a fully valid declaration flows
        // into keyed emission; anything else behaves as if absent.
        if (
            SparseTemporaryKeyAnalyzer.TryGetTemporaryKeyProperty(
                element,
                config,
                cancellationToken,
                out var propertyName
            )
        )
        {
            return propertyName;
        }

        return null;
    }

    private static Location? AttributeLocation(
        AttributeData attribute,
        INamedTypeSymbol fallback
    ) =>
        attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
        ?? fallback.Locations.FirstOrDefault();

    private static string NonNullableTypeName(ITypeSymbol type)
    {
        // Mirror the extraction operator: the generated key reader appends the
        // null-forgiving operator, which turns string? into string but leaves
        // Nullable<T> (including nullable tuples) untouched. Only a top-level
        // reference-type annotation is dropped; nested and value-type
        // annotations stay so the generated key type matches the property.
        // TypeFormat renders nested nullability, which FullyQualifiedFormat
        // omits; without it tuple element annotations would be lost.
        var text = type.ToDisplayString(SparseNaming.TypeFormat);
        if (type.IsReferenceType && text.EndsWith("?", StringComparison.Ordinal))
        {
            return text.Substring(0, text.Length - 1);
        }

        return text;
    }
}
