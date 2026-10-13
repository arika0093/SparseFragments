namespace SparseTypes.Generator.Shared;

/// <summary>Authoritative order and membership staging for keyed reconcile.</summary>
/// <remarks>
/// Builds the persisted-order target list and rebases post-submit
/// insertions, deletions and reorderings by stable identity.
/// </remarks>
internal static class SparseTemporaryKeyOrderEmitter
{
    internal static void EmitCollectIdentities(
        SharedIndentedBuilder code,
        string list,
        string keys,
        string temps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef,
        int indent
    )
    {
        _ = elementType;
        _ = keyType;
        var loopVar = "__c";
        code.AppendLineAt(indent, "foreach (var " + loopVar + "Raw in " + list + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLineAt(indent + 1, "if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLineAt(indent + 1, "var __ck = " + loopVar + "." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__ck, "
                + unassigned
                + ")) { var __ct = "
                + loopVar
                + "."
                + tempProp
                + "; if (__ct.HasValue && __ct.Value != global::System.Guid.Empty) "
                + temps
                + ".Add(__ct.Value); }"
        );
        code.AppendLineAt(indent + 1, "else " + keys + ".Add(__ck);");
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitSurvivingOrder(
        SharedIndentedBuilder code,
        string list,
        string subKeys,
        string subTemps,
        string output,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef,
        int indent
    )
    {
        var loopVar = "__v";
        code.AppendLineAt(indent, "foreach (var " + loopVar + "Raw in " + list + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLineAt(indent + 1, "if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLineAt(indent + 1, "var __vk = " + loopVar + "." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__vk, "
                + unassigned
                + ")) { var __vt = "
                + loopVar
                + "."
                + tempProp
                + "; if (!__vt.HasValue || __vt.Value == global::System.Guid.Empty) continue; if (!"
                + subTemps
                + ".Contains(__vt.Value)) continue;"
        );
        code.AppendLineAt(
            indent + 2,
            "if (map.TryGetValue((__collectionScope, __vt.Value), out var __vpObj) && __vpObj is "
                + elementType
                + " __vp) "
                + output
                + ".Add(__vp."
                + keyProp
                + ");"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(
            indent + 1,
            "else if (" + subKeys + ".Contains(__vk)) " + output + ".Add(__vk);"
        );
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitSubmittedSurvivingOrder(
        SharedIndentedBuilder code,
        string list,
        string liveKeys,
        string liveTemps,
        string output,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef,
        int indent
    )
    {
        var loopVar = "__s";
        code.AppendLineAt(indent, "foreach (var " + loopVar + "Raw in " + list + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLineAt(indent + 1, "if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLineAt(indent + 1, "var __sk2 = " + loopVar + "." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__sk2, "
                + unassigned
                + ")) { var __st2 = "
                + loopVar
                + "."
                + tempProp
                + "; if (!__st2.HasValue || __st2.Value == global::System.Guid.Empty) continue; if (!"
                + liveTemps
                + ".Contains(__st2.Value)) continue;"
        );
        code.AppendLineAt(
            indent + 2,
            "if (map.TryGetValue((__collectionScope, __st2.Value), out var __spObj) && __spObj is "
                + elementType
                + " __sp) "
                + output
                + ".Add(__sp."
                + keyProp
                + ");"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(
            indent + 1,
            "else if (" + liveKeys + ".Contains(__sk2)) " + output + ".Add(__sk2);"
        );
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitPersistedOrderFill(
        SharedIndentedBuilder code,
        string perList,
        string working,
        string ordered,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        bool elementRef,
        int indent
    )
    {
        var loopVar = "__q";
        code.AppendLineAt(indent, "foreach (var " + loopVar + "Raw in " + perList + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLineAt(indent + 1, "if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLineAt(indent + 1, "var __qk = " + loopVar + "." + keyProp + ";");
        // Mapped temp persisted rows join by their assigned key when the
        // submitted temp survives; assigned rows join by key; the rest are
        // server-added and always join.
        code.AppendLineAt(indent + 1, "var __qt = " + loopVar + "." + tempProp + ";");
        code.AppendLineAt(
            indent + 1,
            "bool __isMappedTemp = __qt.HasValue && __qt.Value != global::System.Guid.Empty && "
                + subTemps
                + ".Contains(__qt.Value);"
        );
        code.AppendLineAt(indent + 1, "bool __isSubmittedKey = " + subKeys + ".Contains(__qk);");
        code.AppendLineAt(indent + 1, "if (__isMappedTemp || __isSubmittedKey)");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "var __hit = -1; for (var __hi = 0; __hi < "
                + working
                + ".Count; __hi++) { var __hwRaw = "
                + working
                + "[__hi];"
        );
        if (elementRef)
        {
            code.AppendLineAt(indent + 2, "if (__hwRaw is null) continue;");
        }
        code.AppendLineAt(
            indent + 2,
            "var __hw = __hwRaw; if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__hw."
                + keyProp
                + ", __qk)) { __hit = __hi; break; } }"
        );
        code.AppendLineAt(
            indent + 2,
            "if (__hit >= 0) " + ordered + ".Add(" + working + "[__hit]);"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "else");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            ordered
                + ".Add("
                + elementType
                + ".Fragment.From("
                + loopVar
                + ").ToModel()); __dirty = true;"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitPostAddInsert(
        SharedIndentedBuilder code,
        string liveList,
        string ordered,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef,
        int indent
    )
    {
        _ = elementType;
        // Post-submit additions keep their live relative positions: each
        // addition slots after its nearest surviving predecessor in live
        // order, or at the head when it leads the collection.
        var loopVar = "__n";
        code.AppendLineAt(indent, "foreach (var " + loopVar + "Raw in " + liveList + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLineAt(indent + 1, "if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLineAt(indent + 1, "var __nk = " + loopVar + "." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "bool __isUnassigned = global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__nk, "
                + unassigned
                + ");"
        );
        code.AppendLineAt(indent + 1, "global::System.Guid __nt = global::System.Guid.Empty;");
        code.AppendLineAt(
            indent + 1,
            "if (__isUnassigned) { var __ntOpt = "
                + loopVar
                + "."
                + tempProp
                + "; if (!__ntOpt.HasValue || __ntOpt.Value == global::System.Guid.Empty) return __Fail(\"The live model contains an unassigned element without a temporary identity.\"); __nt = __ntOpt.Value; if ("
                + subTemps
                + ".Contains(__nt)) continue; }"
        );
        code.AppendLineAt(indent + 1, "else if (" + subKeys + ".Contains(__nk)) continue;");
        code.AppendLineAt(
            indent + 1,
            "else { var __dup = false; for (var __di = 0; __di < "
                + ordered
                + ".Count; __di++) { var __dwRaw = "
                + ordered
                + "[__di];"
        );
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "if (__dwRaw is null) continue;");
        }
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__dwRaw."
                + keyProp
                + ", __nk)) { __dup = true; break; } } if (__dup) return __Fail(\"Reconciling produced a duplicate key.\"); }"
        );
        // Find the nearest surviving predecessor in live order.
        code.AppendLineAt(indent + 1, "var __predIndex = -1;");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "var __li = -1; for (var __lj = 0; __lj < "
                + liveList
                + ".Count; __lj++) if (object.ReferenceEquals("
                + liveList
                + "[__lj], "
                + loopVar
                + ")) { __li = __lj; break; }"
        );
        code.AppendLineAt(indent + 2, "for (var __lk = __li - 1; __lk >= 0; __lk--)");
        code.AppendLineAt(indent + 2, "{");
        code.AppendLineAt(indent + 3, "var __candRaw = " + liveList + "[__lk];");
        if (elementRef)
        {
            code.AppendLineAt(indent + 3, "if (__candRaw is null) continue;");
        }
        code.AppendLineAt(indent + 3, "var __cand = __candRaw;");
        code.AppendLineAt(indent + 3, "var __ck = __cand." + keyProp + ";");
        code.AppendLineAt(
            indent + 3,
            "bool __candUn = global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__ck, "
                + unassigned
                + ");"
        );
        code.AppendLineAt(
            indent + 3,
            "if (__candUn) { var __copt = __cand."
                + tempProp
                + "; if (!__copt.HasValue) continue; if (!"
                + subTemps
                + ".Contains(__copt.Value)) continue; if (!map.TryGetValue((__collectionScope, __copt.Value), out var __cObj) || !(__cObj is "
                + elementType
                + " __ce)) continue; __ck = __ce."
                + keyProp
                + "; }"
        );
        code.AppendLineAt(indent + 3, "else if (!" + subKeys + ".Contains(__ck)) continue;");
        code.AppendLineAt(
            indent + 3,
            "for (var __oj = 0; __oj < "
                + ordered
                + ".Count; __oj++) { var __owRaw = "
                + ordered
                + "[__oj];"
        );
        if (elementRef)
        {
            code.AppendLineAt(indent + 3, "if (__owRaw is null) continue;");
        }
        code.AppendLineAt(
            indent + 3,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__owRaw."
                + keyProp
                + ", __ck)) { __predIndex = __oj; break; } }"
        );
        code.AppendLineAt(indent + 3, "if (__predIndex >= 0) break;");
        code.AppendLineAt(indent + 2, "}");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(
            indent + 1,
            "if (__predIndex < 0) "
                + ordered
                + ".Insert(0, "
                + loopVar
                + "); else "
                + ordered
                + ".Insert(__predIndex + 1, "
                + loopVar
                + ");"
        );
        code.AppendLineAt(indent + 1, "__dirty = true;");
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitServerAddedAppend(
        SharedIndentedBuilder code,
        string perList,
        string working,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef,
        int indent
    )
    {
        _ = unassigned;
        var loopVar = "__w";
        code.AppendLineAt(indent, "foreach (var " + loopVar + "Raw in " + perList + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
            code.AppendLineAt(indent + 1, "if (" + loopVar + " is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var " + loopVar + " = " + loopVar + "Raw;");
        }
        code.AppendLineAt(indent + 1, "var __wk = " + loopVar + "." + keyProp + ";");
        code.AppendLineAt(indent + 1, "var __wt = " + loopVar + "." + tempProp + ";");
        code.AppendLineAt(
            indent + 1,
            "if ((__wt.HasValue && __wt.Value != global::System.Guid.Empty && "
                + subTemps
                + ".Contains(__wt.Value)) || "
                + subKeys
                + ".Contains(__wk)) continue;"
        );
        code.AppendLineAt(indent + 1, "var __exists = false;");
        code.AppendLineAt(
            indent + 1,
            "for (var __ei = 0; __ei < "
                + working
                + ".Count; __ei++) { var __ewRaw = "
                + working
                + "[__ei];"
        );
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "if (__ewRaw is null) continue;");
        }
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__ewRaw."
                + keyProp
                + ", __wk)) { __exists = true; break; } }"
        );
        code.AppendLineAt(indent + 1, "if (__exists) continue;");
        code.AppendLineAt(
            indent + 1,
            working
                + ".Add("
                + elementType
                + ".Fragment.From("
                + loopVar
                + ").ToModel()); __dirty = true;"
        );
        code.AppendLineAt(indent, "}");
    }

    internal static void EmitDuplicateGuard(
        SharedIndentedBuilder code,
        string working,
        string seenKeys,
        string seenTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        string unassigned,
        bool elementRef,
        int indent
    )
    {
        _ = elementType;
        _ = keyType;
        code.AppendLineAt(indent, "foreach (var __gRaw in " + working + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var __g = __gRaw;");
            code.AppendLineAt(indent + 1, "if (__g is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var __g = __gRaw;");
        }
        code.AppendLineAt(indent + 1, "var __gk = __g." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__gk, "
                + unassigned
                + ")) { var __gt = __g."
                + tempProp
                + "; if (__gt.HasValue && __gt.Value != global::System.Guid.Empty) { if (!"
                + seenTemps
                + ".Add(__gt.Value)) return __Fail(\"Reconciling produced a duplicate key.\"); } }"
        );
        code.AppendLineAt(
            indent + 1,
            "else if (!"
                + seenKeys
                + ".Add(__gk)) return __Fail(\"Reconciling produced a duplicate key.\");"
        );
        code.AppendLineAt(indent, "}");
    }
}
