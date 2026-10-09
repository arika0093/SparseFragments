using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the nested bindable <c>Observable</c> proxy for a generated model.</summary>
/// <remarks>Wraps the live model instance and exposes notifying views for mutable lists and dictionaries.</remarks>
internal static class SparseObservableEmitter
{
    public static string ObservableTypeName(ImmutableArray<SparseMemberModel> members)
    {
        var taken = new HashSet<string>(members.Select(static member => member.Property.Name));
        var builder = new StringBuilder("Observable");
        while (taken.Contains(builder.ToString()))
        {
            builder.Insert(0, "Sparse");
        }

        return builder.ToString();
    }

    public static void AppendObservable(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect? descriptorDialect
    )
    {
        var observable = ObservableTypeName(members);
        code.AppendLineAt(
            1,
            "/// <summary>Bindable proxy over the live model instance. No state is copied.</summary>"
        );
        code.AppendLineAt(
            1,
            "public sealed class "
                + observable
                + " : global::System.ComponentModel.INotifyPropertyChanged"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "private readonly " + modelType + " __model;");
        code.AppendLineAt(2, "private readonly global::System.Action? __onChanged;");
        code.AppendLineAt(2, "private readonly global::System.Action? __onRawModelAccess;");
        foreach (var member in members)
        {
            if (
                member.ChildModel is not null
                && member.ChildIsReferenceType
                && member.Property.Name != "PropertyChanged"
            )
            {
                var childObservable = ChildObservableType(member);
                code.AppendLineAt(
                    2,
                    "private "
                        + member.ChildModel.Value.NonNullableName
                        + "? __target_"
                        + member.Id
                        + ";"
                );
                code.AppendLineAt(2, "private " + childObservable + "? __proxy_" + member.Id + ";");
            }

            if (IsObservableList(member) || IsObservableDictionary(member))
            {
                var names = CollectionNames(member);
                code.AppendLineAt(
                    2,
                    "private "
                        + CollectionViewType(member, names, runtimeNamespace)
                        + "? __view_"
                        + member.Id
                        + ";"
                );
                code.AppendLineAt(
                    2,
                    "private "
                        + member.Property.Type.NonNullableName
                        + "? __target_collection_"
                        + member.Id
                        + ";"
                );
            }
        }

        code.AppendLineAt(
            2,
            "public "
                + observable
                + "("
                + modelType
                + " value, global::System.Action? onChanged = null, global::System.Action? onRawModelAccess = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if ((object?)value is null) throw new global::System.ArgumentNullException(nameof(value));"
        );
        code.AppendLineAt(3, "__model = value;");
        code.AppendLineAt(3, "__onChanged = onChanged;");
        code.AppendLineAt(3, "__onRawModelAccess = onRawModelAccess;");
        code.AppendLineAt(2, "}");
        if (!members.Any(static member => member.Property.Name == "Model"))
        {
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + " Model { get { __onRawModelAccess?.Invoke(); return __model; } }"
            );
        }

        code.AppendLineAt(2, "internal " + modelType + " __SparseTarget => __model;");
        code.AppendLineAt(
            2,
            "public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;"
        );
        foreach (var member in members)
        {
            if (member.Property.Name == "PropertyChanged")
            {
                // The INotifyPropertyChanged event occupies this name; the member is
                // intentionally not proxied to keep the generated surface compilable.
                continue;
            }

            AppendMember(code, member, members, runtimeNamespace);
        }

        if (descriptorDialect is not null)
        {
            SparseObservableDescriptorEmitter.Append(
                code,
                modelType,
                members,
                runtimeNamespace,
                descriptorDialect
            );
        }

