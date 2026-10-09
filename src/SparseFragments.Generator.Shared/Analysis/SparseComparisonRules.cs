using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

internal static class SparseComparisonRules
{
    public static SparseComparisonRuleSet CreateRuleSet(
        INamedTypeSymbol model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var attributeName = config.ComparisonAttributeMetadataName;
        var comparerTypes = new Dictionary<ITypeSymbol, INamedTypeSymbol?>(
            SymbolEqualityComparer.Default
        );
        if (attributeName is null)
        {
            return new SparseComparisonRuleSet(comparerTypes);
        }

        AddRules(
            comparerTypes,
            model.GetAttributes(),
            attributeName,
            cancellationToken,
            overwrite: false
        );

        var roots = GetTypes(model.ContainingAssembly.GlobalNamespace, cancellationToken)
            .Where(root => !SymbolEqualityComparer.Default.Equals(root, model))
            .Where(root => HasAttribute(root, config.ModelAttributeMetadataName))
            .ToArray();
        if (
            roots.Any(root =>
                HasUnmappedRule(root, attributeName, comparerTypes, cancellationToken)
            )
        )
        {
            var parentRoots = roots
                .Where(root => ReferencesModel(root, model, cancellationToken))
                .ToArray();
            AddInheritedRules(comparerTypes, parentRoots, attributeName, cancellationToken);
        }
        AddRules(
            comparerTypes,
            model.ContainingAssembly.GetAttributes(),
            attributeName,
            cancellationToken,
            overwrite: false
        );
        return new SparseComparisonRuleSet(comparerTypes);
    }

    private static bool HasUnmappedRule(
        INamedTypeSymbol root,
        string attributeName,
        Dictionary<ITypeSymbol, INamedTypeSymbol?> comparerTypes,
        CancellationToken cancellationToken
    )
    {
        foreach (var attribute in root.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                MatchesAttribute(attribute.AttributeClass, attributeName)
                && attribute.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol valueType
                && !comparerTypes.ContainsKey(valueType)
            )
            {
                return true;
            }
        }
        return false;
    }

    private static void AddInheritedRules(
        Dictionary<ITypeSymbol, INamedTypeSymbol?> comparerTypes,
        INamedTypeSymbol[] parentRoots,
        string attributeName,
        CancellationToken cancellationToken
    )
    {
        if (parentRoots.Length == 0)
        {
            return;
        }

        var candidateTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var root in parentRoots)
        {
            foreach (var attribute in root.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    MatchesAttribute(attribute.AttributeClass, attributeName)
                    && attribute.ConstructorArguments.FirstOrDefault().Value
                        is ITypeSymbol valueType
                )
                {
                    candidateTypes.Add(valueType);
                }
            }
        }

        foreach (var valueType in candidateTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (comparerTypes.ContainsKey(valueType))
            {
                continue;
            }

            INamedTypeSymbol? commonComparer = null;
            var allRootsHaveSameRule = true;
            foreach (var root in parentRoots)
            {
                var rule = FindRule(
                    root.GetAttributes(),
                    attributeName,
                    valueType,
                    cancellationToken
                );
                var comparer = rule is null ? null : ComparerType(rule);
                if (
                    comparer is null
                    || (
                        commonComparer is not null
                        && !SymbolEqualityComparer.Default.Equals(commonComparer, comparer)
                    )
                )
                {
                    allRootsHaveSameRule = false;
                    break;
                }

                commonComparer = comparer;
            }

            if (allRootsHaveSameRule)
            {
                comparerTypes.Add(valueType, commonComparer);
            }
        }
    }

    private static void AddRules(
        Dictionary<ITypeSymbol, INamedTypeSymbol?> comparerTypes,
        IEnumerable<AttributeData> attributes,
        string attributeName,
        CancellationToken cancellationToken,
        bool overwrite
    )
    {
        foreach (var attribute in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !MatchesAttribute(attribute.AttributeClass, attributeName)
                || attribute.ConstructorArguments.Length < 2
                || attribute.ConstructorArguments[0].Value is not ITypeSymbol valueType
            )
            {
                continue;
            }

            if (overwrite || !comparerTypes.ContainsKey(valueType))
            {
                comparerTypes[valueType] = ComparerType(attribute);
            }
        }
    }

    private static bool ReferencesModel(
        INamedTypeSymbol root,
        INamedTypeSymbol target,
        CancellationToken cancellationToken
    )
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        return VisitType(root, target, visited, cancellationToken);
    }

    private static bool VisitType(
        ITypeSymbol type,
        INamedTypeSymbol target,
        HashSet<INamedTypeSymbol> visited,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SymbolEqualityComparer.Default.Equals(type, target))
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return VisitType(array.ElementType, target, visited, cancellationToken);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (
            named.TypeArguments.Any(argument =>
                VisitType(argument, target, visited, cancellationToken)
            )
        )
        {
            return true;
        }

        if (SparseModelDiscovery.IsFrameworkType(named) || !visited.Add(named))
        {
            return false;
        }

        return named
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Any(property =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return !property.IsStatic
                    && !property.IsIndexer
                    && property.DeclaredAccessibility == Accessibility.Public
                    && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
                    && VisitType(property.Type, target, visited, cancellationToken);
            });
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(
        INamespaceSymbol root,
        CancellationToken cancellationToken
    )
    {
        var pending = new Stack<INamespaceOrTypeSymbol>(root.GetMembers());
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    pending.Push(member);
                }
            }
            else if (current is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in type.GetTypeMembers())
                {
                    pending.Push(nested);
                }
            }
        }
    }

    private static bool HasAttribute(INamedTypeSymbol model, string attributeName) =>
        model
            .GetAttributes()
            .Any(attribute => MatchesAttribute(attribute.AttributeClass, attributeName));

    private static bool MatchesAttribute(INamedTypeSymbol? attributeType, string attributeName)
    {
        if (attributeType is null)
        {
            return false;
        }
        var simpleClass =
            attributeType.TypeKind == TypeKind.Class
            && attributeType.Arity == 0
            && attributeType.SpecialType == SpecialType.None;
        if (
            simpleClass
            && attributeType.NullableAnnotation != NullableAnnotation.Annotated
            && !attributeName.EndsWith(attributeType.Name, StringComparison.Ordinal)
        )
        {
            return false;
        }
        return attributeType.ToDisplayString() == attributeName;
    }

    private static AttributeData? FindRule(
        IEnumerable<AttributeData> attributes,
        string attributeName,
        ITypeSymbol valueType,
        CancellationToken cancellationToken
    )
    {
        foreach (var attribute in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                MatchesAttribute(attribute.AttributeClass, attributeName)
                && attribute.ConstructorArguments.Length >= 2
                && attribute.ConstructorArguments[0].Value is ITypeSymbol configuredType
                && SymbolEqualityComparer.Default.Equals(configuredType, valueType)
            )
            {
                return attribute;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ComparerType(AttributeData attribute) =>
        attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
}

internal sealed class SparseComparisonRuleSet(
    Dictionary<ITypeSymbol, INamedTypeSymbol?> comparerTypes
)
{
    public bool TryGetComparerType(ITypeSymbol valueType, out INamedTypeSymbol? comparerType) =>
        comparerTypes.TryGetValue(valueType, out comparerType);
}
