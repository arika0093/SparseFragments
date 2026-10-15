namespace SparseTypes;

/// <summary>Selects a rebase policy for a model member without requiring a custom merge strategy.</summary>
/// <remarks>Policies apply to scalar and whole-replace members; granular collections always use their merge strategy.</remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class SparseRebasePolicyAttribute : Attribute
{
    /// <summary>Uses a policy type implementing <see cref="FragmentRebasePolicy{T}"/> for the member type.</summary>
    public SparseRebasePolicyAttribute(Type policyType)
    {
        ArgumentNullException.ThrowIfNull(policyType);
        PolicyType = policyType;
    }

    /// <summary>The custom rebase policy type.</summary>
    public Type? PolicyType { get; }
}
