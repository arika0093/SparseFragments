using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseFragments.Generator.Shared;

/// <summary>Discovers and validates SparseFragments temporary-key metadata on element types.</summary>
/// <remarks>
/// A keyed element type may declare exactly one property marked with
/// parameterless temporary-key attribute whose type is <c>Guid?</c>. The
/// declaration is product-neutral: the attribute metadata name and diagnostic
/// IDs come from <see cref="SparseGeneratorConfig"/>, and analysis stays
/// disabled when the product provides no metadata name.
/// </remarks>
internal static class SparseTemporaryKeyAnalyzer
{
    /// <summary>Determines whether temporary-key analysis applies for the product.</summary>
    public static bool IsEnabled(SparseGeneratorConfig config) =>
        !string.IsNullOrEmpty(config.TemporaryKeyAttributeMetadataName);

    /// <summary>Determines whether an element type declares any temporary-key metadata.</summary>
    public static bool HasTemporaryKeyDeclaration(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled(config))
        {
            return false;
        }

        return FindTemporaryKeyProperties(element, config, cancellationToken).Count > 0;
    }

    /// <summary>Strictly discovers the usable temporary-key property of an element type, if any.</summary>
    /// <remarks>
    /// Returns <c>false</c> when analysis is disabled, when no valid
    /// single-property declaration applies, when the declaration is
    /// invalid, or when the element's key lacks unassigned semantics (a
    /// temporary identity without an unassigned state can never activate).
    /// An explicit property initializer only warns
    /// (<c>TemporaryKeyInitializer</c>) and keeps the declaration usable.
    /// Use <see cref="CollectDiagnostics"/> to report the corresponding
    /// diagnostics.
    /// </remarks>
    public static bool TryGetTemporaryKeyProperty(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        out string? propertyName
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        propertyName = null;
        if (!IsEnabled(config))
        {
            return false;
        }

        var marks = FindTemporaryKeyProperties(element, config, cancellationToken);
        if (marks.Count != 1)
        {
            return false;
        }

        var (property, attribute) = marks[0];
        if (HasAttributeArguments(attribute, cancellationToken))
        {
            return false;
        }

        if (!IsPubliclyReadableInstance(property) || !HasPublicSetter(property))
        {
            return false;
        }

        if (!IsNullableGuid(property.Type))
        {
            return false;
        }

        if (SparseModelDiscovery.IsSparseIgnored(property, config))
        {
            return false;
        }

        if (HasKeyAttribute(property, config))
        {
            return false;
        }

        if (!HasUnassignedKeySemantics(element, config, cancellationToken))
        {
            return false;
        }

        propertyName = property.Name;
        return true;
    }

    /// <summary>Collects temporary-key diagnostics for an element type.</summary>
    /// <remarks>
    /// Returns an empty array when analysis is disabled, when the element has
    /// no temporary-key declaration at all, or when its single declaration is
    /// fully valid (an initializer warning is still reported).
    /// </remarks>
    public static ImmutableArray<SparseGeneratorDiagnostic> CollectDiagnostics(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled(config))
        {
            return ImmutableArray<SparseGeneratorDiagnostic>.Empty;
        }

        var marks = FindTemporaryKeyProperties(element, config, cancellationToken);
        if (marks.Count == 0)
        {
            return ImmutableArray<SparseGeneratorDiagnostic>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<SparseGeneratorDiagnostic>();
        if (marks.Count > 1)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(
                    config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
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
                        config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name + " is ignored"
                    )
                );
                continue;
            }

            // Erroneous constructor arguments vanish from the bound
            // symbols (CS1729), so the syntax decides argument presence.
            if (HasAttributeArguments(attribute, cancellationToken))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name + " takes no arguments"
                    )
                );
                continue;
            }

            if (HasKeyAttribute(property, config))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name + " cannot combine both key attributes"
                    )
                );
                continue;
            }

            // Only validate the property itself when it is the single
            // declaration; multiple marks already explain the failure.
            if (marks.Count != 1)
            {
                continue;
            }

            if (!IsPubliclyReadableInstance(property) || !HasPublicSetter(property))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name
                            + "."
                            + property.Name
                            + " must be publicly readable and settable"
                    )
                );
                continue;
            }

            if (!IsNullableGuid(property.Type))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name + " must be Guid?"
                    )
                );
                continue;
            }

            if (!HasUnassignedKeySemantics(element, config, cancellationToken))
            {
                // Malformed keys already fail through key diagnostics; only a
                // clean key without unassigned semantics rejects the temporary
                // declaration here.
                if (
                    SparseKeyAnalyzer.CollectDiagnostics(element, config, cancellationToken).IsEmpty
                )
                {
                    diagnostics.Add(
                        new SparseGeneratorDiagnostic(
                            config.EffectiveDiagnosticIds.InvalidTemporaryKeyShape,
                            property.Locations.FirstOrDefault(),
                            element.Name
                                + "."
                                + property.Name
                                + " requires unassigned key semantics"
                        )
                    );
                }
                continue;
            }

            // Syntax-based warning only: legitimate assignments in
            // constructors or object initializers stay valid. Any explicit
            // declaration initializer (including = null or = default) warns.
            if (HasExplicitInitializer(property, cancellationToken))
            {
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        config.EffectiveDiagnosticIds.TemporaryKeyInitializer,
                        property.Locations.FirstOrDefault(),
                        element.Name + "." + property.Name
                    )
                    {
                        IsWarning = true,
                    }
                );
            }
        }

        return diagnostics.ToImmutable();
    }

    private static bool HasUnassignedKeySemantics(
        INamedTypeSymbol element,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        // A temporary identity only activates for unassigned permanent keys.
        // Invalid key shapes already fail through key diagnostics; only a
        // well-formed key without unassigned semantics rejects the temporary
        // declaration here.
        if (
            !SparseKeyAnalyzer.TryGetSingleKeyUnassignedExpression(
                element,
                config,
                cancellationToken,
                out var expression
            )
        )
        {
            return false;
        }

        return expression is not null;
    }

    private static List<(
        IPropertySymbol Property,
        AttributeData Attribute
    )> FindTemporaryKeyProperties(
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
                if (
                    attribute.AttributeClass?.ToDisplayString()
                    == config.TemporaryKeyAttributeMetadataName
                )
                {
                    marks.Add((property, attribute));
                    break;
                }
            }
        }

        return marks;
    }

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

    private static bool IsNullableGuid(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
        && named.TypeArguments.Length == 1
        && named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::System.Guid";

    private static bool IsPubliclyReadableInstance(IPropertySymbol property) =>
        !property.IsStatic
        && !property.IsIndexer
        && property.DeclaredAccessibility == Accessibility.Public
        && property.GetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static bool HasPublicSetter(IPropertySymbol property) =>
        property.SetMethod?.DeclaredAccessibility == Accessibility.Public;

    private static bool HasKeyAttribute(IPropertySymbol property, SparseGeneratorConfig config)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == config.KeyAttributeMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttributeArguments(
        AttributeData attribute,
        CancellationToken cancellationToken
    )
    {
        if (attribute.ConstructorArguments.Length > 0 || attribute.NamedArguments.Length > 0)
        {
            return true;
        }

        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken)
                is AttributeSyntax syntax
            && syntax.ArgumentList?.Arguments.Count > 0;
    }

    private static bool HasExplicitInitializer(
        IPropertySymbol property,
        CancellationToken cancellationToken
    )
    {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                syntaxReference.GetSyntax(cancellationToken)
                    is PropertyDeclarationSyntax declaration
                && declaration.Initializer is not null
            )
            {
                return true;
            }
        }

        return false;
    }
}
