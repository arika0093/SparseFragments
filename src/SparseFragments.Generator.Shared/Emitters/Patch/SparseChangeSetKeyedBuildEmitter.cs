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

/// <summary>Projects whole keyed endpoints into typed transitions.</summary>
internal static class SparseChangeSetKeyedBuildEmitter
{
    internal static void AppendBuild(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
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
        var facade = dialect.RuntimeFacade;
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
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendBuildTempDeclarations(
                code,
                elementType
            );
        }
        code.AppendLineAt(3, "if (__beforeHas) foreach (var __item in before.Value!)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __k = __SparseKeyOf_ChangeSet_" + member.Id + "(__item);");
        var hasTempBuild = SparseKeyedCollectionEmitter.HasTemporaryKey(member);
        if (hasTempBuild)
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendBuildTempBeforeBranch(code, member);
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
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
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendBuildTempAfterBranch(code, member);
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
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
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLineAt(
                3,
                "foreach (var __k in __beforeOrder) if (!"
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + " && !__afterMap.ContainsKey(__k)) __removed.Add(__beforeMap[__k]);"
            );
            code.AppendLineAt(
                3,
                "var __afterTempSet = new global::System.Collections.Generic.HashSet<global::System.Guid>(__unassignedTempAfter);"
            );
            code.AppendLineAt(
                3,
                "for (var __tbi = 0; __tbi < __unassignedBefore.Count; __tbi++) if (!__afterTempSet.Contains(__unassignedTempBefore[__tbi])) __removed.Add(__unassignedBefore[__tbi]);"
            );
        }
        else
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
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendBuildTempAddBranch(
                code,
                member,
                trans,
                runtime,
                elementCs,
                elementFrag,
                elementType
            );
        }
        else if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
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
                    + ".Item(__k!, null, default, "
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
                + ".Item(__k!, null, __ib, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__a), __bi, __ai, !__inBefore, false, __isEdited, __isReordered, __fullEdit, false));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "foreach (var __k in __beforeOrder)");
        code.AppendLineAt(3, "{");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLineAt(
                4,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") continue;"
            );
        }
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
                + ".Item(__k!, null, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__b), default, __bi, -1, false, true, false, false, __fullEdit, false));"
        );
        code.AppendLineAt(3, "}");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            SparseChangeSetKeyedTempTransitionEmitter.AppendBuildTempRemovedLoop(
                code,
                member,
                trans,
                runtime,
                elementCs,
                elementFrag,
                elementType
            );
        }
        code.AppendLineAt(
            3,
            "return new "
                + trans
                + "(before, after, __added, __removed, __edited, __beforeOrder, __afterOrder, __orderChanged, __items, __empty);"
        );
        code.AppendLineAt(2, "}");
    }
}
