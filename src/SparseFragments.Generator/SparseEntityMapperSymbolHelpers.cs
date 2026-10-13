using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator;

internal static class SparseEntityMapperSymbolHelpers
{
    internal static ISymbol? FindEntityMember(INamedTypeSymbol entityType, string name)
    {
        for (
            var current = entityType;
            current is not null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member.IsStatic)
                {
                    continue;
                }

                if (member is IPropertySymbol property && property.GetMethod is not null)
                {
                    return property;
                }

                if (member is IFieldSymbol)
                {
                    return member;
                }
            }
        }

        return null;
    }

    internal static ITypeSymbol? GetMemberType(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };

    internal static bool IsScalarType(ITypeSymbol type)
    {
        var underlying =
            type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
                ? named.TypeArguments[0]
                : type;
        switch (underlying.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_String:
            case SpecialType.System_DateTime:
                return true;
            default:
                break;
        }

        if (underlying.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (underlying is INamedTypeSymbol namedType)
        {
            var full = namedType.OriginalDefinition.ToDisplayString();
            if (
                full == "System.DateTimeOffset"
                || full == "System.DateOnly"
                || full == "System.TimeOnly"
                || full == "System.TimeSpan"
                || full == "System.Guid"
            )
            {
                return true;
            }
        }

        return underlying is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Byte
            && array.Rank == 1;
    }

    internal static ITypeSymbol? CollectionElementType(ITypeSymbol? type) =>
        type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol named when named.TypeArguments.Length == 1 => named.TypeArguments[0],
            _ => null,
        };

    internal static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (
            type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Byte
            && array.Rank == 1
        )
        {
            return false;
        }

        return type.AllInterfaces.Any(static iface =>
            iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_IEnumerable
        );
    }

    // Keep scalar compatibility identical to the runtime plan's symbol comparison.
    internal static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.Default.Equals(left, right);
}
