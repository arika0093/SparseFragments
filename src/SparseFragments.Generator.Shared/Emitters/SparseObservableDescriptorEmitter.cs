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
                + ", () => this."
                + property
                + ", "
                + Setter(member, members, runtimeNamespace, dialect)
                + ", "
                + ChildAccessor(member, path)
                + ", "
                + ArrayAccessor(member, path, dialect)
                + ", "
                + DictionaryAccessor(member, path, dialect)
                + "),"
        );
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
            // The trusted accessor skips the raw-model notification: replacement below
            // raises the change itself, so reading the incoming view must not
            // invalidate the session cache as a side effect.
            return "value => { if (value is "
                + viewType
                + " view) { this."
                + replace
                + "(("
                + propertyType
                + ")view.UnsafeModel); return true; } "
                + converted
                + "this."
                + replace
                + "(typed); return true; }";
        }

        return "value => { " + converted + "this." + property + " = typed; return true; }";
    }

    private static string ChildAccessor(SparseMemberModel member, string path)
    {
        if (
            member.ChildModel is null
            || !member.ChildIsReferenceType
            || member.Property.Name == "PropertyChanged"
        )
        {
            return "null";
        }

        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var accessor = AccessorName(member.ChildModel.Value.NonNullableName);
        return "() => this."
            + property
            + " is null ? null : this."
            + property
            + "."
            + accessor
            + "("
            + path
            + ")";
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
            || !member.Property.Type.NonNullableName.EndsWith("[]", System.StringComparison.Ordinal)
        )
        {
            return "null";
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
            + "![index] })";
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
            + " })";
    }

    private static string DictionaryAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (!SparseObservableEmitter.IsObservableDictionary(member))
        {
            return "null";
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
            + "!.Remove(typedKey); } })";
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
