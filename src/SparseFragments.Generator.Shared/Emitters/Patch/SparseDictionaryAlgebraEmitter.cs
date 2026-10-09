namespace SparseFragments.Generator.Shared;

/// <summary>Emits dictionary Compose and Rebase algebra.</summary>
internal static class SparseDictionaryAlgebraEmitter
{
    internal static void EmitDictionaryCompose(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string dictType,
        string runtime,
        string operation,
        string kind,
        string comparer,
        bool hasPatch,
        SparseMemberOperationSplit? split = null
    )
    {
        if (split is not null)
        {
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Composes this dictionary patch with a following patch.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public "
                    + patchName
                    + " Compose("
                    + patchName
                    + " next) => "
                    + split.OperationsType
                    + ".Compose(this, next);"
            );
            split.Shell.AppendLineAt(3, "/// <summary>Composes two dictionary patches.</summary>");
            split.Shell.AppendLineAt(
                3,
                "public static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " first, "
                    + patchName
                    + " second) => "
                    + split.OperationsType
                    + ".Compose(first, second);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Composes a dictionary patch with a following patch.</summary>"
            );
            code.AppendLineAt(
                3,
                "internal static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " self, "
                    + patchName
                    + " next)"
            );
            code.AppendLineAt(3, "{");
            SparseDictionaryPatchEmitter.AppendDictionaryFieldAliases(code);
        }
        else
        {
            // Compose.
            code.AppendLineAt(3, "public " + patchName + " Compose(" + patchName + " next)");
            code.AppendLineAt(3, "{");
        }
        code.AppendLineAt(
            4,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(
            4,
            "if (next.__whole.Kind != "
                + kind
                + ".Keep) { result.__whole = next.__whole; return result; }"
        );
        code.AppendLineAt(4, "if (__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + kind
                + ".Remove) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Remove.\");"
        );
        code.AppendLineAt(
            4,
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + dictType
                + ">.Present(__whole.Value));"
        );
        code.AppendLineAt(4, "result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "var removed = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) removed.Add(k);"
        );
        code.AppendLineAt(
            4,
            "if (next.__removed is not null) foreach (var k in next.__removed) removed.Add(k);"
        );
        code.AppendLineAt(
            4,
            "var set = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__set is not null) foreach (var kv in __set) set[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(
            4,
            "if (next.__set is not null) foreach (var kv in next.__set) { set[kv.Key] = kv.Value; removed.Remove(kv.Key); }"
        );
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) if (next.__set is not null && next.__set.ContainsKey(k)) removed.Remove(k);"
        );
        // Remove keys that are set by this then removed by next? Already: removed contains next removed, set does not contain them unless re-set. Need to drop this-set keys removed by next:
        code.AppendLineAt(
            4,
            "if (next.__removed is not null) foreach (var k in next.__removed) set.Remove(k);"
        );
        // Pre-index next removals once so per-edit checks below are O(1)
        // comparer-correct lookups instead of O(K) List.Contains scans.
        code.AppendLineAt(
            4,
            "var __nextRemoved = (next.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(next.__removed, "
                + comparer
                + ");"
        );
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">? edited = null;"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) { if (kv.Value.__SparseIsEmpty()) continue; if (__nextRemoved.Contains(kv.Key)) continue; edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "); edited[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) { if (kv.Value.__SparseIsEmpty()) continue; if (set.TryGetValue(kv.Key, out var setBase)) { var __folded = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + SparseKeyedCollectionEmitter.ValueFragmentType(member)
                    + "?>.Present("
                    + SparseKeyedCollectionEmitter.ValueFragmentType(member)
                    + ".From(setBase))); if (__folded.IsPresent && __folded.Value is not null) set[kv.Key] = __folded.Value!.ToModel(); continue; } if (edited is not null && edited.TryGetValue(kv.Key, out var first)) { var __composed = first.Compose(kv.Value); if (__composed.__SparseIsEmpty()) edited.Remove(kv.Key); else edited[kv.Key] = __composed; } else (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(
                4,
                "if (edited is not null && edited.Count > 0) result.__edited = edited;"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "var edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) edited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) edited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "foreach (var k in removed) { edited.Remove(k); set.Remove(k); }");
            code.AppendLineAt(4, "foreach (var k in set.Keys) edited.Remove(k);");
            code.AppendLineAt(4, "if (edited.Count > 0) result.__edited = edited;");
        }

        code.AppendLineAt(
            4,
            "if (removed.Count > 0) result.__removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(removed);"
        );
        code.AppendLineAt(4, "if (set.Count > 0) result.__set = set;");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
        if (split is null)
        {
            code.AppendLineAt(
                3,
                "public static "
                    + patchName
                    + " Compose("
                    + patchName
                    + " first, "
                    + patchName
                    + " second) { if (first is null) throw new global::System.ArgumentNullException(nameof(first)); return first.Compose(second); }"
            );
        }
    }

    internal static void EmitDictionaryRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string keyType,
        string valueType,
        string runtime,
        string kind,
        string optionalDict,
        string facade,
        string comparer,
        bool hasPatch,
        string editedValueType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseMemberOperationSplit? split = null
    )
    {
        // Rebase (simplified key-wise).
        var resultType = dialect.RebaseResult(patchName);
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        if (split is not null)
        {
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Rebases this dictionary patch onto a newer member value.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "public static "
                    + resultType
                    + " Rebase("
                    + optionalDict
                    + " baseState, "
                    + patchName
                    + " local, "
                    + optionalDict
                    + " currentState) => "
                    + split.OperationsType
                    + ".Rebase(baseState, local, currentState);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Rebases a dictionary patch onto a newer member value.</summary>"
            );
        }
        code.AppendLineAt(
            3,
            (split is null ? "public static " : "internal static ")
                + resultType
                + " Rebase("
                + optionalDict
                + " baseState, "
                + patchName
                + " local, "
                + optionalDict
                + " currentState)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (local is null) throw new global::System.ArgumentNullException(nameof(local));"
        );
        code.AppendLineAt(
            4,
            "if (local.__SparseIsEmpty()) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(
            4,
            "var conflicts = new global::System.Collections.Generic.List<" + conflictType + ">();"
        );
        code.AppendLineAt(4, optionalDict + " desired;");
        code.AppendLineAt(4, "try { desired = local.Apply(baseState); }");
        code.AppendLineAt(
            4,
            "catch (global::System.InvalidOperationException ex) { conflicts.Add(new "
                + conflictType
                + "(new string[0], "
                + conflictKindType
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, ex.Message)); return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLineAt(4, "if (local.__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(baseState.Value, currentState.Value)) { result.__whole = local.__whole; return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLineAt(
            5,
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(desired.Value, currentState.Value)) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        code.AppendLineAt(
            5,
            "conflicts.Add(new "
                + conflictType
                + "(new string[0], "
                + conflictKindType
                + ".Nested, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, \"The whole dictionary conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(5, "return new " + resultType + "(result, conflicts);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(baseState.Value, currentState.Value)) { result.__set = local.__set is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(local.__set, "
                + comparer
                + "); result.__removed = local.__removed is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__removed); result.__edited = local.__edited is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedValueType
                + ">(local.__edited, "
                + comparer
                + "); return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLineAt(
            4,
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreDictionaryEqual<"
                + keyType
                + ", "
                + valueType
                + ">(desired.Value, currentState.Value)) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        // Per-key three-way (without nested rebase composition for brevity: nested edits rebase recursively when both edited).
        EmitRebaseDictionarySnapshot(code, "baseState", "baseDict", keyType, valueType, comparer);
        EmitRebaseDictionarySnapshot(
            code,
            "currentState",
            "currentDict",
            keyType,
            valueType,
            comparer
        );
        EmitRebaseDictionarySnapshot(code, "desired", "desiredDict", keyType, valueType, comparer);
        // Pre-index locally removed keys once (O(K)) so per-key touch checks are
        // O(1) comparer-correct lookups instead of O(K) List.Contains scans.
        code.AppendLineAt(
            4,
            "var __dictRemoved = (local.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__removed, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var __pendingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__set is not null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)local.__set.Keys : (local.__edited is not null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)local.__edited.Keys : __dictRemoved), "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (local.__set is not null && local.__edited is not null) __pendingKeys.UnionWith(local.__edited.Keys);"
        );
        code.AppendLineAt(4, "__pendingKeys.UnionWith(__dictRemoved);");
        if (hasPatch)
        {
            var valuePatch = SparseKeyedCollectionEmitter.ValuePatchType(member);
            var valueFragment = SparseKeyedCollectionEmitter.ValueFragmentType(member);
            var prefix = SparseKeyedCollectionEmitter.ValuePatchPrefix(member);
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopStart(
                code,
                "baseDict",
                "currentDict",
                "desiredDict"
            );
            code.AppendLineAt(
                5,
                "var inBase = baseDict.TryGetValue(k, out var b); var inCurrent = currentDict.TryGetValue(k, out var c); var inDesired = desiredDict.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "var touchesSet = local.__set is not null && local.__set.ContainsKey(k); var touchesRemoved = __dictRemoved.Contains(k); var touchesEdited = local.__edited is not null && local.__edited.ContainsKey(k);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(d, c));"
            );
            code.AppendLineAt(
                5,
                "if (baseEqCurrent) { if (touchesSet) (result.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = local.__set![k]; else if (touchesRemoved) (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); else (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[k] = local.__edited![k]; continue; }"
            );
            code.AppendLineAt(5, "if (desiredEqCurrent) continue;");
            code.AppendLineAt(5, "if (touchesEdited && inBase && inCurrent && inDesired)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var nested = "
                    + valuePatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(b!)), local.__edited![k], "
                    + runtime
                    + "Optional<"
                    + valueFragment
                    + "?>.Present("
                    + valueFragment
                    + ".From(c!)));"
            );
            code.AppendLineAt(
                6,
                "if (!nested.HasConflicts && !nested.Rebased.__SparseIsEmpty()) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valuePatch
                    + ">("
                    + comparer
                    + "))[k] = nested.Rebased; else foreach (var nc in nested.Conflicts) conflicts.Add(nc.WithPathPrefix(((object?)k)?.ToString() ?? \"<null>\"));"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else conflicts.Add(new "
                    + conflictType
                    + "(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The dictionary entry conflicts with a concurrent change.\"));"
            );
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopEnd(code);
        }
        else
        {
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopStart(
                code,
                "baseDict",
                "currentDict",
                "desiredDict"
            );
            code.AppendLineAt(
                5,
                "var inBase = baseDict.TryGetValue(k, out var b); var inCurrent = currentDict.TryGetValue(k, out var c); var inDesired = desiredDict.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(b, c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual<"
                    + valueType
                    + ">(d, c));"
            );
            code.AppendLineAt(
                5,
                "if (baseEqCurrent) { if (local.__set is not null && local.__set.TryGetValue(k, out var sv)) (result.__set ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = sv; else if (__dictRemoved.Contains(k)) (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); else if (local.__edited is not null && local.__edited.TryGetValue(k, out var ev)) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + "))[k] = ev; continue; }"
            );
            code.AppendLineAt(
                5,
                "if (!desiredEqCurrent) conflicts.Add(new "
                    + conflictType
                    + "(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The dictionary entry conflicts with a concurrent change.\"));"
            );
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopEnd(code);
        }

        code.AppendLineAt(4, "return new " + resultType + "(result, conflicts);");
        code.AppendLineAt(3, "}");
    }

    internal static void EmitRebaseDictionarySnapshot(
        SharedIndentedBuilder code,
        string state,
        string variable,
        string keyType,
        string valueType,
        string comparer
    )
    {
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{keyType}, {valueType}>";
        var interfaceType =
            $"global::System.Collections.Generic.IDictionary<{keyType}, {valueType}>";
        var direct = "__" + variable + "Direct";
        // These indexes are read-only. Keep fallback assignment semantics for custom comparers.
        code.AppendLineAt(
            4,
            $"var {variable} = {state}.IsPresent && {state}.Value is {dictionaryType} {direct} && global::System.Object.Equals({direct}.Comparer, {comparer}) ? {direct} : null;"
        );
        code.AppendLineAt(4, $"if ({variable} is null)");
        code.AppendLineAt(4, "{");
        var source = "__" + variable + "Source";
        code.AppendLineAt(
            5,
            $"var {source} = {state}.IsPresent ? ({interfaceType}?){state}.Value : null;"
        );
        code.AppendLineAt(
            5,
            $"{variable} = new {dictionaryType}({source}?.Count ?? 0, {comparer});"
        );
        code.AppendLineAt(
            5,
            $"if ({source} is not null) foreach (var kv in {source}) {variable}[kv.Key] = kv.Value;"
        );
        code.AppendLineAt(4, "}");
    }
}
