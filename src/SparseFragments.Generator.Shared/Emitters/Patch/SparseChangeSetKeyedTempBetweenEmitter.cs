namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity fragments for keyed-sequence ChangeSet diffing.</summary>
/// <remarks>
/// Hosts the Guid-correlation emission used by
/// <see cref="SparseChangeSetKeyedBetweenEmitter"/> so the assigned-key diff
/// stays focused. All methods assume a tempkey member; callers branch on
/// <see cref="SparseKeyedCollectionEmitter.HasTemporaryKey"/>.
/// </remarks>
internal static class SparseChangeSetKeyedTempBetweenEmitter
{
    /// <summary>Emits the temporary-identity side maps for a keyed diff.</summary>
    internal static void AppendTempLocals(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id
    )
    {
        var elementType = SparseChangeSetBasicsEmitter.ElementTypeOf(member);
        // Temporary-identity side maps: complete per-state correlation for
        // unassigned elements. Key orders keep sentinel slots positionally;
        // these parallel Guid sequences give them stable identity.
        code.AppendLineAt(
            3,
            "var __bT"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLineAt(
            3,
            "var __bTO"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLineAt(
            3,
            "var __aT"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementType
                + ">();"
        );
        code.AppendLineAt(
            3,
            "var __aTO"
                + id
                + " = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
    }

    /// <summary>Emits baseline temporary-identity validation for whole transitions.</summary>
    internal static void AppendTempWholeValidation(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id
    )
    {
        var keyOf = "__SparseKeyOf_ChangeSet_" + id;
        var tempOf = "__SparseTemporaryKeyOf_ChangeSet_" + id;
        // Temporary-keyed baselines are valid when every unassigned
        // element carries a usable, unique temporary identity.
        // Assigned-key precedence is untouched: valid permanent keys
        // never consult the temporary value.
        code.AppendLineAt(
            4,
            "if (__beforeHas"
                + id
                + ") { var __seenTemp = new global::System.Collections.Generic.HashSet<global::System.Guid>(); foreach (var __baselineItem in __before"
                + id
                + ".Value!) { var __baselineKey = "
                + keyOf
                + "(__baselineItem); if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__baselineKey")
                + ") { var __baselineTemp = "
                + tempOf
                + "(__baselineItem); if (!__baselineTemp.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__baselineTemp.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); if (!__seenTemp.Add(__baselineTemp.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); } } }"
        );
    }

    /// <summary>Emits the unassigned branch for the granular before-map build.</summary>
    internal static void AppendTempBeforeBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id
    )
    {
        var tempOf = "__SparseTemporaryKeyOf_ChangeSet_" + id;
        code.AppendLineAt(
            6,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __bt = "
                + tempOf
                + "(__item); if (!__bt.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__bt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry(
                    "__bT" + id,
                    "__bt.Value",
                    "__item"
                )
                + " __bTO"
                + id
                + ".Add(__bt.Value); __beforeOrder"
                + id
                + ".Add(__k); continue; }"
        );
    }

