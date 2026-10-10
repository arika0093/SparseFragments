using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits sequence descriptors for arrays, read-only sequences and observable lists.</summary>
internal static class SparseObservableSequenceDescriptorEmitter
{
    internal static string ArrayAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        if (member.Property.Name == "PropertyChanged")
        {
            return "null";
        }

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
        var literal = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        var element = member.Collection.ElementType;
        var itemType = element.NonNullableName;
        // Arrays hold models directly. Fragment elements still expose nested
        // descriptors through transient proxies that notify the parent property;
        // structural array mutations stay unsupported (fixed size).
        var hasProxy = element.IsFragmentModel && element.IsReferenceType;
        var viewType = hasProxy
            ? itemType + "." + (element.ObservableTypeName ?? "Observable")
            : itemType;
        var descriptors = hasProxy
            ? ", GetItemDescriptors = index => { var item = __sparse_captured[index]; if ((object?)item is null) return null; var view = new "
                + viewType
                + "(item, () => { __Raise("
                + literal
                + "); if (__onChanged is not null) __onChanged(); }, __onRawModelAccess); return view."
                + SparseObservableDescriptorEmitter.AccessorName(itemType)
                + "("
                + path
                + ".At(index)); }, "
                + "GetItemModel = index => (uint)index >= (uint)__sparse_captured.Length ? null : (object?)__sparse_captured[index]"
            : string.Empty;
        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; var __sparse_captured = current; return "
            + dialect.ArrayDescriptorType
            + ".Guarded(new "
            + dialect.ArrayDescriptorType
            + "(typeof("
            + itemType
            + "), "
            + SparseObservableDescriptorEmitter.IsNullableExpression(element.Name)
            + ", new "
            + dialect.ArrayDescriptorAccessType
            + " { Count = () => __sparse_captured.Length, GetItem = index => __sparse_captured[index]"
            + descriptors
            + " }, typeof("
            + itemType
            + ")), () => global::System.Object.ReferenceEquals(this."
            + property
            + ", __sparse_captured)); }";
    }

    /// <summary>Emits a read-only sequence descriptor for list-like shapes.</summary>
    /// <remarks>
    /// Sources already implementing <c>IReadOnlyList&lt;T&gt;</c> stay live; pure
    /// <c>IEnumerable&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c> sources are
    /// snapshotted once per descriptor so repeated <c>GetItem</c> calls do not
    /// re-enumerate. All mutation flags are false.
    /// </remarks>
    internal static string ReadOnlySequenceAccessor(
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
        var itemAccessor = hasProxy
            ? SparseObservableDescriptorEmitter.AccessorName(itemType)
            : string.Empty;
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
                + ".At(index)); }, "
                + "GetItemModel = index => (uint)index >= (uint)items.Count ? null : (object?)items[index]"
            : string.Empty;
        return "() => { var current = this."
            + property
            + "; if ((object?)current is null) return null; var __sparse_captured = current; "
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
            + "return "
            + dialect.ArrayDescriptorType
            + ".Guarded(new "
            + dialect.ArrayDescriptorType
            + "(typeof("
            + itemType
            + "), "
            + SparseObservableDescriptorEmitter.IsNullableExpression(element.Name)
            + ", new "
            + dialect.ArrayDescriptorAccessType
            + " { Count = () => items.Count, GetItem = index => { var item = items[index]; return (object?)("
            + wrap
            + "); }"
            + descriptors
            + " }, typeof("
            + viewType
            + ")), () => global::System.Object.ReferenceEquals(this."
            + property
            + ", __sparse_captured)); }";
    }

    internal static string ListAccessor(
        SparseMemberModel member,
        string path,
        SparseDescriptorDialect dialect
    )
    {
        var property = SparseNaming.EscapeIdentifier(member.Property.Name);
        var names = SparseObservableEmitter.CollectionNames(member);
        var modelItemType = member.Collection.ElementType.Name;
        var itemAccessorName = SparseObservableDescriptorEmitter.AccessorName(
            member.Collection.ElementType.NonNullableName
        );
        var itemViewType = names.HasElementProxy
            ? names.ViewType.TrimEnd('?')
            : member.Collection.ElementType.NonNullableName;
        var descriptorChildAccessor = names.HasElementProxy
            ? "GetItemDescriptors = index => { var item = this."
                + property
                + "![index]; return item is null ? null : item."
                + itemAccessorName
                + "("
                + path
                + ".At(index)); }, "
            : string.Empty;
        // Identity resolver for retained item descriptors: the unwrapped model at
        // an index, or null when out of range. Guards compare it by reference.
        var modelResolver = names.HasElementProxy
            ? "GetItemModel = index => { if ((uint)index >= (uint)this."
                + property
                + "!.Count) return null; var view = this."
                + property
                + "![index]; return view is null ? null : (object?)(view is "
                + itemViewType
                + " proxy ? proxy.__SparseTarget : view); }"
            : "GetItemModel = index => (uint)index >= (uint)this."
                + property
                + "!.Count ? null : (object?)this."
                + property
                + "![index]";
        var conversion = SparseObservableDescriptorEmitter.ValueConversion(
            "value",
            modelItemType,
            "item",
            names.HasElementProxy ? names.ViewType.TrimEnd('?') : null,
            SparseObservableDescriptorEmitter.IsNullable(modelItemType),
            dialect
        );
        var canWrite = "!this." + property + "!.IsReadOnly";
        var canResize = canWrite + " && !this." + property + "!.IsFixedSize";
        // Assigned-key duplicates are rejected before any mutation; unassigned
        // sentinels are exempt and proceed like direct observable mutation.
        var duplicateAdd = DuplicateKeyGuard(member, names, property, "item", null);
        var duplicateSet = DuplicateKeyGuard(member, names, property, "item", "index");
        // Provider failures surface as NotSupportedException for fixed-size or
        // custom lists; the Try contract reports false instead of propagating.
        const string unsupportedGuard =
            "catch (global::System.NotSupportedException) { return false; } ";
        var setItem =
            "TrySetItem = (index, value) => { "
            + modelItemType
            + " item; if (!"
            + canWrite
            + " || (uint)index >= (uint)this."
            + property
            + "!.Count) return false; "
            + conversion
            + duplicateSet
            + "try { this."
            + property
            + "!.SetModel(index, item); } "
            + unsupportedGuard
            + "return true; }, ";
        var add =
            "TryAdd = value => { "
            + modelItemType
            + " item; if (!"
            + canResize
            + ") return false; "
            + conversion
            + duplicateAdd
            + "try { this."
            + property
            + "!.AddModel(item); } "
            + unsupportedGuard
            + "return true; }, ";
        var insert =
            "TryInsert = (index, value) => { "
            + modelItemType
            + " item; if (!"
            + canResize
            + " || index < 0 || index > this."
            + property
            + "!.Count) return false; "
            + conversion
            + duplicateAdd
            + "try { this."
            + property
            + "!.InsertModel(index, item); } "
            + unsupportedGuard
            + "return true; }, ";
        var remove =
            "TryRemoveAt = index => { if (!"
            + canResize
            + " || (uint)index >= (uint)this."
            + property
            + "!.Count) return false; try { this."
            + property
            + "!.RemoveAt(index); } "
            + unsupportedGuard
            + "return true; }, ";
        var move =
            "TryMove = (oldIndex, newIndex) => { if (!"
            + canResize
            + " || (uint)oldIndex >= (uint)this."
            + property
            + "!.Count || (uint)newIndex >= (uint)this."
            + property
            + "!.Count) return false; try { this."
            + property
            + "!.Move(oldIndex, newIndex); } "
            + unsupportedGuard
            + "return true; }, ";
        var keyed = KeyedSequenceMetadata(member, property, names, dialect);
        return "() => this."
            + property
            + " is null ? null : "
            + dialect.ArrayDescriptorType
            + ".Guarded(new "
            + dialect.ArrayDescriptorType
            + "(typeof("
            + member.Collection.ElementType.NonNullableName
            + "), "
            + SparseObservableDescriptorEmitter.IsNullableExpression(
                member.Collection.ElementType.Name
            )
            + ", new "
            + dialect.ArrayDescriptorAccessType
            + " { Count = () => this."
            + property
            + "!.Count, GetItem = index => this."
            + property
            + "![index], CanSetItem = () => "
            + canWrite
            + ", CanAdd = () => "
            + canResize
            + ", CanInsert = () => "
            + canResize
            + ", CanRemove = () => "
            + canResize
            + ", CanMove = () => "
            + canResize
            + ", "
            + descriptorChildAccessor
            + setItem
            + add
            + insert
            + remove
            + move
            + modelResolver
            + (keyed.Resolvers.Length == 0 ? string.Empty : ", " + keyed.Resolvers)
            + " }, typeof("
            + (
                names.HasElementProxy
                    ? names.ViewType.TrimEnd('?')
                    : member.Collection.ElementType.NonNullableName
            )
            + ")"
            + keyed.ConstructorArgs
            + "), static () => true)";
    }

    /// <summary>Emits keyed-identity metadata sourced from key analysis.</summary>
    /// <remarks>
    /// Unkeyed sequences contribute no resolvers and null constructor metadata,
    /// so <c>IsKeyed</c> stays false and lookups report absent.
    /// </remarks>
    private static (string Resolvers, string ConstructorArgs) KeyedSequenceMetadata(
        SparseMemberModel member,
        string property,
        SparseObservableEmitter.CollectionProxyNames names,
        SparseDescriptorDialect dialect
    )
    {
        var keyTypeName = member.Collection.KeyTypeName;
        if (!member.Collection.IsKeyedSequence || keyTypeName is null)
        {
            return (string.Empty, string.Empty);
        }

        var keyCast = dialect.DescriptorValueType + ".TryGet<" + keyTypeName + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyTypeName + ">.Default";
        string KeyOf(string modeller) => KeyOfObjectExpression(member, modeller);
        // Views hold proxies for reference fragments; keys always read the model.
        string Unwrap(string view) => UnwrapModelExpression(names, view);
        var resolvers =
            "GetItemKey = index => { var rawKeyedItem = this."
            + property
            + "![index]; var keyedModel = "
            + Unwrap("rawKeyedItem")
            + "; if (keyedModel is null) return null; return (object?)("
            + KeyOf("keyedModel")
            + "); }, "
            + "IndexOfKey = key => { if (!"
            + keyCast
            + "(key, out var typedKey)) return -1; var keyedList = this."
            + property
            + "!; for (var keyedIndex = 0; keyedIndex < keyedList.Count; keyedIndex++) { var keyedModel = "
            + Unwrap("keyedList[keyedIndex]")
            + "; if (keyedModel is null) continue; if ("
            + comparer
            + ".Equals("
            + KeyOf("keyedModel")
            + ", typedKey)) return keyedIndex; } return -1; }, "
            + "IsUnassignedKey = key => { if (!"
            + keyCast
            + "(key, out var typedKey)) return false; return "
            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "typedKey")
            + "; }, ";
        var namesLiteral =
            "new string[] { "
            + string.Join(
                ", ",
                member.Collection.KeyPropertyNames.Select(name =>
                    SymbolDisplay.FormatLiteral(name, true)
                )
            )
            + " }";
        return (resolvers, ", typeof(" + keyTypeName + "), " + namesLiteral);
    }

    /// <summary>Builds a key expression reading an object-typed model.</summary>
    private static string KeyOfObjectExpression(SparseMemberModel member, string modeller)
    {
        var modelType = member.Collection.ElementType.NonNullableName;
        var cast = "((" + modelType + ")" + modeller + ")";
        var bang = SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "!" : string.Empty;
        // Single-property keys only: the key type is the declared property type,
        // which may itself be a tuple or value object for composite identity.
        var keys = member.Collection.KeyPropertyNames;
        if (keys.Length == 1)
        {
            return cast + "." + SparseNaming.EscapeIdentifier(keys[0]) + bang;
        }

        return "throw new global::System.InvalidOperationException(\"Keyed collection has no usable key property.\")";
    }

    /// <summary>Builds a model expression unwrapping an element view.</summary>
    private static string UnwrapModelExpression(
        SparseObservableEmitter.CollectionProxyNames names,
        string view
    ) =>
        names.HasElementProxy
            ? "("
                + view
                + " is "
                + names.ViewType.TrimEnd('?')
                + " keyedProxy ? (object?)keyedProxy.__SparseTarget : "
                + view
                + ")"
            : "(object?)(" + view + ")";

    /// <summary>Emits assigned-duplicate rejection ahead of keyed mutations.</summary>
    /// <remarks>
    /// Returns empty for unkeyed sequences. Unassigned sentinels are exempt; the
    /// check scans the live list and returns false without mutating or notifying.
    /// </remarks>
    private static string DuplicateKeyGuard(
        SparseMemberModel member,
        SparseObservableEmitter.CollectionProxyNames names,
        string property,
        string modelVariable,
        string? excludeIndex
    )
    {
        var keyTypeName = member.Collection.KeyTypeName;
        if (!member.Collection.IsKeyedSequence || keyTypeName is null)
        {
            return string.Empty;
        }

        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyTypeName + ">.Default";
        // Copy through a fresh local so the null test cannot disturb the
        // converted item's narrowing at the later mutation call.
        var guard =
            "var __duplicateItem = "
            + modelVariable
            + "; if ((object?)__duplicateItem is not null) { var __duplicateKey = "
            + KeyOfObjectExpression(member, "__duplicateItem")
            + "; if (!"
            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__duplicateKey")
            + ") { var __duplicateList = this."
            + property
            + "!; for (var __duplicateIndex = 0; __duplicateIndex < __duplicateList.Count; __duplicateIndex++) { "
            + (
                excludeIndex is null
                    ? string.Empty
                    : "if (__duplicateIndex == " + excludeIndex + ") continue; "
            )
            + "var __duplicateModel = "
            + UnwrapModelExpression(names, "__duplicateList[__duplicateIndex]")
            + "; if (__duplicateModel is null) continue; if ("
            + comparer
            + ".Equals("
            + KeyOfObjectExpression(member, "__duplicateModel")
            + ", __duplicateKey)) return false; } }";
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary identities duplicate-check like assigned keys; elements
            // without a usable identity stay exempt.
            guard +=
                " else { var __duplicateTemp = "
                + TempOfObjectExpression(member, "__duplicateItem")
                + "; if (__duplicateTemp.HasValue && __duplicateTemp.Value != global::System.Guid.Empty) { var __duplicateTempList = this."
                + property
                + "!; for (var __duplicateTempIndex = 0; __duplicateTempIndex < __duplicateTempList.Count; __duplicateTempIndex++) { "
                + (
                    excludeIndex is null
                        ? string.Empty
                        : "if (__duplicateTempIndex == " + excludeIndex + ") continue; "
                )
                + "var __duplicateTempModel = "
                + UnwrapModelExpression(names, "__duplicateTempList[__duplicateTempIndex]")
                + "; if (__duplicateTempModel is null) continue; if ("
                + TempOfObjectExpression(member, "__duplicateTempModel")
                + " == __duplicateTemp.Value) return false; } } } } ";
        }
        else
        {
            guard += " } ";
        }
        return guard;
    }

    /// <summary>Builds a temporary-identity expression reading an object-typed model.</summary>
    private static string TempOfObjectExpression(SparseMemberModel member, string modeller)
    {
        var modelType = member.Collection.ElementType.NonNullableName;
        var cast = "((" + modelType + ")" + modeller + ")";
        return cast + "." + SparseKeyedCollectionEmitter.TemporaryKeyProperty(member);
    }
}
