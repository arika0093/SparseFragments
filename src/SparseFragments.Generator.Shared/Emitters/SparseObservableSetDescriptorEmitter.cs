using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits set descriptors for HashSet, ISet and IReadOnlySet shapes.</summary>
internal static class SparseObservableSetDescriptorEmitter
{
    /// <summary>Emits a set descriptor for HashSet/ISet/IReadOnlySet shapes.</summary>
    /// <remarks>
    /// Reads stay live over the backing set so membership honors its comparer.
    /// Mutation reuses the live set and raises the parent property notification,
    /// but only where the backing set is a mutable <c>ISet&lt;T&gt;</c>. The
    /// <c>IReadOnlySet&lt;T&gt;</c> reference is only named for members declared
    /// with that type (which proves the compilation provides it).
    /// </remarks>
    internal static string SetAccessor(SparseMemberModel member, SparseDescriptorDialect dialect)
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
        var isNullable = SparseObservableDescriptorEmitter.IsNullable(itemType);
        var containsConversion = SparseObservableDescriptorEmitter.ValueConversion(
            "item",
            itemType,
            "typedItem",
            proxyType,
            isNullable,
            dialect
        );
        var mutateConversion = SparseObservableDescriptorEmitter.ValueConversion(
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
            + SparseObservableDescriptorEmitter.IsNullableExpression(itemType)
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
}
