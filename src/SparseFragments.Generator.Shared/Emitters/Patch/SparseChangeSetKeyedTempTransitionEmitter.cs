namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity fragments for keyed-sequence transitions.</summary>
/// <remarks>
/// Hosts the Guid identity surface of keyed transition items
/// (<c>TemporaryKey</c>, lookup, extractors, whole-path capture) used by
/// <see cref="SparseChangeSetKeyedTransitionEmitter"/>. Assigned-key behavior
/// stays in the owning emitter.
/// </remarks>
internal static class SparseChangeSetKeyedTempTransitionEmitter
{
    /// <summary>Emits the TemporaryKey property of a keyed transition item.</summary>
    internal static void AppendItemTemporaryKeyProperty(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        code.AppendLineAt(
            4,
            "/// <summary>Gets the temporary identity for unassigned entries.</summary>"
        );
        code.AppendLineAt(
            4,
            "/// <remarks>Non-null only when the entry key is unassigned and the element type opts into temporary identity; assigned keys always take precedence.</remarks>"
        );
        code.AppendLineAt(
            4,
            SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                ? "public global::System.Guid? TemporaryKey { get; }"
                : "public global::System.Guid? TemporaryKey => null;"
        );
    }

    /// <summary>Emits the temporary-identity lookup of a keyed transition.</summary>
    internal static void AppendGetTemporaryChange(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        code.AppendLineAt(
            3,
            "/// <summary>Looks up the typed change for a temporary identity; never returns null.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Only entries with an unassigned permanent key participate: a valid assigned key always wins, so assigned entries are never returned here even when they retain a temporary value. Unknown, empty, or temporary-less entries return <see cref=\"Item.Empty\"/>. Non-empty results are the same instances produced by enumeration.</remarks>"
        );
        code.AppendLineAt(
            3,
            "/// <param name=\"temporaryKey\">The temporary identity to look up.</param>"
        );
        code.AppendLineAt(3, "/// <returns>The typed change for the identity.</returns>");
        code.AppendLineAt(3, "public Item GetTemporaryChange(global::System.Guid temporaryKey)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (temporaryKey == global::System.Guid.Empty) return Item.Empty;");
        code.AppendLineAt(4, "var __lookup = _temporaryLookup;");
        code.AppendLineAt(4, "if (__lookup is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "__lookup = new global::System.Collections.Generic.Dictionary<global::System.Guid, Item>();"
        );
        code.AppendLineAt(5, "foreach (var __item in _items)");
        code.AppendLineAt(
            6,
            "if (__item.TemporaryKey.HasValue && "
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__item.Key")
                + ") __lookup[__item.TemporaryKey.Value] = __item;"
        );
        code.AppendLineAt(5, "_temporaryLookup = __lookup;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return __lookup.TryGetValue(temporaryKey, out var __found) ? __found : Item.Empty;"
        );
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits the temporary-lookup backing field of a keyed transition.</summary>
    internal static void AppendTemporaryLookupField(SharedIndentedBuilder code)
    {
        code.AppendLineAt(
            3,
            "private global::System.Collections.Generic.Dictionary<global::System.Guid, Item>? _temporaryLookup;"
        );
    }

    /// <summary>Emits the ChangeSet-side temporary-identity extractor.</summary>
    internal static void AppendTemporaryKeyExtractor(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        bool documented
    )
    {
        if (documented)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Extracts the temporary identity for a keyed transition element.</summary>"
            );
        }
        code.AppendLineAt(
            2,
            "private static global::System.Guid? __SparseTemporaryKeyOf_ChangeSet_"
                + member.Id
                + "("
                + elementType
                + " element)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no temporary identity.\");"
        );
        code.AppendLineAt(
            3,
            "return element." + SparseKeyedCollectionEmitter.TemporaryKeyProperty(member) + ";"
        );
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits whole-path temporary capture declarations.</summary>
    internal static void AppendBuildTempDeclarations(SharedIndentedBuilder code, string elementType)
    {
        code.AppendLineAt(
            3,
            "var __seenBuildTemp = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(
            3,
            "var __unassignedBefore = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        code.AppendLineAt(
            3,
            "var __unassignedTempBefore = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
        code.AppendLineAt(
            3,
            "var __unassignedTempBeforeIndex = new global::System.Collections.Generic.List<int>();"
        );
        code.AppendLineAt(
            3,
            "var __unassignedTempAfter = new global::System.Collections.Generic.List<global::System.Guid>();"
        );
    }

    /// <summary>Emits the whole-path before-walk temporary branch.</summary>
    internal static void AppendBuildTempBeforeBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        // Whole-transition snapshots may carry temporary identities as
        // data; at most one side is populated, so positional temp capture
        // here is exact and later stages stamp it onto items.
        code.AppendLineAt(
            4,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __bt = __SparseTemporaryKeyOf_ChangeSet_"
                + member.Id
                + "(__item); if (!__bt.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__bt.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); if (!__seenBuildTemp.Add(__bt.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); __unassignedTempBeforeIndex.Add(__beforeOrder.Count); __beforeOrder.Add(__k); __unassignedBefore.Add(__item); __unassignedTempBefore.Add(__bt.Value); continue; }"
        );
    }

