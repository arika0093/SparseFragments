using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
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
using static SparseFragments.Generator.Shared.SparseChangeSetTransitionEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Rebase core plus merge-collection rebase.</summary>
internal static class SparseChangeSetRebaseEmitter
{
    internal static void AppendRebase(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string rebaseResult,
        string prefix,
        string rebase,
        string between,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType,
        ImmutableArray<string> ignoredSettablePropertyNames = default
    )
    {
        _ = between;
        _ = prefix;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<object?> __SparseState("
                + optionalFragment
                + " state) => state.IsPresent ? "
                + runtime
                + "Optional<object?>.Present((object?)state.Value) : "
                + runtime
                + "Optional<object?>.Missing;"
        );
        code.AppendLineAt(
            2,
            "private static "
                + runtime
                + "Optional<object?> __SparseMember<T>("
                + runtime
                + "Optional<T> value) => value.IsPresent ? "
                + runtime
                + "Optional<object?>.Present((object?)value.Value) : "
                + runtime
                + "Optional<object?>.Missing;"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "public " + rebaseResult + " RebaseOnto(" + optionalFragment + " current)"
        );
        code.AppendLineAt(2, "{");
        var __hasSparseRb = members.Any(static m => IsKeyed(m) || IsDict(m));
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __local = ToPatch();");
        code.AppendLineAt(4, "var __rb = " + rebase + "(__sparse_wholeBefore, __local, current);");
        code.AppendLineAt(4, "if (__rb.Conflicts.Count == 0 && __rb.Patch.__SparseIsEmpty())");
        code.AppendLineAt(5, "return " + rebaseResult + ".Success(Between(current, current));");
        code.AppendLineAt(4, "var __ra = __rb.Patch.Apply(current);");
        code.AppendLineAt(
            4,
            "return new " + rebaseResult + "(Between(current, __ra), __rb.Conflicts);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (IsEmpty) return " + rebaseResult + ".Success(Between(current, current));"
        );
        code.AppendLineAt(3, "if (!current.IsPresent || current.Value is null)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __conf = new " + conflictList + "();");
        code.AppendLineAt(
            4,
            "__conf.Add(new "
                + conflict
                + "(new string[0], "
                + conflictKind
                + ".WholeContribution, __SparseState(__sparse_hasWhole ? __sparse_wholeBefore : default), __SparseState(__sparse_hasWhole ? __sparse_wholeAfter : default), __SparseState(current), \"The contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "return new " + rebaseResult + "(Between(current, current), __conf);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __cur = current.Value!;");
        code.AppendLineAt(3, "var __conflicts = new " + conflictList + "();");
        // Declare rebased locals.
        ComputePublicNames(members, out _, out var __transNames);
        foreach (var member in members)
        {
            if (IsNested(member))
            {
                code.AppendLineAt(
                    3,
                    ChildChangeSet(member, dialect) + "? __r" + member.Id + " = null;"
                );
            }
            else if (IsKeyed(member) || IsDict(member))
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                var trans = __transNames[member.Id];
                var keyType = KeyTypeOf(member);
                code.AppendLineAt(3, "bool __rh" + member.Id + " = false;");
                code.AppendLineAt(3, "bool __rwhole" + member.Id + " = false;");
                code.AppendLineAt(3, opt + " __rwb" + member.Id + " = default;");
                code.AppendLineAt(3, opt + " __rwa" + member.Id + " = default;");
                code.AppendLineAt(
                    3,
                    "global::System.Collections.Generic.List<"
                        + trans
                        + ".Item>? __ritems"
                        + member.Id
                        + " = null;"
                );
                if (IsKeyed(member))
                {
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? __rbO"
                            + member.Id
                            + " = null;"
                    );
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + keyType
                            + ">? __raO"
                            + member.Id
                            + " = null;"
                    );
                }
            }
            else
            {
                var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
                code.AppendLineAt(3, opt + " __rb" + member.Id + " = default;");
                code.AppendLineAt(3, opt + " __ra" + member.Id + " = default;");
                code.AppendLineAt(3, "bool __rh" + member.Id + " = false;");
            }
        }
        if (__hasSparseRb)
            AppendPragmaDisableNullKey(code, 3);
        foreach (var member in members)
        {
            var prop = member.Property.Name;
            var lit = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(prop, true);
            var esc = SparseNaming.EscapeIdentifier(prop);
            code.AppendLineAt(3, "{");
            if (IsNested(member))
            {
                code.AppendLineAt(4, "if (" + NestedField(member) + " is not null)");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    5,
                    "var __nr"
                        + member.Id
                        + " = "
                        + NestedField(member)
                        + ".RebaseOnto(__cur."
                        + esc
                        + ");"
                );
                code.AppendLineAt(5, "foreach (var __c in __nr" + member.Id + ".Conflicts)");
                code.AppendLineAt(5, "{");
                code.AppendLineAt(6, "__conflicts.Add(__c.WithPathPrefix(" + lit + "));");
                code.AppendLineAt(5, "}");
                code.AppendLineAt(
                    5,
                    "__r"
                        + member.Id
                        + " = __nr"
                        + member.Id
                        + ".Patch.IsEmpty ? null : __nr"
                        + member.Id
                        + ".Patch;"
                );
                code.AppendLineAt(4, "}");
            }
            else if (IsKeyed(member))
            {
                AppendKeyedRebaseSparse(code, members, member, esc, lit, runtime, dialect);
            }
            else if (IsDict(member))
            {
                AppendDictRebaseSparse(code, members, member, esc, lit, runtime, dialect);
            }
            else if (member.MergeStrategyType is not null)
            {
                var strat =
                    "Fragment." + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member);
                code.AppendLineAt(4, "if (" + HasField(member) + ")");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    4,
                    "    var __base" + member.Id + " = " + BeforeField(member) + ";"
                );
                code.AppendLineAt(
                    4,
                    "    var __des" + member.Id + " = " + AfterField(member) + ";"
                );
                code.AppendLineAt(4, "    var __curM" + member.Id + " = __cur." + esc + ";");
                code.AppendLineAt(
                    4,
                    "    if ("
                        + strat
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
                        + strat
                        + ".AreEqual(__curM"
                        + member.Id
                        + ".Value, __reb"
                        + member.Id
                        + ".Value))))"
                );
                code.AppendLineAt(5, "        {");
                code.AppendLineAt(6, "            __rh" + member.Id + " = true;");
                code.AppendLineAt(
                    6,
                    "            __rb" + member.Id + " = __curM" + member.Id + ";"
                );
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
                        + " ?? \"The custom merge strategy could not rebase the member.\"));"
                );
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "}");
            }
            else if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
            {
                AppendMergeCollectionRebase(code, member, esc, lit, runtime, dialect);
            }
            else
            {
                code.AppendLineAt(4, "if (" + HasField(member) + ")");
                code.AppendLineAt(4, "{");
                code.AppendLineAt(
                    4,
                    "    var __base" + member.Id + " = " + BeforeField(member) + ";"
                );
                code.AppendLineAt(
                    4,
                    "    var __des" + member.Id + " = " + AfterField(member) + ";"
                );
                code.AppendLineAt(4, "    var __curM" + member.Id + " = __cur." + esc + ";");
                code.AppendLineAt(
                    4,
                    "    if (Fragment.__SparseEqual_"
                        + member.Id
                        + "(__base"
                        + member.Id
                        + ", __curM"
                        + member.Id
                        + "))"
                );
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(5, "        __rh" + member.Id + " = true;");
                code.AppendLineAt(5, "        __rb" + member.Id + " = __curM" + member.Id + ";");
                code.AppendLineAt(5, "        __ra" + member.Id + " = __des" + member.Id + ";");
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(
                    4,
                    "    else if (!Fragment.__SparseEqual_"
                        + member.Id
                        + "(__des"
                        + member.Id
                        + ", __curM"
                        + member.Id
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
                        + member.Id
                        + "), __SparseMember(__des"
                        + member.Id
                        + "), __SparseMember(__curM"
                        + member.Id
                        + "), \"The member conflicts with a concurrent change.\"));"
                );
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "}");
            }
            code.AppendLineAt(3, "}");
        }
        var rargs = new List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            if (IsNested(member))
                rargs.Add("__r" + member.Id);
            else if (IsKeyed(member))
                rargs.AddRange(
                    new[]
                    {
                        "__rh" + member.Id,
                        "__rwhole" + member.Id,
                        "__rwb" + member.Id,
                        "__rwa" + member.Id,
                        "__ritems" + member.Id,
                        "__rbO" + member.Id,
                        "__raO" + member.Id,
                    }
                );
            else if (IsDict(member))
                rargs.AddRange(
                    new[]
                    {
                        "__rh" + member.Id,
                        "__rwhole" + member.Id,
                        "__rwb" + member.Id,
                        "__rwa" + member.Id,
                        "__ritems" + member.Id,
                    }
                );
            else
                rargs.AddRange(
                    new[] { "__rb" + member.Id, "__ra" + member.Id, "__rh" + member.Id }
                );
        }
        code.AppendLineAt(3, "var __rebased = new ChangeSet(" + string.Join(", ", rargs) + ");");
        if (__hasSparseRb)
            AppendPragmaRestoreNullKey(code, 3);
        code.AppendLineAt(3, "return new " + rebaseResult + "(__rebased, __conflicts);");
        code.AppendLineAt(2, "}");
        if (modelType is not null)
        {
            AppendModelRebase(code, modelType, optionalFragment, rebaseResult);
            AppendModelTryApply(
                code,
                modelType,
                optionalFragment,
                dialect.ConflictType,
                ignoredSettablePropertyNames
            );
        }
    }

    private static void AppendModelRebase(
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        string rebaseResult
    )
    {
        code.AppendLineAt(2, "/// <summary>Rebases this change onto an ordinary model.</summary>");
        code.AppendLineAt(
            2,
            "public "
                + rebaseResult
                + " RebaseOnto("
                + modelType
                + " current) => RebaseOnto("
                + optionalFragment
                + ".Present(Fragment.From(current)));"
        );
    }

    private static void AppendModelTryApply(
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        string conflictType,
        ImmutableArray<string> ignoredSettablePropertyNames
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Applies this change to an ordinary model if it can be rebased without conflicts.</summary>"
        );
        code.AppendLineAt(
            2,
            "public bool TryApplyTo("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "return TryApplyTo(current, out updated, out _);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Applies this change to an ordinary model and returns structured conflicts when rebasing fails.</summary>"
        );
        code.AppendLineAt(
            2,
            "public bool TryApplyTo("
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                + modelType
                + "? updated, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var __state = " + optionalFragment + ".Present(Fragment.From(current));"
        );
        code.AppendLineAt(3, "ChangeSet __toApply;");
        code.AppendLineAt(3, "if (__SparseBeforeMatches(__state))");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__toApply = this;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __rebase = RebaseOnto(__state);");
        code.AppendLineAt(4, "if (__rebase.HasConflicts)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "updated = null;");
        code.AppendLineAt(5, "conflicts = __rebase.Conflicts;");
        code.AppendLineAt(5, "return false;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__toApply = __rebase.Patch;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __applied = __toApply.ToPatch().Apply(__state);");
        code.AppendLineAt(
            3,
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"The rebased change does not produce a non-null model root. Use the presence-aware Fragment/Optional API for root presence transitions.\");"
        );
        code.AppendLineAt(3, "var __updatedModel = __applied.Value.ToModel();");
        foreach (
            var ignoredName in ignoredSettablePropertyNames.IsDefault
                ? ImmutableArray<string>.Empty
                : ignoredSettablePropertyNames
        )
        {
            var name = SparseNaming.EscapeIdentifier(ignoredName);
            code.AppendLineAt(3, "__updatedModel." + name + " = current." + name + ";");
        }
        code.AppendLineAt(3, "updated = __updatedModel;");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
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
        code.AppendLineAt(4, "if (" + HasField(member) + ")");
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
