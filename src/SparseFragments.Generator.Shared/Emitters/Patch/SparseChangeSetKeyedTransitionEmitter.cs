using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Typed keyed-sequence transition surface.</summary>
internal static class SparseChangeSetKeyedTransitionEmitter
{
    internal static void AppendKeyedTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string cacheName,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var shell = code;
        var runtime = dialect.RuntimeNamespace;
        var keyType = KeyTypeOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var elementType = ElementTypeOf(member);
        var listType = member.Property.Type.Name;
        var optList = runtime + "Optional<" + listType + ">";
        var optElement = runtime + "Optional<" + elementType + ">";
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var optionalElementFragment = runtime + "Optional<" + elementFrag + "?>";
        var readOnlyList = "global::System.Collections.Generic.IReadOnlyList<";
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        // Transition type.
        code.AppendLineAt(
            2,
            "/// <summary>Typed keyed transition for member '"
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
                + SparseChangeSetContractEmitter.KeyedBases(dialect, member)
        );
        code.AppendLineAt(2, "{");
        // Public surface first, then the internal constructor, then private storage.
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
        code.AppendLineAt(3, "public " + optList + " Before { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the value after the transition.</summary>");
        code.AppendLineAt(3, "public " + optList + " After { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the elements added by the transition.</summary>");
        code.AppendLineAt(3, "public " + readOnlyList + elementType + "> Added { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the elements removed by the transition.</summary>");
        code.AppendLineAt(3, "public " + readOnlyList + elementType + "> Removed { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the entries edited by the transition.</summary>");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + elementCs + "> Edited { get; }"
        );
        code.AppendLineAt(3, "/// <summary>Gets the key order before the transition.</summary>");
        code.AppendLineAt(3, "public " + readOnlyList + keyType + "> BeforeOrder { get; }");
        code.AppendLineAt(3, "/// <summary>Gets the key order after the transition.</summary>");
        code.AppendLineAt(3, "public " + readOnlyList + keyType + "> AfterOrder { get; }");
        code.AppendLineAt(3, "/// <summary>Gets whether the key order changed.</summary>");
        code.AppendLineAt(3, "public bool OrderChanged { get; }");
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
        code.AppendLineAt(3, "/// <summary>A single typed keyed item change.</summary>");
        code.AppendLineAt(
            3,
            "/// <remarks>Empty lookup results expose <see cref=\"IsEmpty\"/> and are never enumerated.</remarks>"
        );
        code.AppendLineAt(
            3,
            "public sealed class Item"
                + SparseChangeSetContractEmitter.KeyedItemBases(dialect, member)
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "/// <summary>Gets the entry key.</summary>");
        code.AppendLineAt(4, "public " + keyType + " Key { get; }");
        SparseChangeSetKeyedTempTransitionEmitter.AppendItemTemporaryKeyProperty(code, member);
        code.AppendLineAt(4, "/// <summary>Gets the value before the transition.</summary>");
        code.AppendLineAt(4, "public " + optElement + " Before { get; }");
        code.AppendLineAt(4, "/// <summary>Gets the value after the transition.</summary>");
        code.AppendLineAt(4, "public " + optElement + " After { get; }");
        code.AppendLineAt(4, "/// <summary>Gets the index before the transition.</summary>");
        code.AppendLineAt(4, "public int BeforeIndex { get; }");
        code.AppendLineAt(4, "/// <summary>Gets the index after the transition.</summary>");
        code.AppendLineAt(4, "public int AfterIndex { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was added.</summary>");
        code.AppendLineAt(4, "public bool IsAdded { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was removed.</summary>");
        code.AppendLineAt(4, "public bool IsRemoved { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was edited.</summary>");
        code.AppendLineAt(4, "public bool IsEdited { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether the entry was reordered.</summary>");
        code.AppendLineAt(4, "public bool IsReordered { get; }");
        code.AppendLineAt(
            4,
            "/// <summary>Gets the nested transition for the edited entry.</summary>"
        );
        code.AppendLineAt(4, "public " + elementCs + " Edit { get; }");
        code.AppendLineAt(
            4,
            "/// <summary>Whether this item carries no semantic change for the requested key.</summary>"
        );
        code.AppendLineAt(4, "public bool IsEmpty { get; }");
        code.AppendLineAt(4, "/// <summary>Gets whether this item contains any changes.</summary>");
        code.AppendLineAt(4, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(
            4,
            "/// <summary>Shared allocation-light empty item; retains no element snapshots.</summary>"
        );
        code.AppendLineAt(
            4,
            "public static Item Empty { get; } = new Item(default!, null, default, default, -1, -1, false, false, false, false, "
                + elementCs
                + ".Between("
                + optionalElementFragment
                + ".Missing, "
                + optionalElementFragment
                + ".Missing), true);"
        );
        code.AppendLineAt(
            4,
            "internal Item("
                + keyType
                + " key, global::System.Guid? temporaryKey, "
                + optElement
                + " before, "
                + optElement
                + " after, int beforeIndex, int afterIndex, bool isAdded, bool isRemoved, bool isEdited, bool isReordered, "
                + elementCs
                + " edit, bool isEmpty)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "Key = key;");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            code.AppendLineAt(5, "TemporaryKey = temporaryKey;");
        code.AppendLineAt(5, "Before = before;");
        code.AppendLineAt(5, "After = after;");
        code.AppendLineAt(5, "BeforeIndex = beforeIndex;");
        code.AppendLineAt(5, "AfterIndex = afterIndex;");
        code.AppendLineAt(5, "IsAdded = isAdded;");
        code.AppendLineAt(5, "IsRemoved = isRemoved;");
        code.AppendLineAt(5, "IsEdited = isEdited;");
        code.AppendLineAt(5, "IsReordered = isReordered;");
        code.AppendLineAt(5, "Edit = edit;");
        code.AppendLineAt(5, "IsEmpty = isEmpty;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the typed change for a stable key; never returns null.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Unchanged or unknown keys return <see cref=\"Item.Empty\"/> (allocation-light singleton shared across lookups). "
                + "The unassigned sentinel is never a stable lookup: it always returns <see cref=\"Item.Empty\"/>. "
                + "Non-empty results are the same instances produced by enumeration. "
                + "BeforeIndex/AfterIndex are absolute collection indexes; IsReordered observes surviving-key relative rank.</remarks>"
        );
        code.AppendLineAt(3, "/// <param name=\"key\">The stable key to look up.</param>");
        code.AppendLineAt(3, "/// <returns>The typed change for the key.</returns>");
        code.AppendLineAt(3, "public Item GetChange(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") return Item.Empty;"
            );
        }
        code.AppendLineAt(4, "var __lookup = _lookup;");
        code.AppendLineAt(4, "if (__lookup is null)");
        code.AppendLineAt(4, "{");
        var lookupCapacity = "_items.Count";
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(5, "int __lookupCount = 0;");
            code.AppendLineAt(
                5,
                "for (int __index = 0; __index < _items.Count; __index++) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        "_items[__index].Key"
                    )
                    + ") __lookupCount++;"
            );
            lookupCapacity = "__lookupCount";
        }
        code.AppendLineAt(
            5,
            "__lookup = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", Item>("
                + lookupCapacity
                + ", "
                + comparer
                + ");"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                5,
                "foreach (var __item in _items) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__item.Key")
                    + ") __lookup[__item.Key] = __item;"
            );
        }
        else
            code.AppendLineAt(5, "foreach (var __item in _items) __lookup[__item.Key] = __item;");
        code.AppendLineAt(5, "_lookup = __lookup;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendGetTemporaryChange(code, member);
        }
        SparseChangeSetContractEmitter.AppendKeyedContractMembers(code, member, dialect);
        code.AppendLineAt(
            3,
            "internal "
                + trans
                + "("
                + optList
                + " before, "
                + optList
                + " after, global::System.Collections.Generic.List<"
                + elementType
                + "> added, global::System.Collections.Generic.List<"
                + elementType
                + "> removed, global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + "> edited, global::System.Collections.Generic.List<"
                + keyType
                + "> beforeOrder, global::System.Collections.Generic.List<"
                + keyType
                + "> afterOrder, bool orderChanged, global::System.Collections.Generic.List<Item> items, bool isEmpty)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Before = before;");
        code.AppendLineAt(4, "After = after;");
        code.AppendLineAt(4, "Added = added.AsReadOnly();");
        code.AppendLineAt(4, "Removed = removed.AsReadOnly();");
        code.AppendLineAt(
            4,
            "Edited = new global::System.Collections.ObjectModel.ReadOnlyDictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(edited);"
        );
        code.AppendLineAt(4, "BeforeOrder = beforeOrder.AsReadOnly();");
        code.AppendLineAt(4, "AfterOrder = afterOrder.AsReadOnly();");
        code.AppendLineAt(4, "OrderChanged = orderChanged;");
        code.AppendLineAt(4, "_items = items.AsReadOnly();");
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
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendTemporaryLookupField(code);
        }
        code.AppendLineAt(2, "}");
        // Key helper: stays on the facade for single-file emission; it moves
        // into the operation container (below) when relocating.
        if (target is null)
        {
            code.AppendLineAt(
                2,
                "private static "
                    + keyType
                    + " __SparseKeyOf_ChangeSet_"
                    + member.Id
                    + "("
                    + elementType
                    + " element)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
            );
            code.AppendLineAt(3, KeyOfBody(member));
            code.AppendLineAt(2, "}");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                SparseChangeSetKeyedTempTransitionEmitter.AppendTemporaryKeyExtractor(
                    code,
                    member,
                    elementType,
                    documented: false
                );
            }
        }
        if (target is not null)
        {
            // Projection algorithms move into the change-set operation
            // container; the typed shell above stays on the facade. The key
            // extractor is only used by the build helper, so it moves along.
            code = target.ChangeSetOperations;
            code.AppendLineAt(
                2,
                "/// <summary>Extracts the stable key for a keyed transition element.</summary>"
            );
            code.AppendLineAt(
                2,
                "private static "
                    + keyType
                    + " __SparseKeyOf_ChangeSet_"
                    + member.Id
                    + "("
                    + elementType
                    + " element)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
            );
            code.AppendLineAt(3, KeyOfBody(member));
            code.AppendLineAt(2, "}");
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                SparseChangeSetKeyedTempTransitionEmitter.AppendTemporaryKeyExtractor(
                    code,
                    member,
                    elementType,
                    documented: true
                );
            }
        }
        SparseChangeSetKeyedBuildEmitter.AppendBuild(code, member, trans, dialect, target);
        SparseChangeSetKeyedProjectionEmitter.AppendProjection(
            shell,
            member,
            prop,
            trans,
            cacheName,
            dialect,
            target
        );
    }

    internal static string KeyOfBody(SparseMemberModel member)
    {
        // Single-property keys only: the key type is the declared property type,
        // which may itself be a tuple or value object for composite identity.
        var keys = member.Collection.KeyPropertyNames;
        if (keys.Length == 1)
            return "return element."
                + SparseNaming.EscapeIdentifier(keys[0])
                + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "!" : "")
                + ";";
        return "throw new global::System.InvalidOperationException(\"Keyed collection has no usable key property.\");";
    }
}
