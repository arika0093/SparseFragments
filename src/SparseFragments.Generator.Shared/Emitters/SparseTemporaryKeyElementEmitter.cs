namespace SparseFragments.Generator.Shared;

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
        // Authoritative membership/order phase: the target list rebuilds
        // from the persisted membership/order, then rebases recorded
        // post-submit adds/removes/reorders by stable identity.
        // Server-added rows survive, server-dropped rows disappear, clean
        // reconciles converge to HasChanges == false, and concurrent
        // local reorder wins deterministically over server reorder.
        // Everything runs on the detached __working list; the live list
        // is untouched until the writeback commit below.
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
        code.AppendLineAt(4, "if (__perList is not null)");
        code.AppendLineAt(4, "{");
        SparseTemporaryKeyOrderEmitter.EmitPersistedOrderFill(
            code,
            "__perList",
            "__working",
            "__ordered",
            "__subKeys",
            "__subTemps",
            elementType,
            keyType,
            keyProp,
            tempProp,
            elementRef,
            5
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__liveList is not null)");
        code.AppendLineAt(4, "{");
        // Predecessor lookup needs indexing even when the model exposes only IEnumerable.
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
            elementRef,
            5
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (__ordered.Count != __working.Count) { __working.Clear(); __working.AddRange(__ordered); __dirty = true; }"
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
        bool elementNullableStruct
    )
    {
        // Submitted-missing elements are post-submit additions and stay;
        // submitted elements missing from the persisted model were dropped
        // and leave; the rest replay post-submit edits onto persisted values
        // (or adopt them when untouched).
        code.AppendLineAt(5, elementType + " __subFound = default" + (elementRef ? "!" : "") + ";");
        code.AppendLineAt(5, "bool __hasSub = false;");
        code.AppendLineAt(5, "if (__subList is not null)");
        AppendFindByKey(
            code,
            "__subList",
            "__subFound",
            "__hasSub",
            elementType,
            keyType,
            keyProp,
            "__k",
            elementRef,
            elementNullableStruct,
            5
        );
        code.AppendLineAt(5, "if (!__hasSub) continue;");
        code.AppendLineAt(5, elementType + " __perFound = default" + (elementRef ? "!" : "") + ";");
        code.AppendLineAt(5, "bool __hasPer = false;");
        code.AppendLineAt(5, "if (__perList is not null)");
        AppendFindByKey(
            code,
            "__perList",
            "__perFound",
            "__hasPer",
            elementType,
            keyType,
            keyProp,
            "__k",
            elementRef,
            elementNullableStruct,
            5
        );
        code.AppendLineAt(5, "if (!__hasPer)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__working.RemoveAt(__i); __i--; __dirty = true; continue;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "var __edit = __transition?.GetChange(__k!)?.Edit;");
        code.AppendLineAt(5, "if (__edit is null || __edit.IsEmpty)");
        code.AppendLineAt(5, "{");
        // Element-granularity adoption: an assigned row without its own
        // post-submit edit adopts the persisted element even when a
        // sibling row keeps the member dirty. The member-wide clean gate
        // would otherwise retain stale pre-save values.
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
        // persisted state; the baseline-free temp-correlated patch carries
        // local field values onto the persisted identity.
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
    }

    private static void AppendFindByKey(
        SharedIndentedBuilder code,
        string list,
        string foundVar,
        string flagVar,
        string elementType,
        string keyType,
        string keyProp,
        string keyExpr,
        bool elementRef,
        bool elementNullableStruct,
        int indent
    )
    {
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "foreach (var __cand in " + list + ")");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(indent + 2, "if (__cand is null) continue;");
            code.AppendLineAt(
                indent + 2,
                "if (global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__cand."
                    + keyProp
                    + ", "
                    + keyExpr
                    + ")) { "
                    + foundVar
                    + " = __cand; "
                    + flagVar
                    + " = true; break; }"
            );
            code.AppendLineAt(indent + 1, "}");
        }
        else if (elementNullableStruct)
        {
            code.AppendLineAt(indent + 1, "foreach (var __candRaw in " + list + ")");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(indent + 2, "if (!__candRaw.HasValue) continue;");
            code.AppendLineAt(indent + 2, "var __cand = __candRaw.Value;");
            code.AppendLineAt(
                indent + 2,
                "if (global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__cand."
                    + keyProp
                    + ", "
                    + keyExpr
                    + ")) { "
                    + foundVar
                    + " = __cand; "
                    + flagVar
                    + " = true; break; }"
            );
            code.AppendLineAt(indent + 1, "}");
        }
        else
        {
            code.AppendLineAt(indent + 1, "foreach (var __cand in " + list + ")");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(
                indent + 2,
                "if (global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__cand."
                    + keyProp
                    + ", "
                    + keyExpr
                    + ")) { "
                    + foundVar
                    + " = __cand; "
                    + flagVar
                    + " = true; break; }"
            );
            code.AppendLineAt(indent + 1, "}");
        }
        code.AppendLineAt(indent, "}");
        _ = elementType;
    }
}
