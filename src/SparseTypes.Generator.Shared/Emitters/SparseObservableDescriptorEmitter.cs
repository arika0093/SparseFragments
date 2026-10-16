using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseTypes.Generator.Shared;

internal static class SparseObservableDescriptorEmitter
{
    internal static string AccessorName(string modelType) =>
        "__SparseGetDescriptors_" + SparseNaming.GetStableTypeHash(modelType, default);

    public static void Append(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect dialect,
        SparseDescriptorEmissionContext context
    )
    {
        var pathType = dialect.EffectivePathType(runtimeNamespace);
        code.AppendLineAt(2, context.MethodDeclaration(modelType, dialect, pathType));
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "return new " + dialect.DescriptorSetType + "(new " + dialect.DescriptorInterface + "[]"
        );
        code.AppendLineAt(3, "{");
        foreach (var member in members)
        {
            AppendMember(code, member, members, runtimeNamespace, dialect, context);
        }

        code.AppendLineAt(3, "});");
        code.AppendLineAt(2, "}");
    }

    private static void AppendMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect dialect,
        SparseDescriptorEmissionContext context
    )
    {
        var name = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var type = member.Property.Type.NonNullableName;
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var path = "pathPrefix.Member(" + literal + ")";
        var canWrite = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        var viewType = ViewTypeName(member, runtimeNamespace);
        // The INotifyPropertyChanged event occupies the PropertyChanged name.
        var eventCollision = member.Property.Name == "PropertyChanged";
        // Raw mutable values escape without a notifying view.
        var receiver = eventCollision
            ? context.Member("__model") + "." + property
            : context.Member(property);
        var getValue = SparseObservableEmitter.ExposesRawMutableReference(member)
            ? "() => { "
                + context.Member("__onRawModelAccess")
                + "?.Invoke(); return "
                + receiver
                + "; }"
            : "() => " + receiver;
        var getModelValue = "() => (object?)" + context.Member("__model") + "." + property;
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
                + context.Attributes(member)
                + ", "
                + getValue
                + ", "
                + Setter(member, members, runtimeNamespace, dialect, eventCollision, context)
                + ", "
                + ChildAccessor(member, path, dialect, context)
                + ", "
                + SparseObservableSequenceDescriptorEmitter.ArrayAccessor(
                    member,
                    path,
                    dialect,
                    context
                )
                + ", "
                + SparseObservableDictionaryDescriptorEmitter.DictionaryAccessor(
                    member,
                    path,
                    dialect,
                    context
                )
                + ", typeof("
                + viewType
                + "), "
                + SparseObservableSetDescriptorEmitter.SetAccessor(member, dialect, context)
                + ", "
                + ShapeExpression(member, dialect)
                + ", "
                + (member.Property.IsRequired ? "true" : "false")
                + ", "
                + (member.Property.IsNullableOblivious ? "true" : "false")
                + ", "
                + getModelValue
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
        // The colliding member has no proxy or view; reads observe model
        // values.
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

    private static string Setter(
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        string runtimeNamespace,
        SparseDescriptorDialect dialect,
        bool eventCollision,
        SparseDescriptorEmissionContext context
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
            // No observable proxy exists for this name.
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
                + ">.Default.Equals("
                + context.Member("__model")
                + "."
                + property
                + ", typed)) return true; "
                + context.Member("__model")
                + "."
                + property
                + " = typed; "
                + context.Member("__Raise")
                + "("
                + literal
                + "); if ("
                + context.Member("__onChanged")
                + " is not null) "
                + context.Member("__onChanged")
                + "(); return true; }";
        }

        if (member.ChildModel is not null && member.ChildIsReferenceType)
        {
            var childObservable = SparseObservableEmitter.ChildObservableType(member);
            var childModelType = member.ChildModel.Value.NonNullableName;
            var nullHandling =
                member.Property.IsNullable || member.Property.IsNullableOblivious
                    ? "if (value is null) { "
                        + context.Member(property)
                        + " = null; return true; } "
                    : "if (value is null) return false; ";
            return "value => { "
                + nullHandling
                + "if (value is "
                + childObservable
                + " proxy) { "
                + context.Member(property)
                + " = proxy; return true; } "
                + "if (!"
                + dialect.DescriptorValueType
                + ".TryGet<"
                + childModelType
                + ">(value, out var model)) return false; "
                + context.Member(property)
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
            // Replacement below raises the change itself, so reading the
            // incoming view must not invalidate the session cache as a side
            return "value => { if (value is "
                + viewType
                + " view) { "
                + context.Member(replace)
                + "(("
                + propertyType
                + ")view.UnsafeModel); return true; } "
                + converted
                + context.Member(replace)
                + "(typed); return true; }";
        }

        return "value => { " + converted + context.Member(property) + " = typed; return true; }";
    }

    private static string ChildAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect,
        SparseDescriptorEmissionContext context
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
        return "() => { var current = "
            + context.Member(property)
            + "; if ((object?)current is null) return null; var captured = current.__SparseTarget; var inner = current."
            + accessor
            + "("
            + path
            + "); return "
            + dialect.DescriptorSetType
            + ".Guarded(inner, () => { var live = "
            + context.Member(property)
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
