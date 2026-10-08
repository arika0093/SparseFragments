namespace SparseFragments.Generator.Shared;

/// <summary>Emits keyed-sequence Apply and Between algebra.</summary>
internal static class SparseKeyedSequenceApplyEmitter
{
    internal static void EmitKeyedApply(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string runtime
    )
    {
        var optionalList = runtime + "Optional<" + listType + ">";
        var kind = runtime + "FragmentOperationKind";
        code.AppendLineAt(3, "public " + optionalList + " Apply(" + optionalList + " current)");
        code.AppendLineAt(3, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "if (current.IsPresent && (object?)current.Value is not null) foreach (var __baselineItem in current.Value!) if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__baselineItem)"
                    )
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLineAt(
            4,
            "if (__whole.Kind != " + kind + ".Keep) return __whole.Apply(current);"
        );
        code.AppendLineAt(4, "if (IsEmpty) return current;");
        code.AppendLineAt(
            4,
            "if (!current.IsPresent || (object?)current.Value is null) throw new global::System.InvalidOperationException(\"Cannot apply granular collection operations to a missing collection.\");"
        );
        code.AppendLineAt(4, "var source = current.Value!;");
        code.AppendLineAt(
            4,
            "var map = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(4, "foreach (var item in source)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLineAt(5, SparseKeyedCollectionEmitter.AddUniqueEntry("map", "k", "item"));
        code.AppendLineAt(4, "}");
        // Removals.
        code.AppendLineAt(
            4,
            "if (__removed is not null) foreach (var k in __removed) map.Remove(k);"
        );
        // Edits.
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!map.TryGetValue(kv.Key, out var existing)) throw new global::System.InvalidOperationException(\"Cannot edit a missing element.\");"
            );
            code.AppendLineAt(
                5,
                "var applied = kv.Value.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(existing)));"
            );
            code.AppendLineAt(
                5,
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(5, "var updated = applied.Value!.ToModel();");
            code.AppendLineAt(
                5,
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(updated), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(5, "map[kv.Key] = updated;");
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (!map.ContainsKey(kv.Key)) throw new global::System.InvalidOperationException(\"Cannot update a missing element.\");"
            );
            code.AppendLineAt(
                5,
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(kv.Value), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an update is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(5, "map[kv.Key] = kv.Value;");
            code.AppendLineAt(4, "}");
        }

        // Adds.
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                4,
                "var __unassignedAdded = new global::System.Collections.Generic.List<"
                    + elementType
                    + ">();"
            );
        code.AppendLineAt(4, "if (__added is not null) foreach (var item in __added)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { __unassignedAdded.Add(item); continue; }"
            );
        }
        code.AppendLineAt(
            5,
            "if (map.ContainsKey(k)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
        );
        code.AppendLineAt(5, "map[k] = item;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "global::System.Collections.Generic.List<" + elementType + "> result;"
        );
        code.AppendLineAt(4, "if (__order is not null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__order.Count != map.Count"
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? " + __unassignedAdded.Count"
                        : ""
                )
                + ") throw new global::System.InvalidOperationException(\"Order must list exactly the final keys.\");"
        );
        code.AppendLineAt(
            5,
            "result = new global::System.Collections.Generic.List<" + elementType + ">(map.Count);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(5, "var __unassignedIndex = 0;");
        code.AppendLineAt(5, "foreach (var k in __order)");
        code.AppendLineAt(5, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                6,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { if (__unassignedIndex >= __unassignedAdded.Count) throw new global::System.InvalidOperationException(\"Order contains too many unassigned-key entries.\"); result.Add(__unassignedAdded[__unassignedIndex++]); continue; }"
            );
        }
        code.AppendLineAt(
            6,
            "if (!map.TryGetValue(k, out var item)) throw new global::System.InvalidOperationException(\"Order lists an unknown key.\");"
        );
        code.AppendLineAt(6, "result.Add(item);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        // The final key set is exactly map.Keys (source minus removals plus adds),
        // so map.Count is the exact result capacity: no re-enumeration of source
        // via Enumerable.Count. The added loop keeps its duplicate guard but
        // resolves it through an O(1) comparer-correct set instead of the former
        // O(N) result.Exists scan per added element (former O(N x K) behavior).
        // The set is built only when adds exist, so pure edit/remove applies pay
        // nothing extra; for valid patches the guard never fires because Apply
        // already rejected added keys that collide with the map.
        code.AppendLineAt(
            5,
            "result = new global::System.Collections.Generic.List<" + elementType + ">(map.Count);"
        );
        code.AppendLineAt(5, "foreach (var item in source)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);"
        );
        code.AppendLineAt(6, "if (map.TryGetValue(k, out var current2)) result.Add(current2);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "if (__added is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __emitted = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            6,
            "foreach (var __placed in result) __emitted.Add("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(__placed));"
        );
        code.AppendLineAt(
            6,
            "foreach (var item in __added) { var k = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); "
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? "if ("
                            + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                            + ") continue; "
                        : ""
                )
                + "if (__emitted.Add(k)) result.Add(map[k]); }"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(6, "result.AddRange(__unassignedAdded);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return "
                + SparseKeyedCollectionEmitter.MaterializeSequence(member, "result", runtime)
                + ";"
        );
        code.AppendLineAt(3, "}");
    }

    internal static void EmitKeyedBetween(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string facade,
        string runtime
    )
    {
        var optionalList = runtime + "Optional<" + listType + ">";
        var operation = runtime + "FragmentOperation<" + listType + ">";
        code.AppendLineAt(
            3,
            "public static "
                + patchName
                + " Between("
                + optionalList
                + " before, "
                + optionalList
                + " after)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var patch = new " + patchName + "();");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                4,
                "if (before.IsPresent && (object?)before.Value is not null) foreach (var __baselineItem in before.Value!) if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(
                        member,
                        SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(__baselineItem)"
                    )
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLineAt(4, "if (before.IsPresent != after.IsPresent)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "patch.__whole = after.IsPresent ? "
                + operation
                + ".Set(after.Value) : "
                + operation
                + ".Remove;"
        );
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (!before.IsPresent) return patch;");
        code.AppendLineAt(
            4,
            "if (global::System.Object.ReferenceEquals(before.Value, after.Value)) return patch;"
        );
        code.AppendLineAt(4, "if ((object?)before.Value is null || (object?)after.Value is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!"
                + facade
                + ".AreEqual((object?)before.Value, (object?)after.Value)) patch.__whole = "
                + operation
                + ".Set(after.Value);"
        );
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if ("
                + facade
                + ".AreEqual((object?)before.Value, (object?)after.Value)) return patch;"
        );
        // Build maps with duplicate detection. Each collection is enumerated exactly
        // once: the before pass collects the map and the order list together, and
        // the added pass reuses afterOrder/afterMap instead of re-enumerating
        // after.Value and recomputing keys. Capacity hints come from a
        // netstandard2.0-safe ICollection/IReadOnlyCollection probe (0 when the
        // member shape exposes no Count); duplicate-key validation and all
        // comparer/key semantics are unchanged.
        code.AppendLineAt(
            4,
            "var __beforeCapacity = before.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __beforeCollection ? __beforeCollection.Count : (before.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __beforeReadOnly ? __beforeReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var beforeMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__beforeCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var beforeOrder = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__beforeCapacity);"
        );
        code.AppendLineAt(4, "foreach (var item in before.Value!)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") throw new global::System.InvalidOperationException(\"An unassigned key cannot appear in the baseline of a keyed collection operation.\");"
            );
        }
        code.AppendLineAt(5, SparseKeyedCollectionEmitter.AddUniqueEntry("beforeMap", "k", "item"));
        code.AppendLineAt(5, "beforeOrder.Add(k);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "var __afterCapacity = after.GetValueOrDefault() is global::System.Collections.Generic.ICollection<"
                + elementType
                + "> __afterCollection ? __afterCollection.Count : (after.GetValueOrDefault() is global::System.Collections.Generic.IReadOnlyCollection<"
                + elementType
                + "> __afterReadOnly ? __afterReadOnly.Count : 0);"
        );
        code.AppendLineAt(
            4,
            "var afterMap = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">(__afterCapacity, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var afterOrder = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__afterCapacity);"
        );
        code.AppendLineAt(
            4,
            "var unassignedAfter = new global::System.Collections.Generic.List<"
                + elementType
                + ">();"
        );
        code.AppendLineAt(4, "foreach (var item in after.Value!)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var k = " + SparseKeyedCollectionEmitter.KeyOfMethod(member) + "(item);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { afterOrder.Add(k); unassignedAfter.Add(item); continue; }"
            );
        }
        code.AppendLineAt(5, SparseKeyedCollectionEmitter.AddUniqueEntry("afterMap", "k", "item"));
        code.AppendLineAt(5, "afterOrder.Add(k);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "var removed = new global::System.Collections.Generic.List<"
                + keyType
                + ">(beforeMap.Count);"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in beforeMap.Keys) if (!afterMap.ContainsKey(k)) removed.Add(k);"
        );
        // Added entries are derived from afterOrder/afterMap (after-order, no key
        // recomputation, no second enumeration of after.Value). The capacity is
        // the exact upper bound |after| - |before intersect after|.
        code.AppendLineAt(
            4,
            "var added = new global::System.Collections.Generic.List<"
                + elementType
                + ">(global::System.Math.Max(0, afterMap.Count - beforeMap.Count + removed.Count));"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(4, "var unassignedIndex = 0;");
        code.AppendLineAt(4, "foreach (var k in afterOrder)");
        code.AppendLineAt(4, "{");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                5,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") { added.Add(unassignedAfter[unassignedIndex++]); continue; }"
            );
        }
        code.AppendLineAt(5, "if (!beforeMap.ContainsKey(k)) added.Add(afterMap[k]);");
        code.AppendLineAt(4, "}");
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            var elementPatch = SparseKeyedCollectionEmitter.ElementPatchType(member);
            var prefix = SparseKeyedCollectionEmitter.ElementPatchPrefix(member);
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">? edited = null;"
            );
            code.AppendLineAt(4, "foreach (var k in beforeMap.Keys)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (!afterMap.TryGetValue(k, out var afterItem)) continue;");
            code.AppendLineAt(5, "var beforeItem = beforeMap[k];");
            code.AppendLineAt(
                5,
                "var nested = "
                    + elementPatch
                    + "."
                    + prefix
                    + "Between("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(beforeItem)), "
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(afterItem)));"
            );
            code.AppendLineAt(
                5,
                "if (!nested.__SparseIsEmpty()) (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementPatch
                    + ">("
                    + comparer
                    + "))[k] = nested;"
            );
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">? edited = null;"
            );
            code.AppendLineAt(4, "foreach (var k in beforeMap.Keys)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (!afterMap.TryGetValue(k, out var afterItem)) continue;");
            code.AppendLineAt(
                5,
                "if (!"
                    + facade
                    + ".AreEqual((object?)beforeMap[k], (object?)afterItem)) (edited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + "))[k] = afterItem;"
            );
            code.AppendLineAt(4, "}");
        }

        code.AppendLineAt(
            4,
            "if (removed.Count == 0 && added.Count == 0 && (edited is null || edited.Count == 0))"
        );
        code.AppendLineAt(4, "{");
        // Order-only change? Before/after equal as sets but order differs -> still need order patch.
        // beforeOrder was already collected in the single before pass above.
        code.AppendLineAt(
            5,
            "if ("
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(beforeOrder, afterOrder)) return patch;"
        );
        code.AppendLineAt(5, "patch.__order = afterOrder;");
        code.AppendLineAt(5, "return patch;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (removed.Count > 0) patch.__removed = removed;");
        code.AppendLineAt(4, "if (added.Count > 0) patch.__added = added;");
        code.AppendLineAt(
            4,
            "if (edited is not null && edited.Count > 0) patch.__edited = edited;"
        );
        code.AppendLineAt(
            4,
            "if (!"
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(beforeOrder, afterOrder)) patch.__order = afterOrder;"
        );
        code.AppendLineAt(4, "return patch;");
        code.AppendLineAt(3, "}");
    }
}
