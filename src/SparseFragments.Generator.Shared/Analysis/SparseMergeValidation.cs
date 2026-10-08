using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>One validation policy for the fragment algebra's built-in merge modes.</summary>
internal static class SparseMergeValidation
{
    public static string? GetUnsupportedReason(
        int mode,
        bool hasChild,
        SparseCollectionKind collection
    ) =>
        mode switch
        {
            SparseMergeModes.Replace => null,
            SparseMergeModes.Deep when !hasChild => "Deep",
            SparseMergeModes.Deep => null,
            SparseMergeModes.Append when collection == SparseCollectionKind.Set =>
                "Append on set types (use an ordered collection or SetUnion)",
            SparseMergeModes.Append
                when collection is SparseCollectionKind.Unsupported or SparseCollectionKind.MutableList =>
                "Append",
            SparseMergeModes.Append => null,
            SparseMergeModes.SetUnion
                when collection is SparseCollectionKind.Unsupported or SparseCollectionKind.MutableList =>
                "SetUnion",
            SparseMergeModes.SetUnion => null,
            SparseMergeModes.Custom => null,
            _ => mode.ToString(),
        };

    /// <summary>
    /// Validates a custom merge strategy type, parameterized by the configured
    /// merge-strategy base metadata name so downstream generators share the rule.
    /// </summary>
    public static bool IsValidCustomStrategy(
        INamedTypeSymbol strategyType,
        ITypeSymbol memberType,
        bool isNestedModel,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        if (
            isNestedModel
            || strategyType.TypeKind != TypeKind.Class
            || strategyType.IsAbstract
            || strategyType.Arity != 0
        )
        {
            return false;
        }

        for (var current = strategyType; current is not null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                current.DeclaredAccessibility
                is not (Accessibility.Public or Accessibility.Internal)
            )
            {
                return false;
            }
        }

        var hasConstructor = strategyType.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
        );
        if (!hasConstructor)
        {
            return false;
        }

        for (var current = strategyType; current is not null; current = current.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                current.OriginalDefinition.ToDisplayString() == config.MergeStrategyBaseMetadataName
                && SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], memberType)
            )
            {
                return true;
            }
        }

        return false;
    }
}
