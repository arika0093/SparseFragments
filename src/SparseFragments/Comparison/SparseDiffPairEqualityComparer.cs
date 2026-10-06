using System.Runtime.CompilerServices;

namespace SparseFragments;

/// <summary>Pair-identity comparer used by generated diff cycle scopes.</summary>
/// <remarks>
/// Two pairs are equal only when both keys and both values are reference-identical.
/// Hash codes combine the identity hash codes so path membership checks stay O(1) expected.
/// </remarks>
internal sealed class SparseDiffPairEqualityComparer
    : IEqualityComparer<KeyValuePair<object, object>>
{
    /// <summary>The shared comparer instance.</summary>
    public static SparseDiffPairEqualityComparer Instance { get; } = new();

    private SparseDiffPairEqualityComparer() { }

    /// <summary>Compares two pairs for reference identity on both sides.</summary>
    public bool Equals(KeyValuePair<object, object> x, KeyValuePair<object, object> y) =>
        ReferenceEquals(x.Key, y.Key) && ReferenceEquals(x.Value, y.Value);

    /// <summary>Returns the combined identity hash code.</summary>
    public int GetHashCode(KeyValuePair<object, object> obj)
    {
        unchecked
        {
            var before = obj.Key is null ? 0 : RuntimeHelpers.GetHashCode(obj.Key);
            var after = obj.Value is null ? 0 : RuntimeHelpers.GetHashCode(obj.Value);
            return (before * 397) ^ after;
        }
    }
}
