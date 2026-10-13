using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace SparseTypes.Generator.Shared;

internal sealed class SharedIndentedBuilder : IDisposable
{
    private StringBuilder? _builder = SharedSourceBufferPool.Rent();
    private readonly CancellationToken _cancellationToken;
    private bool _atLineStart = true;

    public SharedIndentedBuilder(CancellationToken cancellationToken) =>
        _cancellationToken = cancellationToken;

    public CancellationToken CancellationToken => _cancellationToken;

    public int IndentOffset { get; set; }

    private StringBuilder Buffer =>
        _builder ?? throw new ObjectDisposedException(nameof(SharedIndentedBuilder));

    public SharedIndentedBuilder AppendIndent(int level)
    {
        var buffer = Buffer;
        var effective = level + IndentOffset;
        if (effective < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        if (!_atLineStart)
        {
            throw new InvalidOperationException(
                "Explicit indentation requires the start of a line."
            );
        }

        buffer.Append(' ', effective * 4);
        _atLineStart = false;
        return this;
    }

    public SharedIndentedBuilder Append(string value) => AppendRange(value, 0, value.Length);

    /// <summary>Appends a source range while preserving line indentation and cancellation.</summary>
    /// <param name="value">Source text.</param>
    /// <param name="start">First character to append.</param>
    /// <param name="length">Number of characters to append.</param>
    /// <returns>This builder.</returns>
    public SharedIndentedBuilder Append(string value, int start, int length)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        if (start < 0 || start > value.Length)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (length < 0 || length > value.Length - start)
            throw new ArgumentOutOfRangeException(nameof(length));
        return AppendRange(value, start, start + length);
    }

    public SharedIndentedBuilder Append(char value)
    {
        if (value != '\n' && _atLineStart)
        {
            AppendIndent(0);
        }
        Buffer.Append(value);
        _atLineStart = value == '\n';
        return this;
    }

    public SharedIndentedBuilder Append(int value)
    {
        Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    private SharedIndentedBuilder AppendRange(string value, int start, int limit)
    {
        var buffer = Buffer;
        while (start < limit)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var newline = value.IndexOf('\n', start, limit - start);
            var end = newline < 0 ? limit : newline;
            if (end > start)
            {
                if (_atLineStart)
                {
                    AppendIndent(0);
                }
                buffer.Append(value, start, end - start);
            }
            if (newline < 0)
            {
                break;
            }
            buffer.Append('\n');
            _atLineStart = true;
            start = newline + 1;
        }
        return this;
    }

    public SharedIndentedBuilder AppendLine(string value = "")
    {
        Append(value);
        Append('\n');
        return this;
    }

    public SharedIndentedBuilder AppendLineAt(int level, string value)
    {
        if (value.Length != 0)
        {
            AppendIndent(level);
        }
        return AppendLine(value);
    }

    public override string ToString()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        return Buffer.ToString();
    }

    /// <summary>Returns the source buffer for reuse; repeated disposal has no effect.</summary>
    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _builder, null);
        if (buffer is not null)
            SharedSourceBufferPool.Return(buffer);
    }
}
