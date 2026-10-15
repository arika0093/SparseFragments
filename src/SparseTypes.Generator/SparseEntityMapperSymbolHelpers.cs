using Microsoft.CodeAnalysis;

namespace SparseTypes.Generator;

internal static class SparseEntityMapperSymbolHelpers
{
    // The companion can access only visible members in its namespace and
    // assembly; inaccessible members require explicit mapping or diagnostics.
    internal static bool IsAccessibleFromCompanion(ISymbol symbol, INamedTypeSymbol mapper)
    {
        var accessibility = symbol.DeclaredAccessibility;
        if (accessibility == Accessibility.Public)
        {
            return DeclaringTypesAccessible(symbol, mapper);
        }

        if (
            accessibility is Accessibility.Internal or Accessibility.ProtectedAndInternal
            && SymbolEqualityComparer.Default.Equals(
                symbol.ContainingAssembly,
                mapper.ContainingAssembly
            )
        )
        {
            return DeclaringTypesAccessible(symbol, mapper);
        }

        return false;
    }

    internal static bool IsTypeAccessibleFromCompanion(ITypeSymbol type, INamedTypeSymbol mapper)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsTypeAccessibleFromCompanion(array.ElementType, mapper);
            case INamedTypeSymbol named when named.TypeKind == TypeKind.TypeParameter:
                return true;
            case INamedTypeSymbol named:
                return IsAccessibleFromCompanion(named.OriginalDefinition, mapper)
                    && named.TypeArguments.All(argument =>
                        IsTypeAccessibleFromCompanion(argument, mapper)
                    );
            case ITypeParameterSymbol:
                return true;
            case IPointerTypeSymbol pointer:
                return IsTypeAccessibleFromCompanion(pointer.PointedAtType, mapper);
            default:
                return true;
        }
    }

    private static bool DeclaringTypesAccessible(ISymbol symbol, INamedTypeSymbol mapper)
    {
        for (
            var containing = symbol.ContainingType;
            containing is not null;
            containing = containing.ContainingType
        )
        {
            var access = containing.DeclaredAccessibility;
            if (access == Accessibility.Public)
            {
                continue;
            }

            if (
                access is Accessibility.Internal or Accessibility.ProtectedAndInternal
                && SymbolEqualityComparer.Default.Equals(
                    containing.ContainingAssembly,
                    mapper.ContainingAssembly
                )
            )
            {
                continue;
            }

            return false;
        }

        return true;
    }

    // Entity selectors use the same access rules as DTO selectors. Public
    // fields are readable, but nested Object navigation requires properties.
    internal static ISymbol? FindAccessibleEntityMember(
        INamedTypeSymbol entityType,
        string name,
        INamedTypeSymbol mapper
    )
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

                if (
                    member is IPropertySymbol property
                    && !property.IsIndexer
                    && property.GetMethod is not null
                    && IsAccessibleFromCompanion(property, mapper)
                    && IsAccessibleFromCompanion(property.GetMethod, mapper)
                    && IsTypeAccessibleFromCompanion(property.Type, mapper)
                )
                {
                    return property;
                }

                if (
                    member is IFieldSymbol field
                    && IsAccessibleFromCompanion(field, mapper)
                    && IsTypeAccessibleFromCompanion(field.Type, mapper)
                )
                {
                    return field;
                }
            }
        }

        return null;
    }

    // The generated temporary-key assignment runs as an ordinary runtime
    // delegate outside construction.
    internal static bool IsTemporarySetterUsable(
        IPropertySymbol? temporaryProperty,
        INamedTypeSymbol mapper
    ) =>
        temporaryProperty is not null
        && !temporaryProperty.IsStatic
        && !temporaryProperty.IsIndexer
        && temporaryProperty.SetMethod is { IsInitOnly: false }
        && IsAccessibleFromCompanion(temporaryProperty, mapper)
        && IsAccessibleFromCompanion(temporaryProperty.SetMethod, mapper);

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

    // Keep scalar compatibility identical to the runtime plan's symbol
    // comparison.
    internal static bool SameType(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.Default.Equals(left, right);
}
