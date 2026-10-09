namespace SparseFragments.Generator.Shared;

/// <summary>Emits an index into the ordered dictionary removal list.</summary>
internal static class SparseDictionaryRemovalIndexEmitter
{
    internal static void Emit(SharedIndentedBuilder code, string keyType, string comparer)
    {
        code.AppendLineAt(3, "private void __SparseCancelRemoval(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "for (int index = __removed!.Count - 1; index >= 0; index--)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (!" + comparer + ".Equals(__removed[index], key)) continue;");
        code.AppendLineAt(5, "__removed.RemoveAt(index);");
        code.AppendLineAt(5, "__removedLookup = null;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private int __SparseRemovedSlot(" + keyType + " key, int[] slots)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "int mask = slots.Length - 1;");
        code.AppendLineAt(4, "int slot = " + comparer + ".GetHashCode(key!) & mask;");
        code.AppendLineAt(
            4,
            "while (slots[slot] != 0 && !" + comparer + ".Equals(__removed![slots[slot] - 1], key))"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "slot = (slot + 1) & mask;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return slot;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private bool __SparseContainsRemoved(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__removedLookup is not null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "return __removedLookup[__SparseRemovedSlot(key, __removedLookup)] != 0;"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "foreach (var existing in __removed!)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (" + comparer + ".Equals(existing, key)) return true;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return false;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private void __SparseRebuildRemovedIndex(int capacity)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var slots = new int[capacity];");
        code.AppendLineAt(4, "for (int index = 0; index < __removed!.Count; index++)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "slots[__SparseRemovedSlot(__removed[index], slots)] = index + 1;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__removedLookup = slots;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "internal void __SparseReserveRemovals(int count)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (count <= 0) return;");
        code.AppendLineAt(
            4,
            "__removed = new global::System.Collections.Generic.List<" + keyType + ">(count);"
        );
        code.AppendLineAt(4, "if (count >= 32)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "int capacity = 64;");
        code.AppendLineAt(5, "while (capacity < checked(count * 2))");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "capacity = checked(capacity * 2);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "__SparseRebuildRemovedIndex(capacity);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        EmitIndexedAdd(code, keyType);
    }

    internal static void EmitAdd(
        SharedIndentedBuilder code,
        string comparer,
        bool keepReservedIndex
    )
    {
        var condition = keepReservedIndex
            ? "__removedLookup is not null || __removed!.Count >= 32"
            : "__removed!.Count >= 32";
        code.AppendLineAt(4, "if (" + condition + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__SparseAddIndexedRemoval(key);");
        code.AppendLineAt(5, "return;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "foreach (var existing in __removed!) if ("
                + comparer
                + ".Equals(existing, key)) return;"
        );
        code.AppendLineAt(4, "__removed.Add(key);");
        if (!keepReservedIndex)
        {
            code.AppendLineAt(4, "__removedLookup = null;");
        }
    }

    private static void EmitIndexedAdd(SharedIndentedBuilder code, string keyType)
    {
        code.AppendLineAt(3, "private void __SparseAddIndexedRemoval(" + keyType + " key)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__removedLookup is null)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "int capacity = 64;");
        code.AppendLineAt(5, "while (capacity <= checked(__removed!.Count * 2))");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "capacity = checked(capacity * 2);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "__SparseRebuildRemovedIndex(capacity);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "int __slot = __SparseRemovedSlot(key, __removedLookup!);");
        code.AppendLineAt(4, "if (__removedLookup![__slot] != 0) return;");
        code.AppendLineAt(4, "if (__removed!.Count >= __removedLookup.Length / 2)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "__SparseRebuildRemovedIndex(checked(__removedLookup.Length * 2));");
        code.AppendLineAt(5, "__slot = __SparseRemovedSlot(key, __removedLookup!);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__removed!.Add(key);");
        code.AppendLineAt(4, "__removedLookup![__slot] = __removed.Count;");
        code.AppendLineAt(3, "}");
    }
}
