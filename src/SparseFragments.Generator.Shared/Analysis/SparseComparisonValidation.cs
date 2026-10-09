using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

internal static class SparseComparisonValidation
{
    public static bool IsValidComparer(
        INamedTypeSymbol comparerType,
        ITypeSymbol valueType,
        CancellationToken cancellationToken
    )
    {
        if (
            comparerType.TypeKind != TypeKind.Class
            || comparerType.IsAbstract
            || comparerType.Arity != 0
        )
        {
            return false;
        }

        for (var current = comparerType; current is not null; current = current.ContainingType)
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

        if (
            !comparerType.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility
                    is Accessibility.Public
                        or Accessibility.Internal
            )
        )
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return comparerType.AllInterfaces.Any(implemented =>
            implemented.OriginalDefinition.ToDisplayString()
                == "System.Collections.Generic.IEqualityComparer<T>"
            && SymbolEqualityComparer.Default.Equals(implemented.TypeArguments[0], valueType)
        );
    }
}
