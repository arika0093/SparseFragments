using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

internal static class SparseReadOnlyViewEmitter
{
    internal static string ReadOnlyViewTypeName(ImmutableArray<SparseMemberModel> members)
    {
        var taken = new System.Collections.Generic.HashSet<string>(
            members.Select(static member => member.Property.Name),
            System.StringComparer.Ordinal
        );
        var builder = new System.Text.StringBuilder("ReadOnlyView");
        while (taken.Contains(builder.ToString()))
        {
            builder.Insert(0, "Sparse");
        }

        return builder.ToString();
    }

    internal static void AppendReadOnlyView(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var typeName = ReadOnlyViewTypeName(members);
        var collectionName = HelperName(members, "__SparseReadOnlyCollection");
        var dictionaryName = HelperName(members, "__SparseReadOnlyDictionary");
        code.AppendLineAt(
            1,
            "/// <summary>Recursive read-only view over a live model instance.</summary>"
        );
        code.AppendLineAt(1, "public sealed class " + typeName);
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "private readonly " + modelType + " __model;");
        code.AppendLineAt(2, "public " + typeName + "(" + modelType + " model)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if ((object?)model is null) throw new global::System.ArgumentNullException(nameof(model));"
        );
        code.AppendLineAt(3, "__model = model;");
        code.AppendLineAt(2, "}");

        foreach (var member in members)
        {
            AppendMember(code, member, collectionName, dictionaryName);
        }

        AppendCollectionAdapter(code, collectionName);
        AppendDictionaryAdapter(code, dictionaryName, collectionName);
        code.AppendLineAt(1, "}");
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string collectionName,
        string dictionaryName
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        if (member.ChildModel is not null)
        {
            var child = member.ChildModel.Value;
            var viewType =
                child.NonNullableName
                + "."
                + (child.ReadOnlyViewTypeName ?? "ReadOnlyView")
                + (member.Property.IsNullable ? "?" : "");
            code.AppendLineAt(2, "public " + viewType + " " + property);
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "get");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "var current = __model." + property + ";");
            if (!child.IsReferenceType && member.Property.IsNullable)
            {
                code.AppendLineAt(
                    4,
                    "return current.HasValue ? new "
                        + child.NonNullableName
                        + "."
                        + (child.ReadOnlyViewTypeName ?? "ReadOnlyView")
                        + "(current.Value) : null;"
                );
            }
            else
            {
                code.AppendLineAt(
                    4,
                    "return (object?)current is null ? null"
                        + (member.Property.IsNullable ? "" : "!")
                        + " : new "
                        + child.NonNullableName
                        + "."
                        + (child.ReadOnlyViewTypeName ?? "ReadOnlyView")
                        + "(current);"
                );
            }
            code.AppendLineAt(3, "}");
            code.AppendLineAt(2, "}");
            return;
        }

        if (member.Collection.ValueType is not null)
        {
            AppendDictionaryMember(code, member, property, dictionaryName);
            return;
        }

        if (
            member.Collection.ElementType.Name is not null
            && member.Collection.Kind
                is SparseCollectionKind.Array
                    or SparseCollectionKind.List
                    or SparseCollectionKind.MutableList
                    or SparseCollectionKind.Set
        )
        {
            AppendCollectionMember(code, member, property, collectionName);
            return;
        }

        code.AppendLineAt(
            2,
            "public " + member.Property.Type.Name + " " + property + " => __model." + property + ";"
        );
    }

    private static void AppendCollectionMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string collectionName
    )
    {
        var modelElement = member.Collection.ElementType.Name;
        var viewElement = ReadOnlyValueType(member.Collection.ElementType);
        var viewCollection =
            member.Collection.Kind == SparseCollectionKind.Set
                ? "global::System.Collections.Generic.IReadOnlyCollection<" + viewElement + ">"
                : "global::System.Collections.Generic.IReadOnlyList<" + viewElement + ">";
        var nullable = member.Property.IsNullable ? "?" : "";
        code.AppendLineAt(2, "public " + viewCollection + nullable + " " + property);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var current = __model." + property + ";");
        code.AppendLineAt(
            4,
            "if ((object?)current is null) return null" + (member.Property.IsNullable ? ";" : "!;")
        );
        code.AppendLineAt(
            4,
            "return new "
                + collectionName
                + "<"
                + modelElement
                + ", "
                + viewElement
                + ">((global::System.Collections.Generic.IEnumerable<"
                + modelElement
                + ">)current, "
                + ReadOnlyMapper(member.Collection.ElementType)
                + ");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendDictionaryMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        string dictionaryName
    )
    {
        var keyType = member.Collection.ElementType.Name;
        var modelValue = member.Collection.ValueType!.Value.Name;
        var viewValue = ReadOnlyValueType(member.Collection.ValueType.Value);
        var nullable = member.Property.IsNullable ? "?" : "";
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IReadOnlyDictionary<"
                + keyType
                + ", "
                + viewValue
                + ">"
                + nullable
                + " "
                + property
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var current = __model." + property + ";");
        code.AppendLineAt(
            4,
            "if ((object?)current is null) return null" + (member.Property.IsNullable ? ";" : "!;")
        );
        code.AppendLineAt(
            4,
            "return new "
                + dictionaryName
                + "<"
                + keyType
                + ", "
                + modelValue
                + ", "
                + viewValue
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + modelValue
                + ">)current, "
                + ReadOnlyMapper(member.Collection.ValueType.Value)
                + ");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static string ReadOnlyValueType(SparseTypeModel model)
    {
        if (!model.IsFragmentModel)
        {
            return model.Name;
        }

        return model.NonNullableName
            + "."
            + (model.ReadOnlyViewTypeName ?? "ReadOnlyView")
            + (model.Name.EndsWith("?", System.StringComparison.Ordinal) ? "?" : "");
    }

    private static string ReadOnlyMapper(SparseTypeModel model)
    {
        if (!model.IsFragmentModel)
        {
            return "static value => value";
        }

        var typeName = model.NonNullableName + "." + (model.ReadOnlyViewTypeName ?? "ReadOnlyView");
        if (model.IsReferenceType)
        {
            return "value => value is null ? null! : new " + typeName + "(value)";
        }

        return model.Name.EndsWith("?", System.StringComparison.Ordinal)
            ? "value => value.HasValue ? new " + typeName + "(value.Value) : null!"
            : "static value => new " + typeName + "(value)";
    }

    private static string HelperName(ImmutableArray<SparseMemberModel> members, string initialName)
    {
        var taken = new System.Collections.Generic.HashSet<string>(
            members.Select(static member => member.Property.Name),
            System.StringComparer.Ordinal
        );
        var name = new System.Text.StringBuilder(initialName);
        while (taken.Contains(name.ToString()))
        {
            name.Insert(0, '_');
        }

        return name.ToString();
    }

    private static void AppendCollectionAdapter(SharedIndentedBuilder code, string typeName)
    {
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

    private static void AppendDictionaryAdapter(
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
            "private readonly global::System.Collections.Generic.IDictionary<TKey, TSource> _source;"
        );
        code.AppendLineAt(3, "private readonly global::System.Func<TSource, TView> _map;");
        code.AppendLineAt(
            3,
            "public "
                + typeName
                + "(global::System.Collections.Generic.IDictionary<TKey, TSource> source, global::System.Func<TSource, TView> map)"
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
}
