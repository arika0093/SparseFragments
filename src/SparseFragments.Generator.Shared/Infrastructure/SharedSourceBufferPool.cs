using System;
using System.Text;

namespace SparseFragments.Generator.Shared;

internal static class SharedSourceBufferPool
{
    // Four simultaneous source builders fit the usual model emission. Bound retention per thread.
    private const int SlotCount = 4;
    private const int MaximumCapacity = 128 * 1024;

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

    /// <summary>Returns a buffer if a bounded cache slot is available.</summary>
    /// <param name="buffer">A buffer whose owner has finished using it.</param>
    public static void Return(StringBuilder buffer)
    {
        if (buffer.Capacity > MaximumCapacity)
            return;
        var buffers = _buffers ??= new StringBuilder?[SlotCount];
        var smallest = 0;
        for (var index = 0; index < buffers.Length; index++)
        {
            var cached = buffers[index];
            if (cached is null)
            {
                buffer.Clear();
                buffers[index] = buffer;
                return;
            }
            if (cached.Capacity < buffers[smallest]!.Capacity)
                smallest = index;
        }
        // Small nested emitters must not displace the large outer source buffers.
        if (buffer.Capacity > buffers[smallest]!.Capacity)
        {
            buffer.Clear();
            buffers[smallest] = buffer;
        }
    }
}
