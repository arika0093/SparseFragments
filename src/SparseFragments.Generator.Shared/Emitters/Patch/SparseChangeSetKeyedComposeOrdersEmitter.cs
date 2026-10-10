using System.Collections.Immutable;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Order resolution and item enumeration for keyed ChangeSet composition.</summary>
/// <remarks>
/// Computes composed key orders (next wins, else first filtered) and enumerates
/// net items in after-order plus removals in before-order. Temporary-identity
/// orders and enumeration delegate to the temp merge emitter.
/// </remarks>
internal static class SparseChangeSetKeyedComposeOrdersEmitter
{
    internal static void AppendOrdersAndAssembly(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool hasTempCompose
    )
    {
        var id = member.Id;
        var keyType = SparseChangeSetBasicsEmitter.KeyTypeOf(member);
        var trans = SparseChangeSetBasicsEmitter.TransNameFor(members, member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        // Net no-op normalization is implicit (empty dict => has false below).
        // Orders: next wins when present (filtered to net keys), else first filtered (mirrors Patch order compose).
        // Static Enumerable.Contains below: generated code cannot assume "using System.Linq".
        code.AppendLineAt(
            5,
            "var __netRemoved"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(5, "var __addedCount" + id + " = 0;");
        code.AppendLineAt(
            5,
            "foreach (var __kv in __net"
                + id
                + ") { if (__kv.Value.IsAdded) __addedCount"
                + id
                + "++; if (__kv.Value.IsRemoved) __netRemoved"
                + id
                + ".Add(__kv.Key); }"
        );
        // The union key set is no longer needed after merging. Reuse its capacity
        // for pending additions when additions would otherwise cause repeated scans.
        // These keys are a subset of the original union, so the set never grows.
        code.AppendLineAt(5, "var __indexAddedOrder" + id + " = __addedCount" + id + " > 4;");
        code.AppendLineAt(
            5,
            "if (__indexAddedOrder"
                + id
                + ") { __keys"
                + id
                + ".Clear(); foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded) __keys"
                + id
                + ".Add(__kv.Key); }"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o1b = "
                + KeyedBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o1a = "
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o2b = next."
                + KeyedBeforeOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<"
                + keyType
                + ">? __o2a = next."
                + KeyedAfterOrder(member)
                + ";"
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<" + keyType + ">? __nbO" + id + " = __o1b; "
        );
        code.AppendLineAt(
            5,
            "global::System.Collections.Generic.List<" + keyType + ">? __naO" + id + " = null;"
        );
        code.AppendLineAt(5, "if (__o2a is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __no = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__o2a.Count); foreach (var __k in __o2a) if (!__netRemoved"
                + id
                + ".Contains(__k)) { __no.Add(__k); if (__indexAddedOrder"
                + id
                + ") __keys"
                + id
                + ".Remove(__k); }"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded && (__indexAddedOrder"
                + id
                + " ? __keys"
                + id
                + ".Remove(__kv.Key) : !global::System.Linq.Enumerable.Contains(__no, __kv.Key, "
                + comparer
                + "))) __no.Add(__kv.Key);"
        );
        code.AppendLineAt(6, "__naO" + id + " = __no;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else if (__o1a is not null)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var __no = new global::System.Collections.Generic.List<"
                + keyType
                + ">(__o1a.Count); foreach (var __k in __o1a) if (!__netRemoved"
                + id
                + ".Contains(__k)) { __no.Add(__k); if (__indexAddedOrder"
                + id
                + ") __keys"
                + id
                + ".Remove(__k); }"
        );
        code.AppendLineAt(
            6,
            "foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded && (__indexAddedOrder"
                + id
                + " ? __keys"
                + id
                + ".Remove(__kv.Key) : !global::System.Linq.Enumerable.Contains(__no, __kv.Key, "
                + comparer
                + "))) __no.Add(__kv.Key);"
        );
        code.AppendLineAt(6, "__naO" + id + " = __no;");
        code.AppendLineAt(5, "}");
        if (hasTempCompose)
        {
            SparseChangeSetKeyedTempMergeEmitter.AppendTempOrderFinalize(code, member, keyType, id);
        }
        code.AppendLineAt(
            5,
            "if (__net"
                + id
                + ".Count == 0"
                + (
                    SparseKeyedCollectionEmitter.HasUnassignedKey(member)
                        ? " && __unassignedNetItems" + id + ".Count == 0"
                        : ""
                )
                + (
                    hasTempCompose
                        ? " && (!__hasTemp" + id + " || __tnet" + id + ".Count == 0)"
                        : ""
                )
                + ") { }"
        );
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__cb" + id + "_has = true;");
        code.AppendLineAt(
            6,
            "var __elist"
                + id
                + " = new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(__net"
                + id
                + ".Count);"
        );
        // Enumeration: net after-order then net removed in net before-order (mirrors Between).
        if (hasTempCompose)
        {
            // Temporary identities enumerate by Guid order; assigned entries
            // keep the legacy walk when no temps participate.
            code.AppendLineAt(6, "if (__hasTemp" + id + ")");
            code.AppendLineAt(6, "{");
            SparseChangeSetKeyedTempMergeEmitter.AppendTempAssembly(code, member, trans, id);
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
        }
        code.AppendLineAt(6, "if (__naO" + id + " is not null)");
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
        {
            code.AppendLineAt(
                6,
                "{ var __unassignedIndex = 0; foreach (var __k in __naO"
                    + id
                    + ") { if ("
                    + SparseKeyedCollectionEmitter.IsUnassignedExpression(member, "__k")
                    + ") { if (__unassignedIndex < __unassignedNetItems"
                    + id
                    + ".Count) __elist"
                    + id
                    + ".Add(__unassignedNetItems"
                    + id
                    + "[__unassignedIndex++]); continue; } if (__net"
                    + id
                    + ".TryGetValue(__k, out var __e) && !__e.IsRemoved) __elist"
                    + id
                    + ".Add(__e); } }"
            );
        }
        else
            code.AppendLineAt(
                6,
                "{ foreach (var __k in __naO"
                    + id
                    + ") if (__net"
                    + id
                    + ".TryGetValue(__k, out var __e) && !__e.IsRemoved) __elist"
                    + id
                    + ".Add(__e); }"
            );
        code.AppendLineAt(
            6,
            "else foreach (var __kv in __net"
                + id
                + ") if (!__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        if (SparseKeyedCollectionEmitter.HasUnassignedKey(member))
            code.AppendLineAt(
                6,
                "if (__naO"
                    + id
                    + " is null) __elist"
                    + id
                    + ".AddRange(__unassignedNetItems"
                    + id
                    + ");"
            );
        code.AppendLineAt(6, "if (__nbO" + id + " is not null)");
        code.AppendLineAt(
            6,
            "{ foreach (var __k in __nbO"
                + id
                + ") if (__net"
                + id
                + ".TryGetValue(__k, out var __e) && __e.IsRemoved) __elist"
                + id
                + ".Add(__e); }"
        );
        code.AppendLineAt(
            6,
            "else foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsRemoved) __elist"
                + id
                + ".Add(__kv.Value);"
        );
        if (hasTempCompose)
        {
            code.AppendLineAt(6, "}");
        }
        code.AppendLineAt(6, "__cb" + id + "_items = __elist" + id + ";");
        code.AppendLineAt(
            6,
            "__cb" + id + "_bO = __nbO" + id + "; __cb" + id + "_aO = __naO" + id + ";"
        );
        // Normalize order-only equality to sparse (no orders when equal).
        code.AppendLineAt(
            6,
            "if (__nbO"
                + id
                + " is not null && __naO"
                + id
                + " is not null && "
                + facade
                + ".KeyOrderEquals<"
                + keyType
                + ">(__nbO"
                + id
                + ", __naO"
                + id
                + ") && (__net"
                + id
                + ".Count != 0"
                + (hasTempCompose ? " || (__hasTemp" + id + " && __tnet" + id + ".Count != 0)" : "")
                + ")) { bool __onlyOrder = true; foreach (var __kv in __net"
                + id
                + ") if (__kv.Value.IsAdded || __kv.Value.IsRemoved || __kv.Value.IsEdited) { __onlyOrder = false; break; }"
                + (
                    hasTempCompose
                        ? " if (__onlyOrder && __hasTemp"
                            + id
                            + ") foreach (var __tkv in __tnet"
                            + id
                            + ") if (__tkv.Value.IsAdded || __tkv.Value.IsRemoved || __tkv.Value.IsEdited) { __onlyOrder = false; break; }"
                        : ""
                )
                + " if (__onlyOrder && __elist"
                + id
                + ".Count == 0) { __cb"
                + id
                + "_has = false; __cb"
                + id
                + "_items = null; __cb"
                + id
                + "_bO = null; __cb"
                + id
                + "_aO = null;"
                + (hasTempCompose ? " __cb" + id + "_tbO = null; __cb" + id + "_taO = null;" : "")
                + " } }"
        );
        code.AppendLineAt(5, "}");
    }
}
