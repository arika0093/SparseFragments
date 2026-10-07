using System.Buffers;

namespace SparseFragments;

/// <summary>Pooled temporary storage for JSON output with an independent final byte array.</summary>
internal sealed class PooledJsonBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[]? _buffer;
    private int _written;
    private bool _disposed;

    /// <inheritdoc />
    public void Advance(int count)
    {
        if (count < 0 || _buffer is null || count > _buffer.Length - _written)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        _written += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>Copies the written output into an independently owned array.</summary>
    internal byte[] ToArray() => _buffer.AsSpan(0, _written).ToArray();

    private void EnsureCapacity(int sizeHint)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PooledJsonBufferWriter));
        }
        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }
        sizeHint = Math.Max(sizeHint, 1);
        if (_buffer is null)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, 256));
        }
        else if (sizeHint > _buffer.Length - _written)
        {
            var doubled = _buffer.Length <= int.MaxValue / 2 ? _buffer.Length * 2 : int.MaxValue;
            var replacement = ArrayPool<byte>.Shared.Rent(
                Math.Max(checked(_written + sizeHint), doubled)
            );
            _buffer.AsSpan(0, _written).CopyTo(replacement);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = replacement;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
        _disposed = true;
    }
}
