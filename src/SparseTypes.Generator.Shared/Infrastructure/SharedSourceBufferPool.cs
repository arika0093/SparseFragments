using System;
using System.Text;

namespace SparseTypes.Generator.Shared;

internal static class SharedSourceBufferPool
{
    // Share one character budget across four owners rather than reserving it per buffer.
    private const int SlotCount = 4;
    private const int OrdinaryCapacity = 128 * 1024;
    private const int MaximumCapacity = SlotCount * OrdinaryCapacity;

    [ThreadStatic]
    private static StringBuilder?[]? _buffers;

    /// <summary>Rents an empty source buffer with exclusive ownership.</summary>
    /// <returns>An empty buffer.</returns>
    public static StringBuilder Rent()
    {
        var buffers = _buffers;
        if (buffers is not null)
        {
            for (var index = 0; index < buffers.Length; index++)
            {
                var buffer = buffers[index];
                if (buffer is null)
                    continue;
                buffers[index] = null;
                return buffer;
            }
        }
        return new StringBuilder();
    }

    /// <summary>Rents an empty buffer with at least the requested capacity.</summary>
    /// <param name="initialCapacity">Minimum capacity in characters.</param>
    /// <returns>An empty buffer with exclusive ownership.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is negative.</exception>
    public static StringBuilder Rent(int initialCapacity)
    {
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        var compatible = TryRent(initialCapacity);
        if (compatible is not null)
            return compatible;
        // Preserve ordinary cached buffers when no retained buffer fits a large rewrite.
        if (initialCapacity > OrdinaryCapacity)
            return new StringBuilder(initialCapacity);
        var buffer = Rent();
        buffer.EnsureCapacity(initialCapacity);
        return buffer;
    }

    private static StringBuilder? TryRent(int minimumCapacity)
    {
        var buffers = _buffers;
        if (buffers is null)
            return null;
        var best = -1;
        for (var index = 0; index < buffers.Length; index++)
        {
            var buffer = buffers[index];
            if (buffer is null || buffer.Capacity < minimumCapacity)
                continue;
            if (best < 0 || buffer.Capacity < buffers[best]!.Capacity)
                best = index;
        }
        if (best < 0)
            return null;
        var result = buffers[best];
        buffers[best] = null;
        return result;
    }

    /// <summary>Returns a buffer if a bounded cache slot is available.</summary>
    /// <param name="buffer">A buffer whose owner has finished using it.</param>
    public static void Return(StringBuilder buffer)
    {
        if (buffer.Capacity > MaximumCapacity)
            return;
        var buffers = _buffers ??= new StringBuilder?[SlotCount];
        var smallest = -1;
        var empty = -1;
        var retainedCapacity = 0;
        for (var index = 0; index < buffers.Length; index++)
        {
            var cached = buffers[index];
            if (cached is null)
            {
                if (empty < 0)
                    empty = index;
                continue;
            }
            retainedCapacity += cached.Capacity;
            if (smallest < 0 || cached.Capacity < buffers[smallest]!.Capacity)
                smallest = index;
        }
        var target = empty;
        if (target < 0)
        {
            var previousCapacity = buffers[smallest]!.Capacity;
            if (buffer.Capacity <= previousCapacity)
                return;
            retainedCapacity -= previousCapacity;
            target = smallest;
        }
        if (retainedCapacity + buffer.Capacity > MaximumCapacity)
            return;
        buffer.Clear();
        buffers[target] = buffer;
    }
}
