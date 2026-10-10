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
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Between-time sparse diff for keyed sequences.</summary>
internal static class SparseChangeSetKeyedBetweenEmitter
{
    /// <summary>
    /// Emits Between-time sparse diff for a keyed sequence member.
    /// </summary>
    /// <remarks>
    /// Declares locals __h/&#95;_whole/&#95;_wb/&#95;_wa/&#95;_items/&#95;_bO/&#95;_aO.
    /// Whole presence/null transitions retain whole endpoints; granular
    /// Value-&gt;Value transitions retain only per-changed-key items (added-&gt;after,
    /// removed-&gt;before, edited nested ChangeSet + endpoints, reorder-only value)
    /// plus full key orders only when the order actually changed. Unchanged
    /// element values are never retained.
    /// </remarks>
    internal static void AppendKeyedBetweenSparse(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementCs = ElementChangeSetOf(member);
        var elementFrag = ElementFragmentOf(member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        ComputePublicNames(
            System.Collections.Immutable.ImmutableArray.Create(member),
            out _,
            out var transNames
        );
        var trans = transNames[member.Id];
        // Locals.
        code.AppendLineAt(3, "bool __h" + id + " = false;");
        code.AppendLineAt(3, "bool __whole" + id + " = false;");
        code.AppendLineAt(3, opt + " __wb" + id + " = default;");
        code.AppendLineAt(3, opt + " __wa" + id + " = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + trans + ".Item>? __items" + id + " = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __bO" + id + " = null;"
        );
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + keyType + ">? __aO" + id + " = null;"
        );
        var hasTemp = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempLocals(code, member, id);
        }
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __before" + id + " = bf." + escName + ";");
        code.AppendLineAt(4, "var __after" + id + " = af." + escName + ";");
        code.AppendLineAt(
            4,
            "bool __beforeHas"
                + id
                + " = __before"
                + id
                + ".IsPresent && (object?)__before"
                + id
                + ".Value is not null;"
        );
        code.AppendLineAt(
            4,
            "bool __afterHas"
                + id
                + " = __after"
                + id
                + ".IsPresent && (object?)__after"
                + id
                + ".Value is not null;"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempWholeValidation(code, member, id);
            }
            else
                code.AppendLineAt(
                    4,
                    "if (__beforeHas"
                        + id
                        + ") foreach (var __baselineItem in __before"
                        + id
                        + ".Value!) if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                            member,
                            "__SparseKeyOf_ChangeSet_" + id + "(__baselineItem)"
                        )
                        + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection transition.\");"
                );
        }
        // Whole when presence differs or null involved (exact Missing/null/value).
        code.AppendLineAt(
            4,
            "if (__before"
                + id
                + ".IsPresent != __after"
                + id
                + ".IsPresent || !__beforeHas"
                + id
                + " || !__afterHas"
                + id
                + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseEqual_" + id + "(__before" + id + ", __after" + id + "))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true; __whole" + id + " = true;");
        var keyedBefore = "__before" + id;
        var keyedAfter = "__after" + id;
        if (NeedsSnapshot(member))
        {
            keyedBefore = "__SparseSnapshot_" + id + "(" + keyedBefore + ")";
            keyedAfter = "__SparseSnapshot_" + id + "(" + keyedAfter + ")";
        }
        code.AppendLineAt(
            6,
            "__wb" + id + " = " + keyedBefore + "; __wa" + id + " = " + keyedAfter + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // Granular Value -> Value: build key maps and orders (single enumeration each).
        code.AppendLineAt(
            5,
            "var __beforeMap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __beforeOrder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        code.AppendLineAt(5, "foreach (var __item in __before" + id + ".Value!)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var __k = __SparseKeyOf_ChangeSet_" + id + "(__item);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempBeforeBranch(code, member, id);
            else
                code.AppendLineAt(
                    6,
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                        + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection transition.\");"
                );
        }
        code.AppendLineAt(
            6,
            SparseKeyedCollectionEmitter.AddUniqueEntry("__beforeMap" + id, "__k", "__item")
        );
        code.AppendLineAt(6, "__beforeOrder" + id + ".Add(__k);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "var __afterMap"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __afterOrder"
                + id
                + " = new global::System.Collections.Generic.List<"
                + keyType
                + ">();"
        );
        if (!hasTemp)
            code.AppendLineAt(
                5,
                "var __unassignedAfter"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + elementType
                    + ">();"
            );
        code.AppendLineAt(5, "foreach (var __item in __after" + id + ".Value!)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "var __k = __SparseKeyOf_ChangeSet_" + id + "(__item);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempAfterBranch(code, member, id);
            else
                code.AppendLineAt(
                    6,
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                        + ") { __afterOrder"
                        + id
                        + ".Add(__k!); __unassignedAfter"
                        + id
                        + ".Add(__item); continue; }"
                );
        }
        code.AppendLineAt(
            6,
            SparseKeyedCollectionEmitter.AddUniqueEntry("__afterMap" + id, "__k", "__item")
        );
        code.AppendLineAt(6, "__afterOrder" + id + ".Add(__k!);");
        code.AppendLineAt(5, "}");
        // Edited (nested sparse ChangeSets for surviving keys).
        code.AppendLineAt(
            5,
            "var __edited"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementCs
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "foreach (var __k in __beforeMap" + id + ".Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!__afterMap" + id + ".TryGetValue(__k, out var __afterItem)) continue;"
        );
        code.AppendLineAt(6, "var __beforeItem = __beforeMap" + id + "[__k];");
        code.AppendLineAt(
            6,
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
        code.AppendLineAt(6, "if (!__nested.IsEmpty) __edited" + id + "[__k] = __nested;");
        code.AppendLineAt(5, "}");
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempEditedLoop(code, member, runtime, id);
        }
        code.AppendLineAt(
            5,
            "bool __orderChanged"
                + id
                + " = !"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__beforeOrder"
                + id
                + ", __afterOrder"
                + id
                + ")"
                + (
                    hasTemp
                        ? " || !"
                            + facade
                            + ".KeyOrderEquals<global::System.Guid>(__bTO"
                            + id
                            + ", __aTO"
                            + id
                            + ")"
                        : ""
                )
                + ";"
        );
        // Empty when no added/removed/edited and order unchanged.
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempAddedRemovedProbes(code, member, id);
        }
        else
        {
            code.AppendLineAt(
                5,
                "bool __hasAdded"
                    + id
                    + " = __unassignedAfter"
                    + id
                    + ".Count > 0; if (!__hasAdded"
                    + id
                    + ") foreach (var __k in __afterOrder"
                    + id
                    + ") if (!__beforeMap"
                    + id
                    + ".ContainsKey(__k)) { __hasAdded"
                    + id
                    + " = true; break; }"
            );
            code.AppendLineAt(
                5,
                "bool __hasRemoved"
                    + id
                    + " = false; foreach (var __k in __beforeOrder"
                    + id
                    + ") if (!__afterMap"
                    + id
                    + ".ContainsKey(__k)) { __hasRemoved"
                    + id
                    + " = true; break; }"
            );
        }
        code.AppendLineAt(
            5,
            "if (!__hasAdded"
                + id
                + " && !__hasRemoved"
                + id
                + " && __edited"
                + id
                + ".Count == 0"
                + (hasTemp ? " && __editedT" + id + ".Count == 0" : "")
                + " && !__orderChanged"
                + id
                + ") { }"
        );
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true;");
        // Surviving-rank reorder set (relative order, not absolute shifts).
        code.AppendLineAt(
            6,
            "var __beforeRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "{ var __r = 0; foreach (var __k in __beforeOrder"
                + id
                + ") if (__afterMap"
                + id
                + ".ContainsKey(__k)) __beforeRank"
                + id
                + "[__k] = __r++; }"
        );
        code.AppendLineAt(
            6,
            "var __afterRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "{ var __r = 0; foreach (var __k in __afterOrder"
                + id
                + ") if ("
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "!"
                            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                            + " && "
                        : ""
                )
                + "__beforeMap"
                + id
                + ".ContainsKey(__k!)) __afterRank"
                + id
                + "[__k!] = __r++; }"
        );
        code.AppendLineAt(
            6,
            "var __reordered"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __beforeRank"
                + id
                + ") if (__afterRank"
                + id
                + ".TryGetValue(__kv.Key, out var __ar) && __ar != __kv.Value) __reordered"
                + id
                + ".Add(__kv.Key);"
        );
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempRanks(code, id);
        }
        code.AppendLineAt(
            6,
            "var __beforeIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "for (var __i = 0; __i < __beforeOrder"
                + id
                + ".Count; __i++) __beforeIndex"
                + id
                + "[__beforeOrder"
                + id
                + "[__i]] = __i;"
        );
        code.AppendLineAt(
            6,
            "var __afterIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", int>("
                + comparer
                + ");"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                6,
                "for (var __i = 0; __i < __afterOrder"
                    + id
                    + ".Count; __i++) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        "__afterOrder" + id + "[__i]"
                    )
                    + ") __afterIndex"
                    + id
                    + "[__afterOrder"
                    + id
                    + "[__i]] = __i;"
            );
        }
        else
            code.AppendLineAt(
                6,
                "for (var __i = 0; __i < __afterOrder"
                    + id
                    + ".Count; __i++) __afterIndex"
                    + id
                    + "[__afterOrder"
                    + id
                    + "[__i]] = __i;"
            );
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempIndexes(code, member, id);
        }
        code.AppendLineAt(
            6,
            "var __list"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>();"
        );
        // After-order changed keys (added / edited / reorder-only).
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
                code.AppendLineAt(6, "var __tempOrdinal" + id + " = 0;");
            else
                code.AppendLineAt(6, "var __afterOrdinal = 0; var __unassignedOrdinal = 0;");
        }
        code.AppendLineAt(6, "foreach (var __k in __afterOrder" + id + ")");
        code.AppendLineAt(6, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            if (hasTemp)
            {
                SparseChangeSetKeyedTempBetweenEmitter.AppendTempItemBranch(
                    code,
                    member,
                    trans,
                    runtime,
                    id
                );
            }
            else
                code.AppendLineAt(
                    7,
                    "if ("
                        + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                        + ") { var __ua = __unassignedAfter"
                        + id
                        + "[__unassignedOrdinal++]; var __uaEdit = "
                        + elementCs
                        + ".Between(default, "
                        + runtime
                        + "Optional<"
                        + elementFrag
                        + "?>.Present("
                        + elementFrag
                        + ".From(__ua))); __list"
                        + id
                        + ".Add(new "
                        + trans
                        + ".Item(__k!, null, default, "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__ua), -1, __afterOrdinal++, true, false, false, false, __uaEdit, false)); continue; }"
                );
        }
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member) && !hasTemp)
            code.AppendLineAt(7, "__afterOrdinal++;");
        code.AppendLineAt(
            7,
            "bool __inBefore = __beforeMap" + id + ".TryGetValue(__k, out var __b);"
        );
        code.AppendLineAt(7, "var __a = __afterMap" + id + "[__k];");
        code.AppendLineAt(
            7,
            "bool __isEdited = __edited" + id + ".TryGetValue(__k, out var __edit);"
        );
        code.AppendLineAt(7, "bool __isReordered = __reordered" + id + ".Contains(__k);");
        code.AppendLineAt(7, "if (!__inBefore || __isEdited || __isReordered)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
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
            8,
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
            8,
            "var __fullEdit = __isEdited ? __edit! : " + elementCs + ".Between(__eb, __ea);"
        );
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementType
                + "> __ib = __inBefore && !__isEdited && !__isReordered ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b!) : default;"
        );
        code.AppendLineAt(
            8,
            "int __bi = __beforeIndex" + id + ".TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(
            8,
            "int __ai = __afterIndex" + id + ".TryGetValue(__k, out var __av) ? __av : -1;"
        );
        code.AppendLineAt(
            8,
            runtime
                + "Optional<"
                + elementType
                + "> __ia = !__isEdited && !__isReordered ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__a) : default; __list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k!, null, __ib, __ia, __bi, __ai, !__inBefore, false, __isEdited, __isReordered, __fullEdit, false));"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        // Removed keys in before-order.
        code.AppendLineAt(6, "foreach (var __k in __beforeOrder" + id + ")");
        code.AppendLineAt(6, "{");
        if (hasTemp)
        {
            // Sentinel slots belong to the temporary order; assigned removal
            // below would misread them. Temporary removals follow separately.
            code.AppendLineAt(
                7,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") continue;"
            );
        }
        code.AppendLineAt(7, "if (__afterMap" + id + ".ContainsKey(__k)) continue;");
        code.AppendLineAt(7, "var __b = __beforeMap" + id + "[__k];");
        code.AppendLineAt(
            7,
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
        code.AppendLineAt(7, "var __fullEdit = " + elementCs + ".Between(__eb, default);");
        code.AppendLineAt(
            7,
            "int __bi = __beforeIndex" + id + ".TryGetValue(__k, out var __bv) ? __bv : -1;"
        );
        code.AppendLineAt(
            7,
            "__list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k, null, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b), default, __bi, -1, false, true, false, false, __fullEdit, false));"
        );
        code.AppendLineAt(6, "}");
        if (hasTemp)
        {
            SparseChangeSetKeyedTempBetweenEmitter.AppendTempRemovedLoop(
                code,
                member,
                trans,
                runtime,
                id
            );
        }
        code.AppendLineAt(6, "__items" + id + " = __list" + id + ";");
        // Always retain full key orders (keys only, never element payloads) so typed
        // BeforeOrder/AfterOrder preserve #94 absolute semantics; OrderChanged (via
        // KeyOrderEquals downstream) distinguishes reorder from membership shifts.
        code.AppendLineAt(
            6,
            "__bO" + id + " = __beforeOrder" + id + "; __aO" + id + " = __afterOrder" + id + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }
}