    /// <summary>Emits the unassigned branch for the granular after-walk.</summary>
    internal static void AppendTempAfterBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id
    )
    {
        var tempOf = "__SparseTemporaryKeyOf_ChangeSet_" + id;
        code.AppendLineAt(
            6,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __at = "
                + tempOf
                + "(__item); if (!__at.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__at.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); "
                + SparseKeyedCollectionEmitter.AddUniqueTempEntry(
                    "__aT" + id,
                    "__at.Value",
                    "__item"
                )
                + " __aTO"
                + id
                + ".Add(__at.Value); __afterOrder"
                + id
                + ".Add(__k!); continue; }"
        );
    }

    /// <summary>Emits the temporary-identity edited loop.</summary>
    internal static void AppendTempEditedLoop(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string runtime,
        int id
    )
    {
        var elementCs = SparseChangeSetBasicsEmitter.ElementChangeSetOf(member);
        var elementFrag = SparseChangeSetBasicsEmitter.ElementFragmentOf(member);
        // Temporary-identity edits: correlated by Guid, never by sentinel.
        // Assigned-key precedence means these pairs are always unassigned
        // on both sides; the nested diff ignores the identity itself.
        code.AppendLineAt(
            5,
            "var __editedT"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + elementCs
                + ">();"
        );
        code.AppendLineAt(5, "foreach (var __tk in __bT" + id + ".Keys)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (!__aT" + id + ".TryGetValue(__tk, out var __afterTempItem)) continue;"
        );
        code.AppendLineAt(6, "var __beforeTempItem = __bT" + id + "[__tk];");
        code.AppendLineAt(
            6,
            "var __nestedTemp = "
                + elementCs
                + ".Between("
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__beforeTempItem)), "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__afterTempItem)));"
        );
        code.AppendLineAt(
            6,
            "if (!__nestedTemp.IsEmpty) __editedT" + id + "[__tk] = __nestedTemp;"
        );
        code.AppendLineAt(5, "}");
    }

    /// <summary>Emits added/removed probes covering temporary identities.</summary>
    internal static void AppendTempAddedRemovedProbes(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id
    )
    {
        code.AppendLineAt(
            5,
            "bool __hasTempAdded"
                + id
                + " = false; foreach (var __tk in __aT"
                + id
                + ".Keys) if (!__bT"
                + id
                + ".ContainsKey(__tk)) { __hasTempAdded"
                + id
                + " = true; break; }"
        );
        code.AppendLineAt(
            5,
            "bool __hasAdded"
                + id
                + " = __hasTempAdded"
                + id
                + "; if (!__hasAdded"
                + id
                + ") foreach (var __k in __afterOrder"
                + id
                + ") if (!"
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + " && !__beforeMap"
                + id
                + ".ContainsKey(__k)) { __hasAdded"
                + id
                + " = true; break; }"
        );
        code.AppendLineAt(
            5,
            "bool __hasTempRemoved"
                + id
                + " = false; foreach (var __tk in __bT"
                + id
                + ".Keys) if (!__aT"
                + id
                + ".ContainsKey(__tk)) { __hasTempRemoved"
                + id
                + " = true; break; }"
        );
        code.AppendLineAt(
            5,
            "bool __hasRemoved"
                + id
                + " = __hasTempRemoved"
                + id
                + "; foreach (var __k in __beforeOrder"
                + id
                + ") if (!"
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + " && !__afterMap"
                + id
                + ".ContainsKey(__k)) { __hasRemoved"
                + id
                + " = true; break; }"
        );
    }

    /// <summary>Emits surviving-rank reorder sets for temporary identities.</summary>
    internal static void AppendTempRanks(SharedIndentedBuilder code, int id)
    {
        // Surviving-rank reorder set for temporary identities, mirroring
        // the assigned computation so temp-only reorders are observed.
        code.AppendLineAt(
            6,
            "var __beforeTempRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, int>();"
        );
        code.AppendLineAt(
            6,
            "{ var __r = 0; foreach (var __tk in __bTO"
                + id
                + ") if (__aT"
                + id
                + ".ContainsKey(__tk)) __beforeTempRank"
                + id
                + "[__tk] = __r++; }"
        );
        code.AppendLineAt(
            6,
            "var __afterTempRank"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, int>();"
        );
        code.AppendLineAt(
            6,
            "{ var __r = 0; foreach (var __tk in __aTO"
                + id
                + ") if (__bT"
                + id
                + ".ContainsKey(__tk)) __afterTempRank"
                + id
                + "[__tk] = __r++; }"
        );
        code.AppendLineAt(
            6,
            "var __reorderedTemp"
                + id
                + " = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __beforeTempRank"
                + id
                + ") if (__afterTempRank"
                + id
                + ".TryGetValue(__kv.Key, out var __ar) && __ar != __kv.Value) __reorderedTemp"
                + id
                + ".Add(__kv.Key);"
        );
    }

    /// <summary>Emits absolute index maps for temporary identities.</summary>
    internal static void AppendTempIndexes(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int id
    )
    {
        // Absolute indexes for temporary identities: sentinel slots in the
        // key orders zip with the parallel Guid orders one-to-one.
        code.AppendLineAt(
            6,
            "var __beforeTempIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, int>();"
        );
        code.AppendLineAt(
            6,
            "{ var __ti = 0; for (var __i = 0; __i < __beforeOrder"
                + id
                + ".Count; __i++) if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    "__beforeOrder" + id + "[__i]"
                )
                + ") __beforeTempIndex"
                + id
                + ".Add(__bTO"
                + id
                + "[__ti++], __i); }"
        );
        code.AppendLineAt(
            6,
            "var __afterTempIndex"
                + id
                + " = new global::System.Collections.Generic.Dictionary<global::System.Guid, int>();"
        );
        code.AppendLineAt(
            6,
            "{ var __ti = 0; for (var __i = 0; __i < __afterOrder"
                + id
                + ".Count; __i++) if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                    member,
                    "__afterOrder" + id + "[__i]"
                )
                + ") __afterTempIndex"
                + id
                + ".Add(__aTO"
                + id
                + "[__ti++], __i); }"
        );
    }

    /// <summary>Emits the unassigned slot branch for temp-aware item assembly.</summary>
    internal static void AppendTempItemBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        int id
    )
    {
        var elementCs = SparseChangeSetBasicsEmitter.ElementChangeSetOf(member);
        var elementFrag = SparseChangeSetBasicsEmitter.ElementFragmentOf(member);
        var elementType = SparseChangeSetBasicsEmitter.ElementTypeOf(member);
        // Sentinel slots consume the parallel Guid order: the slot
        // sequence and the temporary order stay one-to-one by
        // construction, so identity never degrades to position.
        code.AppendLineAt(
            7,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __tt = __aTO"
                + id
                + "[__tempOrdinal"
                + id
                + "++]; var __tua = __aT"
                + id
                + "[__tt]; bool __tinBefore = __bT"
                + id
                + ".TryGetValue(__tt, out var __ttb); bool __tisEdited = __editedT"
                + id
                + ".TryGetValue(__tt, out var __ttedit); bool __ttisReordered = __reorderedTemp"
                + id
                + ".Contains(__tt); if (!__tinBefore || __tisEdited || __ttisReordered) { "
                + runtime
                + "Optional<"
                + elementFrag
                + "?> __tteb = __tinBefore ? "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ttb!)) : default; "
                + runtime
                + "Optional<"
                + elementFrag
                + "?> __ttea = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__tua)); var __ttfullEdit = __tisEdited ? __ttedit! : "
                + elementCs
                + ".Between(__tteb, __ttea); "
                + runtime
                + "Optional<"
                + elementType
                + "> __ttib = __tinBefore && !__tisEdited && !__ttisReordered ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__ttb!) : default; "
                + runtime
                + "Optional<"
                + elementType
                + "> __ttia = !__tisEdited && !__ttisReordered ? "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__tua) : default; int __ttbi = __beforeTempIndex"
                + id
                + ".TryGetValue(__tt, out var __ttbv) ? __ttbv : -1; int __ttai = __afterTempIndex"
                + id
                + ".TryGetValue(__tt, out var __ttav) ? __ttav : -1; __list"
                + id
                + ".Add(new "
                + trans
                + ".Item(__k!, __tt, __ttib, __ttia, __ttbi, __ttai, !__tinBefore, false, __tisEdited, __ttisReordered, __ttfullEdit, false)); } continue; }"
        );
    }

    /// <summary>Emits temporary removals in before-temporary order.</summary>
    internal static void AppendTempRemovedLoop(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        int id
    )
    {
        var elementCs = SparseChangeSetBasicsEmitter.ElementChangeSetOf(member);
        var elementFrag = SparseChangeSetBasicsEmitter.ElementFragmentOf(member);
        var elementType = SparseChangeSetBasicsEmitter.ElementTypeOf(member);
        var keyOf = "__SparseKeyOf_ChangeSet_" + id;
        code.AppendLineAt(6, "foreach (var __tk in __bTO" + id + ")");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "if (__aT" + id + ".ContainsKey(__tk)) continue;");
        code.AppendLineAt(7, "var __trb = __bT" + id + "[__tk];");
        code.AppendLineAt(
            7,
            runtime
                + "Optional<"
                + elementFrag
                + "?> __treb = "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__trb));"
        );
        code.AppendLineAt(7, "var __trfullEdit = " + elementCs + ".Between(__treb, default);");
        code.AppendLineAt(
            7,
            "int __trbi = __beforeTempIndex"
                + id
                + ".TryGetValue(__tk, out var __trbv) ? __trbv : -1;"
        );
        code.AppendLineAt(
            7,
            "__list"
                + id
                + ".Add(new "
                + trans
                + ".Item("
                + keyOf
                + "(__trb), __tk, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__trb), default, __trbi, -1, false, true, false, false, __trfullEdit, false));"
        );
        code.AppendLineAt(6, "}");
    }
}
