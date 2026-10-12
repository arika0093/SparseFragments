using System.ComponentModel;
using System.Threading;

namespace SparseFragments.CompilerServices;

/// <summary>Owns an empty key set for one generated uniqueness check.</summary>
/// <typeparam name="TKey">The collection key type.</typeparam>
/// <remarks>The set must not be retained or accessed after disposal.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparseKeyValidationScope<TKey> : IDisposable
    where TKey : notnull
{
    private HashSet<TKey>? _keys;
    private readonly int _capacity;

    internal SparseKeyValidationScope(HashSet<TKey> keys, int capacity)
    {
        _keys = keys;
        _capacity = capacity;
    }

    /// <summary>Gets the set exclusively owned by this scope.</summary>
    /// <exception cref="ObjectDisposedException">The scope has been disposed.</exception>
    public HashSet<TKey> Keys =>
        _keys ?? throw new ObjectDisposedException(nameof(SparseKeyValidationScope<TKey>));

    /// <summary>Returns bounded storage for reuse on the current thread.</summary>
    public void Dispose()
    {
        var keys = Interlocked.Exchange(ref _keys, null);
        if (keys is not null)
            SparseKeyValidationPool<TKey>.Return(keys, _capacity);
    }
}

internal static class SparseKeyValidationPool<TKey>
    where TKey : notnull
{
    private const int MaximumRetainedEntries = 8192;

    [ThreadStatic]
    private static KeyValuePair<HashSet<TKey>?, int> _cached;

    internal static SparseKeyValidationScope<TKey> Rent(
        int capacity,
        Func<int, HashSet<TKey>> create
    )
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (create is null)
            throw new ArgumentNullException(nameof(create));

        var keys = _cached.Key;
        if (keys is null || _cached.Value < capacity)
            keys = create(capacity);
        else
            capacity = _cached.Value;
        _cached = default;
        return new SparseKeyValidationScope<TKey>(keys, capacity);
    }

    internal static void Return(HashSet<TKey> keys, int capacity)
    {
        capacity = Math.Max(capacity, keys.Count);
        if (capacity > MaximumRetainedEntries)
            return;
        keys.Clear();
        if (_cached.Key is null || capacity > _cached.Value)
        {
            _cached = new KeyValuePair<HashSet<TKey>?, int>(keys, capacity);
        }
    }
}
