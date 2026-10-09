namespace SparseFragments;

/// <summary>Selects a rebase policy for a model member without requiring a custom merge strategy.</summary>
/// <remarks>
/// The policy type must derive from <see cref="FragmentRebasePolicy{T}"/>
/// with <c>T</c> exactly matching the member type. It applies to scalar and
/// whole-replace members; nested, keyed, dictionary, and merge-collection
/// members always reconcile granularly. A member-level policy takes
/// precedence over the merge strategy's <c>TryRebase</c> during rebase.
/// </remarks>
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
