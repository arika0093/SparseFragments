using System.Text.Json.Nodes;

namespace SparseFragments;

/// <summary>RFC 6902 structural JSON equality used by the <c>test</c> operation.</summary>
/// <remarks>
/// RFC 6902 §4.6 defines equality structurally: strings by exact value, numbers by
/// numerical equality (so <c>1</c>, <c>1.0</c> and <c>10e-1</c> are equal), arrays by
/// length/order with recursive equality, objects by identical member sets with
/// recursively equal values regardless of member order. This deliberately does not
/// rely on <see cref="JsonNode.DeepEquals(JsonNode?, JsonNode?)"/> lexical behavior.
/// </remarks>
internal static class RfcJsonEquality
{
    public static bool AreEqual(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is JsonValue leftValue && right is JsonValue rightValue)
        {
            return JsonValuesEqual(leftValue, rightValue);
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            if (leftArray.Count != rightArray.Count)
            {
                return false;
            }

            for (var index = 0; index < leftArray.Count; index++)
            {
                if (!AreEqual(leftArray[index], rightArray[index]))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            if (leftObject.Count != rightObject.Count)
            {
                return false;
            }

            foreach (var property in leftObject)
            {
                if (
                    !rightObject.TryGetPropertyValue(property.Key, out var current)
                    || !AreEqual(property.Value, current)
                )
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S2589",
        Justification = "Second TryGetValue runs only when decimal conversion fails on at least one operand."
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Bug",
        "S1244",
        Justification = "RFC 6902 requires exact numerical equality; decimal covers representable values, double fallback covers the rest."
    )]
    private static bool JsonValuesEqual(JsonValue left, JsonValue right)
    {
        var leftKind = left.GetValueKind();
        if (leftKind == System.Text.Json.JsonValueKind.Number)
        {
            // JSON numbers with different lexical forms share the same value kind.
            if (
                left.TryGetValue<decimal>(out var leftDecimal)
                && right.TryGetValue<decimal>(out var rightDecimal)
            )
            {
                return leftDecimal == rightDecimal;
            }

            if (
                left.TryGetValue<double>(out var leftDouble)
                && right.TryGetValue<double>(out var rightDouble)
            )
            {
                return leftDouble.Equals(rightDouble);
            }

            return false;
        }

        if (leftKind != right.GetValueKind())
        {
            return false;
        }

        return leftKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => true,
            System.Text.Json.JsonValueKind.Null => true,
            System.Text.Json.JsonValueKind.String => left.GetValue<string>()
                == right.GetValue<string>(),
            _ => JsonNode.DeepEquals(left, right),
        };
    }
}
