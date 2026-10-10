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
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var shell = code;
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var listType = member.Property.Type.Name;
        var optList = runtime + "Optional<" + listType + ">";
        var optElement = runtime + "Optional<" + elementType + ">";
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var optionalElementFragment = runtime + "Optional<" + elementFrag + "?>";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
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
        code.AppendLineAt(3, "public sealed class Item");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "/// <summary>Gets the entry key.</summary>");
        code.AppendLineAt(4, "public " + keyType + " Key { get; }");
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
            "public static Item Empty { get; } = new Item(default!, default, default, -1, -1, false, false, false, false, "
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
                + " key, "
                + optElement
                + " before, "
                + optElement
                + " after, int beforeIndex, int afterIndex, bool isAdded, bool isRemoved, bool isEdited, bool isReordered, "
                + elementCs
                + " edit, bool isEmpty)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "Key = key;");
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
        code.AppendLineAt(4, "var __lookup = _lookup;");
        code.AppendLineAt(4, "if (__lookup is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__lookup = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", Item>("
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
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "key")
                    + ") return Item.Empty;"
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
        }
        // Build helper (semantic before/after projection over sparse storage; never reads patch ops).
        code.AppendLineAt(
            2,
            (target is null ? "private " : "internal static ")
                + trans
                + " __SparseBuild_"
                + member.Id
                + "("
                + optList
                + " before, "
                + optList
                + " after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var __comparer = " + comparer + ";");
        code.AppendLineAt(
            3,
            "bool __beforeHas = before.IsPresent && (object?)before.Value is not null;"
        );
        code.AppendLineAt(
            3,
            "bool __afterHas = after.IsPresent && (object?)after.Value is not null;"
        );
        code.AppendLineAt(3, "if (!__beforeHas && !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "bool __eq = Fragment.__SparseEqual_" + member.Id + "(before, after);"
        );
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(__comparer), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), false, new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), __eq);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __beforeOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            3,
            "var __beforeMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__comparer);"
        );
        code.AppendLineAt(3, "if (__beforeHas) foreach (var __item in before.Value!)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __k = __SparseKeyOf_ChangeSet_" + member.Id + "(__item);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed transition.\");"
            );
        code.AppendLineAt(
            4,
            SparseKeyedCollectionEmitter.AddUniqueEntry("__beforeMap", "__k", "__item")
        );
        code.AppendLineAt(4, "__beforeOrder.Add(__k);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __afterOrder = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            3,
            "var __afterMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "var __unassignedAfter = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        code.AppendLineAt(3, "if (__afterHas) foreach (var __item in after.Value!)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __k = __SparseKeyOf_ChangeSet_" + member.Id + "(__item);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") { __afterOrder.Add(__k!); __unassignedAfter.Add(__item); continue; }"
            );
        code.AppendLineAt(
            4,
            SparseKeyedCollectionEmitter.AddUniqueEntry("__afterMap", "__k", "__item")
        );
        code.AppendLineAt(4, "__afterOrder.Add(__k!);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __added = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                3,
                "var __unassignedAddedIndex = 0; foreach (var __k in __afterOrder) { if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") { __added.Add(__unassignedAfter[__unassignedAddedIndex++]); continue; } if (!__beforeMap.ContainsKey(__k)) __added.Add(__afterMap[__k]); }"
            );
        }
        else
            code.AppendLineAt(
                3,
                "foreach (var __k in __afterOrder) if (!__beforeMap.ContainsKey(__k)) __added.Add(__afterMap[__k]);"
            );
        code.AppendLineAt(
            3,
            "var __removed = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(
            3,
            "foreach (var __k in __beforeOrder) if (!__afterMap.ContainsKey(__k)) __removed.Add(__beforeMap[__k]);"
        );
        code.AppendLineAt(
            3,
            "var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">(__comparer);"
        );
        code.AppendLineAt(3, "foreach (var __k in __beforeMap.Keys)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (!__afterMap.TryGetValue(__k, out var __afterItem)) continue;");
        code.AppendLineAt(4, "var __beforeItem = __beforeMap[__k];");
        code.AppendLineAt(
            4,
            "var __nested = "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__beforeItem)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__afterItem)));"
        );
        code.AppendLineAt(4, "if (!__nested.IsEmpty) __edited[__k] = __nested;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "bool __orderChanged = !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__beforeOrder, __afterOrder);"
        );
        code.AppendLineAt(
            3,
            "bool __empty = __added.Count == 0 && __removed.Count == 0 && __edited.Count == 0 && !__orderChanged;"
        );
        // Surviving-rank reorder set (relative order, not absolute index shifts).
        code.AppendLineAt(
            3,
            "var __beforeRank = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "{ var __r = 0; foreach (var __k in __beforeOrder) if (__afterMap.ContainsKey(__k)) __beforeRank[__k] = __r++; }"
        );
        code.AppendLineAt(
            3,
            "var __afterRank = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "{ var __r = 0; foreach (var __k in __afterOrder) if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "!"
                            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                            + " && "
                        : ""
                )
                + "__beforeMap.ContainsKey(__k!)) __afterRank[__k!] = __r++; }"
        );
        code.AppendLineAt(
            3,
            "var __reordered = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "foreach (var __kv in __beforeRank) if (__afterRank.TryGetValue(__kv.Key, out var __ar) && __ar != __kv.Value) __reordered.Add(__kv.Key);"
        );
        code.AppendLineAt(
            3,
            "var __beforeIndex = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        code.AppendLineAt(
            3,
            "for (var __i = 0; __i < __beforeOrder.Count; __i++) __beforeIndex[__beforeOrder[__i]] = __i;"
        );
        code.AppendLineAt(
            3,
            "var __afterIndex = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>(__comparer);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                3,
                "for (var __i = 0; __i < __afterOrder.Count; __i++) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        "__afterOrder[__i]"
                    )
                    + ") __afterIndex[__afterOrder[__i]] = __i;"
            );
        else
            code.AppendLineAt(
                3,
                "for (var __i = 0; __i < __afterOrder.Count; __i++) __afterIndex[__afterOrder[__i]] = __i;"
            );
        code.AppendLineAt(
            3,
            "var __items = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(3, "var __afterPosition = 0; var __unassignedPosition = 0;");
        code.AppendLineAt(3, "foreach (var __k in __afterOrder)");
        code.AppendLineAt(3, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") { var __ua = __unassignedAfter[__unassignedPosition++]; var __uaEdit = "
                    + elementCs
                    + ".Between(default, "
                    + optionalElementFragment
                    + ".Present("
                    + elementFrag
                    + ".From(__ua))); __items.Add(new "
                    + trans
                    + ".Item(__k!, default, "
                    + runtime
                    + "Optional<"
                    + elementType
                    + ">.Present(__ua), -1, __afterPosition++, true, false, false, false, __uaEdit, false)); continue; }"
            );
        }
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(4, "__afterPosition++;");
        code.AppendLineAt(4, "bool __inBefore = __beforeMap.TryGetValue(__k, out var __b);");
        code.AppendLineAt(4, "var __a = __afterMap[__k];");
        code.AppendLineAt(4, "bool __isEdited = __edited.TryGetValue(__k, out var __edit);");
        code.AppendLineAt(4, "bool __isReordered = __reordered.Contains(__k);");
        code.AppendLineAt(4, "if (!__inBefore || __isEdited || __isReordered)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = __inBefore ? "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b!)) : default;"
        );
        code.AppendLineAt(
            5,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __ea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__a));"
        );
        code.AppendLineAt(
            5,
            "var __fullEdit = __isEdited ? __edit! : " + elementCs + ".Between(__eb, __ea);"
        );
        code.AppendLineAt(
            5,
            optElement
                + " __ib = __inBefore ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b!) : default;"
        );
        code.AppendLineAt(
            5,
            "int __bi = __beforeIndex.TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(5, "int __ai = __afterIndex.TryGetValue(__k, out var __av) ? __av : -1;");
        code.AppendLineAt(
            5,
            "__items.Add(new "
                + trans
                + ".Item(__k!, __ib, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__a), __bi, __ai, !__inBefore, false, __isEdited, __isReordered, __fullEdit, false));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "foreach (var __k in __beforeOrder)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__afterMap.ContainsKey(__k)) continue;");
        code.AppendLineAt(4, "var __b = __beforeMap[__k];");
        code.AppendLineAt(
            4,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __eb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__b));"
        );
        code.AppendLineAt(4, "var __fullEdit = " + elementCs + ".Between(__eb, default);");
        code.AppendLineAt(
            4,
            "int __bi = __beforeIndex.TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(
            4,
            "__items.Add(new "
                + trans
                + ".Item(__k!, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b), default, __bi, -1, false, true, false, false, __fullEdit, false));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(before, after, __added, __removed, __edited, __beforeOrder, __afterOrder, __orderChanged, __items, __empty);"
        );
        code.AppendLineAt(2, "}");
        // Sparse projection: derive typed transition from canonical
        // sparse storage without full member snapshots.
        if (target is null)
        {
            code.AppendLineAt(2, "private " + trans + " __SparseProject_" + member.Id + "()");
        }
        else
        {
            code.AppendLineAt(
                2,
                "/// <summary>Projects the typed keyed transition from canonical sparse storage.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal static " + trans + " __SparseProject_" + member.Id + "(ChangeSet self)"
            );
        }
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            AppendMemberStateAliases(code, member);
        }
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + (target is null ? "(), __SparseAfter_" : "(self), __SparseAfter_")
                + member.Id
                + (target is null ? "());" : "(self));")
        );
        code.AppendLineAt(3, "if (!" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(default, default, new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.List<"
                + elementType
                + ">(), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + "), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), new global::System.Collections.Generic.List<"
                + keyType
                + ">(), false, new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), true);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if ("
                + KeyedWholeFlag(member)
                + ") return __SparseBuild_"
                + member.Id
                + "("
                + KeyedWholeBefore(member)
                + ", "
                + KeyedWholeAfter(member)
                + ");"
        );
        code.AppendLineAt(
            3,
            "var __stored = "
                + KeyedItems(member)
                + " ?? new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        code.AppendLineAt(
            3,
            "var __added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(); var __removed = new global::System.Collections.Generic.List<"
                + elementType
                + ">(); var __edited = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            3,
            "foreach (var __it in __stored) { if (__it.IsAdded) __added.Add(__it.After.Value!); else if (__it.IsRemoved) __removed.Add(__it.Before.Value!); else if (__it.IsEdited) __edited[__it.Key] = __it.Edit; }"
        );
        code.AppendLineAt(
            3,
            "var __bO = "
                + KeyedBeforeOrder(member)
                + " ?? new global::System.Collections.Generic.List<"
                + keyType
                + ">(); var __aO = "
                + KeyedAfterOrder(member)
                + " ?? new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(
            3,
            "bool __oc = "
                + KeyedBeforeOrder(member)
                + " is not null && "
                + KeyedAfterOrder(member)
                + " is not null && !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__bO, __aO);"
        );
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(default, default, __added, __removed, __edited, __bO, __aO, __oc, __stored, false);"
        );
        code.AppendLineAt(2, "}");
        var property = target is null ? code : shell;
        property.AppendLineAt(
            2,
            "/// <summary>Gets the typed keyed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        property.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        if (target is not null)
        {
            shell.AppendLineAt(
                2,
                "public "
                    + trans
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseProject_"
                    + member.Id
                    + "(this);"
            );
        }
        else
        {
            code.AppendLineAt(
                2,
                "public " + trans + " " + prop + " => __SparseProject_" + member.Id + "();"
            );
        }
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
