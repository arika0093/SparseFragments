namespace SparseFragments.Generator.Shared;

internal static class SparseReadOnlyAdapterEmitter
{
    internal static void AppendCollectionAdapter(SharedIndentedBuilder code, string typeName)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Streaming read-only list view over a live enumerable source.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Sources must be finite, repeatable enumerables that stay stable during a view read (issue #172). <c>Count</c> is O(1) for <c>ICollection</c>/<c>IReadOnlyCollection</c> sources and a full enumeration otherwise; the indexer is O(1) for <c>IList</c>/<c>IReadOnlyList</c> sources and re-enumerates from the start otherwise, so a full indexed loop over a streaming source is quadratic and should be a <c>foreach</c> instead. Each access enumerates anew with no snapshot: single-pass sources drain and per-enumeration-varying sources reflect the latest enumeration.</remarks>"
        );
        code.AppendLineAt(
            2,
            "private sealed class "
                + typeName
                + "<TSource, TView> : global::System.Collections.Generic.IReadOnlyList<TView>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.IEnumerable<TSource> _source;"
        );
        code.AppendLineAt(3, "private readonly global::System.Func<TSource, TView> _map;");
        code.AppendLineAt(
            3,
            "public "
                + typeName
                + "(global::System.Collections.Generic.IEnumerable<TSource> source, global::System.Func<TSource, TView> map)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_source = source;");
        code.AppendLineAt(4, "_map = map;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "public int Count => _source is global::System.Collections.Generic.ICollection<TSource> collection ? collection.Count : _source is global::System.Collections.Generic.IReadOnlyCollection<TSource> readOnly ? readOnly.Count : global::System.Linq.Enumerable.Count(_source);"
        );
        code.AppendLineAt(
            3,
            "public TView this[int index] => _source is global::System.Collections.Generic.IList<TSource> list ? _map(list[index]) : _source is global::System.Collections.Generic.IReadOnlyList<TSource> readOnly ? _map(readOnly[index]) : __SparseGet(index);"
        );
        code.AppendLineAt(3, "private TView __SparseGet(int index)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (index < 0) throw new global::System.ArgumentOutOfRangeException(nameof(index));"
        );
        code.AppendLineAt(4, "var current = 0;");
        code.AppendLineAt(
            4,
            "foreach (var item in _source) { if (current++ == index) return _map(item); }"
        );
        code.AppendLineAt(
            4,
            "throw new global::System.ArgumentOutOfRangeException(nameof(index));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<TView> GetEnumerator() => global::System.Linq.Enumerable.Select(_source, _map).GetEnumerator();"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(2, "}");
    }

    internal static void AppendDictionaryAdapter(
        SharedIndentedBuilder code,
        string typeName,
        string collectionName
    )
    {
        code.AppendLineAt(
            2,
            "private sealed class "
                + typeName
                + "<TKey, TSource, TView> : global::System.Collections.Generic.IReadOnlyDictionary<TKey, TView> where TKey : notnull"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> _source;"
        );
        code.AppendLineAt(3, "private readonly global::System.Func<TSource, TView> _map;");
        code.AppendLineAt(
            3,
            "public "
                + typeName
                + "(global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> source, global::System.Func<TSource, TView> map)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_source = source;");
        code.AppendLineAt(4, "_map = map;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public int Count => _source.Count;");
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerable<TKey> Keys => new "
                + collectionName
                + "<TKey, TKey>(_source.Keys, static key => key);"
        );
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerable<TView> Values => global::System.Linq.Enumerable.Select(_source.Values, _map);"
        );
        code.AppendLineAt(3, "public TView this[TKey key] => _map(_source[key]);");
        code.AppendLineAt(3, "public bool ContainsKey(TKey key) => _source.ContainsKey(key);");
        code.AppendLineAt(3, "public bool TryGetValue(TKey key, out TView value)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (_source.TryGetValue(key, out var current)) { value = _map(current); return true; }"
        );
        code.AppendLineAt(4, "value = default!;");
        code.AppendLineAt(4, "return false;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TView>> GetEnumerator()"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "foreach (var pair in _source) yield return new global::System.Collections.Generic.KeyValuePair<TKey, TView>(pair.Key, _map(pair.Value));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(2, "}");
    }

    internal static void AppendDictionaryEntriesAdapter(SharedIndentedBuilder code, string typeName)
    {
        code.AppendLineAt(
            2,
            "private sealed class "
                + typeName
                + "<TKey, TSource, TKeyView, TValueView> : global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>> where TKey : notnull"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> _source;"
        );
        code.AppendLineAt(3, "private readonly global::System.Func<TKey, TKeyView> _mapKey;");
        code.AppendLineAt(
            3,
            "private readonly global::System.Func<TSource, TValueView> _mapValue;"
        );
        code.AppendLineAt(
            3,
            "public "
                + typeName
                + "(global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> source, global::System.Func<TKey, TKeyView> mapKey, global::System.Func<TSource, TValueView> mapValue)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_source = source;");
        code.AppendLineAt(4, "_mapKey = mapKey;");
        code.AppendLineAt(4, "_mapValue = mapValue;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public int Count => _source.Count;");
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>> GetEnumerator()"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "foreach (var pair in _source) yield return new global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>(_mapKey(pair.Key), _mapValue(pair.Value));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(2, "}");
    }
}
