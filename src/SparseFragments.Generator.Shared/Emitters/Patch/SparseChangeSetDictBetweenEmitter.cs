using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Between-time sparse diff for dictionaries.</summary>
internal static class SparseChangeSetDictBetweenEmitter
{
    /// <summary>Emits Between-time sparse diff for a dictionary member.</summary>
    internal static void AppendDictBetweenSparse(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string escName,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        ComputePublicNames(
            System.Collections.Immutable.ImmutableArray.Create(member),
            out _,
            out var transNames
        );
        var trans = transNames[member.Id];
        code.AppendLineAt(3, "bool __h" + id + " = false;");
        code.AppendLineAt(3, "bool __whole" + id + " = false;");
        code.AppendLineAt(3, opt + " __wb" + id + " = default;");
        code.AppendLineAt(3, opt + " __wa" + id + " = default;");
        code.AppendLineAt(
            3,
            "global::System.Collections.Generic.List<" + trans + ".Item>? __items" + id + " = null;"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __before" + id + " = bf." + escName + ";");
        code.AppendLineAt(4, "var __after" + id + " = af." + escName + ";");
        code.AppendLineAt(
            4,
            "bool __beforeHas"
                + id
                + " = __before"
                + id
                + ".IsPresent && (object?)__before"
                + id
                + ".Value is not null;"
        );
        code.AppendLineAt(
            4,
            "bool __afterHas"
                + id
                + " = __after"
                + id
                + ".IsPresent && (object?)__after"
                + id
                + ".Value is not null;"
        );
        code.AppendLineAt(
            4,
            "if (__before"
                + id
                + ".IsPresent != __after"
                + id
                + ".IsPresent || !__beforeHas"
                + id
                + " || !__afterHas"
                + id
                + ")"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (!Fragment.__SparseEqual_" + id + "(__before" + id + ", __after" + id + "))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true; __whole" + id + " = true;");
        code.AppendLineAt(
            6,
            "__wb" + id + " = __before" + id + "; __wa" + id + " = __after" + id + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __beforeDict"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)__before"
                + id
                + ".Value!, "
                + comparer
                + ");"
        );
        code.AppendLineAt(
            5,
            "var __afterDict"
                + id
                + " = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)__after"
                + id
                + ".Value!, "
                + comparer
                + ");"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                5,
                "var __edited"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueCs
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __added"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __removed"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(5, "foreach (var __k in __beforeDict" + id + ".Keys)");
            code.AppendLineAt(
                5,
                "{ if (!__afterDict"
                    + id
                    + ".ContainsKey(__k)) __removed"
                    + id
                    + "[__k] = __beforeDict"
                    + id
                    + "[__k]; }"
            );
            code.AppendLineAt(5, "foreach (var __kv in __afterDict" + id + ")");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (!__beforeDict"
                    + id
                    + ".TryGetValue(__kv.Key, out var __b)) { __added"
                    + id
                    + "[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                6,
                "var __nested = "
                    + valueCs
                    + ".Between("
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__b!)), "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value)));"
            );
            code.AppendLineAt(6, "if (!__nested.IsEmpty) __edited" + id + "[__kv.Key] = __nested;");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "if (__added"
                    + id
                    + ".Count == 0 && __removed"
                    + id
                    + ".Count == 0 && __edited"
                    + id
                    + ".Count == 0) { }"
            );
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "__h" + id + " = true;");
            code.AppendLineAt(
                6,
                "var __list"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>();"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __added"
                    + id
                    + ") { var __a = "
                    + optValue
                    + ".Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __ea = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(default, __ea); __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __removed"
                    + id
                    + ") { var __b = "
                    + optValue
                    + ".Present(__kv.Value); "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?> __eb = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!)); var __edit = "
                    + valueCs
                    + ".Between(__eb, default); __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __edited"
                    + id
                    + ") { var __b = "
                    + optValue
                    + ".Present(__beforeDict"
                    + id
                    + "[__kv.Key]); var __a = "
                    + optValue
                    + ".Present(__afterDict"
                    + id
                    + "[__kv.Key]); __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, __a, false, false, true, __kv.Value, false)); }"
            );
            code.AppendLineAt(6, "__items" + id + " = __list" + id + ";");
            code.AppendLineAt(5, "}");
        }
        else
        {
            code.AppendLineAt(
                5,
                "var __added"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __removed"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(
                5,
                "var __edited"
                    + id
                    + " = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">("
                    + comparer
                    + ");"
            );
            code.AppendLineAt(5, "foreach (var __k in __beforeDict" + id + ".Keys)");
            code.AppendLineAt(
                5,
                "{ if (!__afterDict"
                    + id
                    + ".ContainsKey(__k)) __removed"
                    + id
                    + "[__k] = __beforeDict"
                    + id
                    + "[__k]; }"
            );
            code.AppendLineAt(5, "foreach (var __kv in __afterDict" + id + ")");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "if (!__beforeDict"
                    + id
                    + ".TryGetValue(__kv.Key, out var __b)) { __added"
                    + id
                    + "[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                6,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__b, (object?)__kv.Value)) __edited"
                    + id
                    + "[__kv.Key] = __kv.Value;"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "if (__added"
                    + id
                    + ".Count == 0 && __removed"
                    + id
                    + ".Count == 0 && __edited"
                    + id
                    + ".Count == 0) { }"
            );
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "__h" + id + " = true;");
            code.AppendLineAt(
                6,
                "var __list"
                    + id
                    + " = new global::System.Collections.Generic.List<"
                    + trans
                    + ".Item>();"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __added"
                    + id
                    + ") __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + optValue
                    + ".Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __removed"
                    + id
                    + ") __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + optValue
                    + ".Present(__kv.Value), default, false, true, false, false));"
            );
            code.AppendLineAt(
                6,
                "foreach (var __kv in __edited"
                    + id
                    + ") __list"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + optValue
                    + ".Present(__beforeDict"
                    + id
                    + "[__kv.Key]), "
                    + optValue
                    + ".Present(__kv.Value), false, false, true, false));"
            );
            code.AppendLineAt(6, "__items" + id + " = __list" + id + ";");
            code.AppendLineAt(5, "}");
        }
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }
}
