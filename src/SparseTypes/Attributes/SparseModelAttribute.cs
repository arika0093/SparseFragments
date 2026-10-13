namespace SparseTypes;

/// <summary>Marks a partial class or struct for sparse generation.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SparseModelAttribute : Attribute
{
    /// <summary>Initializes a new instance of the attribute.</summary>
    public SparseModelAttribute() { }
}
