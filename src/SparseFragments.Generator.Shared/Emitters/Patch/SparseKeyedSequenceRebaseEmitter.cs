namespace SparseFragments.Generator.Shared;

/// <summary>Emits keyed-sequence Rebase algebra including the per-key three-way merge.</summary>
internal static class SparseKeyedSequenceRebaseEmitter
{
    internal static void EmitKeyedRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string facade,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var resultType = dialect.RebaseResult(patchName);
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        code.AppendLineAt(
            3,
            "public static "
                + resultType
                + " Rebase("
                + runtime
                + "Optional<"
                + listType
                + "> baseState, "
                + patchName
                + " local, "
                + runtime
                + "Optional<"
                + listType
                + "> currentState)"
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
        code.AppendLineAt(4, runtime + "Optional<" + listType + "> desired;");
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
        code.AppendLineAt(
            4,
            "if (local.__whole.Kind != " + runtime + "FragmentOperationKind.Keep)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if ("
                + facade
                + ".AreEqual((object?)baseState.Value, (object?)currentState.Value)) { result.__whole = local.__whole; return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLineAt(
            5,
            "var desiredState = desired; if ("
                + facade
                + ".AreEqual((object?)desiredState.Value, (object?)currentState.Value)) return "
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
                + "Optional<object?>.Missing, \"The whole collection conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(5, "return new " + resultType + "(result, conflicts);");
        code.AppendLineAt(4, "}");
        // Granular: if base == current keep local; if desired == current drop; else per-key merge with nested rebase where possible.
        // NOTE: desired is computed defensively below (missing/duplicate base yields a conflict, not a throw).
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreEqual((object?)baseState.Value, (object?)currentState.Value)) { result.__added = local.__added is null ? null : new global::System.Collections.Generic.List<"
                + elementType
                + ">(local.__added); result.__removed = local.__removed is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__removed); result.__edited = local.__edited is null ? null : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + (hasPatch ? SparseKeyedCollectionEmitter.ElementPatchType(member) : elementType)
                + ">(local.__edited, "
                + comparer
                + "); result.__order = local.__order is null ? null : new global::System.Collections.Generic.List<"
                + keyType
                + ">(local.__order); return new "
                + resultType
                + "(result, conflicts); }"
        );
        code.AppendLineAt(
            4,
            "if (desired.IsPresent == currentState.IsPresent && "
                + facade
                + ".AreEqual((object?)desired.Value, (object?)currentState.Value)) return "
                + resultType
                + ".Success(new "
                + patchName
                + "());"
        );
        EmitKeyedRebasePerKey(
            code,
            member,
            elementType,
            keyType,
            hasPatch,
            comparer,
            facade,
            dialect
        );
        code.AppendLineAt(4, "return new " + resultType + "(result, conflicts);");
        code.AppendLineAt(3, "}");
    }

