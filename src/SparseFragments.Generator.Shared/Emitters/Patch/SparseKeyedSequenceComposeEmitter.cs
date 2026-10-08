namespace SparseFragments.Generator.Shared;

/// <summary>Emits keyed-sequence Compose algebra.</summary>
internal static class SparseKeyedSequenceComposeEmitter
{
    internal static void EmitKeyedCompose(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string patchName,
        string elementType,
        string keyType,
        string listType,
        bool hasPatch,
        string comparer,
        string runtime
    )
    {
        var kind = runtime + "FragmentOperationKind";
        var operation = runtime + "FragmentOperation<" + listType + ">";
        code.AppendLineAt(3, "public " + patchName + " Compose(" + patchName + " next)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(4, "var result = new " + patchName + "();");
        code.AppendLineAt(4, "if (next.__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "result.__whole = next.__whole;");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "if (__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__whole.Kind == "
                + kind
                + ".Remove) throw new global::System.InvalidOperationException(\"Cannot compose granular operations after a whole Remove.\");"
        );
        code.AppendLineAt(
            5,
            "var applied = next.Apply("
                + runtime
                + "Optional<"
                + listType
                + ">.Present(__whole.Value));"
        );
        code.AppendLineAt(5, "result.__whole = " + operation + ".Set(applied.Value!);");
        code.AppendLineAt(5, "return result;");
        code.AppendLineAt(4, "}");
        // Granular + granular: merge key operation logs preserving sequential semantics.
        // Added keys.
        code.AppendLineAt(
            4,
            "var thisAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (__added is not null) foreach (var item in __added) thisAdded["
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item)] = item;"
        );
        code.AppendLineAt(
            4,
            "var nextAdded = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + elementType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "if (next.__added is not null) foreach (var item in next.__added) nextAdded["
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item)] = item;"
        );
        code.AppendLineAt(
            4,
            "var thisRemoved = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(("
                + "global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)"
                + "(__removed ?? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)new "
                + keyType
                + "[0]), "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "var nextRemoved = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">(("
                + "global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)"
                + "(next.__removed ?? (global::System.Collections.Generic.IEnumerable<"
                + keyType
                + ">)new "
                + keyType
                + "[0]), "
                + comparer
                + ");"
        );
        // Net removals: removed by either, minus re-added later. netRemoved keeps
        // insertion order (this removals, then next removals) while
        // __netRemovedSet gives O(1) comparer-correct dedup instead of the
        // former O(K^2) Enumerable.Contains scan per candidate key.
        code.AppendLineAt(
            4,
            "var netRemoved = new global::System.Collections.Generic.List<"
                + keyType
                + ">(thisRemoved.Count + nextRemoved.Count);"
        );
        code.AppendLineAt(
            4,
            "var __netRemovedSet = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in thisRemoved) if (!nextAdded.ContainsKey(k) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        code.AppendLineAt(
            4,
            "foreach (var k in nextRemoved) if ((!thisAdded.ContainsKey(k) || thisRemoved.Contains(k)) && __netRemovedSet.Add(k)) netRemoved.Add(k);"
        );
        // Net adds: this-added surviving next-removal (with next edits applied), plus next-added.
        code.AppendLineAt(
            4,
            "var netAdded = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        if (hasPatch)
        {
            var elementFragment = SparseKeyedCollectionEmitter.ElementFragmentType(member);
            code.AppendLineAt(4, "foreach (var kv in thisAdded)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var nextEdit))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "var applied = nextEdit.Apply("
                    + runtime
                    + "Optional<"
                    + elementFragment
                    + "?>.Present("
                    + elementFragment
                    + ".From(kv.Value)));"
            );
            code.AppendLineAt(
                6,
                "if (!applied.IsPresent || applied.Value is null) throw new global::System.InvalidOperationException(\"Element edit removed the element. Use Remove instead.\");"
            );
            code.AppendLineAt(
                6,
                "if (!"
                    + comparer
                    + ".Equals("
                    + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                    + "(applied.Value!.ToModel()), kv.Key)) throw new global::System.InvalidOperationException(\"Changing an element's identity through an edit is not allowed. Use remove-old + add-new instead.\");"
            );
            code.AppendLineAt(6, "netAdded.Add(applied.Value!.ToModel());");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else netAdded.Add(kv.Value);");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLineAt(
                4,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">? netEdited = null;"
            );
            code.AppendLineAt(4, "if (__edited is not null) foreach (var kv in __edited)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(5, "if (nextAdded.ContainsKey(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var n2) && !n2.__SparseIsEmpty()) { var __c = kv.Value.Compose(n2); if (__c.__SparseIsEmpty()) { netEdited?.Remove(kv.Key); } else (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = __c; }"
            );
            code.AppendLineAt(
                5,
                "else if ((next.__edited is null || !next.__edited.ContainsKey(kv.Key)) && !kv.Value.__SparseIsEmpty()) (netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited)"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue;"
            );
            code.AppendLineAt(5, "if (kv.Value.__SparseIsEmpty()) continue;");
            code.AppendLineAt(
                5,
                "if (netEdited is not null && netEdited.ContainsKey(kv.Key)) continue;"
            );
            code.AppendLineAt(
                5,
                "(netEdited ??= new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + SparseKeyedCollectionEmitter.ElementPatchType(member)
                    + ">("
                    + comparer
                    + "))[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLineAt(4, "if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLineAt(
                4,
                "if (netEdited is not null && netEdited.Count > 0) result.__edited = netEdited;"
            );
        }
        else
        {
            code.AppendLineAt(4, "foreach (var kv in thisAdded)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (nextRemoved.Contains(kv.Key)) continue;");
            code.AppendLineAt(
                5,
                "if (next.__edited is not null && next.__edited.TryGetValue(kv.Key, out var upd)) netAdded.Add(upd); else netAdded.Add(kv.Value);"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (next.__added is not null) foreach (var item in next.__added) netAdded.Add(item);"
            );
            code.AppendLineAt(
                4,
                "var netEdited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                4,
                "if (__edited is not null) foreach (var kv in __edited) if (!nextRemoved.Contains(kv.Key) && !nextAdded.ContainsKey(kv.Key)) netEdited[kv.Key] = kv.Value;"
            );
            code.AppendLineAt(
                4,
                "if (next.__edited is not null) foreach (var kv in next.__edited) { if (thisAdded.ContainsKey(kv.Key) || thisRemoved.Contains(kv.Key)) continue; netEdited[kv.Key] = kv.Value; }"
            );
            code.AppendLineAt(4, "if (netRemoved.Count > 0) result.__removed = netRemoved;");
            code.AppendLineAt(4, "if (netAdded.Count > 0) result.__added = netAdded;");
            code.AppendLineAt(4, "if (netEdited.Count > 0) result.__edited = netEdited;");
        }

        // Order: next order wins when present (filtered to net keys), else this order filtered.
        code.AppendLineAt(4, "if (next.__order is not null && next.__order.Count > 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __nextOrder = new global::System.Collections.Generic.List<"
                + keyType
                + ">(); foreach (var k in next.__order) if (!netRemoved.Contains(k)) __nextOrder.Add(k);"
        );
        code.AppendLineAt(
            5,
            "if (next.__added is not null) foreach (var item in next.__added) { var __ak = "
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item); if (!__nextOrder.Contains(__ak, "
                + comparer
                + ")) __nextOrder.Add(__ak); }"
        );
        code.AppendLineAt(5, "if (__nextOrder.Count > 0) result.__order = __nextOrder;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__order is not null && __order.Count > 0)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var order = new global::System.Collections.Generic.List<" + keyType + ">();"
        );
        code.AppendLineAt(
            5,
            "foreach (var k in __order) if (!nextRemoved.Contains(k)) order.Add(k);"
        );
        code.AppendLineAt(
            5,
            "if (next.__added is not null) foreach (var item in next.__added) order.Add("
                + SparseKeyedCollectionEmitter.KeyOfMethod(member)
                + "(item));"
        );
        code.AppendLineAt(5, "if (order.Count > 0) result.__order = order;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return result;");
        code.AppendLineAt(3, "}");
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
