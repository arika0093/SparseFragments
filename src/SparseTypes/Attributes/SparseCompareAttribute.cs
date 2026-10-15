namespace SparseTypes;

/// <summary>Configures semantic equality for a model member type.</summary>
/// <remarks>Generated models keep one comparer instance per configured member and may call it concurrently.</remarks>
[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = true,
    Inherited = false
)]
public sealed class SparseCompareAttribute : Attribute
{
    /// <summary>Maps a value type to an equality comparer.</summary>
    /// <param name="valueType">The exact member type to compare.</param>
    /// <param name="comparerType">A concrete comparer with an accessible parameterless constructor.</param>
    public SparseCompareAttribute(Type valueType, Type comparerType)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentNullException.ThrowIfNull(comparerType);
        ValueType = valueType;
        ComparerType = comparerType;
    }

    /// <summary>The member type matched by this rule.</summary>
    public Type ValueType { get; }

    /// <summary>The equality comparer selected by this rule.</summary>
    public Type ComparerType { get; }
}