    internal static void EmitKeyedRebasePerKey(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        bool hasPatch,
        string comparer,
        string facade,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var conflictType = dialect.ConflictType;
        var conflictKindType = dialect.ConflictKindType;
        // Build maps. Capacity hints use a netstandard2.0-safe
        // ICollection/IReadOnlyCollection probe (0 when the member shape
        // exposes no Count); duplicate-key validation is unchanged.
        code.AppendLineAt(
            4,
            "var __baseCapacity = baseState.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __baseCollection ? __baseCollection.Count : (baseState.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __baseReadOnly ? __baseReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var baseMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__baseCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); if (!baseMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); }"
        );
        code.AppendLineAt(
            4,
            "var __currentCapacity = currentState.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __currentCollection ? __currentCollection.Count : (currentState.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __currentReadOnly ? __currentReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var currentMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__currentCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); if (!currentMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); }"
        );
        code.AppendLineAt(
            4,
            "var __desiredCapacity = desired.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __desiredCollection ? __desiredCollection.Count : (desired.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __desiredReadOnly ? __desiredReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var desiredMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__desiredCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); if (!desiredMap.TryAdd(k, item)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\"); }"
        );
        // Pre-index locally touched keys once (O(K)) so the per-key loop below
        // resolves touches with O(1) comparer-correct lookups instead of O(K)
        // List.Exists scans per union key (former O(N x K) behavior). For valid
        // patches (unique added/removed keys, as enforced by Add/Remove and by
        // Between/Compose construction) the maps below resolve exactly the same
        // keys as the scans did.
        code.AppendLineAt(
            4,
            "var __touchedAdded = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var __addedByKey = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">((local.__added is null) ? 0 : local.__added.Count, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (local.__added is not null) foreach (var __touchedItem in local.__added) { var __touchedKey = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__touchedItem); __touchedAdded.Add(__touchedKey); __addedByKey[__touchedKey] = __touchedItem; }"
        );
        code.AppendLineAt(
            4,
            "var __touchedRemoved = (local.__removed is null) ? new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ") : new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__removed, "
                + comparer
                + ");"
        );
        // Seed from edited keys for an exact capacity hint, then merge other local touches.
        // Removing keys while scanning base/current/desired preserves first-occurrence
        // key identity and conflict order without allocating an O(N) union.
        code.AppendLineAt(
            4,
            "var __pendingKeys = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(local.__edited is null ? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)__touchedAdded : local.__edited.Keys, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (local.__edited is not null) __pendingKeys.UnionWith(__touchedAdded);"
        );
        code.AppendLineAt(4, "__pendingKeys.UnionWith(__touchedRemoved);");
        if (hasPatch)
        {
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            var prefix = SparseKeyedCollectionEmitter.ElementPatchPrefix(member);
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopStart(code);
            code.AppendLineAt(
                5,
                "var inBase = baseMap.TryGetValue(k, out var b); var inCurrent = currentMap.TryGetValue(k, out var c); var inDesired = desiredMap.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqualsCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual((object?)b, (object?)c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqualsCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual((object?)d, (object?)c));"
            );
            code.AppendLineAt(5, "if (baseEqualsCurrent)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (__touchedRemoved.Contains(k)) { (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); }"
            );
            code.AppendLineAt(
                6,
                "else if (__addedByKey.TryGetValue(k, out var __rebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).Add(__rebasedAdded);"
            );
            code.AppendLineAt(
                6,
                "if (local.__edited is not null && local.__edited.TryGetValue(k, out var edit) && !edit.__SparseIsEmpty()) { (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = edit; }"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else if (desiredEqualsCurrent) { }");
            code.AppendLineAt(
                5,
                "else if (inBase && inCurrent && inDesired && local.__edited is not null && local.__edited.TryGetValue(k, out var localEdit))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var nested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Rebase("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(b!)), localEdit, "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(c!)));"
            );
            code.AppendLineAt(
                6,
                "if (!nested.HasConflicts && !nested.Patch.__SparseIsEmpty()) (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = nested.Patch;"
            );
            code.AppendLineAt(
                6,
                "else foreach (var nc in nested.Conflicts) conflicts.Add(nc.WithPathPrefix(((object?)k)?.ToString() ?? \"<null>\"));"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "conflicts.Add(new "
                    + conflictType
                    + "(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The keyed element conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(5, "}");
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopEnd(code);
            // Order: keep local order only when current order unchanged and no order conflict.
            code.AppendLineAt(4, "if (local.__order is not null && local.__order.Count > 0)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var baseOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) baseOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var currentOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) currentOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var desiredOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) desiredOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "if ("
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(baseOrder, currentOrder)) result.__order = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(local.__order);"
            );
            code.AppendLineAt(
                5,
                "else if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(desiredOrder, currentOrder)) conflicts.Add(new "
                    + conflictType
                    + "(new string[] { \"order\" }, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)baseOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)desiredOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)currentOrder), \"The collection order conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(4, "}");
        }
        else
        {
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopStart(code);
            code.AppendLineAt(
                5,
                "var inBase = baseMap.TryGetValue(k, out var b); var inCurrent = currentMap.TryGetValue(k, out var c); var inDesired = desiredMap.TryGetValue(k, out var d);"
            );
            code.AppendLineAt(
                5,
                "bool baseEqualsCurrent = inBase == inCurrent && (!inBase || "
                    + facade
                    + ".AreEqual((object?)b, (object?)c));"
            );
            code.AppendLineAt(
                5,
                "bool desiredEqualsCurrent = inDesired == inCurrent && (!inDesired || "
                    + facade
                    + ".AreEqual((object?)d, (object?)c));"
            );
            code.AppendLineAt(5, "if (baseEqualsCurrent)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (__touchedRemoved.Contains(k)) { (result.__removed ??= new global::System.Collections.Generic.List<"
                    + keyType
                    + ">()).Add(k); }"
            );
            code.AppendLineAt(
                6,
                "else if (__addedByKey.TryGetValue(k, out var __rebasedAdded)) (result.__added ??= new global::System.Collections.Generic.List<"
                    + elementType
                    + ">()).Add(__rebasedAdded);"
            );
            code.AppendLineAt(
                6,
                "else if (local.__edited is not null && local.__edited.TryGetValue(k, out var upd)) { (result.__edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + "))[k] = upd; }"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else if (!desiredEqualsCurrent)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "conflicts.Add(new "
                    + conflictType
                    + "(new string[] { ((object?)k)?.ToString() ?? \"<null>\" }, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)b), "
                    + runtime
                    + "Optional<object?>.Present((object?)d), "
                    + runtime
                    + "Optional<object?>.Present((object?)c), \"The keyed element conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(5, "}");
            SparseKeyedCollectionEmitter.AppendPendingKeyLoopEnd(code);
            code.AppendLineAt(4, "if (local.__order is not null && local.__order.Count > 0)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var baseOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (baseState.IsPresent && (object?)baseState.Value is not null) foreach (var item in baseState.Value!) baseOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var currentOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (currentState.IsPresent && (object?)currentState.Value is not null) foreach (var item in currentState.Value!) currentOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "var desiredOrder = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(); if (desired.IsPresent && (object?)desired.Value is not null) foreach (var item in desired.Value!) desiredOrder.Add("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(item));"
            );
            code.AppendLineAt(
                5,
                "if ("
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(baseOrder, currentOrder)) result.__order = new global::System.Collections.Generic.List<"
                    + keyType
                    + ">(local.__order);"
            );
            code.AppendLineAt(
                5,
                "else if (!"
                    + facade
                    + ".KeyOrderEquals<"
                    + keyType
                    + ">(desiredOrder, currentOrder)) conflicts.Add(new "
                    + conflictType
                    + "(new string[] { \"order\" }, "
                    + conflictKindType
                    + ".Nested, "
                    + runtime
                    + "Optional<object?>.Present((object?)baseOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)desiredOrder), "
                    + runtime
                    + "Optional<object?>.Present((object?)currentOrder), \"The collection order conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(4, "}");
        }
    }
}
