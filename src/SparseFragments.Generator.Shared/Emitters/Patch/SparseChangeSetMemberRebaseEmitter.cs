using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Scalar and merge-collection member rebase for generated change sets.</summary>
internal static class SparseChangeSetMemberRebaseEmitter
{
    /// <summary>
    /// Emits a redacted-before guard for one member block.
    /// </summary>
    /// <remarks>
    /// The guard evaluates before the member's own condition so granular
    /// bodies stay untouched. Strict mode reports a secret-safe conflict and
    /// excludes the member; the default replays <paramref name="passthrough"/>
    /// as the explicit desired operation with no historical comparison.
    /// </remarks>
    internal static void AppendRedactedGuard(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        string runtime,
        string conflict,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string hasCondition,
        string[] passthrough
    )
    {
        code.AppendLineAt(
            3,
            "var __red"
                + member.Id
                + " = "
                + hasCondition
                + " && __SparseIsRedacted(options, "
                + lit
                + ");"
        );
        code.AppendLineAt(3, "if (__red" + member.Id + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__SparseRejectsRedacted(options))");
        code.AppendLineAt(4, "{");
        SparseRebaseOptionEmitter.AppendRedactedConflict(
            code,
            4,
            runtime,
            conflict,
            dialect,
            "new string[] { " + lit + " }",
            "__conflicts"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        foreach (var line in passthrough)
        {
            code.AppendLineAt(5, line);
        }
        code.AppendLineAt(4, "}");
        code.AppendLineAt(3, "}");
    }

    /// <summary>Emits scalar-style redacted passthrough (replay desired onto current).</summary>
    internal static string[] ScalarPassthrough(SparseMemberModel member, string esc) =>
        [
            "__rh" + member.Id + " = true;",
            "__rb" + member.Id + " = __cur." + esc + ";",
            "__ra" + member.Id + " = " + AfterField(member) + ";",
        ];

    /// <summary>Emits one presence-aware reconciler attempt with no-op normalization.</summary>
    internal static void AppendReconcilerAttempt(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        string reconciler,
        string areEqual,
        string reasonFallback,
        string conflict,
        string conflictKind
    )
    {
        code.AppendLineAt(
            4,
            "    if ("
                + reconciler
                + ".TryRebase(__base"
                + member.Id
                + ", __des"
                + member.Id
                + ", __curM"
                + member.Id
                + ", out var __reb"
                + member.Id
                + ", out var __reason"
                + member.Id
                + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(
            5,
            "        if (!((!__reb"
                + member.Id
                + ".IsPresent && !__curM"
                + member.Id
                + ".IsPresent) || (__reb"
                + member.Id
                + ".IsPresent && __curM"
                + member.Id
                + ".IsPresent && "
                + areEqual
                + ")))"
        );
        code.AppendLineAt(5, "        {");
        code.AppendLineAt(6, "            __rh" + member.Id + " = true;");
        code.AppendLineAt(6, "            __rb" + member.Id + " = __curM" + member.Id + ";");
        code.AppendLineAt(6, "            __ra" + member.Id + " = __reb" + member.Id + ";");
        code.AppendLineAt(5, "        }");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(4, "    else");
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(
            5,
            "        __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + conflictKind
                + ".CustomStrategy, __SparseMember(__base"
                + member.Id
                + "), __SparseMember(__des"
                + member.Id
                + "), __SparseMember(__curM"
                + member.Id
                + "), __reason"
                + member.Id
                + " ?? \""
                + reasonFallback
                + "\"));"
        );
        code.AppendLineAt(4, "    }");
    }

    /// <summary>
    /// Emits plain scalar rebase with the caller-wide fallback mode applied.
    /// </summary>
    /// <remarks>
    /// The mode only fills the built-in default slot: members with an explicit
    /// rebase policy or merge strategy never reach this path. PreferIncoming
    /// replays divergent edits as overwrites, PreferCurrent drops them, and
    /// FailOnConflict keeps the three-way shape below.
    /// </remarks>
    internal static void AppendScalarModeRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string lit,
        string conflictKind,
        string conflict,
        string modeType
    )
    {
        var id = member.Id;
        code.AppendLineAt(
            4,
            "    var __mode"
                + id
                + " = options is null ? "
                + modeType
                + ".Default : options.DefaultRebaseMode;"
        );
        code.AppendLineAt(
            4,
            "    if (__mode"
                + id
                + " == "
                + modeType
                + ".PreferIncoming && !Fragment.__SparseEqual_"
                + id
                + "(__des"
                + id
                + ", __curM"
                + id
                + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(5, "        __rh" + id + " = true;");
        code.AppendLineAt(5, "        __rb" + id + " = __curM" + id + ";");
        code.AppendLineAt(5, "        __ra" + id + " = __des" + id + ";");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    else if (__mode"
                + id
                + " != "
                + modeType
                + ".PreferCurrent && Fragment.__SparseEqual_"
                + id
                + "(__base"
                + id
                + ", __curM"
                + id
                + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(5, "        __rh" + id + " = true;");
        code.AppendLineAt(5, "        __rb" + id + " = __curM" + id + ";");
        code.AppendLineAt(5, "        __ra" + id + " = __des" + id + ";");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    else if (__mode"
                + id
                + " != "
                + modeType
                + ".PreferCurrent && !Fragment.__SparseEqual_"
                + id
                + "(__des"
                + id
                + ", __curM"
                + id
                + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(
            5,
            "        __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + conflictKind
                + ".Scalar, __SparseMember(__base"
                + id
                + "), __SparseMember(__des"
                + id
                + "), __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "    }");
    }

    /// <summary>
    /// Emits merge-aware rebase for Append/SetUnion scalar-collection members.
    /// </summary>
    /// <remarks>
    /// Unlike plain scalar equality, Append members replay the locally appended suffix onto
    /// the current prefix and SetUnion members replay locally added elements beside the
    /// current set; both consume only the retained per-member before/desired plus the
    /// supplied current member, mirroring the generated Patch rebase dispatch (typed
    /// set/sequence fast paths with a boxed semantic fallback). A rebased value equal to
    /// current normalizes to no-op; an unmergeable concurrent change reports a structured
    /// member conflict while leaving other paths untouched.
    /// </remarks>
    internal static void AppendMergeCollectionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string lit,
        string runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var valueType = FragmentValueType(member);
        var elementType = member.Collection.ElementType.Name;
        var opt = runtime + "Optional<" + valueType + ">";
        var facade = dialect.RuntimeFacade;
        var comparer = dialect.RuntimeFacade;
        var conflict = dialect.ConflictType;
        var kind =
            member.MergeMode == SparseMergeModes.Append
                ? dialect.ConflictKindType + ".CollectionAppend"
                : dialect.ConflictKindType + ".CollectionSetUnion";
        var isSet =
            member.MergeMode == SparseMergeModes.SetUnion
            && member.Collection.CloneKind == SparseCloneCollectionKind.Set;
        var isTypedSequence =
            !isSet
            && member.Collection.CloneKind
                is SparseCloneCollectionKind.Array
                    or SparseCloneCollectionKind.List
            && member.Collection.ElementType.UsesDefaultScalarEquality;
        AppendRedactedGuard(
            code,
            member,
            lit,
            runtime,
            conflict,
            dialect,
            HasField(member),
            ScalarPassthrough(member, esc)
        );
        code.AppendLineAt(4, "if (" + HasField(member) + " && !__red" + id + ")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "    var __base" + id + " = " + BeforeField(member) + ";");
        code.AppendLineAt(4, "    var __des" + id + " = " + AfterField(member) + ";");
        code.AppendLineAt(4, "    var __curM" + id + " = __cur." + esc + ";");
        code.AppendLineAt(
            4,
            "    if (Fragment.__SparseEqual_" + id + "(__base" + id + ", __curM" + id + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(5, "        __rh" + id + " = true;");
        code.AppendLineAt(5, "        __rb" + id + " = __curM" + id + ";");
        code.AppendLineAt(5, "        __ra" + id + " = __des" + id + ";");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    else if (__base"
                + id
                + ".IsPresent && __des"
                + id
                + ".IsPresent && __curM"
                + id
                + ".IsPresent && (object?)__base"
                + id
                + ".Value is not null && (object?)__des"
                + id
                + ".Value is not null && (object?)__curM"
                + id
                + ".Value is not null)"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(5, "        " + valueType + " __rebuilt" + id + " = default!;");
        code.AppendLineAt(5, "        string? __reason" + id + ";");
        code.AppendLineAt(5, "        bool __ok" + id + ";");
        if (isSet)
        {
            code.AppendLineAt(
                5,
                "        __ok"
                    + id
                    + " = "
                    + facade
                    + ".TryRebaseSetUnion<"
                    + elementType
                    + ">(__base"
                    + id
                    + ".Value!, __des"
                    + id
                    + ".Value!, __curM"
                    + id
                    + ".Value!, out var __rv"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            code.AppendLineAt(
                5,
                "        if (__ok" + id + ") __rebuilt" + id + " = __rv" + id + ";"
            );
        }
        else if (isTypedSequence)
        {
            var typedMethod =
                member.MergeMode == SparseMergeModes.Append
                    ? "TryRebaseSequenceAppend"
                    : "TryRebaseSequenceSetUnion";
            if (member.Collection.CloneKind == SparseCloneCollectionKind.Array)
                typedMethod += "Array";
            var boxedMethod =
                member.MergeMode == SparseMergeModes.Append
                    ? "TryRebaseAppend"
                    : "TryRebaseSetUnion";
            var readOnly = "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
            var list = "global::System.Collections.Generic.List<" + elementType + ">";
            string NativeInput(string state, string variable) =>
                "(object?)"
                + state
                + ".Value is "
                + readOnly
                + " "
                + variable
                + " && ("
                + variable
                + " is "
                + elementType
                + "[] || "
                + variable
                + ".GetType() == typeof("
                + list
                + "))";
            code.AppendLineAt(
                5,
                "        if ("
                    + NativeInput("__base" + id, "__bv" + id)
                    + " && "
                    + NativeInput("__des" + id, "__dv" + id)
                    + " && "
                    + NativeInput("__curM" + id, "__cv" + id)
                    + ")"
            );
            code.AppendLineAt(5, "        {");
            code.AppendLineAt(
                6,
                "            __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + typedMethod
                    + "<"
                    + elementType
                    + ">(__bv"
                    + id
                    + ", __dv"
                    + id
                    + ", __cv"
                    + id
                    + ", null, out var __tv"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            code.AppendLineAt(
                6,
                "            if (__ok" + id + ") __rebuilt" + id + " = __tv" + id + ";"
            );
            code.AppendLineAt(5, "        }");
            code.AppendLineAt(5, "        else");
            code.AppendLineAt(5, "        {");
            code.AppendLineAt(
                6,
                "            var __bb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__base"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                6,
                "            var __cb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__curM"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                6,
                "            var __db"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__des"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                6,
                "            __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + boxedMethod
                    + "(__bb"
                    + id
                    + ", __db"
                    + id
                    + ", __cb"
                    + id
                    + ", (object? __l, object? __r) => "
                    + comparer
                    + ".AreEqual(__l, __r), out var __bx"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            var boxedResult = SparseFragmentExpressions.MaterializeCollection(
                member,
                "global::System.Linq.Enumerable.Cast<" + elementType + ">(__bx" + id + ")"
            );
            code.AppendLineAt(
                6,
                "            if (__ok" + id + ") __rebuilt" + id + " = " + boxedResult + ";"
            );
            code.AppendLineAt(5, "        }");
        }
        else
        {
            var boxedMethod =
                member.MergeMode == SparseMergeModes.Append
                    ? "TryRebaseAppend"
                    : "TryRebaseSetUnion";
            code.AppendLineAt(
                5,
                "        var __bb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__base"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                5,
                "        var __cb"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__curM"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                5,
                "        var __db"
                    + id
                    + " = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__des"
                    + id
                    + ".Value));"
            );
            code.AppendLineAt(
                5,
                "        __ok"
                    + id
                    + " = "
                    + facade
                    + "."
                    + boxedMethod
                    + "(__bb"
                    + id
                    + ", __db"
                    + id
                    + ", __cb"
                    + id
                    + ", (object? __l, object? __r) => "
                    + comparer
                    + ".AreEqual(__l, __r), out var __bx"
                    + id
                    + ", out __reason"
                    + id
                    + ");"
            );
            var boxedResult = SparseFragmentExpressions.MaterializeCollection(
                member,
                "global::System.Linq.Enumerable.Cast<" + elementType + ">(__bx" + id + ")"
            );
            code.AppendLineAt(
                5,
                "        if (__ok" + id + ") __rebuilt" + id + " = " + boxedResult + ";"
            );
        }
        code.AppendLineAt(5, "        if (__ok" + id + ")");
        code.AppendLineAt(5, "        {");
        code.AppendLineAt(
            6,
            "            " + opt + " __rebOpt" + id + " = " + opt + ".Present(__rebuilt" + id + ");"
        );
        code.AppendLineAt(
            6,
            "            if (!Fragment.__SparseEqual_"
                + id
                + "(__curM"
                + id
                + ", __rebOpt"
                + id
                + "))"
        );
        code.AppendLineAt(6, "            {");
        code.AppendLineAt(7, "                __rh" + id + " = true;");
        code.AppendLineAt(7, "                __rb" + id + " = __curM" + id + ";");
        code.AppendLineAt(7, "                __ra" + id + " = __rebOpt" + id + ";");
        code.AppendLineAt(6, "            }");
        code.AppendLineAt(5, "        }");
        code.AppendLineAt(5, "        else");
        code.AppendLineAt(5, "        {");
        code.AppendLineAt(
            6,
            "            __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + kind
                + ", __SparseMember(__base"
                + id
                + "), __SparseMember(__des"
                + id
                + "), __SparseMember(__curM"
                + id
                + "), __reason"
                + id
                + " ?? \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(5, "        }");
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    else if (!Fragment.__SparseEqual_" + id + "(__des" + id + ", __curM" + id + "))"
        );
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(
            5,
            "        __conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + lit
                + " }, "
                + kind
                + ", __SparseMember(__base"
                + id
                + "), __SparseMember(__des"
                + id
                + "), __SparseMember(__curM"
                + id
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(4, "}");
    }
}
