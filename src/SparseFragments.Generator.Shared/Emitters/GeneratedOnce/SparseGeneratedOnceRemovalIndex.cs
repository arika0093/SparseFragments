using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Builds the compilation-scoped ordered removal-index source (#183).</summary>
/// <remarks>
/// Ports the ordered-removal bookkeeping out of
/// <c>SparseDictionaryRemovalIndexEmitter</c> verbatim: the removal list,
/// optional indexed hash slots, cancellation, de-duplication and index
/// capacity management. The mechanics depend only on the key type and its
/// default equality comparer, not the containing model, so one generic
/// helper serves dictionary and keyed-sequence patches. Per-member patch
/// storage keeps its sparse/lazy fields and calls these kernels.
/// </remarks>
internal static class SparseGeneratedOnceRemovalIndex
{
    /// <summary>Builds the shared removal-index source for the explicit namespace.</summary>
    /// <param name="implementationNamespace">Explicit generated namespace.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Compilation-scoped source text.</returns>
    public static string BuildSource(
        string implementationNamespace,
        CancellationToken cancellationToken
    )
    {
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(
            code,
            implementationNamespace,
            cancellationToken
        );
        var comparer = "global::System.Collections.Generic.EqualityComparer<TKey>.Default";
        code.AppendLineAt(
            1,
            "/// <summary>Ordered removal-index kernels shared by keyed patch types.</summary>"
        );
        code.AppendLineAt(
            1,
            "internal static class " + SparseGeneratedOnceNames.RemovalIndex + "<TKey>"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "internal static void CancelRemoval(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, TKey key)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "for (int index = removed.Count - 1; index >= 0; index--)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (!" + comparer + ".Equals(removed[index], key)) continue;");
        code.AppendLineAt(4, "removed.RemoveAt(index);");
        code.AppendLineAt(4, "lookup = null;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static int RemovedSlot(global::System.Collections.Generic.List<TKey> removed, TKey key, int[] slots)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "int mask = slots.Length - 1;");
        code.AppendLineAt(3, "int slot = " + comparer + ".GetHashCode(key!) & mask;");
        code.AppendLineAt(
            3,
            "while (slots[slot] != 0 && !" + comparer + ".Equals(removed[slots[slot] - 1], key))"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "slot = (slot + 1) & mask;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return slot;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static bool ContainsRemoved(global::System.Collections.Generic.List<TKey> removed, int[]? lookup, TKey key)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var slots = lookup;");
        code.AppendLineAt(3, "if (slots is not null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "return slots[RemovedSlot(removed, key, slots)] != 0;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "foreach (var existing in removed)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (" + comparer + ".Equals(existing, key)) return true;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static void RebuildIndex(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, int capacity)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var slots = new int[capacity];");
        code.AppendLineAt(3, "for (int index = 0; index < removed.Count; index++)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "slots[RemovedSlot(removed, removed[index], slots)] = index + 1;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "lookup = slots;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static void ReserveRemovals(ref global::System.Collections.Generic.List<TKey>? removed, ref int[]? lookup, int count)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (count <= 0) return;");
        code.AppendLineAt(3, "removed = new global::System.Collections.Generic.List<TKey>(count);");
        code.AppendLineAt(3, "if (count >= 32)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "int capacity = 64;");
        code.AppendLineAt(4, "while (capacity < checked(count * 2))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "capacity = checked(capacity * 2);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "RebuildIndex(removed, ref lookup, capacity);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "internal static void AddRemoval(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, TKey key, bool keepReservedIndex)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var reserved = keepReservedIndex && lookup is not null;");
        code.AppendLineAt(3, "if (reserved || removed.Count >= 32)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "AddIndexedRemoval(removed, ref lookup, key);");
        code.AppendLineAt(4, "return;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "foreach (var existing in removed) if (" + comparer + ".Equals(existing, key)) return;"
        );
        code.AppendLineAt(3, "removed.Add(key);");
        code.AppendLineAt(3, "if (!keepReservedIndex)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "lookup = null;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "private static void AddIndexedRemoval(global::System.Collections.Generic.List<TKey> removed, ref int[]? lookup, TKey key)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var slots = lookup;");
        code.AppendLineAt(3, "if (slots is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "int capacity = 64;");
        code.AppendLineAt(4, "while (capacity <= checked(removed.Count * 2))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "capacity = checked(capacity * 2);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "RebuildIndex(removed, ref lookup, capacity);");
        code.AppendLineAt(4, "slots = lookup!;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "int __slot = RemovedSlot(removed, key, slots);");
        code.AppendLineAt(3, "if (slots[__slot] != 0) return;");
        code.AppendLineAt(3, "if (removed.Count >= slots.Length / 2)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "RebuildIndex(removed, ref lookup, checked(slots.Length * 2));");
        code.AppendLineAt(4, "slots = lookup!;");
        code.AppendLineAt(4, "__slot = RemovedSlot(removed, key, slots);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "removed.Add(key);");
        code.AppendLineAt(3, "slots[__slot] = removed.Count;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(1, "}");
        code.AppendLine("}");
        return code.ToString();
    }
}
