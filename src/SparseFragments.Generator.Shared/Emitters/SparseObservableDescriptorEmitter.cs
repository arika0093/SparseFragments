using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

internal static class SparseObservableDescriptorEmitter
{
    internal static string AccessorName(string modelType) =>
        "__SparseGetDescriptors_" + SparseNaming.GetStableTypeHash(modelType, default);

    public static void Append(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect dialect
    )
    {
        var accessorName = AccessorName(modelType);
        code.AppendLineAt(
            2,
            "internal "
                + dialect.DescriptorSetInterface
                + " "
                + accessorName
                + "(string pathPrefix)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "return new " + dialect.DescriptorSetType + "(new " + dialect.DescriptorInterface + "[]"
        );
        code.AppendLineAt(3, "{");
        foreach (var member in members)
        {
            if (member.Property.Name == "PropertyChanged")
            {
                continue;
            }

            AppendMember(code, member, members, runtimeNamespace, dialect);
        }

        code.AppendLineAt(3, "});");
        code.AppendLineAt(2, "}");
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect dialect
    )
    {
        var name = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var type = member.Property.Type.NonNullableName;
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var path =
            "(pathPrefix.Length == 0 ? " + literal + " : pathPrefix + \".\" + " + literal + ")";
        var canWrite = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        var viewType = ViewTypeName(member, runtimeNamespace);
        // Raw mutable values escape without a notifying view; route the read
        // through the session's raw-access callback so cached HasChanges is dropped.
        var getValue = SparseObservableEmitter.ExposesRawMutableReference(member)
            ? "() => { __onRawModelAccess?.Invoke(); return this." + property + "; }"
            : "() => this." + property;
        code.AppendLineAt(
            4,
            "new "
                + dialect.DescriptorType
                + "("
                + name
                + ", "
                + path
                + ", typeof("
                + type
                + "), "
                + (member.Property.IsNullable ? "true" : "false")
                + ", "
                + (canWrite ? "true" : "false")
                + ", "
                + Attributes(member)
                + ", "
                + getValue
                + ", "
                + Setter(member, members, runtimeNamespace, dialect)
                + ", "
                + ChildAccessor(member, path, dialect)
                + ", "
                + ArrayAccessor(member, path, dialect)
                + ", "
                + DictionaryAccessor(member, path, dialect)
                + ", typeof("
                + viewType
                + "), "
                + SetAccessor(member, dialect)
                + "),"
        );
    }

    private static string ViewTypeName(SparseMemberModel member, string runtimeNamespace)
    {
        if (member.ChildModel is not null && member.ChildIsReferenceType)
        {
            return SparseObservableEmitter.ChildObservableType(member);
        }

        if (
            SparseObservableEmitter.IsObservableList(member)
            || SparseObservableEmitter.IsObservableDictionary(member)
        )
        {
            var names = SparseObservableEmitter.CollectionNames(member);
            return SparseObservableEmitter.CollectionViewType(member, names, runtimeNamespace);
        }

        return member.Property.Type.NonNullableName;
    }

    private static string Attributes(SparseMemberModel member) =>
        string.IsNullOrEmpty(member.Property.AttributeExpressions)
            ? "global::System.Array.Empty<global::System.Attribute>()"
            : "new global::System.Attribute[] { " + member.Property.AttributeExpressions + " }";

    private static string Setter(
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect dialect
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var propertyType = member.Property.Type.Name;
        if (member.Property.IsReadOnly || member.Property.IsInitOnly)
        {
            return "null";
        }

        if (member.ChildModel is not null && member.ChildIsReferenceType)
        {
            var childObservable = SparseObservableEmitter.ChildObservableType(member);
            var childModelType = member.ChildModel.Value.NonNullableName;
            var nullHandling = member.Property.IsNullable
                ? "if (value is null) { this." + property + " = null; return true; } "
                : "if (value is null) return false; ";
            return "value => { "
                + nullHandling
                + "if (value is "
                + childObservable
                + " proxy) { this."
                + property
                + " = proxy; return true; } "
                + "if (!"
                + dialect.DescriptorValueType
                + ".TryGet<"
                + childModelType
                + ">(value, out var model)) return false; "
                + "this."
                + property
                + " = new "
                + childObservable
                + "(model); return true; }";
        }

        var converted =
            "if (!"
            + dialect.DescriptorValueType
            + ".TryGet<"
            + propertyType
            + ">(value, out var typed)) return false; ";
        if (
            SparseObservableEmitter.IsObservableList(member)
            || SparseObservableEmitter.IsObservableDictionary(member)
        )
        {
            var replace = SparseObservableEmitter.ReplacementMethodName(member, "Replace", members);
            var viewType = SparseObservableEmitter.CollectionViewType(
                member,
                SparseObservableEmitter.CollectionNames(member),
                runtimeNamespace
            );
            return "value => { if (value is "
                + viewType
                + " view) { this."
                + replace
                + "(("
                + propertyType
                + ")view.Model); return true; } "
                + converted
                + "this."
                + replace
                + "(typed); return true; }";
        }

        return "value => { " + converted + "this." + property + " = typed; return true; }";
    }

    private static string ChildAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (
            member.ChildModel is null
            || !member.ChildIsReferenceType
            || member.Property.Name == "PropertyChanged"
        )
        {
            return "null";
        }

        // Instance-bound descriptors: capture the current child model and fail
        // writes safely once the parent resolves to a different instance.
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var accessor = AccessorName(member.ChildModel.Value.NonNullableName);
        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; var captured = current.__SparseTarget; var inner = current."
            + accessor
            + "("
            + path
            + "); return "
            + dialect.DescriptorSetType
            + ".Guarded(inner, () => { var live = this."
            + property
            + "; if ((object?)live is null) return false; return global::System.Object.ReferenceEquals(live.__SparseTarget, captured); }); }";
    }

