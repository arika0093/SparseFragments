namespace SparseTypes;

/// <summary>Declares stable identity for keyed structural collections.</summary>
/// <remarks>Mark one readable, non-static property as the stable element key. An optional sentinel can represent a newly added keyed element.</remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseKeyAttribute : Attribute
{
    /// <summary>Optional key value reserved for elements whose identity has not yet been assigned.</summary>
    public object? Unassigned { get; set; }

    /// <summary>Marks a property as the stable key.</summary>
    public SparseKeyAttribute() { }
}
