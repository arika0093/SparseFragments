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
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels
    )
    {
        var typeName = ReadOnlyViewTypeName(members);
        var modelFieldName = HelperName(MemberNames(members), "__model");
        var allMemberNames = MemberNames(members)
            .AddRange(readOnlyViewModels.SelectMany(static view => MemberNames(view.Members)));
        var collectionName = HelperName(allMemberNames, "__SparseReadOnlyCollection");
        var dictionaryName = HelperName(allMemberNames, "__SparseReadOnlyDictionary");
        var dictionaryEntriesName = HelperName(allMemberNames, "__SparseReadOnlyDictionaryEntries");
        var pocoViewNames = PocoViewTypeNames(
            typeName,
            modelFieldName,
            allMemberNames,
            readOnlyViewModels,
            collectionName,
            dictionaryName,
            dictionaryEntriesName
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
                dictionaryEntriesName,
                pocoViewNames
            );
        }

        foreach (var viewModel in readOnlyViewModels)
        {
            if (pocoViewNames.TryGetValue(viewModel.Key, out var pocoViewName))
            {
                AppendValueReadOnlyView(
                    code,
                    viewModel,
                    pocoViewName,
                    collectionName,
                    dictionaryName,
                    dictionaryEntriesName,
                    pocoViewNames
                );
            }
        }

        SparseReadOnlyAdapterEmitter.AppendCollectionAdapter(code, collectionName);
        SparseReadOnlyAdapterEmitter.AppendDictionaryAdapter(code, dictionaryName, collectionName);
        SparseReadOnlyAdapterEmitter.AppendDictionaryEntriesAdapter(code, dictionaryEntriesName);
        code.AppendLineAt(1, "}");
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int indent,
        string modelFieldName,
        string collectionName,
        string dictionaryName,
        string dictionaryEntriesName,
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
                dictionaryEntriesName,
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
            member.Property.Type.PocoReadOnlyViewKey is not null
            && pocoViewNames.TryGetValue(
                member.Property.Type.PocoReadOnlyViewKey,
                out var pocoViewName
            )
        )
        {
            AppendPocoMember(
                code,
                member,
                property,
                indent,
                modelFieldName,
                pocoViewName,
                member.Property.Type.PocoReadOnlyViewKey
                    == SparseWellKnownNames.OpaqueReadOnlyViewKey
            );
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
        code.AppendLineAt(
            indent,
            "/// <remarks>Streaming view: <c>Count</c> and the indexer are O(1) for collection- or list-backed sources and enumerate otherwise; prefer <c>foreach</c> over indexed loops for streaming sources.</remarks>"
        );
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
        string dictionaryEntriesName,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        var keyType = member.Collection.ElementType.Name;
        var modelValue = member.Collection.ValueType!.Value.Name;
        var viewKey = ReadOnlyValueType(member.Collection.ElementType, pocoViewNames);
        var viewValue = ReadOnlyValueType(member.Collection.ValueType.Value, pocoViewNames);
        var nullable = member.Property.IsNullable ? "?" : "";
        AppendPropertySummary(code, member, indent);
        var dictionaryViewType =
            keyType == viewKey
                ? "global::System.Collections.Generic.IReadOnlyDictionary<"
                    + keyType
                    + ", "
                    + viewValue
                    + ">"
                : "global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<"
                    + viewKey
                    + ", "
                    + viewValue
                    + ">>";
        code.AppendLineAt(indent, "public " + dictionaryViewType + nullable + " " + property);
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "get");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "var current = " + modelFieldName + "." + property + ";");
        code.AppendLineAt(
            indent + 2,
            "if ((object?)current is null) return null" + (member.Property.IsNullable ? ";" : "!;")
        );
        var sourceType =
            "(global::System.Collections.Generic.IReadOnlyDictionary<"
            + keyType
            + ", "
            + modelValue
            + ">)current";
        var adapter = keyType == viewKey ? dictionaryName : dictionaryEntriesName;
        var adapterType =
            keyType == viewKey
                ? "<" + keyType + ", " + modelValue + ", " + viewValue + ">"
                : "<" + keyType + ", " + modelValue + ", " + viewKey + ", " + viewValue + ">";
        var mapValues = ReadOnlyMapper(member.Collection.ValueType.Value, pocoViewNames);
        var mapKey =
            keyType == viewKey
                ? string.Empty
                : ReadOnlyMapper(member.Collection.ElementType, pocoViewNames) + ", ";
        code.AppendLineAt(
            indent + 2,
            "return new "
                + adapter
                + adapterType
                + "("
                + sourceType
                + ", "
                + mapKey
                + mapValues
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
        string pocoViewName,
        bool isOpaque
    )
    {
        var nullable = member.Property.IsNullable ? "?" : "";
        var currentArgument = isOpaque ? "(object?)current" : "current";
        AppendPropertySummary(code, member, indent);
        code.AppendLineAt(indent, "public " + pocoViewName + nullable + " " + property);
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "get");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "var current = " + modelFieldName + "." + property + ";");
        code.AppendLineAt(
            indent + 2,
            member.Property.IsNullable
                ? "return (object?)current is null ? null : new "
                    + pocoViewName
                    + "("
                    + currentArgument
                    + ");"
                : "return new " + pocoViewName + "(" + currentArgument + ");"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    private static void AppendValueReadOnlyView(
        SharedIndentedBuilder code,
        SparseReadOnlyViewModel viewModel,
        string viewTypeName,
        string collectionName,
        string dictionaryName,
        string dictionaryEntriesName,
        System.Collections.Generic.Dictionary<string, string> pocoViewNames
    )
    {
        var modelFieldName = HelperName(MemberNames(viewModel.Members), "__model");
        code.AppendLineAt(
            2,
            "/// <summary>Read-only view over a live " + viewModel.Name + " instance.</summary>"
        );
        code.AppendLineAt(2, "public readonly struct " + viewTypeName);
        code.AppendLineAt(2, "{");
        if (viewModel.StoresValue)
        {
            code.AppendLineAt(
                3,
                "private readonly " + viewModel.SourceTypeName + " " + modelFieldName + ";"
            );
        }
        code.AppendLineAt(
            3,
            "internal " + viewTypeName + "(" + viewModel.SourceTypeName + " model)"
        );
        code.AppendLineAt(3, "{");
        if (!viewModel.IsOpaque)
        {
            code.AppendLineAt(
                4,
                "if ((object?)model is null) throw new global::System.ArgumentNullException(nameof(model));"
            );
        }
        code.AppendLineAt(4, viewModel.StoresValue ? modelFieldName + " = model;" : "_ = model;");
        code.AppendLineAt(3, "}");

        if (viewModel.IsOpaque)
        {
            code.AppendLineAt(
                3,
                "/// <summary>Indicates whether this opaque value contains a reference.</summary>"
            );
            code.AppendLineAt(
                3,
                viewModel.StoresValue
                    ? "public bool HasValue => (object?)" + modelFieldName + " is not null;"
                    : "public bool HasValue => true;"
            );
        }

        foreach (var member in viewModel.Members)
        {
            AppendMember(
                code,
                member,
                3,
                modelFieldName,
                collectionName,
                dictionaryName,
                dictionaryEntriesName,
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
            model.PocoReadOnlyViewKey is not null
            && pocoViewNames.TryGetValue(model.PocoReadOnlyViewKey, out var pocoViewName)
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
            model.PocoReadOnlyViewKey is not null
            && pocoViewNames.TryGetValue(model.PocoReadOnlyViewKey, out var pocoViewName)
        )
        {
            var isOpaque = model.PocoReadOnlyViewKey == SparseWellKnownNames.OpaqueReadOnlyViewKey;
            var valueExpression = isOpaque ? "(object?)value" : "value";
            return model.Name.EndsWith("?", System.StringComparison.Ordinal)
                ? "static value => value is null ? null : new "
                    + pocoViewName
                    + "("
                    + valueExpression
                    + ")"
                : "static value => new " + pocoViewName + "(" + valueExpression + ")";
        }

        return "static value => value";
    }

    private static System.Collections.Generic.Dictionary<string, string> PocoViewTypeNames(
        string typeName,
        string modelFieldName,
        ImmutableArray<string> memberNames,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        string collectionName,
        string dictionaryName,
        string dictionaryEntriesName
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
            dictionaryEntriesName,
        };
        var result = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.Ordinal
        );
        foreach (
            var (key, modelName) in readOnlyViewModels.Select(static view => (view.Key, view.Name))
        )
        {
            if (result.ContainsKey(key))
            {
                continue;
            }

            var separator = key.LastIndexOf('_');
            var suffix = separator < 0 ? key : key[(separator + 1)..];
            var name = new System.Text.StringBuilder(
                "__SparseReadOnlyPoco_" + modelName + "_" + suffix
            );
            while (!taken.Add(name.ToString()))
            {
                name.Insert(0, "_");
            }

            result.Add(key, name.ToString());
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
}
