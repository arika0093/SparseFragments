namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>Identifies the complete collection ancestry for nested correlation.</summary>
/// <remarks>Each segment retains its typed key comparer or temporary identity.</remarks>
internal sealed class CollectionScopePath
{
    private readonly CollectionScopeIdentity[] _segments;

    private CollectionScopePath(CollectionScopeIdentity[] segments)
    {
        _segments = segments;
    }

    public static CollectionScopePath Empty { get; } = new([]);

    public bool IsEmpty => _segments.Length == 0;

    public CollectionScopePath Append(CollectionScopeIdentity scope)
    {
        if (scope.Kind == CollectionScopeKind.Unscoped)
        {
            return this;
        }

        var segments = new CollectionScopeIdentity[_segments.Length + 1];
        _segments.CopyTo(segments, 0);
        segments[^1] = scope;
        return new CollectionScopePath(segments);
    }

    public bool Matches(CollectionScopePath other)
    {
        if (_segments.Length != other._segments.Length)
        {
            return false;
        }

        for (var index = 0; index < _segments.Length; index++)
        {
            if (!Matches(_segments[index], other._segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(CollectionScopeIdentity left, CollectionScopeIdentity right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            CollectionScopeKind.Assigned when left.Key is not null && right.Key is not null => (
                left.KeyComparer ?? right.KeyComparer ?? EqualityComparer<object>.Default
            ).Equals(left.Key, right.Key),
            CollectionScopeKind.Temporary => left.Temp == right.Temp,
            _ => true,
        };
    }
}