        code.AppendLineAt(2, "internal void __SparseRefresh()");
        code.AppendLineAt(2, "{");
        foreach (
            var property in members
                .Select(static member => member.Property)
                .Where(static property => property.Name != "PropertyChanged")
        )
        {
            code.AppendLineAt(
                3,
                "__Raise(" + SymbolDisplay.FormatLiteral(property.Name, true) + ");"
            );
        }
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private void __Raise(string propertyName) => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));"
        );
        code.AppendLineAt(1, "}");
    }

    internal static string ChildObservableType(SparseMemberModel member)
    {
        var model = member.ChildModel!.Value;
        if (model.ObservableTypeName is not null)
        {
            return model.NonNullableName + "." + model.ObservableTypeName;
        }

        var fragment = member.ChildFragmentType!;
        return fragment.Substring(0, fragment.Length - "Fragment".Length) + "Observable";
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var canWrite = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        if (member.ChildModel is not null && member.ChildIsReferenceType)
        {
            AppendReferenceChild(code, member, name, literal, canWrite);
            return;
        }

        if (IsObservableList(member))
        {
            AppendObservableList(code, member, name, literal, canWrite, members, runtimeNamespace);
            return;
        }

        if (IsObservableDictionary(member))
        {
            AppendObservableDictionary(
                code,
                member,
                name,
                literal,
                canWrite,
                members,
                runtimeNamespace
            );
            return;
        }

        var type = member.Property.Type.Name;
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + type + ">.Default";
        code.AppendLineAt(2, "public " + type + " " + name);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get => __model." + name + ";");
        if (canWrite)
        {
            code.AppendLineAt(3, "set");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (" + comparer + ".Equals(__model." + name + ", value)) return;"
            );
            code.AppendLineAt(4, "__model." + name + " = value;");
            code.AppendLineAt(4, "__Raise(" + literal + ");");
            code.AppendLineAt(4, "if (__onChanged is not null) __onChanged();");
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(2, "}");
    }

    internal static bool IsObservableList(SparseMemberModel member)
    {
        var type = member.Property.Type.NonNullableName;
        return member.Collection.ElementType.Name is not null
            && (
                member.Collection.Kind
                    is SparseCollectionKind.List
                        or SparseCollectionKind.MutableList
                || type.StartsWith(
                    "global::System.Collections.ObjectModel.Collection<",
                    StringComparison.Ordinal
                )
                || type.StartsWith(
                    "System.Collections.ObjectModel.Collection<",
                    StringComparison.Ordinal
                )
                || type.StartsWith(
                    "global::System.Collections.ObjectModel.ObservableCollection<",
                    StringComparison.Ordinal
                )
                || type.StartsWith(
                    "System.Collections.ObjectModel.ObservableCollection<",
                    StringComparison.Ordinal
                )
            );
    }

    internal static bool IsObservableDictionary(SparseMemberModel member)
    {
        var type = member.Property.Type.NonNullableName;
        return member.Collection.ValueType is not null
            && (
                type.StartsWith(
                    "global::System.Collections.Generic.Dictionary<",
                    StringComparison.Ordinal
                )
                || type.StartsWith(
                    "System.Collections.Generic.Dictionary<",
                    StringComparison.Ordinal
                )
                || type.StartsWith(
                    "global::System.Collections.Generic.IDictionary<",
                    StringComparison.Ordinal
                )
                || type.StartsWith(
                    "System.Collections.Generic.IDictionary<",
                    StringComparison.Ordinal
                )
            );
    }

    internal static CollectionProxyNames CollectionNames(SparseMemberModel member)
    {
        var element = member.Collection.ElementType;
        var value = member.Collection.ValueType;
        var hasElementProxy = value is null && element.IsFragmentModel && element.IsReferenceType;
        var hasValueProxy =
            value is not null && value.Value.IsFragmentModel && value.Value.IsReferenceType;
        var modelType = element.Name;
        var viewType = element.Name;
        if (hasElementProxy)
        {
            viewType = ObservableElementType(element);
        }

        if (value is not null)
        {
            modelType = value.Value.Name;
            viewType = hasValueProxy ? ObservableElementType(value.Value) : modelType;
        }

        return new CollectionProxyNames(
            modelType,
            viewType,
            hasElementProxy || hasValueProxy,
            value is not null
        );
    }

    private static string ObservableElementType(SparseTypeModel model) =>
        model.NonNullableName
        + "."
        + (model.ObservableTypeName ?? "Observable")
        + (model.Name.EndsWith("?", StringComparison.Ordinal) ? "?" : "");

    private static void AppendObservableList(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string name,
        string literal,
        bool canWrite,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace
    )
    {
        var types = CollectionNames(member);
        var collectionType = CollectionViewType(member, types, runtimeNamespace);
        var nullable = member.Property.IsNullable ? "?" : "";
        code.AppendLineAt(2, "public " + collectionType + nullable + " " + name);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var current = __model." + name + ";");
        code.AppendLineAt(
            4,
            "if ((object?)current is null) { __view_"
                + member.Id
                + "?.Dispose(); __view_"
                + member.Id
                + " = null; __target_collection_"
                + member.Id
                + " = null; return null"
                + (nullable.Length == 0 ? "!" : "")
                + "; }"
        );
        code.AppendLineAt(
            4,
            "if (!global::System.Object.ReferenceEquals(__target_collection_"
                + member.Id
                + ", current) || __view_"
                + member.Id
                + "?.IsDisposed == true) { __view_"
                + member.Id
                + "?.Dispose(); __target_collection_"
                + member.Id
                + " = current; __view_"
                + member.Id
                + " = new "
                + collectionType
                + "((global::System.Collections.Generic.IList<"
                + member.Collection.ElementType.Name
                + ">)current, "
                + ListWrap(types, "__onRawModelAccess")
                + ", "
                + ListUnwrap(types)
                + ", () => { __Raise("
                + literal
                + "); if (__onChanged is not null) __onChanged(); }, "
                + (types.HasElementProxy ? "true" : "false")
                + "); }"
        );
        code.AppendLineAt(4, "return __view_" + member.Id + "!;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        if (canWrite)
        {
            var methodName = ReplacementMethodName(member, "Replace", members);
            code.AppendLineAt(
                2,
                "/// <summary>Replaces the live collection while rebuilding its notifying view.</summary>"
            );
            code.AppendLineAt(
                2,
                "public void " + methodName + "(" + member.Property.Type.Name + " value)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(__model." + name + ", value)) return;"
            );
            code.AppendLineAt(3, "__view_" + member.Id + "?.Dispose();");
            code.AppendLineAt(3, "__view_" + member.Id + " = null;");
            code.AppendLineAt(3, "__target_collection_" + member.Id + " = null;");
            code.AppendLineAt(3, "__model." + name + " = value;");
            code.AppendLineAt(3, "__Raise(" + literal + ");");
            code.AppendLineAt(3, "if (__onChanged is not null) __onChanged();");
            code.AppendLineAt(2, "}");
        }
    }

    private static string ListWrap(CollectionProxyNames types, string rawModelAccess) =>
        types.HasElementProxy
            ? "(item, changed) => item is null ? default! : new "
                + types.ViewType.TrimEnd('?')
                + "(item, changed, "
                + rawModelAccess
                + ")"
            : "static (item, _) => item";

    private static string ListUnwrap(CollectionProxyNames types) =>
        types.HasElementProxy
            ? "item => item is null ? default! : item.__SparseTarget"
            : "static item => item";

    private static void AppendObservableDictionary(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string name,
        string literal,
        bool canWrite,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace
    )
    {
        var types = CollectionNames(member);
        var keyType = member.Collection.ElementType.Name;
        var modelValueType = member.Collection.ValueType!.Value.Name;
        var dictionaryType = CollectionViewType(member, types, runtimeNamespace);
        var nullable = member.Property.IsNullable ? "?" : "";
        code.AppendLineAt(2, "public " + dictionaryType + nullable + " " + name);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var current = __model." + name + ";");
        code.AppendLineAt(
            4,
            "if ((object?)current is null) { __view_"
                + member.Id
                + "?.Dispose(); __view_"
                + member.Id
                + " = null; __target_collection_"
                + member.Id
                + " = null; return null"
                + (nullable.Length == 0 ? "!" : "")
                + "; }"
        );
        code.AppendLineAt(
            4,
            "if (!global::System.Object.ReferenceEquals(__target_collection_"
                + member.Id
                + ", current) || __view_"
                + member.Id
                + "?.IsDisposed == true) { __view_"
                + member.Id
                + "?.Dispose(); __target_collection_"
                + member.Id
                + " = current; __view_"
                + member.Id
                + " = new "
                + dictionaryType
                + "((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + modelValueType
                + ">)current, "
                + DictionaryWrap(types, "__onRawModelAccess")
                + ", "
                + DictionaryUnwrap(types)
                + ", () => { __Raise("
                + literal
                + "); if (__onChanged is not null) __onChanged(); }, "
                + (types.HasElementProxy ? "true" : "false")
                + "); }"
        );
        code.AppendLineAt(4, "return __view_" + member.Id + "!;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        if (canWrite)
        {
            var methodName = ReplacementMethodName(member, "Replace", members);
            code.AppendLineAt(
                2,
                "/// <summary>Replaces the live dictionary while rebuilding its notifying view.</summary>"
            );
            code.AppendLineAt(
                2,
                "public void " + methodName + "(" + member.Property.Type.Name + " value)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(__model." + name + ", value)) return;"
            );
            code.AppendLineAt(3, "__view_" + member.Id + "?.Dispose();");
            code.AppendLineAt(3, "__view_" + member.Id + " = null;");
            code.AppendLineAt(3, "__target_collection_" + member.Id + " = null;");
            code.AppendLineAt(3, "__model." + name + " = value;");
            code.AppendLineAt(3, "__Raise(" + literal + ");");
            code.AppendLineAt(3, "if (__onChanged is not null) __onChanged();");
            code.AppendLineAt(2, "}");
        }
    }

    private static string DictionaryWrap(CollectionProxyNames types, string rawModelAccess) =>
        types.HasElementProxy
            ? "(item, changed) => item is null ? default! : new "
                + types.ViewType.TrimEnd('?')
                + "(item, changed, "
                + rawModelAccess
                + ")"
            : "static (item, _) => item";

    private static string DictionaryUnwrap(CollectionProxyNames types) =>
        types.HasElementProxy
            ? "item => item is null ? default! : item.__SparseTarget"
            : "static item => item";

    internal static string CollectionViewType(
        SparseMemberModel member,
        CollectionProxyNames types,
        string runtimeNamespace
    ) =>
        types.IsDictionary
            ? runtimeNamespace
                + "SparseObservableDictionary<"
                + member.Collection.ElementType.Name
                + ", "
                + member.Collection.ValueType!.Value.Name
                + ", "
                + types.ViewType
                + ">"
            : runtimeNamespace
                + "SparseObservableList<"
                + member.Collection.ElementType.Name
                + ", "
                + types.ViewType
                + ">";

    internal static string ReplacementMethodName(
        SparseMemberModel member,
        string prefix,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var candidate = new StringBuilder(prefix + member.Property.Name);
        while (members.Any(other => other.Property.Name == candidate.ToString()))
        {
            candidate.Insert(0, "Sparse");
        }

        return candidate.ToString();
    }

    internal readonly record struct CollectionProxyNames(
        string ModelType,
        string ViewType,
        bool HasElementProxy,
        bool IsDictionary
    );

    private static void AppendReferenceChild(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string name,
        string literal,
        bool canWrite
    )
    {
        var childModel = member.ChildModel!.Value.NonNullableName;
        var childObservable = ChildObservableType(member);
        // The property is observable-typed so nested bindings observe changes; the
        // setter unwraps back to the model type for replacement.
        code.AppendLineAt(2, "public " + childObservable + "? " + name);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var current = __model." + name + ";");
        code.AppendLineAt(
            4,
            "if ((object?)current is null) { __target_"
                + member.Id
                + " = null; __proxy_"
                + member.Id
                + " = null; return null; }"
        );
        code.AppendLineAt(
            4,
            "if (!global::System.Object.ReferenceEquals(__target_"
                + member.Id
                + ", current)) { __target_"
                + member.Id
                + " = current; __proxy_"
                + member.Id
                + " = new "
                + childObservable
                + "(current, () => { __Raise("
                + literal
                + "); if (__onChanged is not null) __onChanged(); }, __onRawModelAccess); }"
        );
        code.AppendLineAt(4, "return __proxy_" + member.Id + ";");
        code.AppendLineAt(3, "}");
        if (canWrite)
        {
            code.AppendLineAt(3, "set");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "var next = value is null ? null : value.__SparseTarget;");
            code.AppendLineAt(
                4,
                "if (global::System.Object.ReferenceEquals(__model." + name + ", next)) return;"
            );
            code.AppendLineAt(4, "__model." + name + " = (" + childModel + ")next!;");
            code.AppendLineAt(4, "__target_" + member.Id + " = null;");
            code.AppendLineAt(4, "__proxy_" + member.Id + " = null;");
            code.AppendLineAt(4, "__Raise(" + literal + ");");
            code.AppendLineAt(4, "if (__onChanged is not null) __onChanged();");
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(2, "}");
    }
}