    /// <summary>Emits the whole-path after-walk temporary branch.</summary>
    internal static void AppendBuildTempAfterBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member
    )
    {
        code.AppendLineAt(
            4,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __at = __SparseTemporaryKeyOf_ChangeSet_"
                + member.Id
                + "(__item); if (!__at.HasValue) throw new global::System.InvalidOperationException(\"An unassigned keyed element is missing its temporary identity.\"); if (__at.Value == global::System.Guid.Empty) throw new global::System.InvalidOperationException(\"An unassigned keyed element has a reserved temporary identity.\"); if (!__seenBuildTemp.Add(__at.Value)) throw new global::System.InvalidOperationException(\"Duplicate temporary key in keyed collection.\"); __afterOrder.Add(__k!); __unassignedAfter.Add(__item); __unassignedTempAfter.Add(__at.Value); continue; }"
        );
    }

    /// <summary>Emits whole-path temporary removals in before order.</summary>
    internal static void AppendBuildTempRemovedLoop(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string elementType
    )
    {
        // Whole-path temporary removals in before order; at most one side
        // is populated, so positional temp capture above stays exact.
        code.AppendLineAt(3, "for (var __tbi = 0; __tbi < __unassignedBefore.Count; __tbi++)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__afterTempSet.Contains(__unassignedTempBefore[__tbi])) continue;"
        );
        code.AppendLineAt(4, "var __trb = __unassignedBefore[__tbi];");
        code.AppendLineAt(4, "var __trt = __unassignedTempBefore[__tbi];");
        code.AppendLineAt(
            4,
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
        code.AppendLineAt(4, "var __trfullEdit = " + elementCs + ".Between(__treb, default);");
        code.AppendLineAt(
            4,
            "__items.Add(new "
                + trans
                + ".Item(__SparseKeyOf_ChangeSet_"
                + member.Id
                + "(__trb), __trt, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__trb), default, __unassignedTempBeforeIndex[__tbi], -1, false, true, false, false, __trfullEdit, false));"
        );
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits the whole-path unassigned-add branch with temp stamps.</summary>
    internal static void AppendBuildTempAddBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string runtime,
        string elementCs,
        string elementFrag,
        string elementType
    )
    {
        code.AppendLineAt(
            4,
            "if ("
                + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                + ") { var __upos = __unassignedPosition++; var __ua = __unassignedAfter[__upos]; var __ut = __unassignedTempAfter[__upos]; var __uaEdit = "
                + elementCs
                + ".Between(default, "
                + runtime
                + "Optional<"
                + elementFrag
                + "?>.Present("
                + elementFrag
                + ".From(__ua))); __items.Add(new "
                + trans
                + ".Item(__k!, __ut, default, "
                + runtime
                + "Optional<"
                + elementType
                + ">.Present(__ua), -1, __afterPosition++, true, false, false, false, __uaEdit, false)); continue; }"
        );
    }
}
