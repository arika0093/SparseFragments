using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Typed dictionary transition surface.</summary>
internal static class SparseChangeSetDictTransitionEmitter
{
    private const int MaxCombinedLookupCount = 2048;

    internal static void AppendDictTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string cacheName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dictType = member.Property.Type.Name;
        var optDict = runtime + "Optional<" + dictType + ">";
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var optionalValueFragment = hasPatch ? runtime + "Optional<" + valueFrag + "?>" : null;
        var editedType = hasPatch ? valueCs! : valueType;
        code.AppendLineAt(
            2,
            "/// <summary>Typed dictionary transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(
            2,
            "public sealed class "
                + trans
                + " : global::System.Collections.Generic.IEnumerable<"
                + trans
                + ".Item>"
                + SparseChangeSetContractEmitter.DictionaryBases(dialect, member)
        );
        code.AppendLineAt(2, "{");
        // Public surface first, then the internal constructor, then private
        // storage.
        code.AppendLineAt(
            3,
            "/// <summary>Gets whether this transition contains no changes.</summary>"
        );
        code.AppendLineAt(3, "public bool IsEmpty { get; }");
        code.AppendLineAt(
            3,
            "/// <summary>Gets whether this transition contains any changes.</summary>"
        );
        code.AppendLineAt(3, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(3, "/// <summary>Gets the value before the transition.</summary>");
        code.AppendLineAt(3, "public " + optDict + " Before { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the value after the transition.</summary>");
        code.AppendLineAt(3, "public " + optDict + " After { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the entries added by the transition.</summary>");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + valueType + "> Added { get; }"
        );
        code.AppendLineAt(3, "/// <summary>Gets the entries removed by the transition.</summary>");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + valueType + "> Removed { get; }"
        );
        code.AppendLineAt(3, "/// <summary>Gets the entries edited by the transition.</summary>");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + editedType + "> Edited { get; }"
        );
        code.AppendLineAt(3, "/// <summary>Gets the enumerator over changed entries.</summary>");
        code.AppendLineAt(3, "/// <returns>The enumerator over changed entries.</returns>");
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(3, "/// <summary>A single typed dictionary entry change.</summary>");
        code.AppendLineAt(
            3,
            "public sealed class Item"
                + SparseChangeSetContractEmitter.DictionaryItemBases(dialect, member)
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "/// <summary>Gets the entry key.</summary>");
        code.AppendLineAt(4, "public " + keyType + " Key { get; }");
        code.AppendLineAt(4, "/// <summary>Gets the value before the transition.</summary>");
        code.AppendLineAt(4, "public " + optValue + " Before { get; }");
        code.AppendLineAt(4, "/// <summary>Gets the value after the transition.</summary>");
        code.AppendLineAt(4, "public " + optValue + " After { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was added.</summary>");
        code.AppendLineAt(4, "public bool IsAdded { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was removed.</summary>");
        code.AppendLineAt(4, "public bool IsRemoved { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was edited.</summary>");
        code.AppendLineAt(4, "public bool IsEdited { get; }");
        if (hasPatch)
        {
            code.AppendLineAt(
                4,
                "/// <summary>Gets the nested transition for the edited entry.</summary>"
            );
            code.AppendLineAt(4, "public " + valueCs + " Edit { get; }");
        }
        code.AppendLineAt(
            4,
            "/// <summary>Whether this entry carries no semantic change for the requested key.</summary>"
        );
        code.AppendLineAt(4, "public bool IsEmpty { get; }");
        code.AppendLineAt(
            4,
            "/// <summary>Gets whether this entry contains any changes.</summary>"
        );
        code.AppendLineAt(4, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(
            4,
            "/// <summary>Shared allocation-light empty entry; retains no value snapshots.</summary>"
        );
        if (hasPatch)
            code.AppendLineAt(
                4,
                "public static Item Empty { get; } = new Item(default!, default, default, false, false, false, "
                    + valueCs
                    + ".Between("
                    + optionalValueFragment
                    + ".Missing, "
                    + optionalValueFragment
                    + ".Missing), true);"
            );
        else
            code.AppendLineAt(
                4,
                "public static Item Empty { get; } = new Item(default!, default, default, false, false, false, true);"
            );
        if (hasPatch)
            code.AppendLineAt(
                4,
                "internal Item("
                    + keyType
                    + " key, "
                    + optValue
                    + " before, "
                    + optValue
                    + " after, bool isAdded, bool isRemoved, bool isEdited, "
                    + valueCs
                    + " edit, bool isEmpty)"
            );
        else
            code.AppendLineAt(
                4,
                "internal Item("
                    + keyType
                    + " key, "
                    + optValue
                    + " before, "
                    + optValue
                    + " after, bool isAdded, bool isRemoved, bool isEdited, bool isEmpty)"
            );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "Key = key;");
        code.AppendLineAt(5, "Before = before;");
        code.AppendLineAt(5, "After = after;");
        code.AppendLineAt(5, "IsAdded = isAdded;");
        code.AppendLineAt(5, "IsRemoved = isRemoved;");
        code.AppendLineAt(5, "IsEdited = isEdited;");
        if (hasPatch)
            code.AppendLineAt(5, "Edit = edit;");
        code.AppendLineAt(5, "IsEmpty = isEmpty;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the typed change for a dictionary key; never returns null.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton). "
                + "Non-empty results are the same instances produced by enumeration.</remarks>"
        );
        code.AppendLineAt(3, "/// <param name=\"key\">The dictionary key to look up.</param>");
        code.AppendLineAt(3, "/// <returns>The typed change for the key.</returns>");
        code.AppendLineAt(3, "public Item GetChange(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        AppendLookupBody(code);
        code.AppendLineAt(3, "}");
        SparseChangeSetContractEmitter.AppendDictionaryContractMembers(code, member, dialect);
        code.AppendLineAt(
            3,
            "internal "
                + trans
                + "("
                + optDict
                + " before, "
                + optDict
                + " after, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> added, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> removed, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + "> edited, global::System.Collections.Generic.List<Item> items, bool isEmpty)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Before = before;");
        code.AppendLineAt(4, "After = after;");
        code.AppendLineAt(
            4,
            "Added = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + valueType
                + ">(added);"
        );
        code.AppendLineAt(
            4,
            "Removed = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + valueType
                + ">(removed);"
        );
        code.AppendLineAt(
            4,
            "Edited = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + editedType
                + ">(edited);"
        );
        code.AppendLineAt(4, "_items = items.AsReadOnly();");
        code.AppendLineAt(4, "_itemCount = items.Count;");
        code.AppendLineAt(4, "IsEmpty = isEmpty;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "private readonly global::System.Collections.Generic.IReadOnlyList<Item> _items;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<" + keyType + ", Item>? _lookup;"
        );
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", Item>?[]? _lookupPartitions;"
        );
        code.AppendLineAt(3, "private readonly int _itemCount;");
        AppendLookupHelpers(code, keyType, comparer);
        code.AppendLineAt(2, "}");
        SparseChangeSetDictBuildEmitter.AppendBuild(code, member, trans, dialect, target);
        SparseChangeSetDictProjectionEmitter.AppendProjection(
            code,
            member,
            prop,
            trans,
            cacheName,
            dialect,
            target
        );
    }

    private static void AppendLookupBody(SharedIndentedBuilder code)
    {
        code.AppendLineAt(4, "var __lookup = _lookup;");
        code.AppendLineAt(
            4,
            "if (__lookup is not null && __lookup.TryGetValue(key, out var __found)) return __found;"
        );
        code.AppendLineAt(4, "return __SparseGetUncachedChange(key);");
    }

    private static void AppendLookupHelpers(
        SharedIndentedBuilder code,
        string keyType,
        string comparer
    )
    {
        var dictionary = "global::System.Collections.Generic.Dictionary<" + keyType + ", Item>";
        code.AppendLineAt(3, "private Item __SparseGetUncachedChange(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (_itemCount > "
                + MaxCombinedLookupCount
                + ") return __SparseGetPartitionedChange(key);"
        );
        code.AppendLineAt(4, "var __lookup = _lookup ?? __SparseBuildLookup();");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
        // Keep index construction and the large-diff path out of the hot public
        // lookup.
        code.AppendLineAt(3, "private " + dictionary + " __SparseBuildLookup()");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var __lookup = new " + dictionary + "(_itemCount, " + comparer + ");"
        );
        code.AppendLineAt(4, "foreach (var __item in _items) __lookup[__item.Key] = __item;");
        code.AppendLineAt(4, "_lookup = __lookup;");
        code.AppendLineAt(4, "return __lookup;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private Item __SparseGetPartitionedChange(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        // The public lookup already tried the most recently used partition.
        code.AppendLineAt(4, "var __lookup = _lookup;");
        code.AppendLineAt(4, "Item? __found;");
        code.AppendLineAt(4, "var __partitions = _lookupPartitions;");
        code.AppendLineAt(4, "if (__partitions is not null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "foreach (var __candidate in __partitions)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__candidate is not null && !global::System.Object.ReferenceEquals(__candidate, __lookup) && __candidate.TryGetValue(key, out __found))"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "_lookup = __candidate;");
        code.AppendLineAt(7, "return __found;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "int __kind = Added.ContainsKey(key) ? 0 : Removed.ContainsKey(key) ? 1 : Edited.ContainsKey(key) ? 2 : -1;"
        );
        code.AppendLineAt(4, "if (__kind < 0) return Item.Empty;");
        code.AppendLineAt(4, "if (__partitions is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__partitions = new " + dictionary + "?[3];");
        code.AppendLineAt(5, "_lookupPartitions = __partitions;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__lookup = __partitions[__kind];");
        code.AppendLineAt(4, "if (__lookup is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "int __count = __kind == 0 ? Added.Count : __kind == 1 ? Removed.Count : Edited.Count;"
        );
        code.AppendLineAt(5, "__lookup = new " + dictionary + "(__count, " + comparer + ");");
        code.AppendLineAt(5, "foreach (var __item in _items)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "int __itemKind = __item.IsAdded ? 0 : __item.IsRemoved ? 1 : __item.IsEdited ? 2 : -1;"
        );
        code.AppendLineAt(6, "if (__itemKind == __kind) __lookup[__item.Key] = __item;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "__partitions[__kind] = __lookup;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "_lookup = __lookup;");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(key, out __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
    }
}
