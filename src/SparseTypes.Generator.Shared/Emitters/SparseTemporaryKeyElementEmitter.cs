namespace SparseTypes.Generator.Shared;

/// <summary>Keyed element reconcile builders for fragment models.</summary>
/// <remarks>
/// Owns element-granularity adoption and authoritative membership/order
/// staging so retarget replays post-submit edits onto persisted elements.
/// </remarks>
internal static class SparseTemporaryKeyElementEmitter
{
    internal static void AppendKeyedMembershipReconcile(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        string? keyProp,
        string? tempProp,
        string? unassigned,
        string optionalFragment,
        bool elementRef,
        bool elementNullableStruct
    )
    {
        // Rebuild from persisted membership/order, then rebase local edits by
        // stable identity on a detached list before committing writeback.
        if (keyProp is null || tempProp is null || unassigned is null)
        {
            return;
        }

        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        _ = prop;
        _ = optionalFragment;
        _ = elementNullableStruct;

        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            3,
            "var __subKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            3,
            "var __subTemps = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(3, "if (__subList is not null)");
        code.AppendLineAt(3, "{");
        SparseTemporaryKeyOrderEmitter.EmitCollectIdentities(
            code,
            "__subList",
            "__subKeys",
            "__subTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            elementRef,
            4
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __liveKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            3,
            "var __liveTemps = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(3, "if (__liveList is not null)");
        code.AppendLineAt(3, "{");
        SparseTemporaryKeyOrderEmitter.EmitCollectIdentities(
            code,
            "__liveList",
            "__liveKeys",
            "__liveTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            elementRef,
            4
        );
        code.AppendLineAt(3, "}");
        // Live surviving order mapped to final assigned keys versus
        // submitted surviving order decides who wins ordering.
        code.AppendLineAt(
            3,
            "var __liveSurviving = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(3, "if (__liveList is not null)");
        code.AppendLineAt(3, "{");
        SparseTemporaryKeyOrderEmitter.EmitSurvivingOrder(
            code,
            "__liveList",
            "__subKeys",
            "__subTemps",
            "__liveSurviving",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            elementRef,
            4
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __subSurviving = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(3, "if (__subList is not null)");
        code.AppendLineAt(3, "{");
        SparseTemporaryKeyOrderEmitter.EmitSubmittedSurvivingOrder(
            code,
            "__subList",
            "__liveKeys",
            "__liveTemps",
            "__subSurviving",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            elementRef,
            4
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __hasLocalReorder = false;");
        code.AppendLineAt(3, "if (__liveSurviving.Count == __subSurviving.Count)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "for (var __oi = 0; __oi < __liveSurviving.Count; __oi++) if (!"
                + comparer
                + ".Equals(__liveSurviving[__oi], __subSurviving[__oi])) { __hasLocalReorder = true; break; }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else { __hasLocalReorder = true; }");
        code.AppendLineAt(3, "if (!__hasLocalReorder)");
        code.AppendLineAt(3, "{");
        // Clean path: persisted order plus post-submit insertions.
        code.AppendLineAt(
            4,
            "var __ordered = new global::System.Collections.Generic.List<"
                + member.Collection.ElementType.Name
                + ">();"
        );
        SparseTemporaryKeyOrderEmitter.EmitBuildKeyLookup(
            code,
            "__working",
            "__workingByKey",
            "__workingHasNullKey",
            "__workingNullKey",
            elementType,
            keyType,
            keyProp,
            member.Collection.KeyIsReferenceType,
            elementRef,
            elementNullableStruct,
            4
        );
        code.AppendLineAt(4, "if (__perList is not null)");
        code.AppendLineAt(4, "{");
        SparseTemporaryKeyOrderEmitter.EmitPersistedOrderFill(
            code,
            "__perList",
            "__workingByKey",
            "__workingHasNullKey",
            "__workingNullKey",
            "__ordered",
            "__subKeys",
            "__subTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            member.Collection.KeyIsReferenceType,
            elementRef,
            5
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__liveList is not null)");
        code.AppendLineAt(4, "{");
        // Predecessor lookup needs indexing even when the model exposes only
        // IEnumerable.
        code.AppendLineAt(
            5,
            "var __liveOrder = __liveList as global::System.Collections.Generic.IReadOnlyList<"
                + member.Collection.ElementType.Name
                + "> ?? new global::System.Collections.Generic.List<"
                + member.Collection.ElementType.Name
                + ">(__liveList);"
        );
        SparseTemporaryKeyOrderEmitter.EmitPostAddInsert(
            code,
            "__liveOrder",
            "__ordered",
            "__subKeys",
            "__subTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            member.Collection.KeyIsReferenceType,
            elementRef,
            5
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (__ordered!.Count != __working!.Count) { __working.Clear(); __working.AddRange(__ordered); __dirty = true; }"
        );
        code.AppendLineAt(
            4,
            "else { for (var __oi = 0; __oi < __ordered.Count; __oi++) { var __a = __ordered[__oi]; var __b = __working[__oi]; bool __same = object.ReferenceEquals(__a, __b); if (!__same && __a is not null && __b is not null) { __same = "
                + elementType
                + ".Fragment.__SparseAreEqual("
                + optionalFragment
                + ".Present("
                + elementType
                + ".Fragment.From(("
                + elementType
                + ")__a!)), "
                + optionalFragment
                + ".Present("
                + elementType
                + ".Fragment.From(("
                + elementType
                + ")__b!))); } if (!__same) { __working.Clear(); __working.AddRange(__ordered); __dirty = true; break; } } }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        // Local reorder wins: live order already holds merged surviving
        // plus post-adds; only server-added rows append in persisted order.
        code.AppendLineAt(4, "if (__perList is not null)");
        code.AppendLineAt(4, "{");
        SparseTemporaryKeyOrderEmitter.EmitServerAddedAppend(
            code,
            "__perList",
            "__working",
            "__subKeys",
            "__subTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            elementRef,
            5
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        // Final duplicate guard before commit.
        code.AppendLineAt(
            3,
            "var __seenKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            3,
            "var __seenTemps = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        SparseTemporaryKeyOrderEmitter.EmitDuplicateGuard(
            code,
            "__working",
            "__seenKeys",
            "__seenTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            elementRef,
            3
        );
        code.AppendLineAt(3, "}");
    }

    internal static void AppendKeyedAssignedElement(
        SharedIndentedBuilder code,
        string runtimeNamespace,
        SparseMemberModel member,
        string elementType,
        string keyType,
        string keyProp,
        string optionalFragment,
        string eUse,
        bool elementRef,
        bool elementNullableStruct,
        bool keyIsReferenceType,
        string subLookup,
        string subHasNullKey,
        string subNullKey,
        string perLookup,
        string perHasNullKey,
        string perNullKey
    )
    {
        // Submitted-missing elements are post-submit additions and stay.
        code.AppendLineAt(5, elementType + " __subFound = default" + (elementRef ? "!" : "") + ";");
        code.AppendLineAt(5, "bool __hasSub = false;");
        EmitIndexedFind(
            code,
            subLookup,
            subHasNullKey,
            subNullKey,
            "__subFound",
            "__hasSub",
            "__k",
            keyType,
            keyIsReferenceType
        );
        code.AppendLineAt(
            5,
            "if (!__hasSub) { __working[__write++] = __working[__i]"
                + (elementRef ? "!" : "")
                + "; continue; }"
        );
        code.AppendLineAt(5, elementType + " __perFound = default" + (elementRef ? "!" : "") + ";");
        code.AppendLineAt(5, "bool __hasPer = false;");
        EmitIndexedFind(
            code,
            perLookup,
            perHasNullKey,
            perNullKey,
            "__perFound",
            "__hasPer",
            "__k",
            keyType,
            keyIsReferenceType
        );
        code.AppendLineAt(5, "if (!__hasPer)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__dirty = true; continue;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "var __edit = __transition?.GetChange(__k!)?.Edit;");
        code.AppendLineAt(5, "if (__edit is null || __edit.IsEmpty)");
        code.AppendLineAt(5, "{");
        // An assigned row without its own post-submit edit adopts the persisted
        // element even when a sibling row keeps the member dirty.
        code.AppendLineAt(
            6,
            "if (!"
                + elementType
                + ".Fragment.__SparseAreEqual("
                + optionalFragment
                + ".Present("
                + elementType
                + ".Fragment.From("
                + eUse
                + ")), "
                + optionalFragment
                + ".Present("
                + elementType
                + ".Fragment.From(__perFound)))) { __working[__i] = "
                + elementType
                + ".Fragment.From(__perFound).ToModel(); __dirty = true; }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        // The edit sits on the submitted state while adoption targets the
        // persisted state.
        code.AppendLineAt(
            6,
            "var __applied = __edit.ToPatch().Apply(" + elementType + ".Fragment.From(__perFound));"
        );
        code.AppendLineAt(
            6,
            "if (!__applied.IsPresent || __applied.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
        );
        code.AppendLineAt(6, "__working[__i] = __applied.Value.ToModel(); __dirty = true;");
        code.AppendLineAt(5, "}");
        _ = runtimeNamespace;
        _ = member;
        _ = elementNullableStruct;
    }

    private static void EmitIndexedFind(
        SharedIndentedBuilder code,
        string lookup,
        string hasNullKey,
        string nullKeyValue,
        string foundVar,
        string flagVar,
        string keyExpr,
        string keyType,
        bool keyIsReferenceType
    )
    {
        var lookupResult = foundVar + "Lookup";
        if (keyType.EndsWith("?", System.StringComparison.Ordinal))
        {
            code.AppendLineAt(5, "if (!" + keyExpr + ".HasValue)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if ("
                    + hasNullKey
                    + ") { "
                    + foundVar
                    + " = "
                    + nullKeyValue
                    + "; "
                    + flagVar
                    + " = true; }"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else if ("
                    + lookup
                    + ".TryGetValue("
                    + keyExpr
                    + ".Value, out var "
                    + lookupResult
                    + ")) { "
                    + foundVar
                    + " = "
                    + lookupResult
                    + "!; "
                    + flagVar
                    + " = true; }"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(5, "if (" + keyExpr + " is null)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if ("
                    + hasNullKey
                    + ") { "
                    + foundVar
                    + " = "
                    + nullKeyValue
                    + "; "
                    + flagVar
                    + " = true; }"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else if ("
                    + lookup
                    + ".TryGetValue("
                    + keyExpr
                    + ", out var "
                    + lookupResult
                    + ")) { "
                    + foundVar
                    + " = "
                    + lookupResult
                    + "!; "
                    + flagVar
                    + " = true; }"
            );
        }
        else
        {
            code.AppendLineAt(
                5,
                "if ("
                    + lookup
                    + ".TryGetValue("
                    + keyExpr
                    + ", out var "
                    + lookupResult
                    + ")) { "
                    + foundVar
                    + " = "
                    + lookupResult
                    + "!; "
                    + flagVar
                    + " = true; }"
            );
        }
    }
}
