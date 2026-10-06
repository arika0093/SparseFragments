using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared JSON naming metadata for generated fragments.</summary>
internal static class SparseJsonNaming
{
    public const int JsonIgnoreNever = 0;
    public const int JsonIgnoreAlways = 1;

    public const string JsonPropertyNameAttribute =
        "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    public const string JsonIgnoreAttribute = "System.Text.Json.Serialization.JsonIgnoreAttribute";

    public static string GetJsonPropertyName(
        IPropertySymbol property,
        CancellationToken cancellationToken,
        out bool isExplicit
    )
    {
        foreach (var attribute in property.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                attribute.AttributeClass?.ToDisplayString() == JsonPropertyNameAttribute
                && attribute.ConstructorArguments.FirstOrDefault().Value is string configuredName
            )
            {
                isExplicit = true;
                return configuredName;
            }
        }

        isExplicit = false;
        return property.Name;
    }

    public static int GetJsonIgnoreCondition(
        IPropertySymbol property,
        CancellationToken cancellationToken
    )
    {
        foreach (var attribute in property.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass?.ToDisplayString() != JsonIgnoreAttribute)
            {
                continue;
            }

            var condition = JsonIgnoreAlways;
            foreach (var argument in attribute.NamedArguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    string.Equals(argument.Key, "Condition", System.StringComparison.Ordinal)
                    && TryParseCondition(argument.Value, out var namedCondition)
                )
                {
                    condition = namedCondition;
                }
            }

            if (attribute.ConstructorArguments.Length == 1)
            {
                var hasExplicitCondition = attribute.NamedArguments.Any(static argument =>
                    string.Equals(argument.Key, "Condition", System.StringComparison.Ordinal)
                );

                if (
                    !hasExplicitCondition
                    && TryParseCondition(attribute.ConstructorArguments[0], out var ctorCondition)
                )
                {
                    condition = ctorCondition;
                }
            }

            return condition;
        }

        return JsonIgnoreNever;
    }

    private static bool TryParseCondition(TypedConstant constant, out int condition)
    {
        // JsonIgnoreCondition arrives as its underlying integral value; accept
        // every integral representation (int/long plus the narrower/wider forms
        // other generators observe) so the shared parser stays complete.
        if (constant.Value is int intValue && intValue >= 0 && intValue <= 3)
        {
            condition = intValue;
            return true;
        }

        if (constant.Value is long longValue && longValue >= 0 && longValue <= 3)
        {
            condition = (int)longValue;
            return true;
        }

        if (constant.Value is sbyte or byte or short or ushort or uint or ulong)
        {
            // All remaining unsigned/signed integral forms fit in long, so this
            // conversion cannot fail.
            var numeric = System.Convert.ToInt64(
                constant.Value,
                System.Globalization.CultureInfo.InvariantCulture
            );
            if (numeric >= 0 && numeric <= 3)
            {
                condition = (int)numeric;
                return true;
            }
        }

        condition = JsonIgnoreAlways;
        return false;
    }
}
