using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds typed dictionary views for whole endpoints.</summary>
internal static class SparseChangeSetDictBuildEmitter
{
    internal static void AppendBuild(
        SharedIndentedBuilder shell,
        SparseMemberModel member,
        string trans,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target
    )
    {
        var code = target is null ? shell : target.ChangeSetOperations;
        var runtime = dialect.RuntimeNamespace;
        var keyType = KeyTypeOf(member);
        var valueType = ValueTypeOf(member);
        var optDict = runtime + "Optional<" + member.Property.Type.Name + ">";
        var optValue = runtime + "Optional<" + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var facade = dialect.RuntimeFacade;
        var hasPatch = member.Collection.ValueType?.IsFragmentModel == true;
        var valueCs = hasPatch ? ValueChangeSetOf(member) : null;
        var valueFrag = hasPatch ? ValueFragmentOf(member) : null;
        var editedType = hasPatch ? valueCs! : valueType;
        code.AppendLineAt(
            2,
            (target is null ? "private " : "internal static ")
                + trans
                + " __SparseBuild_"
                + member.Id
                + "("
                + optDict
                + " before, "
                + optDict
                + " after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var __comparer = " + comparer + ";");
        code.AppendLineAt(
            3,
            "bool __beforeHas = before.IsPresent && (object?)before.Value is not null;"
        );
        code.AppendLineAt(
            3,
            "bool __afterHas = after.IsPresent && (object?)after.Value is not null;"
        );
        code.AppendLineAt(3, "if (!__beforeHas && !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "bool __eq = Fragment.__SparseEqual_" + member.Id + "(before, after);"
        );
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer), new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__comparer), new global::System.Collections.Generic.List<"
                + trans
                + ".Item>(), __eq);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (!__beforeHas || !__afterHas)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var __added = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            4,
            "if (__afterHas) foreach (var __kv in after.Value!) __added[__kv.Key] = __kv.Value;"
        );
        code.AppendLineAt(
            4,
            "var __removed = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            4,
            "if (__beforeHas) foreach (var __kv in before.Value!) __removed[__kv.Key] = __kv.Value;"
        );
        code.AppendLineAt(
            4,
            "var __items = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                4,
                "foreach (var __kv in __added) { "
                    + optValue
                    + " __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
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
                    + ".Between(default, __ea); __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                4,
                "foreach (var __kv in __removed) { "
                    + optValue
                    + " __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
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
                    + ".Between(__eb, default); __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
        }
        else
        {
            code.AppendLineAt(
                4,
                "foreach (var __kv in __added) __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                4,
                "foreach (var __kv in __removed) __items.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), default, false, true, false, false));"
            );
        }
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(before, after, __added, __removed, new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + editedType
                + ">(__comparer), __items, false);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __beforeDict = before.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __bd && global::System.Object.Equals(__bd.Comparer, __comparer) ? __bd : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)before.Value!, __comparer);"
        );
        code.AppendLineAt(
            3,
            "var __afterDict = after.Value is global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + "> __ad && global::System.Object.Equals(__ad.Comparer, __comparer) ? __ad : new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)after.Value!, __comparer);"
        );
        code.AppendLineAt(
            3,
            "var __added2 = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "var __removed2 = new global::System.Collections.Generic.Dictionary<"
                + keyType
                + ", "
                + valueType
                + ">(__comparer);"
        );
        code.AppendLineAt(
            3,
            "foreach (var __k in __beforeDict.Keys) if (!__afterDict.ContainsKey(__k)) __removed2[__k] = __beforeDict[__k];"
        );
        if (hasPatch)
        {
            code.AppendLineAt(
                3,
                "var __edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueCs
                    + ">(__comparer);"
            );
            code.AppendLineAt(3, "foreach (var __kv in __afterDict)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__beforeDict.TryGetValue(__kv.Key, out var __b)) { __added2[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                4,
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
            code.AppendLineAt(4, "if (!__nested.IsEmpty) __edited[__kv.Key] = __nested;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "bool __empty = __added2.Count == 0 && __removed2.Count == 0 && __edited.Count == 0;"
            );
            code.AppendLineAt(
                3,
                "var __items2 = new global::System.Collections.Generic.List<" + trans + ".Item>();"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __added2) { var __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
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
                    + ".Between(default, __ea); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, __a, true, false, false, __edit, false)); }"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __removed2) { var __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value); "
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
                    + ".Between(__eb, default); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, default, false, true, false, __edit, false)); }"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __edited) { var __b = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__beforeDict[__kv.Key]); var __a = "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__afterDict[__kv.Key]); __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, __b, __a, false, false, true, __kv.Value, false)); }"
            );
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(before, after, __added2, __removed2, __edited, __items2, __empty);"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "var __edited = new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + valueType
                    + ">(__comparer);"
            );
            code.AppendLineAt(3, "foreach (var __kv in __afterDict)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "if (!__beforeDict.TryGetValue(__kv.Key, out var __b)) { __added2[__kv.Key] = __kv.Value; continue; }"
            );
            code.AppendLineAt(
                4,
                "if (!"
                    + facade
                    + ".AreEqual((object?)__b, (object?)__kv.Value)) __edited[__kv.Key] = __kv.Value;"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "bool __empty = __added2.Count == 0 && __removed2.Count == 0 && __edited.Count == 0;"
            );
            code.AppendLineAt(
                3,
                "var __items2 = new global::System.Collections.Generic.List<" + trans + ".Item>();"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __added2) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, default, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), true, false, false, false));"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __removed2) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), default, false, true, false, false));"
            );
            code.AppendLineAt(
                3,
                "foreach (var __kv in __edited) __items2.Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__beforeDict[__kv.Key]), "
                    + runtime
                    + "Optional<"
                    + valueType
                    + ">.Present(__kv.Value), false, false, true, false));"
            );
            code.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(before, after, __added2, __removed2, __edited, __items2, __empty);"
            );
        }
        code.AppendLineAt(2, "}");
    }
}
