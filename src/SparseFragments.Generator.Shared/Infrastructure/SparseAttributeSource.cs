using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

internal static class SparseAttributeSource
{
    public static string FormatPropertyAttributes(IPropertySymbol property)
    {
        var attributes = property
            .GetAttributes()
            .Select(FormatAttribute)
            .Where(static expression => expression is not null);
        return string.Join(", ", attributes);
    }

    private static string? FormatAttribute(AttributeData attribute)
    {
        if (attribute.AttributeClass is null)
        {
            return null;
        }

        var arguments = attribute.ConstructorArguments.Select(FormatConstant).ToArray();
        if (arguments.Any(static argument => argument is null))
        {
            return null;
        }

        var typeName = attribute.AttributeClass.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );
        var source = "new " + typeName + "(" + string.Join(", ", arguments) + ")";
        if (attribute.NamedArguments.IsDefaultOrEmpty)
        {
            return source;
        }

        var assignments = attribute
            .NamedArguments.Select(pair =>
            {
                var value = FormatConstant(pair.Value);
                return value is null
                    ? null
                    : SparseNaming.EscapeIdentifier(pair.Key) + " = " + value;
            })
            .ToArray();
        if (assignments.Any(static assignment => assignment is null))
        {
            return null;
        }

        return source + " { " + string.Join(", ", assignments) + " }";
    }

    private static string? FormatConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return constant.Type is null
                ? "null"
                : "("
                    + constant.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    + ")null!";
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            if (constant.Type is not IArrayTypeSymbol arrayType)
            {
                return null;
            }

            var values = constant.Values.Select(FormatConstant).ToArray();
            if (values.Any(static value => value is null))
            {
                return null;
            }

            return "new "
                + arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + "[] { "
                + string.Join(", ", values)
                + " }";
        }

        if (constant.Kind == TypedConstantKind.Type && constant.Value is ITypeSymbol type)
        {
            return "typeof(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
        }

        if (constant.Type?.TypeKind == TypeKind.Enum)
        {
            var enumType = constant.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var underlying = ((INamedTypeSymbol)constant.Type).EnumUnderlyingType?.SpecialType;
            var value = FormatPrimitive(constant.Value, underlying);
            return value is null ? null : "(" + enumType + ")" + value;
        }

        if (constant.Kind != TypedConstantKind.Primitive)
        {
            return null;
        }

        return FormatPrimitive(constant.Value, constant.Type?.SpecialType);
    }

    private static string? FormatPrimitive(object? value, SpecialType? type)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string text)
        {
            return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, true);
        }

        if (value is char character)
        {
            return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(character, true);
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        var literal = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (literal is null)
        {
            return null;
        }

        return type switch
        {
            SpecialType.System_UInt32 => literal + "U",
            SpecialType.System_Int64 => literal + "L",
            SpecialType.System_UInt64 => literal + "UL",
            SpecialType.System_Single => literal + "F",
            SpecialType.System_Double => literal + "D",
            SpecialType.System_Decimal => literal + "M",
            _ => literal,
        };
    }
}
