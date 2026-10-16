namespace SparseTypes.Generator.Shared;

/// <summary>Authoritative order and membership staging for keyed reconcile.</summary>
/// <remarks>
/// Builds the persisted-order target list and rebases post-submit
/// insertions, deletions and reorderings by stable identity.
/// </remarks>
internal static class SparseTemporaryKeyOrderEmitter
{
    internal static void EmitBuildKeyLookup(
        SharedIndentedBuilder code,
        string list,
        string lookup,
        string hasNullKey,
        string nullKeyValue,
        string elementType,
        string keyType,
        string keyProp,
        bool keyIsReferenceType,
        bool elementRef,
        bool elementNullableStruct,
        int indent
    )
    {
        var nullableValueKey = keyType.EndsWith("?", StringComparison.Ordinal);
        var nullableKey = nullableValueKey || keyIsReferenceType;
        var lookupKeyType = nullableValueKey ? keyType.Substring(0, keyType.Length - 1) : keyType;
        code.AppendLineAt(
            indent,
            "var "
                + lookup
                + " = new global::System.Collections.Generic.Dictionary<"
                + lookupKeyType
                + ", "
                + elementType
                + ">(global::System.Collections.Generic.EqualityComparer<"
                + lookupKeyType
                + ">.Default);"
        );
        if (nullableKey)
        {
            code.AppendLineAt(indent, "var " + hasNullKey + " = false;");
            code.AppendLineAt(
                indent,
                elementType + " " + nullKeyValue + " = default" + (elementRef ? "!" : "") + ";"
            );
        }
        code.AppendLineAt(indent, "if (" + list + " is not null)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "foreach (var __idxRaw in " + list + ")");
        code.AppendLineAt(indent + 1, "{");
        var candidate = "__idxRaw";
        if (elementRef)
        {
            code.AppendLineAt(indent + 2, "if (__idxRaw is null) continue;");
        }
        else if (elementNullableStruct)
        {
            code.AppendLineAt(indent + 2, "if (!__idxRaw.HasValue) continue;");
            code.AppendLineAt(indent + 2, "var __idx = __idxRaw.Value;");
            candidate = "__idx";
        }
        code.AppendLineAt(indent + 2, "var __idxKey = " + candidate + "." + keyProp + ";");
        if (nullableValueKey)
        {
            code.AppendLineAt(indent + 2, "if (!__idxKey.HasValue)");
            code.AppendLineAt(indent + 2, "{");
            code.AppendLineAt(
                indent + 3,
                "if (!"
                    + hasNullKey
                    + ") { "
                    + hasNullKey
                    + " = true; "
                    + nullKeyValue
                    + " = "
                    + candidate
                    + "; }"
            );
            code.AppendLineAt(indent + 2, "}");
            code.AppendLineAt(
                indent + 2,
                "else " + lookup + ".TryAdd(__idxKey.Value, " + candidate + ");"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(indent + 2, "if (__idxKey is null)");
            code.AppendLineAt(indent + 2, "{");
            code.AppendLineAt(
                indent + 3,
                "if (!"
                    + hasNullKey
                    + ") { "
                    + hasNullKey
                    + " = true; "
                    + nullKeyValue
                    + " = "
                    + candidate
                    + "; }"
            );
            code.AppendLineAt(indent + 2, "}");
            code.AppendLineAt(
                indent + 2,
                "else " + lookup + ".TryAdd(__idxKey, " + candidate + ");"
            );
        }
        else
        {
            code.AppendLineAt(indent + 2, lookup + ".TryAdd(__idxKey, " + candidate + ");");
        }
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

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
        string workingLookup,
        string workingHasNullKey,
        string workingNullKeyValue,
        string ordered,
        string subKeys,
        string subTemps,
        string elementType,
        string keyType,
        string keyProp,
        string tempProp,
        bool keyIsReferenceType,
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
        // submitted temp survives.
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
        if (keyType.EndsWith("?", StringComparison.Ordinal))
        {
            code.AppendLineAt(indent + 2, "if (!__qk.HasValue)");
            code.AppendLineAt(indent + 2, "{");
            code.AppendLineAt(
                indent + 3,
                "if (" + workingHasNullKey + ") " + ordered + ".Add(" + workingNullKeyValue + ");"
            );
            code.AppendLineAt(indent + 2, "}");
            code.AppendLineAt(indent + 2, "else if (");
            code.AppendLineAt(
                indent + 3,
                workingLookup
                    + ".TryGetValue(__qk.Value, out var __hit)) "
                    + ordered
                    + ".Add(__hit!);"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(indent + 2, "if (__qk is null)");
            code.AppendLineAt(indent + 2, "{");
            code.AppendLineAt(
                indent + 3,
                "if (" + workingHasNullKey + ") " + ordered + ".Add(" + workingNullKeyValue + ");"
            );
            code.AppendLineAt(indent + 2, "}");
            code.AppendLineAt(indent + 2, "else if (");
            code.AppendLineAt(
                indent + 3,
                workingLookup + ".TryGetValue(__qk, out var __hit)) " + ordered + ".Add(__hit!);"
            );
        }
        else
        {
            code.AppendLineAt(
                indent + 2,
                "if ("
                    + workingLookup
                    + ".TryGetValue(__qk, out var __hit)) "
                    + ordered
                    + ".Add(__hit!);"
            );
        }
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
        bool keyIsReferenceType,
        bool elementRef,
        int indent
    )
    {
        _ = elementType;
        var nullableValueKey = keyType.EndsWith("?", StringComparison.Ordinal);
        var nullableKey = nullableValueKey || keyIsReferenceType;
        var lookupKeyType = nullableValueKey ? keyType.Substring(0, keyType.Length - 1) : keyType;
        code.AppendLineAt(
            indent,
            "var __orderedIndex = new global::System.Collections.Generic.Dictionary<"
                + lookupKeyType
                + ", int>(global::System.Collections.Generic.EqualityComparer<"
                + lookupKeyType
                + ">.Default);"
        );
        if (nullableKey)
        {
            code.AppendLineAt(indent, "var __orderedNullIndex = -1;");
        }
        code.AppendLineAt(indent, "for (var __oi = 0; __oi < " + ordered + ".Count; __oi++)");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var __or = " + ordered + "[__oi];");
            code.AppendLineAt(indent + 1, "if (__or is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var __or = " + ordered + "[__oi];");
        }
        code.AppendLineAt(indent + 1, "var __ok = __or." + keyProp + ";");
        if (nullableValueKey)
        {
            code.AppendLineAt(
                indent + 1,
                "if (__ok.HasValue) __orderedIndex.TryAdd(__ok.Value, __oi); else if (__orderedNullIndex < 0) __orderedNullIndex = __oi;"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(
                indent + 1,
                "if (__ok is null) { if (__orderedNullIndex < 0) __orderedNullIndex = __oi; } else __orderedIndex.TryAdd(__ok, __oi);"
            );
        }
        else
        {
            code.AppendLineAt(indent + 1, "__orderedIndex.TryAdd(__ok, __oi);");
        }
        code.AppendLineAt(indent, "}");
        code.AppendLineAt(
            indent,
            "var __after = new global::System.Collections.Generic.List<"
                + elementType
                + ">?["
                + ordered
                + ".Count];"
        );
        code.AppendLineAt(
            indent,
            "var __head = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(
            indent,
            "var __postAddKeys = new global::System.Collections.Generic.HashSet<"
                + lookupKeyType
                + ">(global::System.Collections.Generic.EqualityComparer<"
                + lookupKeyType
                + ">.Default);"
        );
        code.AppendLineAt(indent, "var __predecessor = -1;");
        code.AppendLineAt(indent, "foreach (var __nRaw in " + liveList + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "var __n = __nRaw;");
            code.AppendLineAt(indent + 1, "if (__n is null) continue;");
        }
        else
        {
            code.AppendLineAt(indent + 1, "var __n = __nRaw;");
        }
        code.AppendLineAt(indent + 1, "var __nk = __n." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "var __isUnassigned = global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__nk, "
                + unassigned
                + ");"
        );
        code.AppendLineAt(indent + 1, "if (__isUnassigned)");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(indent + 2, "var __nt = __n." + tempProp + ";");
        code.AppendLineAt(
            indent + 2,
            "if (!__nt.HasValue || __nt.Value == global::System.Guid.Empty) return __Fail(\"The live model contains an unassigned element without a temporary identity.\");"
        );
        code.AppendLineAt(indent + 2, "if (" + subTemps + ".Contains(__nt.Value))");
        code.AppendLineAt(indent + 2, "{");
        if (nullableValueKey)
        {
            code.AppendLineAt(
                indent + 3,
                "if (map.TryGetValue((__collectionScope, __nt.Value), out var __mapped) && __mapped is "
                    + elementType
                    + " __mappedElement) { var __mappedKey = __mappedElement."
                    + keyProp
                    + "; if (__mappedKey.HasValue) { if (__orderedIndex.TryGetValue(__mappedKey.Value, out var __mappedIndex)) __predecessor = __mappedIndex; } else if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex; }"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(
                indent + 3,
                "if (map.TryGetValue((__collectionScope, __nt.Value), out var __mapped) && __mapped is "
                    + elementType
                    + " __mappedElement) { var __mappedKey = __mappedElement."
                    + keyProp
                    + "; if (__mappedKey is null) { if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex; } else if (__orderedIndex.TryGetValue(__mappedKey, out var __mappedIndex)) __predecessor = __mappedIndex; }"
            );
        }
        else
        {
            code.AppendLineAt(
                indent + 3,
                "if (map.TryGetValue((__collectionScope, __nt.Value), out var __mapped) && __mapped is "
                    + elementType
                    + " __mappedElement && __orderedIndex.TryGetValue(__mappedElement."
                    + keyProp
                    + ", out var __mappedIndex)) __predecessor = __mappedIndex;"
            );
        }
        code.AppendLineAt(indent + 2, "continue;");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "else if (" + subKeys + ".Contains(__nk))");
        code.AppendLineAt(indent + 1, "{");
        if (nullableValueKey)
        {
            code.AppendLineAt(
                indent + 2,
                "if (__nk.HasValue) { if (__orderedIndex.TryGetValue(__nk.Value, out var __submittedIndex)) __predecessor = __submittedIndex; } else if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex;"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(
                indent + 2,
                "if (__nk is null) { if (__orderedNullIndex >= 0) __predecessor = __orderedNullIndex; } else if (__orderedIndex.TryGetValue(__nk, out var __submittedIndex)) __predecessor = __submittedIndex;"
            );
        }
        else
        {
            code.AppendLineAt(
                indent + 2,
                "if (__orderedIndex.TryGetValue(__nk, out var __submittedIndex)) __predecessor = __submittedIndex;"
            );
        }
        code.AppendLineAt(indent + 2, "continue;");
        code.AppendLineAt(indent + 1, "}");
        if (nullableValueKey)
        {
            code.AppendLineAt(
                indent + 1,
                "if (!__isUnassigned && ((!__nk.HasValue && __orderedNullIndex >= 0) || (__nk.HasValue && (__orderedIndex.ContainsKey(__nk.Value) || !__postAddKeys.Add(__nk.Value))))) return __Fail(\"Reconciling produced a duplicate key.\");"
            );
        }
        else if (keyIsReferenceType)
        {
            code.AppendLineAt(
                indent + 1,
                "if (!__isUnassigned && ((__nk is null && __orderedNullIndex >= 0) || (__nk is not null && (__orderedIndex.ContainsKey(__nk) || !__postAddKeys.Add(__nk))))) return __Fail(\"Reconciling produced a duplicate key.\");"
            );
        }
        else
        {
            code.AppendLineAt(
                indent + 1,
                "if (!__isUnassigned && (__orderedIndex.ContainsKey(__nk) || !__postAddKeys.Add(__nk))) return __Fail(\"Reconciling produced a duplicate key.\");"
            );
        }
        code.AppendLineAt(indent + 1, "if (__predecessor == -1) __head.Add(__n);");
        code.AppendLineAt(
            indent + 1,
            "else { var __bucket = __after[__predecessor]; if (__bucket is null) __after[__predecessor] = __bucket = new global::System.Collections.Generic.List<"
                + elementType
                + ">(); __bucket.Add(__n); }"
        );
        code.AppendLineAt(indent + 1, "__dirty = true;");
        code.AppendLineAt(indent, "}");
        code.AppendLineAt(
            indent,
            "var __flattened = new global::System.Collections.Generic.List<"
                + elementType
                + ">("
                + ordered
                + ".Count + __head.Count);"
        );
        code.AppendLineAt(
            indent,
            "for (var __hi = __head.Count - 1; __hi >= 0; __hi--) __flattened.Add(__head[__hi]);"
        );
        code.AppendLineAt(
            indent,
            "for (var __oi = 0; __oi < "
                + ordered
                + ".Count; __oi++) { __flattened.Add("
                + ordered
                + "[__oi]); var __bucket = __after[__oi]; if (__bucket is not null) for (var __bi = __bucket.Count - 1; __bi >= 0; __bi--) __flattened.Add(__bucket[__bi]); }"
        );
        code.AppendLineAt(indent, ordered + ".Clear(); " + ordered + ".AddRange(__flattened);");
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
        code.AppendLineAt(
            indent,
            "var __existingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default);"
        );
        code.AppendLineAt(
            indent,
            "var __existingTemps = new global::System.Collections.Generic.HashSet<global::System.Guid>();"
        );
        code.AppendLineAt(indent, "foreach (var __knownRaw in " + working + ")");
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "if (__knownRaw is null) continue;");
        }
        code.AppendLineAt(indent + 1, "var __knownKey = __knownRaw." + keyProp + ";");
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__knownKey, "
                + unassigned
                + ")) { var __knownTemp = __knownRaw."
                + tempProp
                + "; if (__knownTemp.HasValue && __knownTemp.Value != global::System.Guid.Empty) __existingTemps.Add(__knownTemp.Value); } else __existingKeys.Add(__knownKey);"
        );
        code.AppendLineAt(indent, "}");
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
        code.AppendLineAt(
            indent + 1,
            "if (global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__wk, "
                + unassigned
                + ")) { if (__wt.HasValue && __wt.Value != global::System.Guid.Empty && !__existingTemps.Add(__wt.Value)) return __Fail(\"Reconciling produced a duplicate key.\"); } else if (!__existingKeys.Add(__wk)) continue;"
        );
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
