namespace SparseFragments.Generator.Shared;

internal static class SparseKeyedRemovalIndexEmitter
{
    internal static void Emit(SharedIndentedBuilder code, string keyType, string comparer)
    {
        code.AppendLineAt(
            3,
            "private sealed class __SparseIndexedRemovals : global::System.Collections.Generic.List<"
                + keyType
                + ">"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "internal int[]? Slots;");
        code.AppendLineAt(
            4,
            "internal __SparseIndexedRemovals(global::System.Collections.Generic.List<"
                + keyType
                + "> source, int[] slots) : base(source.Capacity)"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "AddRange(source);");
        code.AppendLineAt(5, "Slots = slots;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private int[]? __removedLookup");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "get => (__removed as __SparseIndexedRemovals)?.Slots;");
        code.AppendLineAt(4, "set");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (__removed is __SparseIndexedRemovals indexed)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "indexed.Slots = value;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (value is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__removed = new __SparseIndexedRemovals(__removed!, value);");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
        SparseDictionaryRemovalIndexEmitter.Emit(code, keyType, comparer);
    }
}
