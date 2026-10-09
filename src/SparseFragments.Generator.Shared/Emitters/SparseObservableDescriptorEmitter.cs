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
        // The INotifyPropertyChanged event occupies the PropertyChanged name, so its
        // descriptor targets the underlying model directly with the same
        // notification behavior a proxy would produce. Non-scalar shapes with
        // this name stay omitted (documented limitation).
        var eventCollision = member.Property.Name == "PropertyChanged";
        // Raw mutable values escape without a notifying view; route the read
        // through the session's raw-access callback so cached HasChanges is dropped.
        var receiver = eventCollision ? "__model." + property : "this." + property;
        var getValue = SparseObservableEmitter.ExposesRawMutableReference(member)
            ? "() => { __onRawModelAccess?.Invoke(); return " + receiver + "; }"
            : "() => " + receiver;
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
                + Setter(member, members, runtimeNamespace, dialect, eventCollision)
                + ", "
                + ChildAccessor(member, path, dialect)
                + ", "
                + SparseObservableSequenceDescriptorEmitter.ArrayAccessor(member, path, dialect)
                + ", "
                + SparseObservableDictionaryDescriptorEmitter.DictionaryAccessor(
                    member,
                    path,
                    dialect
                )
                + ", typeof("
                + viewType
                + "), "
                + SparseObservableSetDescriptorEmitter.SetAccessor(member, dialect)
                + ", "
                + ShapeExpression(member, dialect)
                + ", "
                + (member.Property.IsRequired ? "true" : "false")
                + ", "
                + (member.Property.IsNullableOblivious ? "true" : "false")
                + "),"
        );
    }

    /// <summary>Emits static shape metadata independent of live instances.</summary>
    private static string ShapeExpression(SparseMemberModel member, SparseDescriptorDialect dialect)
    {
        var eventCollision = member.Property.Name == "PropertyChanged";
        var hasChild =
            member.ChildModel is not null && member.ChildIsReferenceType && !eventCollision;
        var elementKnown = member.Collection.ElementType.Name is not null;
        var scalarSequence =
            elementKnown
            && member.Collection.ValueType is null
            && (
                member.Property.Type.NonNullableName.EndsWith("[]", System.StringComparison.Ordinal)
                || member.Collection.Kind == SparseCollectionKind.Array
            );
        var hasArray =
            !eventCollision && (SparseObservableEmitter.IsObservableList(member) || scalarSequence);
        var hasDictionary =
            !eventCollision
            && (
                SparseObservableEmitter.IsObservableDictionary(member)
                || (
                    member.Collection.ValueType is not null
                    && member.Collection.CloneKind == SparseCloneCollectionKind.Dictionary
                )
            );
        var hasSet =
            !eventCollision
            && elementKnown
            && member.Collection.ValueType is null
            && member.Collection.Kind == SparseCollectionKind.Set;
        string TypeOrNull(bool present, string name) => present ? "typeof(" + name + ")" : "null";
        return "new "
            + dialect.DescriptorShapeType
            + " { HasChild = "
            + (hasChild ? "true" : "false")
            + ", ChildType = "
            + TypeOrNull(hasChild, member.ChildModel?.NonNullableName ?? "object")
            + ", HasArray = "
            + (hasArray ? "true" : "false")
            + ", ArrayItemType = "
            + TypeOrNull(hasArray, member.Collection.ElementType.NonNullableName)
            + ", ArrayItemNullable = "
            + (hasArray && IsNullable(member.Collection.ElementType.Name) ? "true" : "false")
            + ", HasDictionary = "
            + (hasDictionary ? "true" : "false")
            + ", DictionaryKeyType = "
            + TypeOrNull(hasDictionary, member.Collection.ElementType.NonNullableName)
            + ", DictionaryValueType = "
            + TypeOrNull(hasDictionary, member.Collection.ValueType?.NonNullableName ?? "object")
            + ", DictionaryValueNullable = "
            + (
                hasDictionary && IsNullable(member.Collection.ValueType?.Name ?? string.Empty)
                    ? "true"
                    : "false"
            )
            + ", HasSet = "
            + (hasSet ? "true" : "false")
            + ", SetItemType = "
            + TypeOrNull(hasSet, member.Collection.ElementType.NonNullableName)
            + ", SetItemNullable = "
            + (hasSet && IsNullable(member.Collection.ElementType.Name) ? "true" : "false")
            + " }";
    }

    private static string ViewTypeName(SparseMemberModel member, string runtimeNamespace)
    {
        // The colliding member has no proxy or view; reads observe model values.
        if (member.Property.Name == "PropertyChanged")
        {
            return member.Property.Type.NonNullableName;
        }

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
        SparseDescriptorDialect dialect,
        bool eventCollision
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var propertyType = member.Property.Type.Name;
        if (member.Property.IsReadOnly || member.Property.IsInitOnly)
        {
            return "null";
        }

        if (eventCollision)
        {
            // No observable proxy exists for this name; scalar members assign the
            // model directly with identical equality and notification behavior.
            if (
                member.ChildModel is not null
                || SparseObservableEmitter.IsObservableList(member)
                || SparseObservableEmitter.IsObservableDictionary(member)
                || member.Collection.Kind == SparseCollectionKind.Set
            )
            {
                return "null";
            }

            var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
            return "value => { if (!"
                + dialect.DescriptorValueType
                + ".TryGet<"
                + propertyType
                + ">(value, out var typed)) return false; if (global::System.Collections.Generic.EqualityComparer<"
                + propertyType
                + ">.Default.Equals(__model."
                + property
                + ", typed)) return true; __model."
                + property
                + " = typed; __Raise("
                + literal
                + "); if (__onChanged is not null) __onChanged(); return true; }";
        }

        if (member.ChildModel is not null && member.ChildIsReferenceType)
        {
            var childObservable = SparseObservableEmitter.ChildObservableType(member);
            var childModelType = member.ChildModel.Value.NonNullableName;
            var nullHandling =
                member.Property.IsNullable || member.Property.IsNullableOblivious
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

    internal static string ValueConversion(
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

    internal static bool IsNullable(string? typeName) =>
        typeName is not null && typeName.EndsWith("?", System.StringComparison.Ordinal);

    internal static string IsNullableExpression(string typeName) =>
        IsNullable(typeName) ? "true" : "false";
}
