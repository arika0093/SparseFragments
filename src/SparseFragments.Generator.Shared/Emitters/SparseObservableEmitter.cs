using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the nested bindable <c>Observable</c> proxy for a generated model.</summary>
/// <remarks>Wraps the live model instance and exposes notifying views for mutable lists and dictionaries.</remarks>
internal static class SparseObservableEmitter
{
    /// <summary>Resolves the observable proxy type name using the standalone product family.</summary>
    public static string ObservableTypeName(ImmutableArray<SparseMemberModel> members) =>
        ObservableTypeName(members, SparseFamilyNames.Standalone);

    /// <summary>Resolves the observable proxy type name for the given product family.</summary>
    public static string ObservableTypeName(
        ImmutableArray<SparseMemberModel> members,
        SparseFamilyNames family
    )
    {
        return SparseSemanticReference.ObservableRootName(
            members.Select(static member => member.Property.Name),
            family
        );
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
        // Public surface first: constructor, Model, event, properties, replacements.
        code.AppendLineAt(
            2,
            "/// <summary>Initializes a bindable proxy over the live model instance.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"value\">The live model instance to wrap. No state is copied.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"onChanged\">Callback invoked after a proxied property changes.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"onRawModelAccess\">Callback invoked when the raw model is accessed.</param>"
        );
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
            code.AppendLineAt(2, "/// <summary>Gets the underlying live model instance.</summary>");
            code.AppendLineAt(
                2,
                "public "
                    + modelType
                    + " Model { get { __onRawModelAccess?.Invoke(); return __model; } }"
            );
        }

        code.AppendLineAt(
            2,
            "/// <summary>Occurs when a proxied property value changes.</summary>"
        );
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

            AppendMember(code, member, runtimeNamespace);
        }

        foreach (var member in members)
        {
            if (member.Property.Name == "PropertyChanged")
            {
                continue;
            }

            AppendReplacement(code, member, members);
        }

        code.AppendLineAt(2, "internal " + modelType + " __SparseTarget => __model;");
        code.AppendLineAt(2, "internal readonly " + modelType + " __model;");
        code.AppendLineAt(2, "internal readonly global::System.Action? __onChanged;");
        code.AppendLineAt(2, "internal readonly global::System.Action? __onRawModelAccess;");

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
        foreach (var member in members)
        {
            if (member.Property.Name == "PropertyChanged")
            {
                continue;
            }

            if (!IsObservableList(member) && !IsObservableDictionary(member))
            {
                continue;
            }

            // Views whose backing instance was replaced are retired so bindings
            // re-fetch; views wrapping the same bulk-mutated instance get one Reset,
            // unless the backing collection already notifies on its own.
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            code.AppendLineAt(
                3,
                "if (!global::System.Object.ReferenceEquals(__target_collection_"
                    + member.Id
                    + ", __model."
                    + name
                    + ")) { __view_"
                    + member.Id
                    + "?.Dispose(); __view_"
                    + member.Id
                    + " = null; __target_collection_"
                    + member.Id
                    + " = null; }"
            );
            code.AppendLineAt(
                3,
                "else if (__view_"
                    + member.Id
                    + " is not null && !(__target_collection_"
                    + member.Id
                    + " is global::System.Collections.Specialized.INotifyCollectionChanged)) __view_"
                    + member.Id
                    + ".NotifyReset(false);"
            );
        }
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal void __Raise(string propertyName) => PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(propertyName));"
        );
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

            if (
                (IsObservableList(member) || IsObservableDictionary(member))
                && member.Property.Name != "PropertyChanged"
            )
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

        code.AppendLineAt(1, "}");
    }

    internal static string ChildObservableType(SparseMemberModel member) =>
        ChildObservableType(member, SparseFamilyNames.Standalone);

    internal static string ChildObservableType(SparseMemberModel member, SparseFamilyNames family)
    {
        var model = member.ChildModel!.Value;
        return SparseSemanticReference.ChildObservableType(
            model.NonNullableName,
            member.ChildFragmentType!,
            model.ObservableTypeName,
            family
        );
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
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
            AppendObservableList(code, member, name, literal, runtimeNamespace);
            return;
        }

        if (IsObservableDictionary(member))
        {
            AppendObservableDictionary(code, member, name, literal, runtimeNamespace);
            return;
        }

        var type = member.Property.Type.Name;
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + type + ">.Default";
        AppendPropertySummary(code, member);
        code.AppendLineAt(2, "public " + type + " " + name);
        code.AppendLineAt(2, "{");
        if (ExposesRawMutableReference(member))
        {
            // Raw mutable references escape without a notifying view; invalidate the
            // session's cached HasChanges so a later in-place mutation is observed.
            code.AppendLineAt(
                3,
                "get { __onRawModelAccess?.Invoke(); return __model." + name + "; }"
            );
        }
        else
        {
            code.AppendLineAt(3, "get => __model." + name + ";");
        }
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

    /// <summary>Determines whether reads expose a raw mutable model reference.</summary>
    /// <remarks>
    /// True for arrays and unproxied reference shapes (mutable POCOs, opaque
    /// objects). Strings are immutable and stay cheap; child references and
    /// observable collections already surface notifying views, except for the
    /// PropertyChanged name, which has no proxy or view at all.
    /// </remarks>
    internal static bool ExposesRawMutableReference(SparseMemberModel member)
    {
        if (!member.Property.Type.IsReferenceType)
        {
            return false;
        }

        if (member.Property.Type.NonNullableName is ("string" or "global::System.String"))
        {
            return false;
        }

        if (member.Property.Name == "PropertyChanged")
        {
            return true;
        }

        return (member.ChildModel is null || !member.ChildIsReferenceType)
            && !IsObservableList(member)
            && !IsObservableDictionary(member);
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
        string runtimeNamespace
    )
    {
        var types = CollectionNames(member);
        var collectionType = CollectionViewType(member, types, runtimeNamespace);
        var nullable = member.Property.IsNullable ? "?" : "";
        AppendPropertySummary(code, member);
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
                + ", __onRawModelAccess); }"
        );
        code.AppendLineAt(4, "return __view_" + member.Id + "!;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendReplacement(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var canWrite = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        if (!canWrite)
        {
            return;
        }

        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        if (IsObservableList(member))
        {
            var methodName = ReplacementMethodName(member, "Replace", members);
            code.AppendLineAt(
                2,
                "/// <summary>Replaces the live collection while rebuilding its notifying view.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"value\">The replacement collection instance.</param>"
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
            return;
        }

        if (IsObservableDictionary(member))
        {
            var methodName = ReplacementMethodName(member, "Replace", members);
            code.AppendLineAt(
                2,
                "/// <summary>Replaces the live dictionary while rebuilding its notifying view.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"value\">The replacement dictionary instance.</param>"
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

    private static void AppendPropertySummary(
        SharedIndentedBuilder code,
        SparseMemberModel member
    ) =>
        code.AppendLineAt(
            2,
            "/// <summary>Gets the observable value of member '"
                + member.Property.Name
                + "'.</summary>"
        );

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
        string runtimeNamespace
    )
    {
        var types = CollectionNames(member);
        var keyType = member.Collection.ElementType.Name;
        var modelValueType = member.Collection.ValueType!.Value.Name;
        var dictionaryType = CollectionViewType(member, types, runtimeNamespace);
        var nullable = member.Property.IsNullable ? "?" : "";
        AppendPropertySummary(code, member);
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
                + ", __onRawModelAccess); }"
        );
        code.AppendLineAt(4, "return __view_" + member.Id + "!;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
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
        AppendPropertySummary(code, member);
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
