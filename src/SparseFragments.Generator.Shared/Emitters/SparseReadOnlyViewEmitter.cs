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
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels
    )
    {
        var typeName = ReadOnlyViewTypeName(members);
        var modelFieldName = HelperName(MemberNames(members), "__model");
        var allMemberNames = MemberNames(members)
            .AddRange(pocoCloneModels.SelectMany(static poco => MemberNames(poco.Members)));
        var collectionName = HelperName(allMemberNames, "__SparseReadOnlyCollection");
        var dictionaryName = HelperName(allMemberNames, "__SparseReadOnlyDictionary");
        var pocoViewNames = PocoViewTypeNames(
            typeName,
            modelFieldName,
            allMemberNames,
            pocoCloneModels,
            collectionName,
            dictionaryName
        );
        code.AppendLineAt(
            1,
            "/// <summary>Recursive read-only view over a live model instance.</summary>"
        );
        code.AppendLineAt(1, "public sealed class " + typeName);
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "private readonly " + modelType + " " + modelFieldName + ";");
        code.AppendLineAt(2, "/// <summary>Creates a read-only view over a live model.</summary>");
        code.AppendLineAt(2, "/// <param name=\"model\">The model to expose.</param>");
        code.AppendLineAt(2, "public " + typeName + "(" + modelType + " model)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if ((object?)model is null) throw new global::System.ArgumentNullException(nameof(model));"
        );
        code.AppendLineAt(3, modelFieldName + " = model;");
        code.AppendLineAt(2, "}");

        foreach (var member in members)
        {
            AppendMember(
                code,
                member,
                2,
                modelFieldName,
                collectionName,
                dictionaryName,
                pocoViewNames
            );
        }

        foreach (var poco in pocoCloneModels)
        {
            if (pocoViewNames.TryGetValue(poco.CloneHelperName, out var pocoViewName))
            {
                AppendPocoReadOnlyView(
                    code,
                    poco,
                    pocoViewName,
                    collectionName,
                    dictionaryName,
                    pocoViewNames
                );
            }
        }

        AppendCollectionAdapter(code, collectionName);
        AppendDictionaryAdapter(code, dictionaryName, collectionName);
        code.AppendLineAt(1, "}");
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int indent,
        string modelFieldName,
        string collectionName,
        string dictionaryName,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        if (member.ChildModel is SparseTypeModel child && child.IsFragmentModel)
        {
            var viewType =
                child.NonNullableName
                + "."
                + (child.ReadOnlyViewTypeName ?? "ReadOnlyView")
                + (member.Property.IsNullable ? "?" : "");
            AppendPropertySummary(code, member, indent);
            code.AppendLineAt(indent, "public " + viewType + " " + property);
            code.AppendLineAt(indent, "{");
            code.AppendLineAt(indent + 1, "get");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(indent + 2, "var current = " + modelFieldName + "." + property + ";");
            if (!child.IsReferenceType && member.Property.IsNullable)
            {
                code.AppendLineAt(
                    indent + 2,
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
                    indent + 2,
                    "return (object?)current is null ? null"
                        + (member.Property.IsNullable ? "" : "!")
                        + " : new "
                        + child.NonNullableName
                        + "."
                        + (child.ReadOnlyViewTypeName ?? "ReadOnlyView")
                        + "(current);"
                );
            }
            code.AppendLineAt(indent + 1, "}");
            code.AppendLineAt(indent, "}");
            return;
        }

        if (member.Collection.ValueType is not null)
        {
            AppendDictionaryMember(
                code,
                member,
                property,
                indent,
                modelFieldName,
                dictionaryName,
                pocoViewNames
            );
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
            AppendCollectionMember(
                code,
                member,
                property,
                indent,
                modelFieldName,
                collectionName,
                pocoViewNames
            );
            return;
        }

        if (
            member.Property.Type.PocoCloneHelperName is not null
            && pocoViewNames.TryGetValue(
                member.Property.Type.PocoCloneHelperName,
                out var pocoViewName
            )
        )
        {
            AppendPocoMember(code, member, property, indent, modelFieldName, pocoViewName);
            return;
        }

        AppendPropertySummary(code, member, indent);
        code.AppendLineAt(
            indent,
            "public "
                + member.Property.Type.Name
                + " "
                + property
                + " => "
                + modelFieldName
                + "."
                + property
                + ";"
        );
    }

    private static void AppendCollectionMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        int indent,
        string modelFieldName,
        string collectionName,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        var modelElement = member.Collection.ElementType.Name;
        var viewElement = ReadOnlyValueType(member.Collection.ElementType, pocoViewNames);
        var viewCollection =
            member.Collection.Kind == SparseCollectionKind.Set
                ? "global::System.Collections.Generic.IReadOnlyCollection<" + viewElement + ">"
                : "global::System.Collections.Generic.IReadOnlyList<" + viewElement + ">";
        var nullable = member.Property.IsNullable ? "?" : "";
        AppendPropertySummary(code, member, indent);
        code.AppendLineAt(indent, "public " + viewCollection + nullable + " " + property);
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "get");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "var current = " + modelFieldName + "." + property + ";");
        code.AppendLineAt(
            indent + 2,
            "if ((object?)current is null) return null" + (member.Property.IsNullable ? ";" : "!;")
        );
        code.AppendLineAt(
            indent + 2,
            "return new "
                + collectionName
                + "<"
                + modelElement
                + ", "
                + viewElement
                + ">((global::System.Collections.Generic.IEnumerable<"
                + modelElement
                + ">)current, "
                + ReadOnlyMapper(member.Collection.ElementType, pocoViewNames)
                + ");"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    private static void AppendDictionaryMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        int indent,
        string modelFieldName,
        string dictionaryName,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        var keyType = member.Collection.ElementType.Name;
        var modelValue = member.Collection.ValueType!.Value.Name;
        var viewValue = ReadOnlyValueType(member.Collection.ValueType.Value, pocoViewNames);
        var nullable = member.Property.IsNullable ? "?" : "";
        AppendPropertySummary(code, member, indent);
        code.AppendLineAt(
            indent,
            "public global::System.Collections.Generic.IReadOnlyDictionary<"
                + keyType
                + ", "
                + viewValue
                + ">"
                + nullable
                + " "
                + property
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "get");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "var current = " + modelFieldName + "." + property + ";");
        code.AppendLineAt(
            indent + 2,
            "if ((object?)current is null) return null" + (member.Property.IsNullable ? ";" : "!;")
        );
        code.AppendLineAt(
            indent + 2,
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
                + ReadOnlyMapper(member.Collection.ValueType.Value, pocoViewNames)
                + ");"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    private static void AppendPocoMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string property,
        int indent,
        string modelFieldName,
        string pocoViewName
    )
    {
        var nullable = member.Property.IsNullable ? "?" : "";
        AppendPropertySummary(code, member, indent);
        code.AppendLineAt(indent, "public " + pocoViewName + nullable + " " + property);
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "get");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "var current = " + modelFieldName + "." + property + ";");
        code.AppendLineAt(
            indent + 2,
            "return (object?)current is null ? null"
                + (member.Property.IsNullable ? "" : "!")
                + " : new "
                + pocoViewName
                + "(current);"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    private static void AppendPocoReadOnlyView(
        SharedIndentedBuilder code,
        SparsePocoCloneModel poco,
        string viewTypeName,
        string collectionName,
        string dictionaryName,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        var modelFieldName = HelperName(MemberNames(poco.Members), "__model");
        code.AppendLineAt(
            2,
            "/// <summary>Read-only view over a live " + poco.Model.Name + " instance.</summary>"
        );
        code.AppendLineAt(2, "public readonly struct " + viewTypeName);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "private readonly " + poco.Model.ModelTypeName + " " + modelFieldName + ";"
        );
        code.AppendLineAt(
            3,
            "internal " + viewTypeName + "(" + poco.Model.ModelTypeName + " model)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if ((object?)model is null) throw new global::System.ArgumentNullException(nameof(model));"
        );
        code.AppendLineAt(4, modelFieldName + " = model;");
        code.AppendLineAt(3, "}");

        foreach (var member in poco.Members)
        {
            AppendMember(
                code,
                member,
                3,
                modelFieldName,
                collectionName,
                dictionaryName,
                pocoViewNames
            );
        }

        code.AppendLineAt(2, "}");
    }

    private static string ReadOnlyValueType(
        SparseTypeModel model,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        if (model.IsFragmentModel)
        {
            return model.NonNullableName
                + "."
                + (model.ReadOnlyViewTypeName ?? "ReadOnlyView")
                + (model.Name.EndsWith("?", System.StringComparison.Ordinal) ? "?" : "");
        }

        if (
            model.PocoCloneHelperName is not null
            && pocoViewNames.TryGetValue(model.PocoCloneHelperName, out var pocoViewName)
        )
        {
            return pocoViewName
                + (model.Name.EndsWith("?", System.StringComparison.Ordinal) ? "?" : "");
        }

        return model.Name;
    }

    private static void AppendPropertySummary(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int indent
    ) =>
        code.AppendLineAt(
            indent,
            "/// <summary>Gets the read-only value of member '"
                + member.Property.Name
                + "'.</summary>"
        );

    private static string ReadOnlyMapper(
        SparseTypeModel model,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        if (model.IsFragmentModel)
        {
            var typeName =
                model.NonNullableName + "." + (model.ReadOnlyViewTypeName ?? "ReadOnlyView");
            if (model.IsReferenceType)
            {
                return "value => value is null ? null! : new " + typeName + "(value)";
            }

            return model.Name.EndsWith("?", System.StringComparison.Ordinal)
                ? "value => value.HasValue ? new " + typeName + "(value.Value) : null!"
                : "static value => new " + typeName + "(value)";
        }

        if (
            model.PocoCloneHelperName is not null
            && pocoViewNames.TryGetValue(model.PocoCloneHelperName, out var pocoViewName)
        )
        {
            return
                model.IsReferenceType && model.Name.EndsWith("?", System.StringComparison.Ordinal)
                ? "static value => value is null ? null : new " + pocoViewName + "(value)"
                : "static value => new " + pocoViewName + "(value)";
        }

        return "static value => value";
    }

    private static System.Collections.Generic.Dictionary<string, string> PocoViewTypeNames(
        string typeName,
        string modelFieldName,
        ImmutableArray<string> memberNames,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        string collectionName,
        string dictionaryName
    )
    {
        var taken = new System.Collections.Generic.HashSet<string>(
            memberNames,
            System.StringComparer.Ordinal
        )
        {
            typeName,
            modelFieldName,
            collectionName,
            dictionaryName,
        };
        var result = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.Ordinal
        );
        foreach (
            var (cloneHelperName, modelName) in pocoCloneModels.Select(static poco =>
                (poco.CloneHelperName, poco.Model.Name)
            )
        )
        {
            if (cloneHelperName is null || result.ContainsKey(cloneHelperName))
            {
                continue;
            }

            var separator = cloneHelperName.LastIndexOf('_');
            var suffix = separator < 0 ? cloneHelperName : cloneHelperName[(separator + 1)..];
            var name = new System.Text.StringBuilder(
                "__SparseReadOnlyPoco_" + modelName + "_" + suffix
            );
            while (!taken.Add(name.ToString()))
            {
                name.Insert(0, "_");
            }

            result.Add(cloneHelperName, name.ToString());
        }

        return result;
    }

    private static ImmutableArray<string> MemberNames(ImmutableArray<SparseMemberModel> members) =>
        members.Select(static member => member.Property.Name).ToImmutableArray();

    private static string HelperName(ImmutableArray<string> memberNames, string initialName)
    {
        var taken = new System.Collections.Generic.HashSet<string>(
            memberNames,
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
