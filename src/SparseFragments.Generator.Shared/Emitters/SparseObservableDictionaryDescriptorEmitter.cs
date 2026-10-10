using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits dictionary descriptors for observable and read-only dictionary shapes.</summary>
internal static class SparseObservableDictionaryDescriptorEmitter
{
    /// <summary>Emits a read-only dictionary descriptor for sorted/read-only shapes.</summary>
    /// <remarks>
    /// SortedDictionary, SortedList and IReadOnlyDictionary members have no
    /// notifying observable view, so the descriptor stays live over the current
    /// IReadOnlyDictionary without supporting size or value mutations.
    /// </remarks>
    internal static string ReadOnlyDictionaryAccessor(
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
                + SparseObservableDescriptorEmitter.AccessorName(valueTypeDecl)
                + "("
                + path
                + ".Key(typedKey)); }, "
            : string.Empty;
        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; var __sparse_captured = current; "
            + "var source = (global::System.Collections.Generic.IReadOnlyDictionary<"
            + keyType
            + ", "
            + valueType.Name
            + ">)current; "
            + "return "
            + dialect.DictionaryDescriptorType
            + ".Guarded(new "
            + dialect.DictionaryDescriptorType
            + "(typeof("
            + keyTypeDecl
            + "), typeof("
            + valueTypeDecl
            + "), "
            + SparseObservableDescriptorEmitter.IsNullableExpression(valueType.Name)
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
            + "GetValueModel = key => { if (key is null || !"
            + keyCast
            + "(key, out var typedKey) || !source.TryGetValue(typedKey, out var foundModel)) return null; return (object?)foundModel; }"
            + "}, typeof("
            + viewType
            + ")), () => global::System.Object.ReferenceEquals(this."
            + property
            + ", __sparse_captured)); }";
    }

    internal static string DictionaryAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (member.Property.Name == "PropertyChanged")
        {
            return "null";
        }

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
        var valueConversion = SparseObservableDescriptorEmitter.ValueConversion(
            "value",
            modelValueType,
            "typedValue",
            names.HasElementProxy ? names.ViewType.TrimEnd('?') : null,
            SparseObservableDescriptorEmitter.IsNullable(modelValueType),
            dialect
        );
        var childAccessor = names.HasElementProxy
            ? "GetValueDescriptors = key => { if (key is null || !"
                + keyCast
                + "(key, out var typedKey) || !this."
                + property
                + "!.TryGetValue(typedKey, out var item) || item is null) return null; return item."
                + SparseObservableDescriptorEmitter.AccessorName(
                    member.Collection.ValueType.Value.NonNullableName
                )
                + "("
                + path
                + ".Key(typedKey)); }, "
            : string.Empty;
        var viewTypeName = names.HasElementProxy
            ? names.ViewType.TrimEnd('?')
            : member.Collection.ValueType.Value.NonNullableName;
        // Identity resolver for retained value descriptors: the unwrapped model
        // for a key, or null when the key is absent or unconvertible.
        var valueResolver = names.HasElementProxy
            ? "GetValueModel = key => { if (key is null || !"
                + keyCast
                + "(key, out var typedKey) || !this."
                + property
                + "!.TryGetValue(typedKey, out var view)) return null; return view is null ? null : (object?)(view is "
                + viewTypeName
                + " proxy ? proxy.__SparseTarget : view); }"
            : "GetValueModel = key => { if (key is null || !"
                + keyCast
                + "(key, out var typedKey) || !this."
                + property
                + "!.TryGetValue(typedKey, out var found)) return null; return (object?)found; }";
        return "() => this."
            + property
            + " is null ? null : "
            + dialect.DictionaryDescriptorType
            + ".Guarded(new "
            + dialect.DictionaryDescriptorType
            + "(typeof("
            + keyType
            + "), typeof("
            + member.Collection.ValueType.Value.NonNullableName
            + "), "
            + SparseObservableDescriptorEmitter.IsNullableExpression(modelValueType)
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
            + "!.Remove(typedKey); }, "
            + valueResolver
            + " }, typeof("
            + viewTypeName
            + ")), static () => true)";
    }
}
