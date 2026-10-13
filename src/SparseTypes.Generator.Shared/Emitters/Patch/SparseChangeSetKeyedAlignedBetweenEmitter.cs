using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

internal static class SparseChangeSetKeyedAlignedBetweenEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string transition,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var key = KeyTypeOf(member);
        var element = ElementTypeOf(member);
        var items = "global::System.Collections.Generic.List<" + transition + ".Item>";
        var order = "global::System.Collections.Generic.List<" + key + ">";
        var sequence = "global::System.Collections.Generic.IEnumerable<" + element + ">";
        var indexed = "global::System.Collections.Generic.IReadOnlyList<" + element + ">";
        var keyOf = "__SparseKeyOf_ChangeSet_" + member.Id;
        var name = "__tryAligned" + member.Id;
        code.AppendLineAt(
            5,
            "static bool "
                + name
                + "("
                + sequence
                + " sourceBefore, "
                + sequence
                + " sourceAfter, out "
                + items
                + "? items, out "
                + order
                + "? order)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "items = null; order = null;");
        code.AppendLineAt(
            6,
            "if (sourceBefore is not "
                + indexed
                + " b || sourceAfter is not "
                + indexed
                + " a) return false;"
        );
        code.AppendLineAt(6, "var count = b.Count;");
        code.AppendLineAt(6, "if (count != a.Count) return false;");
        code.AppendLineAt(6, "if (count == 0) return true;");
        code.AppendLineAt(
            6,
            "var comparer = global::System.Collections.Generic.EqualityComparer<"
                + key
                + ">.Default;"
        );
        // Reject nonaligned inputs before allocating or comparing element values.
        code.AppendLineAt(6, "for (var i = 0; i < count; i++)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "if ((object?)b[i] is null || (object?)a[i] is null) return false;");
        code.AppendLineAt(7, "var k = " + keyOf + "(b[i]);");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                7,
                "if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "k")
                    + ") return false;"
            );
        code.AppendLineAt(7, "if (!comparer.Equals(k, " + keyOf + "(a[i]))) return false;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "var keys = new " + order + "(count);");
        if (dialect.KeyValidationScopeFactory is not null)
        {
            code.AppendLineAt(
                6,
                "static void ValidateKeys(" + indexed + " b, " + order + " keys, int count)"
            );
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "using (var keyScope = "
                    + dialect.KeyValidationScopeFactory
                    + "<"
                    + key
                    + ">(count, static capacity => new global::System.Collections.Generic.HashSet<"
                    + key
                    + ">("
                    + (dialect.HashSetSupportsCapacity ? "capacity, " : "")
                    + "global::System.Collections.Generic.EqualityComparer<"
                    + key
                    + ">.Default)))"
            );
            code.AppendLineAt(7, "{");
            code.AppendLineAt(8, "var seen = keyScope.Keys;");
            AppendValidation(code, keyOf, true, 8);
            code.AppendLineAt(7, "}");
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "ValidateKeys(b, keys, count);");
        }
        else
        {
            var seenType = dialect.HashSetSupportsCapacity
                ? "global::System.Collections.Generic.HashSet<" + key + ">"
                : "global::System.Collections.Generic.Dictionary<" + key + ", byte>";
            code.AppendLineAt(6, "var seen = new " + seenType + "(count, comparer);");
            AppendValidation(code, keyOf, dialect.HashSetSupportsCapacity, 6);
        }
        code.AppendLineAt(6, "for (var i = 0; i < count; i++)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "var edit = " + ElementChangeSetOf(member) + ".Between(b[i], a[i]);");
        code.AppendLineAt(7, "if (edit.IsEmpty) continue;");
        code.AppendLineAt(7, "items ??= new " + items + "();");
        code.AppendLineAt(
            7,
            "items.Add(new "
                + transition
                + ".Item(keys[i], null, default, default, i, i, false, false, true, false, edit, false));"
        );
        code.AppendLineAt(6, "}");
        // Canonical orders are immutable; equal orders can share their key-only storage.
        code.AppendLineAt(6, "if (items is not null) order = keys;");
        code.AppendLineAt(6, "return true;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if ("
                + name
                + "(__before"
                + member.Id
                + ".Value!, __after"
                + member.Id
                + ".Value!, out __items"
                + member.Id
                + ", out var __alignedOrder"
                + member.Id
                + "))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + member.Id + " = __items" + member.Id + " is not null;");
        code.AppendLineAt(
            6,
            "__bO"
                + member.Id
                + " = __alignedOrder"
                + member.Id
                + "; __aO"
                + member.Id
                + " = __alignedOrder"
                + member.Id
                + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
    }

    private static void AppendValidation(
        SharedIndentedBuilder code,
        string keyOf,
        bool useSet,
        int indent
    )
    {
        code.AppendLineAt(indent, "for (var i = 0; i < count; i++)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "var k = " + keyOf + "(b[i]);");
        if (useSet)
            code.AppendLineAt(
                indent + 1,
                "if ((object?)k is null) throw new global::System.ArgumentNullException(\"key\");"
            );
        code.AppendLineAt(
            indent + 1,
            useSet
                ? "if (!seen.Add(k)) throw new global::System.InvalidOperationException(\"Duplicate key in keyed collection.\");"
                : SparseKeyedCollectionEmitter.AddUniqueEntry("seen", "k", "0")
        );
        code.AppendLineAt(indent + 1, "keys.Add(k);");
        code.AppendLineAt(indent, "}");
    }

    internal static void AppendGeneralStart(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string transition,
        string runtime
    )
    {
        var id = member.Id;
        var items = "global::System.Collections.Generic.List<" + transition + ".Item>";
        var order = "global::System.Collections.Generic.List<" + KeyTypeOf(member) + ">";
        var optional = runtime + "Optional<" + FragmentValueType(member) + ">";
        var parameters = optional + " __before" + id + ", " + optional + " __after" + id;
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            var map =
                "global::System.Collections.Generic.Dictionary<global::System.Guid, "
                + ElementTypeOf(member)
                + ">";
            var temporaryOrder = "global::System.Collections.Generic.List<global::System.Guid>";
            parameters +=
                ", "
                + map
                + " __bT"
                + id
                + ", "
                + temporaryOrder
                + " __bTO"
                + id
                + ", "
                + map
                + " __aT"
                + id
                + ", "
                + temporaryOrder
                + " __aTO"
                + id;
        }
        // Give the general path its own JIT profile instead of leaving it cold behind alignment.
        code.AppendLineAt(
            6,
            "static (bool Has, "
                + items
                + "? Items, "
                + order
                + "? BeforeOrder, "
                + order
                + "? AfterOrder) __general"
                + id
                + "("
                + parameters
                + ")"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "bool __h" + id + " = false;");
        code.AppendLineAt(7, items + "? __items" + id + " = null;");
        code.AppendLineAt(7, order + "? __bO" + id + " = null;");
        code.AppendLineAt(7, order + "? __aO" + id + " = null;");
    }

    internal static void AppendGeneralEnd(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var id = member.Id;
        code.AppendLineAt(
            7,
            "return (__h" + id + ", __items" + id + ", __bO" + id + ", __aO" + id + ");"
        );
        code.AppendLineAt(6, "}");
        var arguments = "__before" + id + ", __after" + id;
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            arguments += ", __bT" + id + ", __bTO" + id + ", __aT" + id + ", __aTO" + id;
        code.AppendLineAt(
            6,
            "(__h"
                + id
                + ", __items"
                + id
                + ", __bO"
                + id
                + ", __aO"
                + id
                + ") = __general"
                + id
                + "("
                + arguments
                + ");"
        );
    }
}
