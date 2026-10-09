using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Builds the compilation-scoped read-only adapter source (#181).</summary>
/// <remarks>
/// Ports the model-independent streaming adapters out of
/// <c>SparseReadOnlyAdapterEmitter</c> verbatim: the list adapter keeps its
/// O(1)/re-enumerating Count/indexer contract (issue #172), the dictionary
/// adapter maps values lazily, and the entries adapter maps keys and values.
/// Per-model <c>ReadOnlyView</c> output instantiates these shared types with
/// model-specific mappings instead of redefining them.
/// </remarks>
internal static class SparseGeneratedOnceReadOnlyAdapters
{
    /// <summary>Builds the shared adapter source for the explicit namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="includeCollection">Whether any model needs the list adapter.</param>
    /// <param name="includeDictionary">Whether any model needs the dictionary adapter.</param>
    /// <param name="includeEntries">Whether any model needs the entries adapter.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(
        string implementationNamespace,
        bool includeCollection,
        bool includeDictionary,
        bool includeEntries,
        CancellationToken cancellationToken
    )
    {
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(
            code,
            implementationNamespace,
            cancellationToken
        );
        if (includeCollection)
        {
            AppendCollectionAdapter(code, implementationNamespace);
        }

        if (includeDictionary)
        {
            AppendDictionaryAdapter(code, implementationNamespace);
        }

        if (includeEntries)
        {
            AppendDictionaryEntriesAdapter(code);
        }

        code.AppendLine("}");
        return code.ToString();
    }

    private static void AppendCollectionAdapter(
        SharedIndentedBuilder code,
        string implementationNamespace
    )
    {
        code.AppendLineAt(
            1,
            "/// <summary>Streaming read-only list view over a live enumerable source.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>Sources must be finite, repeatable enumerables that stay stable during a view read (issue #172). <c>Count</c> is O(1) for <c>ICollection</c>/<c>IReadOnlyCollection</c> sources and a full enumeration otherwise; the indexer is O(1) for <c>IList</c>/<c>IReadOnlyList</c> sources and re-enumerates from the start otherwise, so a full indexed loop over a streaming source is quadratic and should be a <c>foreach</c> instead. Each access enumerates anew with no snapshot: single-pass sources drain and per-enumeration-varying sources reflect the latest enumeration.</remarks>"
        );
        code.AppendLineAt(
            1,
            "internal sealed class "
                + SparseGeneratedOnceNames.ReadOnlyListAdapter
                + "<TSource, TView> : global::System.Collections.Generic.IReadOnlyList<TView>"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "private readonly global::System.Collections.Generic.IEnumerable<TSource> _source;"
        );
        code.AppendLineAt(2, "private readonly global::System.Func<TSource, TView> _map;");
        code.AppendLineAt(
            2,
            "public "
                + SparseGeneratedOnceNames.ReadOnlyListAdapter
                + "(global::System.Collections.Generic.IEnumerable<TSource> source, global::System.Func<TSource, TView> map)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_source = source;");
        code.AppendLineAt(3, "_map = map;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "public int Count => _source is global::System.Collections.Generic.ICollection<TSource> collection ? collection.Count : _source is global::System.Collections.Generic.IReadOnlyCollection<TSource> readOnly ? readOnly.Count : global::System.Linq.Enumerable.Count(_source);"
        );
        code.AppendLineAt(
            2,
            "public TView this[int index] => _source is global::System.Collections.Generic.IList<TSource> list ? _map(list[index]) : _source is global::System.Collections.Generic.IReadOnlyList<TSource> readOnly ? _map(readOnly[index]) : __SparseGet(index);"
        );
        code.AppendLineAt(2, "private TView __SparseGet(int index)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (index < 0) throw new global::System.ArgumentOutOfRangeException(nameof(index));"
        );
        code.AppendLineAt(3, "var current = 0;");
        code.AppendLineAt(
            3,
            "foreach (var item in _source) { if (current++ == index) return _map(item); }"
        );
        code.AppendLineAt(
            3,
            "throw new global::System.ArgumentOutOfRangeException(nameof(index));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerator<TView> GetEnumerator() => global::System.Linq.Enumerable.Select(_source, _map).GetEnumerator();"
        );
        code.AppendLineAt(
            2,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(1, "}");
        _ = implementationNamespace;
    }

    private static void AppendDictionaryAdapter(
        SharedIndentedBuilder code,
        string implementationNamespace
    )
    {
        var collection = SparseGeneratedOnceNames.QualifiedListAdapter(implementationNamespace);
        code.AppendLineAt(
            1,
            "internal sealed class "
                + SparseGeneratedOnceNames.ReadOnlyDictionaryAdapter
                + "<TKey, TSource, TView> : global::System.Collections.Generic.IReadOnlyDictionary<TKey, TView> where TKey : notnull"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "private readonly global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> _source;"
        );
        code.AppendLineAt(2, "private readonly global::System.Func<TSource, TView> _map;");
        code.AppendLineAt(
            2,
            "public "
                + SparseGeneratedOnceNames.ReadOnlyDictionaryAdapter
                + "(global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> source, global::System.Func<TSource, TView> map)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_source = source;");
        code.AppendLineAt(3, "_map = map;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "public int Count => _source.Count;");
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerable<TKey> Keys => new "
                + collection
                + "<TKey, TKey>(_source.Keys, static key => key);"
        );
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerable<TView> Values => global::System.Linq.Enumerable.Select(_source.Values, _map);"
        );
        code.AppendLineAt(2, "public TView this[TKey key] => _map(_source[key]);");
        code.AppendLineAt(2, "public bool ContainsKey(TKey key) => _source.ContainsKey(key);");
        code.AppendLineAt(2, "public bool TryGetValue(TKey key, out TView value)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (_source.TryGetValue(key, out var current)) { value = _map(current); return true; }"
        );
        code.AppendLineAt(3, "value = default!;");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TView>> GetEnumerator()"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "foreach (var pair in _source) yield return new global::System.Collections.Generic.KeyValuePair<TKey, TView>(pair.Key, _map(pair.Value));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(1, "}");
    }

    private static void AppendDictionaryEntriesAdapter(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            1,
            "internal sealed class "
                + SparseGeneratedOnceNames.ReadOnlyDictionaryEntriesAdapter
                + "<TKey, TSource, TKeyView, TValueView> : global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>> where TKey : notnull"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "private readonly global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> _source;"
        );
        code.AppendLineAt(2, "private readonly global::System.Func<TKey, TKeyView> _mapKey;");
        code.AppendLineAt(
            2,
            "private readonly global::System.Func<TSource, TValueView> _mapValue;"
        );
        code.AppendLineAt(
            2,
            "public "
                + SparseGeneratedOnceNames.ReadOnlyDictionaryEntriesAdapter
                + "(global::System.Collections.Generic.IReadOnlyDictionary<TKey, TSource> source, global::System.Func<TKey, TKeyView> mapKey, global::System.Func<TSource, TValueView> mapValue)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_source = source;");
        code.AppendLineAt(3, "_mapKey = mapKey;");
        code.AppendLineAt(3, "_mapValue = mapValue;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "public int Count => _source.Count;");
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>> GetEnumerator()"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "foreach (var pair in _source) yield return new global::System.Collections.Generic.KeyValuePair<TKeyView, TValueView>(_mapKey(pair.Key), _mapValue(pair.Value));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(1, "}");
    }
}
