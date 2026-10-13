using static SparseTypes.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetEmitter;
using static SparseTypes.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseTypes.Generator.Shared;

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
        var dictBefore = "__before" + id;
        var dictAfter = "__after" + id;
        if (NeedsSnapshot(member))
        {
            dictBefore = "__SparseSnapshot_" + id + "(" + dictBefore + ")";
            dictAfter = "__SparseSnapshot_" + id + "(" + dictAfter + ")";
        }
        code.AppendLineAt(
            6,
            "__wb" + id + " = " + dictBefore + "; __wa" + id + " = " + dictAfter + ";"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        AppendNormalizedInput(
            code,
            "__beforeDict" + id,
            "__before" + id + ".Value!",
            keyType,
            valueType
        );
        AppendNormalizedInput(
            code,
            "__afterDict" + id,
            "__after" + id + ".Value!",
            keyType,
            valueType
        );
        AppendItemAssembly(code, member, trans, dialect);
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    private static void AppendItemAssembly(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var itemList = "global::System.Collections.Generic.List<" + trans + ".Item>";
        var optValue = dialect.RuntimeNamespace + "Optional<" + ValueTypeOf(member) + ">";
        code.AppendLineAt(
            5,
            itemList
                + "? __added"
                + id
                + " = null, __removed"
                + id
                + " = null, __edited"
                + id
                + " = null;"
        );
        code.AppendLineAt(5, "foreach (var __kv in __beforeDict" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (__afterDict" + id + ".ContainsKey(__kv.Key)) continue;");
        AppendPresenceItem(code, member, trans, dialect, isAdded: false, indent: 6);
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "foreach (var __kv in __afterDict" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (!__beforeDict" + id + ".TryGetValue(__kv.Key, out var __b))");
        code.AppendLineAt(6, "{");
        AppendPresenceItem(code, member, trans, dialect, isAdded: true, indent: 7);
        code.AppendLineAt(7, "continue;");
        code.AppendLineAt(6, "}");
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            code.AppendLineAt(
                6,
                "var __nested = " + ValueChangeSetOf(member) + ".Between(__b!, __kv.Value);"
            );
            code.AppendLineAt(
                6,
                "if (!__nested.IsEmpty) (__edited"
                    + id
                    + " ??= new "
                    + itemList
                    + "()).Add(new "
                    + trans
                    + ".Item(__kv.Key, default, default, false, false, true, __nested, false));"
            );
        }
        else
        {
            code.AppendLineAt(
                6,
                "if (!"
                    + dialect.RuntimeFacade
                    + ".AreEqual(__b, __kv.Value)) (__edited"
                    + id
                    + " ??= new "
                    + itemList
                    + "()).Add(new "
                    + trans
                    + ".Item(__kv.Key, "
                    + optValue
                    + ".Present(__b), "
                    + optValue
                    + ".Present(__kv.Value), false, false, true, false));"
            );
        }
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (__added"
                + id
                + " is not null || __removed"
                + id
                + " is not null || __edited"
                + id
                + " is not null)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__h" + id + " = true;");
        // Reuse the first group while preserving the established added/removed/edited order.
        code.AppendLineAt(
            6,
            "var __list = __added" + id + " ?? __removed" + id + " ?? __edited" + id + "!;"
        );
        code.AppendLineAt(
            6,
            "int __count = (__added"
                + id
                + "?.Count ?? 0) + (__removed"
                + id
                + "?.Count ?? 0) + (__edited"
                + id
                + "?.Count ?? 0);"
        );
        code.AppendLineAt(6, "if (__list.Capacity < __count) __list.Capacity = __count;");
        code.AppendLineAt(
            6,
            "if (__removed"
                + id
                + " is not null && !global::System.Object.ReferenceEquals(__list, __removed"
                + id
                + ")) __list.AddRange(__removed"
                + id
                + ");"
        );
        code.AppendLineAt(
            6,
            "if (__edited"
                + id
                + " is not null && !global::System.Object.ReferenceEquals(__list, __edited"
                + id
                + ")) __list.AddRange(__edited"
                + id
                + ");"
        );
        code.AppendLineAt(6, "__items" + id + " = __list;");
        code.AppendLineAt(5, "}");
    }

    private static void AppendPresenceItem(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        bool isAdded,
        int indent
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var optValue = runtime + "Optional<" + ValueTypeOf(member) + ">";
        var group = (isAdded ? "__added" : "__removed") + member.Id;
        var itemList = "global::System.Collections.Generic.List<" + trans + ".Item>";
        code.AppendLineAt(indent, "var __value = " + optValue + ".Present(__kv.Value);");
        var editArgument = string.Empty;
        if (member.Collection.ValueType?.IsFragmentModel == true)
        {
            var valueFrag = ValueFragmentOf(member);
            code.AppendLineAt(
                indent,
                "var __fragment = "
                    + runtime
                    + "Optional<"
                    + valueFrag
                    + "?>.Present("
                    + valueFrag
                    + ".From(__kv.Value!));"
            );
            var endpoints = isAdded ? "default, __fragment" : "__fragment, default";
            code.AppendLineAt(
                indent,
                "var __edit = " + ValueChangeSetOf(member) + ".Between(" + endpoints + ");"
            );
            editArgument = ", __edit";
        }
        var valuesAndFlags = isAdded
            ? "default, __value, true, false, false"
            : "__value, default, false, true, false";
        code.AppendLineAt(
            indent,
            "("
                + group
                + " ??= new "
                + itemList
                + "()).Add(new "
                + trans
                + ".Item(__kv.Key, "
                + valuesAndFlags
                + editArgument
                + ", false));"
        );
    }

    private static void AppendNormalizedInput(
        SharedIndentedBuilder code,
        string target,
        string source,
        string keyType,
        string valueType
    )
    {
        var dictionary =
            "global::System.Collections.Generic.Dictionary<" + keyType + ", " + valueType + ">";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var native = target + "Native";
        // Between only reads these maps. Other comparers still need default-key normalization.
        code.AppendLineAt(
            5,
            "var "
                + target
                + " = "
                + source
                + " is "
                + dictionary
                + " "
                + native
                + " && "
                + native
                + ".GetType() == typeof("
                + dictionary
                + ")"
                + " && (global::System.Object.ReferenceEquals("
                + native
                + ".Comparer, "
                + comparer
                + ")"
                + " || global::System.Object.ReferenceEquals("
                + native
                + ".Comparer, global::System.StringComparer.Ordinal))"
                + " ? "
                + native
                + " : new "
                + dictionary
                + "((global::System.Collections.Generic.IDictionary<"
                + keyType
                + ", "
                + valueType
                + ">)"
                + source
                + ", "
                + comparer
                + ");"
        );
    }
}
