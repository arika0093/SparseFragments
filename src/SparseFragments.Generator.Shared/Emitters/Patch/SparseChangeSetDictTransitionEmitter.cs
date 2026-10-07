using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Typed dictionary transition surface.</summary>
internal static class SparseChangeSetDictTransitionEmitter
{
    internal static void AppendDictTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var dictType = member.Property.Type.Name;
        var optDict = runtime + "Optional<" + dictType + ">";
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var readOnlyDict = "global::System.Collections.Generic.IReadOnlyDictionary<";
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
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
        );
        code.AppendLineAt(2, "{");
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
        code.AppendLineAt(4, "IsEmpty = isEmpty;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public bool IsEmpty { get; }");
        code.AppendLineAt(3, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(3, "public " + optDict + " Before { get; }");
        code.AppendLineAt(3, "public " + optDict + " After { get; }");
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + valueType + "> Added { get; }"
        );
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + valueType + "> Removed { get; }"
        );
        code.AppendLineAt(
            3,
            "public " + readOnlyDict + keyType + ", " + editedType + "> Edited { get; }"
        );
        code.AppendLineAt(
            3,
            "public global::System.Collections.Generic.IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();"
        );
        code.AppendLineAt(3, "/// <summary>A single typed dictionary entry change.</summary>");
        code.AppendLineAt(3, "public sealed class Item");
        code.AppendLineAt(3, "{");
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
        code.AppendLineAt(4, "public " + keyType + " Key { get; }");
        code.AppendLineAt(4, "public " + optValue + " Before { get; }");
        code.AppendLineAt(4, "public " + optValue + " After { get; }");
        code.AppendLineAt(4, "public bool IsAdded { get; }");
        code.AppendLineAt(4, "public bool IsRemoved { get; }");
        code.AppendLineAt(4, "public bool IsEdited { get; }");
        if (hasPatch)
            code.AppendLineAt(4, "public " + valueCs + " Edit { get; }");
        code.AppendLineAt(
            4,
            "/// <summary>Whether this entry carries no semantic change for the requested key.</summary>"
        );
        code.AppendLineAt(4, "public bool IsEmpty { get; }");
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
                    + ".Between(default, default), true);"
            );
        else
            code.AppendLineAt(
                4,
                "public static Item Empty { get; } = new Item(default!, default, default, false, false, false, true);"
            );
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
        code.AppendLineAt(5, "foreach (var __item in _items) __lookup[__item.Key] = __item;");
        code.AppendLineAt(5, "_lookup = __lookup;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(key, out var __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private "
                + trans
                + " __SparseBuild_"
                + member.Id
                + "("
                + optDict
                + " before, "
                + optDict
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
                + "(before, after, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__comparer), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), __eq);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!__beforeHas || !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var __added = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            4,
            "if (__afterHas) foreach (var __kv in after.Value!) __added[__kv.Key] = __kv.Value;"
        );
        code.AppendLineAt(
            4,
            "var __removed = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            4,
            "if (__beforeHas) foreach (var __kv in before.Value!) __removed[__kv.Key] = __kv.Value;"
        );
        code.AppendLineAt(
            4,
            "var __items = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                4,
                "foreach (var __kv in __added) { "
                    + optValue
                    + " __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(default, __ea); __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                4,
                "foreach (var __kv in __removed) { "
                    + optValue
                    + " __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(__eb, default); __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "foreach (var __kv in __added) __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                4,
                "foreach (var __kv in __removed) __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), default, false, true, false, false));"
            );
        }
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, __added, __removed, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__comparer), __items, false);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __beforeDict = before.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __bd && global::System.Object.Equals(__bd.Comparer, __comparer) ? __bd : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)before.Value!, __comparer);"
        );
        code.AppendLineAt(
            3,
            "var __afterDict = after.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __ad && global::System.Object.Equals(__ad.Comparer, __comparer) ? __ad : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)after.Value!, __comparer);"
        );
        code.AppendLineAt(
            3,
            "var __added2 = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "var __removed2 = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "foreach (var __k in __beforeDict.Keys) if (!__afterDict.ContainsKey(__k)) __removed2[__k] = __beforeDict[__k];"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                3,
                "var __edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueCs
                    + ">(__comparer);"
            );
            code.AppendLineAt(3, "foreach (var __kv in __afterDict)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__beforeDict.TryGetValue(__kv.Key, out var __b)) { __added2[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                4,
                "var __nested = "
                    + valueCs
                    + ".Between("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__b!)), "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value)));"
            );
            code.AppendLineAt(4, "if (!__nested.IsEmpty) __edited[__kv.Key] = __nested;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "bool __empty = __added2.Count == 0 && __removed2.Count == 0 && __edited.Count == 0;"
            );
            code.AppendLineAt(
                3,
                "var __items2 = new global::System.Collections.Generic.List<" + trans + ".Item>();"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __added2) { var __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(default, __ea); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __removed2) { var __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(__eb, default); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __edited) { var __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__beforeDict[__kv.Key]); var __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__afterDict[__kv.Key]); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, __a, false, false, true, __kv.Value, false)); }"
            );
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(before, after, __added2, __removed2, __edited, __items2, __empty);"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "var __edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">(__comparer);"
            );
            code.AppendLineAt(3, "foreach (var __kv in __afterDict)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__beforeDict.TryGetValue(__kv.Key, out var __b)) { __added2[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                4,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__b, (object?)__kv.Value)) __edited[__kv.Key] = __kv.Value;"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "bool __empty = __added2.Count == 0 && __removed2.Count == 0 && __edited.Count == 0;"
            );
            code.AppendLineAt(
                3,
                "var __items2 = new global::System.Collections.Generic.List<" + trans + ".Item>();"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __added2) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __removed2) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), default, false, true, false, false));"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __edited) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__beforeDict[__kv.Key]), "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), false, false, true, false));"
            );
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(before, after, __added2, __removed2, __edited, __items2, __empty);"
            );
        }
        code.AppendLineAt(2, "}");
        // Sparse projection from canonical storage.
        code.AppendLineAt(2, "private " + trans + " __SparseProject_" + member.Id + "()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (__sparse_hasWhole) return __SparseBuild_"
                + member.Id
                + "(__SparseBefore_"
                + member.Id
                + "(), __SparseAfter_"
                + member.Id
                + "());"
        );
        code.AppendLineAt(3, "if (!" + HasField(member) + ")");
        code.AppendLineAt(3, "{");
        EmitDictEmptyReturn(code, member, trans, comparer);
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
        // Granular: derive Added/Removed/Edited maps from stored items (no full snapshots).
        EmitDictGranularReturn(
            code,
            member,
            trans,
            comparer,
            keyType,
            valueType,
            editedType,
            hasPatch
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed dictionary transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(
            2,
            "public " + trans + " " + prop + " => __SparseProject_" + member.Id + "();"
        );
    }
}
