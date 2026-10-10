using System;
using System.Text;

namespace SparseFragments.Generator.Shared;

internal sealed class SparseRewriteBuffer
{
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

    internal void MarkCode(int index)
    {
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
        _builder ??= new StringBuilder(_source.Length);
        _builder.Append(_source, _copied, index - _copied);
        _builder.Append(_newValue);
        _copied = index + _oldValue.Length;
        _match = _source.IndexOf(_oldValue, _copied, StringComparison.Ordinal);
    }

    internal string BuildResult() =>
        _builder is null
            ? _source
            : _builder.Append(_source, _copied, _source.Length - _copied).ToString();
}
