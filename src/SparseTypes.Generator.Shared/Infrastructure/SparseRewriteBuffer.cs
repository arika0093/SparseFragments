using System;
using System.Text;
using System.Threading;

namespace SparseTypes.Generator.Shared;

internal sealed class SparseRewriteBuffer : IDisposable
{
    // IndexOf uses -1 for exhaustion; -2 keeps disposal distinct without
    // another field.
    private const int Disposed = -2;
    private readonly string _source;
    private readonly string _oldValue;
    private readonly string _newValue;
    private StringBuilder? _builder;
    private int _match;
    private int _copied;

    internal SparseRewriteBuffer(string source, string oldValue, string newValue, int firstMatch)
    {
        _source = source;
        _oldValue = oldValue;
        _newValue = newValue;
        _match = firstMatch;
    }

    internal bool HasCandidate => _match >= 0;

    internal int NextCandidate => _match;

    internal void MarkCode(int index)
    {
        ThrowIfDisposed();
        if (index >= _match)
        {
            ReplaceAt(index);
        }
    }

    private void ReplaceAt(int index)
    {
        if (index > _match)
        {
            _match = _source.IndexOf(_oldValue, index, StringComparison.Ordinal);
        }
        if (index != _match)
        {
            return;
        }
        _builder ??= SharedSourceBufferPool.Rent(_source.Length);
        _builder.Append(_source, _copied, index - _copied);
        _builder.Append(_newValue);
        _copied = index + _oldValue.Length;
        _match = _source.IndexOf(_oldValue, _copied, StringComparison.Ordinal);
    }

    internal string BuildResult()
    {
        ThrowIfDisposed();
        return _builder is null
            ? _source
            : _builder.Append(_source, _copied, _source.Length - _copied).ToString();
    }

    /// <summary>Returns the source buffer after the rewrite completes or fails.</summary>
    public void Dispose()
    {
        _match = Disposed;
        var builder = Interlocked.Exchange(ref _builder, null);
        if (builder is not null)
            SharedSourceBufferPool.Return(builder);
    }

    private void ThrowIfDisposed()
    {
        if (_match == Disposed)
            throw new ObjectDisposedException(nameof(SparseRewriteBuffer));
    }
}
