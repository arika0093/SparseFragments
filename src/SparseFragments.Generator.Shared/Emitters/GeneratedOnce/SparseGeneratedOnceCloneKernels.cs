using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Builds the compilation-scoped collection clone kernel source (#182).</summary>
/// <remarks>
/// Ports the model-independent clone kernels out of
/// <c>SparseFragmentCollectionCloneEmitter</c> verbatim: count probing,
/// set/dictionary/array/list kernels with reference-identity tracking, cycle
/// handling, comparer preservation and the TFM-specific
/// <c>IReadOnlySet{T}</c> bridge. Per-model clone code passes model-specific
/// typed item/key/value clone delegates plus the shared clone context and
/// calls these kernels through the qualified container name.
/// </remarks>
internal static class SparseGeneratedOnceCloneKernels
{
    /// <summary>Builds the shared clone kernel source for the explicit namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="includePortableSetView">Whether the portable set bridge is needed.</param>
    /// <param name="hashSetSupportsCapacity">Whether <c>HashSet{T}</c> keeps a capacity constructor.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(
        string implementationNamespace,
        bool includePortableSetView,
        bool hashSetSupportsCapacity,
        CancellationToken cancellationToken
    )
    {
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(
            code,
            implementationNamespace,
            cancellationToken
        );
        code.AppendLineAt(
            1,
            "/// <summary>Shared generic collection clone kernels with identity tracking.</summary>"
        );
        code.AppendLineAt(1, "internal static class " + SparseGeneratedOnceNames.CloneKernels);
        code.AppendLineAt(1, "{");
        AppendKernels(code, includePortableSetView, hashSetSupportsCapacity);
        code.AppendLineAt(1, "}");
        code.AppendLine("}");
        return code.ToString();
    }

    private static void AppendKernels(
        SharedIndentedBuilder code,
        bool includePortableSetView,
        bool hashSetSupportsCapacity
    )
    {
        var setCapacity = hashSetSupportsCapacity ? "__CloneCollectionCount(source), " : "";
        code.AppendLineAt(
            2,
            "internal static int __CloneCollectionCount<T>(global::System.Collections.Generic.IEnumerable<T> source) => (source as global::System.Collections.Generic.ICollection<T>)?.Count ?? (source as global::System.Collections.Generic.IReadOnlyCollection<T>)?.Count ?? 0;"
        );
        code.AppendLineAt(
            2,
            "internal static TSet __CloneSet<T, TSet>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, T>? cloneElement)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (context.TryGetValue(source, out var existing))");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (existing is TSet typed) return typed;");
        if (includePortableSetView)
            code.AppendLineAt(
                4,
                "if (existing is __SparseReadOnlySet<T> view && view.Inner is TSet inner) return inner;"
            );
        code.AppendLineAt(4, "return (TSet)existing;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "global::System.Collections.Generic.ISet<T> clone;");
        code.AppendLineAt(
            3,
            "clone = new global::System.Collections.Generic.HashSet<T>("
                + setCapacity
                + "(source as global::System.Collections.Generic.HashSet<T>)?.Comparer);"
        );
        code.AppendLineAt(3, "context.Add(source, clone);");
        // A HashSet copy constructor can retain a source's oversized capacity and
        // cross the LOH threshold. Union into the count-sized set instead.
        code.AppendLineAt(3, "if (cloneElement is null) clone.UnionWith(source);");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var item in source) clone.Add(cloneElement(item));");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return (TSet)(object)clone;");
        code.AppendLineAt(2, "}");
        if (includePortableSetView)
        {
            code.AppendLineAt(
                2,
                "internal static TSet __CloneSetView<T, TSet>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, T>? cloneElement)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "if (context.TryGetValue(source, out var existing))");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "if (existing is TSet typed) return typed;");
            code.AppendLineAt(
                4,
                "if (existing is global::System.Collections.Generic.ISet<T> existingSet)"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "var bridged = new __SparseReadOnlySet<T>(existingSet);");
            code.AppendLineAt(5, "context[source] = bridged;");
            code.AppendLineAt(5, "return (TSet)(object)bridged;");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "return (TSet)existing;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "global::System.Collections.Generic.ISet<T> clone;");
            code.AppendLineAt(
                3,
                "if (source is __SparseReadOnlySet<T> sourceView) clone = sourceView.CloneEmpty();"
            );
            code.AppendLineAt(
                3,
                "else clone = new global::System.Collections.Generic.HashSet<T>("
                    + setCapacity
                    + "(source as global::System.Collections.Generic.HashSet<T>)?.Comparer);"
            );
            code.AppendLineAt(3, "var view = new __SparseReadOnlySet<T>(clone);");
            code.AppendLineAt(3, "context.Add(source, view);");
            code.AppendLineAt(3, "if (cloneElement is null) clone.UnionWith(source);");
            code.AppendLineAt(3, "else");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "foreach (var item in source) view.Add(cloneElement(item));");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "return (TSet)(object)view;");
            code.AppendLineAt(2, "}");
        }
        code.AppendLineAt(
            2,
            "internal static TDictionary __CloneDictionary<TKey, TValue, TDictionary>(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<TKey, TKey>? cloneKey, global::System.Func<TValue, TValue>? cloneValue) where TKey : notnull"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (context.TryGetValue(source, out var existing)) return (TDictionary)existing;"
        );
        code.AppendLineAt(3, "global::System.Collections.Generic.IDictionary<TKey, TValue> clone;");
        code.AppendLineAt(
            3,
            "if (cloneKey is null && cloneValue is null && source is global::System.Collections.Generic.Dictionary<TKey, TValue> dictionary)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            3,
            "clone = new global::System.Collections.Generic.Dictionary<TKey, TValue>(dictionary, dictionary.Comparer);"
        );
        code.AppendLineAt(3, "context.Add(source, clone);");
        code.AppendLineAt(3, "return (TDictionary)(object)clone;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (source is global::System.Collections.Generic.SortedDictionary<TKey, TValue> sorted) clone = new global::System.Collections.Generic.SortedDictionary<TKey, TValue>(sorted.Comparer);"
        );
        code.AppendLineAt(
            3,
            "else if (source is global::System.Collections.Generic.SortedList<TKey, TValue> sortedList) clone = new global::System.Collections.Generic.SortedList<TKey, TValue>(sortedList.Comparer);"
        );
        code.AppendLineAt(
            3,
            "else clone = new global::System.Collections.Generic.Dictionary<TKey, TValue>(__CloneCollectionCount(source), (source as global::System.Collections.Generic.Dictionary<TKey, TValue>)?.Comparer);"
        );
        code.AppendLineAt(3, "context.Add(source, clone);");
        code.AppendLineAt(
            3,
            "foreach (var pair in source) clone.Add(cloneKey is null ? pair.Key : cloneKey(pair.Key), cloneValue is null ? pair.Value : cloneValue(pair.Value));"
        );
        code.AppendLineAt(3, "return (TDictionary)(object)clone;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static TCollection __CloneArray<T, TCollection>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, T>? cloneElement)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (context.TryGetValue(source, out var existing)) return (TCollection)existing;"
        );
        code.AppendLineAt(
            3,
            "if (source is global::System.Collections.Generic.List<T>) return __CloneList<T, TCollection>(source, context, cloneElement);"
        );
        code.AppendLineAt(
            3,
            "if (source is global::System.Collections.Generic.HashSet<T>) return __CloneSet<T, TCollection>(source, context, cloneElement);"
        );
        code.AppendLineAt(
            3,
            "var values = source as T[] ?? global::System.Linq.Enumerable.ToArray(source);"
        );
        code.AppendLineAt(3, "var clone = new T[values.Length];");
        code.AppendLineAt(3, "context.Add(source, clone);");
        code.AppendLineAt(
            3,
            "if (cloneElement is null) global::System.Array.Copy(values, clone, values.Length);"
        );
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "for (var index = 0; index < values.Length; index++) clone[index] = cloneElement(values[index]);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return (TCollection)(object)clone;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static TCollection __CloneList<T, TCollection>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Collections.Generic.Dictionary<object, object> context, global::System.Func<T, T>? cloneElement)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (context.TryGetValue(source, out var existing)) return (TCollection)existing;"
        );
        code.AppendLineAt(
            3,
            "if (source is T[]) return __CloneArray<T, TCollection>(source, context, cloneElement);"
        );
        code.AppendLineAt(
            3,
            "var clone = new global::System.Collections.Generic.List<T>(__CloneCollectionCount(source));"
        );
        code.AppendLineAt(3, "context.Add(source, clone);");
        code.AppendLineAt(3, "if (cloneElement is null) clone.AddRange(source);");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "foreach (var item in source) clone.Add(cloneElement(item));");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return (TCollection)(object)clone;");
        code.AppendLineAt(2, "}");
        if (includePortableSetView)
        {
            AppendPortableSetView(code, hashSetSupportsCapacity);
        }
    }

    private static void AppendPortableSetView(
        SharedIndentedBuilder code,
        bool hashSetSupportsCapacity
    )
    {
        code.AppendLineAt(
            2,
            "internal sealed class __SparseReadOnlySet<T> : global::System.Collections.Generic.ISet<T>, global::System.Collections.Generic.IReadOnlySet<T>"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.ISet<T> __inner;"
        );
        code.AppendLineAt(
            3,
            "public __SparseReadOnlySet(global::System.Collections.Generic.ISet<T> inner) => __inner = inner;"
        );
        code.AppendLineAt(3, "public global::System.Collections.Generic.ISet<T> Inner => __inner;");
        code.AppendLineAt(3, "public int Count => __inner.Count;");
        code.AppendLineAt(3, "public bool IsReadOnly => __inner.IsReadOnly;");
        code.AppendLineAt(3, "public bool Add(T item) => __inner.Add(item);");
        code.AppendLineAt(
            3,
            "void global::System.Collections.Generic.ICollection<T>.Add(T item) => __inner.Add(item);"
        );
        code.AppendLineAt(3, "public void Clear() => __inner.Clear();");
        code.AppendLineAt(3, "public bool Contains(T item) => __inner.Contains(item);");
        code.AppendLineAt(
            3,
            "public void CopyTo(T[] array, int arrayIndex) => __inner.CopyTo(array, arrayIndex);"
        );
        code.AppendLineAt(3, "public bool Remove(T item) => __inner.Remove(item);");
        code.AppendLineAt(
            3,
            "public void ExceptWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.ExceptWith(other);"
        );
        code.AppendLineAt(
            3,
            "public void IntersectWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IntersectWith(other);"
        );
        code.AppendLineAt(
            3,
            "public bool IsProperSubsetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsProperSubsetOf(other);"
        );
        code.AppendLineAt(
            3,
            "public bool IsProperSupersetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsProperSupersetOf(other);"
        );
        code.AppendLineAt(
            3,
            "public bool IsSubsetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsSubsetOf(other);"
        );
        code.AppendLineAt(
            3,
            "public bool IsSupersetOf(global::System.Collections.Generic.IEnumerable<T> other) => __inner.IsSupersetOf(other);"
        );
        code.AppendLineAt(
            3,
            "public bool Overlaps(global::System.Collections.Generic.IEnumerable<T> other) => __inner.Overlaps(other);"
        );
        code.AppendLineAt(
            3,
            "public bool SetEquals(global::System.Collections.Generic.IEnumerable<T> other) => __inner.SetEquals(other);"
        );
        code.AppendLineAt(
            3,
            "public void SymmetricExceptWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.SymmetricExceptWith(other);"
        );
        code.AppendLineAt(
            3,
            "public void UnionWith(global::System.Collections.Generic.IEnumerable<T> other) => __inner.UnionWith(other);"
        );
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<T> GetEnumerator() => __inner.GetEnumerator();"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => __inner.GetEnumerator();"
        );
        code.AppendLineAt(3, "public global::System.Collections.Generic.ISet<T> CloneEmpty()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__inner is global::System.Collections.Generic.HashSet<T> hash) return new global::System.Collections.Generic.HashSet<T>("
                + (hashSetSupportsCapacity ? "hash.Count, " : "")
                + "hash.Comparer);"
        );
        code.AppendLineAt(
            4,
            "return new global::System.Collections.Generic.HashSet<T>("
                + (hashSetSupportsCapacity ? "Count, null" : "")
                + ");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }
}
