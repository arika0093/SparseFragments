using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared capability plan for constructing and projecting model values.</summary>
internal readonly record struct ModelConstructionPlan(bool CanOverlayAfterConstruction)
{
    public static bool HasUnsupportedStructuralMembers(
        INamedTypeSymbol pocoType,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var constructor = ModelConstructorBinding.AnalyzeStructural(
            pocoType,
            config,
            cancellationToken
        );
        if (constructor is null)
            return true;
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (
            var current = pocoType;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            hierarchy.Push(current);
        }

        while (hierarchy.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = hierarchy.Pop();
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (property.IsStatic || property.IsIndexer)
                {
                    continue;
                }
                if (SparseModelDiscovery.IsSparseIgnored(property, config))
                {
                    continue;
                }

                // Computed key metadata ([SparseKey] or ISparseKeyed<>.SparseKey) is
                // identity, not construction state: it is extracted, never overlaid.
                if (IsComputedKeyMetadata(property, pocoType, config, cancellationToken))
                {
                    continue;
                }

                var hasPublicGetter =
                    property.GetMethod?.DeclaredAccessibility == Accessibility.Public;
                var hasPublicSetter =
                    property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                if (!hasPublicGetter && !hasPublicSetter)
                {
                    continue;
                }

                if (
                    !hasPublicGetter
                    || (
                        !hasPublicSetter
                        && !(
                            property.SetMethod is null
                            && constructor.Parameters.Any(parameter =>
                                parameter.PropertyName == property.Name
                            )
                        )
                    )
                    || (RoslynSymbolCompat.IsRequired(property) && !constructor.SetsRequiredMembers)
                    || (
                        property.SetMethod?.IsInitOnly == true
                        && !constructor.Parameters.Any(parameter =>
                            parameter.PropertyName == property.Name
                        )
                    )
                )
                {
                    return true;
                }
            }

            if (
                current
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .Any(static field =>
                        !field.IsStatic
                        && !field.IsConst
                        && field.DeclaredAccessibility == Accessibility.Public
                    )
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a property is computed identity metadata rather than state.
    /// </summary>
    /// <remarks>
    /// A getter-only <c>[SparseKey]</c> property (e.g.
    /// <c>public ServerKey Key => new(TenantId, Id)</c>) or the <c>SparseKey</c> getter
    /// of an <c>ISparseKeyed&lt;TKey&gt;</c> implementation is extracted for keyed
    /// collection identity and never constructed or overlaid, so it must not mark the
    /// containing type as structurally unsupported. Properties with any setter remain
    /// construction state and keep the existing rules.
    /// </remarks>
    private static bool IsComputedKeyMetadata(
        IPropertySymbol property,
        INamedTypeSymbol pocoType,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (property.SetMethod is not null)
        {
            return false;
        }

        foreach (var attribute in property.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() == config.KeyAttributeMetadataName)
            {
                return true;
            }
        }

        if (
            string.Equals(property.Name, config.KeyPropertyName, StringComparison.Ordinal)
            && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
        )
        {
            foreach (var implemented in pocoType.AllInterfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    implemented.OriginalDefinition?.ToDisplayString()
                    == config.KeyedInterfaceMetadataName
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IEnumerable<ISymbol> UnsupportedRequiredMembers(
        INamedTypeSymbol model,
        IEnumerable<IPropertySymbol> properties,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var represented = new HashSet<ISymbol>(properties, SymbolEqualityComparer.Default);
        for (var type = model; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    member is IPropertySymbol ignoredProperty
                    && SparseModelDiscovery.IsSparseIgnored(ignoredProperty, config)
                )
                {
                    continue;
                }
                if (
                    member is IPropertySymbol or IFieldSymbol
                    && RoslynSymbolCompat.IsRequired(member)
                    && !represented.Contains(member)
                )
                    yield return member;
            }
        }
    }

    public static ModelConstructionPlan ForStructuralMembers(
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor
    ) =>
        new(
            members.All(member =>
                (!member.Property.IsRequired || constructor?.SetsRequiredMembers == true)
                && (
                    !member.Property.IsInitOnly
                    || (
                        constructor is not null
                        && constructor.Parameters.Any(parameter =>
                            parameter.PropertyName == member.Property.Name
                        )
                    )
                )
            )
        );

    public static ModelConstructionPlan ForMembers(ImmutableArray<SparseMemberModel> members) =>
        new(
            members.All(static member => !member.Property.IsInitOnly && !member.Property.IsRequired)
        );

    public static bool HasRootParameterlessConstructor(
        INamedTypeSymbol model,
        CancellationToken cancellationToken
    )
    {
        foreach (var constructor in model.InstanceConstructors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (constructor.Parameters.IsEmpty)
                return true;
        }
        return model.IsValueType;
    }
}
