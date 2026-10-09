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

        // The per-compilation index is built once and shared by every model.
        // Without any root-level comparison rules, inheritance cannot apply
        // and the reference-graph scan is skipped entirely.
        var index = SparseComparisonIndex.ForAssembly(
            model.ContainingAssembly,
            config,
            cancellationToken
        );
        var parentRoots = index.HasComparisonRules
            ? index
                .Roots.Select((root, ordinal) => (root, ordinal))
                .Where(candidate =>
                    !SymbolEqualityComparer.Default.Equals(candidate.root, model)
                    && index.ReferencesModel(candidate.ordinal, model)
                )
                .Select(static candidate => candidate.root)
                .ToArray()
            : [];
        AddInheritedRules(comparerTypes, parentRoots, attributeName, cancellationToken);
        AddRules(
            comparerTypes,
            model.ContainingAssembly.GetAttributes(),
            attributeName,
            cancellationToken,
            overwrite: false
        );
        return new SparseComparisonRuleSet(comparerTypes);
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
                    attribute.AttributeClass?.ToDisplayString() == attributeName
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
                attribute.AttributeClass?.ToDisplayString() != attributeName
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
                attribute.AttributeClass?.ToDisplayString() == attributeName
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