    private static string ArrayAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (SparseObservableEmitter.IsObservableList(member))
        {
            return ListAccessor(member, path, dialect);
        }

        if (
            member.Collection.ElementType.Name is null
            || member.Collection.ValueType is not null
            || (
                member.Collection.Kind != SparseCollectionKind.Array
                && !member.Property.Type.NonNullableName.EndsWith(
                    "[]",
                    System.StringComparison.Ordinal
                )
            )
        )
        {
            return "null";
        }

        if (
            !member.Property.Type.NonNullableName.EndsWith("[]", System.StringComparison.Ordinal)
            && member.Collection.Kind == SparseCollectionKind.Array
        )
        {
            return ReadOnlySequenceAccessor(member, path, dialect);
        }

        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var itemType = member.Collection.ElementType.NonNullableName;
        return "() => this."
            + property
            + " is null ? null : new "
            + dialect.ArrayDescriptorType
            + "(typeof("
            + itemType
            + "), "
            + IsNullableExpression(member.Collection.ElementType.Name)
            + ", new "
            + dialect.ArrayDescriptorAccessType
            + " { Count = () => this."
            + property
            + "!.Length, GetItem = index => this."
            + property
            + "![index] }, typeof("
            + itemType
            + "))";
    }

    /// <summary>Emits a read-only sequence descriptor for list-like shapes.</summary>
    /// <remarks>
    /// Sources already implementing <c>IReadOnlyList&lt;T&gt;</c> stay live; pure
    /// <c>IEnumerable&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c> sources are
    /// snapshotted once per descriptor so repeated <c>GetItem</c> calls do not
    /// re-enumerate. All mutation flags are false.
    /// </remarks>
    private static string ReadOnlySequenceAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var element = member.Collection.ElementType;
        var itemType = element.NonNullableName;
        var hasProxy = element.IsFragmentModel && element.IsReferenceType;
        var viewType = hasProxy
            ? itemType + "." + (element.ObservableTypeName ?? "Observable")
            : itemType;
        var itemAccessor = hasProxy ? AccessorName(itemType) : string.Empty;
        // Transient element proxies notify the parent property; the collection itself
        // has no notifying view, so size mutations are unsupported.
        var changed =
            "() => { __Raise(" + literal + "); if (__onChanged is not null) __onChanged(); }";
        var wrap = hasProxy
            ? "item is null ? null : new "
                + viewType
                + "(item, "
                + changed
                + ", __onRawModelAccess)"
            : "item";
        var descriptors = hasProxy
            ? ", GetItemDescriptors = index => { var item = items[index]; var view = (object?)("
                + wrap
                + "); return view is null ? null : (("
                + viewType
                + ")view)."
                + itemAccessor
                + "("
                + path
                + " + \"[\" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\"); }"
            : string.Empty;
        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; "
            + "var source = (global::System.Collections.Generic.IEnumerable<"
            + element.Name
            + ">)current; "
            + "var live = source as global::System.Collections.Generic.IReadOnlyList<"
            + element.Name
            + ">; "
            + "global::System.Collections.Generic.IReadOnlyList<"
            + element.Name
            + "> items = live ?? (global::System.Collections.Generic.IReadOnlyList<"
            + element.Name
            + ">)global::System.Linq.Enumerable.ToArray(source); "
            + "return new "
            + dialect.ArrayDescriptorType
            + "(typeof("
            + itemType
            + "), "
            + IsNullableExpression(element.Name)
            + ", new "
            + dialect.ArrayDescriptorAccessType
            + " { Count = () => items.Count, GetItem = index => { var item = items[index]; return (object?)("
            + wrap
            + "); }"
            + descriptors
            + " }, typeof("
            + viewType
            + ")); }";
    }

    private static string ListAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var names = SparseObservableEmitter.CollectionNames(member);
        var modelItemType = member.Collection.ElementType.Name;
        var itemAccessorName = AccessorName(member.Collection.ElementType.NonNullableName);
        var descriptorChildAccessor = names.HasElementProxy
            ? "GetItemDescriptors = index => { var item = this."
                + property
                + "![index]; return item is null ? null : item."
                + itemAccessorName
                + "("
                + path
                + " + \"[\" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\"); }, "
            : string.Empty;
        var conversion = ValueConversion(
            "value",
            modelItemType,
            "item",
            names.HasElementProxy ? names.ViewType.TrimEnd('?') : null,
            IsNullable(modelItemType),
            dialect
        );
        var mutable = "!this." + property + "!.IsReadOnly";
        var setItem =
            "TrySetItem = (index, value) => { "
            + modelItemType
            + " item; if (!"
            + mutable
            + " || (uint)index >= (uint)this."
            + property
            + "!.Count) return false; "
            + conversion
            + "this."
            + property
            + "!.SetModel(index, item); return true; }, ";
        var add =
            "TryAdd = value => { "
            + modelItemType
            + " item; if (!"
            + mutable
            + ") return false; "
            + conversion
            + "this."
            + property
            + "!.AddModel(item); return true; }, ";
        var insert =
            "TryInsert = (index, value) => { "
            + modelItemType
            + " item; if (!"
            + mutable
            + " || index < 0 || index > this."
            + property
            + "!.Count) return false; "
            + conversion
            + "this."
            + property
            + "!.InsertModel(index, item); return true; }, ";
        var remove =
            "TryRemoveAt = index => { if (!"
            + mutable
            + " || (uint)index >= (uint)this."
            + property
            + "!.Count) return false; this."
            + property
            + "!.RemoveAt(index); return true; }, ";
        var move =
            "TryMove = (oldIndex, newIndex) => { if (!"
            + mutable
            + " || (uint)oldIndex >= (uint)this."
            + property
            + "!.Count || (uint)newIndex >= (uint)this."
            + property
            + "!.Count) return false; this."
            + property
            + "!.Move(oldIndex, newIndex); return true; }";
        return "() => this."
            + property
            + " is null ? null : new "
            + dialect.ArrayDescriptorType
            + "(typeof("
            + member.Collection.ElementType.NonNullableName
            + "), "
            + IsNullableExpression(member.Collection.ElementType.Name)
            + ", new "
            + dialect.ArrayDescriptorAccessType
            + " { Count = () => this."
            + property
            + "!.Count, GetItem = index => this."
            + property
            + "![index], CanSetItem = () => "
            + mutable
            + ", CanAdd = () => "
            + mutable
            + ", CanInsert = () => "
            + mutable
            + ", CanRemove = () => "
            + mutable
            + ", CanMove = () => "
            + mutable
            + ", "
            + descriptorChildAccessor
            + setItem
            + add
            + insert
            + remove
            + move
            + " }, typeof("
            + (
                names.HasElementProxy
                    ? names.ViewType.TrimEnd('?')
                    : member.Collection.ElementType.NonNullableName
            )
            + "))";
    }

    /// <summary>Emits a read-only dictionary descriptor for sorted/read-only shapes.</summary>
    /// <remarks>
    /// SortedDictionary, SortedList and IReadOnlyDictionary members have no
    /// notifying observable view, so the descriptor stays live over the current
    /// IReadOnlyDictionary without supporting size or value mutations.
    /// </remarks>
    private static string ReadOnlyDictionaryAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (
            member.Collection.ValueType is not SparseTypeModel valueType
            || member.Collection.ElementType.Name is null
            || valueType.Name is null
            || member.Collection.CloneKind != SparseCloneCollectionKind.Dictionary
        )
        {
            return "null";
        }

        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var keyType = member.Collection.ElementType.Name;
        var keyTypeDecl = member.Collection.ElementType.NonNullableName;
        var valueTypeDecl = valueType.NonNullableName;
        var hasProxy = valueType.IsFragmentModel && valueType.IsReferenceType;
        var viewType = hasProxy
            ? valueTypeDecl + "." + (valueType.ObservableTypeName ?? "Observable")
            : valueTypeDecl;
        var keyCast = dialect.DescriptorValueType + ".TryGet<" + keyType + ">";
        var changed =
            "() => { __Raise(" + literal + "); if (__onChanged is not null) __onChanged(); }";
        string WrapValue(string variable) =>
            hasProxy
                ? variable
                    + " is null ? null : new "
                    + viewType
                    + "("
                    + variable
                    + ", "
                    + changed
                    + ", __onRawModelAccess)"
                : variable;
        var childAccessor = hasProxy
            ? "GetValueDescriptors = key => { if (key is null || !"
                + keyCast
                + "(key, out var typedKey) || !source.TryGetValue(typedKey, out var item)) return null; var view = (object?)("
                + WrapValue("item")
                + "); return view is null ? null : (("
                + viewType
                + ")view)."
                + AccessorName(valueTypeDecl)
                + "("
                + path
                + " + \"[\" + global::System.Convert.ToString(key, global::System.Globalization.CultureInfo.InvariantCulture) + \"]\"); }, "
            : string.Empty;
        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; "
            + "var source = (global::System.Collections.Generic.IReadOnlyDictionary<"
            + keyType
            + ", "
            + valueType.Name
            + ">)current; "
            + "return new "
            + dialect.DictionaryDescriptorType
            + "(typeof("
            + keyTypeDecl
            + "), typeof("
            + valueTypeDecl
            + "), "
            + IsNullableExpression(valueType.Name)
            + ", new "
            + dialect.DictionaryDescriptorAccessType
            + " { Count = () => source.Count, "
            + "Keys = () => global::System.Linq.Enumerable.Cast<object?>(source.Keys), "
            + "TryGetValue = key => { if (key is null || !"
            + keyCast
            + "(key, out var typedKey) || !source.TryGetValue(typedKey, out var foundValue)) return (false, (object?)null); return (true, (object?)("
            + WrapValue("foundValue")
            + ")); }, "
            + childAccessor
            + "}, typeof("
            + viewType
            + ")); }";
    }

    private static string DictionaryAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (!SparseObservableEmitter.IsObservableDictionary(member))
        {
            return ReadOnlyDictionaryAccessor(member, path, dialect);
        }

        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var keyType = member.Collection.ElementType.Name;
        var modelValueType = member.Collection.ValueType!.Value.Name;
        var names = SparseObservableEmitter.CollectionNames(member);
        var mutable = "!this." + property + "!.IsReadOnly";
        var keyCast = dialect.DescriptorValueType + ".TryGet<" + keyType + ">";
        var valueConversion = ValueConversion(
            "value",
            modelValueType,
            "typedValue",
            names.HasElementProxy ? names.ViewType.TrimEnd('?') : null,
            IsNullable(modelValueType),
            dialect
        );
        var childAccessor = names.HasElementProxy
            ? "GetValueDescriptors = key => { if (key is null || !"
                + keyCast
                + "(key, out var typedKey) || !this."
                + property
                + "!.TryGetValue(typedKey, out var item) || item is null) return null; return item."
                + AccessorName(member.Collection.ValueType.Value.NonNullableName)
                + "("
                + path
                + " + \"[\" + global::System.Convert.ToString(key, global::System.Globalization.CultureInfo.InvariantCulture) + \"]\"); }, "
            : string.Empty;
        return "() => this."
            + property
            + " is null ? null : new "
            + dialect.DictionaryDescriptorType
            + "(typeof("
            + keyType
            + "), typeof("
            + member.Collection.ValueType.Value.NonNullableName
            + "), "
            + IsNullableExpression(modelValueType)
            + ", new "
            + dialect.DictionaryDescriptorAccessType
            + " { Count = () => this."
            + property
            + "!.Count, CanAdd = () => "
            + mutable
            + ", CanRemove = () => "
            + mutable
            + ", CanSet = () => "
            + mutable
            + ", Keys = () => global::System.Linq.Enumerable.Cast<object?>(this."
            + property
            + "!.Keys), "
            + "TryGetValue = key => { if (key is null || !"
            + keyCast
            + "(key, out var typedKey) || !this."
            + property
            + "!.TryGetValue(typedKey, out var result)) return (false, (object?)null); return (true, (object?)result); }, "
            + childAccessor
            + "TryAdd = (key, value) => { if (key is null || !"
            + mutable
            + " || !"
            + keyCast
            + "(key, out var typedKey) || this."
            + property
            + "!.ContainsKey(typedKey)) return false; "
            + modelValueType
            + " typedValue; "
            + valueConversion
            + "this."
            + property
            + "!.AddModel(typedKey, typedValue); return true; }, "
            + "TrySetValue = (key, value) => { if (key is null || !"
            + mutable
            + " || !"
            + keyCast
            + "(key, out var typedKey) || !"
            + "this."
            + property
            + "!.ContainsKey(typedKey)) return false; "
            + modelValueType
            + " typedValue; "
            + valueConversion
            + "this."
            + property
            + "!.SetModel(typedKey, typedValue); return true; }, "
            + "TryRemove = key => { if (key is null || !"
            + mutable
            + " || !"
            + keyCast
            + "(key, out var typedKey)) return false; return this."
            + property
            + "!.Remove(typedKey); } }, typeof("
            + (
                names.HasElementProxy
                    ? names.ViewType.TrimEnd('?')
                    : member.Collection.ValueType.Value.NonNullableName
            )
            + "))";
    }

    /// <summary>Emits a set descriptor for HashSet/ISet/IReadOnlySet shapes.</summary>
    /// <remarks>
    /// Reads stay live over the backing set so membership honors its comparer.
    /// Mutation reuses the live set and raises the parent property notification,
    /// but only where the backing set is a mutable <c>ISet&lt;T&gt;</c>. The
    /// <c>IReadOnlySet&lt;T&gt;</c> reference is only named for members declared
    /// with that type (which proves the compilation provides it).
    /// </remarks>
    private static string SetAccessor(SparseMemberModel member, SparseDescriptorDialect dialect)
    {
        if (
            member.Collection.Kind != SparseCollectionKind.Set
            || member.Collection.ElementType.Name is null
            || member.Collection.ValueType is not null
        )
        {
            return "null";
        }

        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var element = member.Collection.ElementType;
        var itemType = element.Name;
        var readOnlyDeclared = member.Property.Type.NonNullableName.Contains("IReadOnlySet<");
        var hasProxy = element.IsFragmentModel && element.IsReferenceType;
        var proxyType = hasProxy
            ? element.NonNullableName + "." + (element.ObservableTypeName ?? "Observable")
            : null;
        var isNullable = IsNullable(itemType);
        var containsConversion = ValueConversion(
            "item",
            itemType,
            "typedItem",
            proxyType,
            isNullable,
            dialect
        );
        var mutateConversion = ValueConversion(
            "value",
            itemType,
            "typedItem",
            proxyType,
            isNullable,
            dialect
        );
        var notify = "__Raise(" + literal + "); if (__onChanged is not null) __onChanged();";
        string count;
        string contains;
        string readOnlyLocals;
        if (readOnlyDeclared)
        {
            readOnlyLocals =
                "var readOnly = source as global::System.Collections.Generic.IReadOnlySet<"
                + itemType
                + ">; if (edit is null && readOnly is null) return null; ";
            count = "() => edit is not null ? edit.Count : readOnly!.Count";
            contains =
                "item => { "
                + itemType
                + " typedItem; "
                + containsConversion
                + "return edit is not null ? edit.Contains(typedItem) : readOnly!.Contains(typedItem); }";
        }
        else
        {
            readOnlyLocals = "if (edit is null) return null; ";
            count = "() => edit.Count";
            contains =
                "item => { "
                + itemType
                + " typedItem; "
                + containsConversion
                + "return edit.Contains(typedItem); }";
        }

        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; "
            + "var source = (global::System.Collections.Generic.IEnumerable<"
            + itemType
            + ">)current; "
            + "var edit = source as global::System.Collections.Generic.ISet<"
            + itemType
            + ">; "
            + readOnlyLocals
            + "return new "
            + dialect.SetDescriptorType
            + "(typeof("
            + element.NonNullableName
            + "), "
            + IsNullableExpression(itemType)
            + ", new "
            + dialect.SetDescriptorAccessType
            + " { Count = "
            + count
            + ", Items = () => global::System.Linq.Enumerable.Cast<object?>(source), "
            + "Contains = "
            + contains
            + ", CanAdd = () => edit is not null && !edit.IsReadOnly, "
            + "CanRemove = () => edit is not null && !edit.IsReadOnly, "
            + "TryAdd = value => { if (edit is null || edit.IsReadOnly) return false; "
            + itemType
            + " typedItem; "
            + mutateConversion
            + "if (!edit.Add(typedItem)) return false; "
            + notify
            + " return true; }, "
            + "TryRemove = value => { if (edit is null || edit.IsReadOnly) return false; "
            + itemType
            + " typedItem; "
            + mutateConversion
            + "if (!edit.Remove(typedItem)) return false; "
            + notify
            + " return true; } }); }";
    }

    private static string ValueConversion(
        string value,
        string modelType,
        string variable,
        string? observableType,
        bool isNullable,
        SparseDescriptorDialect dialect
    )
    {
        var cast = dialect.DescriptorValueType + ".TryGet<" + modelType + ">";
        var rejectNull = isNullable ? string.Empty : "if (" + value + " is null) return false; ";
        if (observableType is null)
        {
            return rejectNull
                + "if (!"
                + cast
                + "("
                + value
                + ", out "
                + variable
                + ")) return false; ";
        }

        return rejectNull
            + "if ("
            + value
            + " is "
            + observableType
            + " proxy) "
            + variable
            + " = proxy.__SparseTarget; else if (!"
            + cast
            + "("
            + value
            + ", out "
            + variable
            + ")) return false; ";
    }

    private static bool IsNullable(string typeName) =>
        typeName.EndsWith("?", System.StringComparison.Ordinal);

    private static string IsNullableExpression(string typeName) =>
        IsNullable(typeName) ? "true" : "false";
}
