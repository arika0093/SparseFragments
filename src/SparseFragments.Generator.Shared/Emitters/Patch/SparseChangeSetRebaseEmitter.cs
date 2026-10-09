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

/// <summary>Rebase core: whole-contribution, nested, keyed, and dictionary dispatch.</summary>
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
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        bool canApplyInPlace = false,
        SparseOperationTarget? target = null
    )
    {
        _ = between;
        _ = prefix;
        var shell = code;
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
        var optionsType = SparseRebaseOptionEmitter.OptionsType(dialect);
        var modeType = SparseRebaseOptionEmitter.ModeType(dialect);
        var missingValue = runtime + "Optional<object?>.Missing";
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <remarks>Redacted-before members pass through as explicit operations unless the options reject them.</remarks>"
            );
            code.AppendLineAt(
                2,
                "public "
                    + rebaseResult
                    + " RebaseOnto("
                    + optionalFragment
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".RebaseOnto(this, current, options);"
            );
            code = target.ChangeSetOperations;
        }
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
        SparseRebaseOptionEmitter.AppendHelpers(code, dialect);
        code.AppendLineAt(
            2,
            "/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Redacted-before members pass through as explicit operations unless the options reject them.</remarks>"
        );
        code.AppendLineAt(
            2,
            (target is null ? "public " : "internal static ")
                + rebaseResult
                + " RebaseOnto("
                + (target is null ? string.Empty : "ChangeSet self, ")
                + optionalFragment
                + " current, "
                + optionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        if (target is not null)
        {
            AppendSelfAliases(code, members);
        }
        var __hasSparseRb = members.Any(static m => IsKeyed(m) || IsDict(m));
        code.AppendLineAt(3, "if (__sparse_hasWhole)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__SparseIsRedacted(options, \"\"))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "    if (__SparseRejectsRedacted(options))");
        code.AppendLineAt(4, "    {");
        code.AppendLineAt(4, "        var __rconf = new " + conflictList + "();");
        code.AppendLineAt(
            4,
            "        __rconf.Add(new "
                + conflict
                + "(new string[0], "
                + conflictKind
                + ".RedactedBefore, "
                + missingValue
                + ", "
                + missingValue
                + ", "
                + missingValue
                + ", \""
                + SparseRebaseOptionEmitter.RedactedReason
                + "\"));"
        );
        code.AppendLineAt(
            4,
            "        return new " + rebaseResult + "(Between(current, current), __rconf);"
        );
        code.AppendLineAt(4, "    }");
        code.AppendLineAt(
            4,
            "    return " + rebaseResult + ".Success(Between(current, __sparse_wholeAfter));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            target is null ? "var __local = ToPatch();" : "var __local = ToPatch(self);"
        );
        code.AppendLineAt(
            4,
            "var __rb = " + rebase + "(__sparse_wholeBefore, __local, current, options);"
        );
        code.AppendLineAt(4, "if (__rb.Conflicts.Count == 0 && __rb.Rebased.__SparseIsEmpty())");
        code.AppendLineAt(5, "return " + rebaseResult + ".Success(Between(current, current));");
        code.AppendLineAt(4, "var __ra = __rb.Rebased.Apply(current);");
        code.AppendLineAt(
            4,
            "return new " + rebaseResult + "(Between(current, __ra), __rb.Conflicts);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            target is null
                ? "if (IsEmpty) return " + rebaseResult + ".Success(Between(current, current));"
                : "if (self.IsEmpty) return "
                    + rebaseResult
                    + ".Success(Between(current, current));"
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
                code.AppendLineAt(4, "    if (__SparseIsRedacted(options, " + lit + "))");
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(4, "        if (__SparseRejectsRedacted(options))");
                code.AppendLineAt(4, "        {");
                SparseRebaseOptionEmitter.AppendRedactedConflict(
                    code,
                    4,
                    runtime,
                    conflict,
                    dialect,
                    "new string[] { " + lit + " }",
                    "__conflicts"
                );
                code.AppendLineAt(4, "        }");
                code.AppendLineAt(
                    4,
                    "        else __r" + member.Id + " = " + NestedField(member) + ";"
                );
                code.AppendLineAt(4, "    }");
                code.AppendLineAt(4, "    else");
                code.AppendLineAt(4, "    {");
                code.AppendLineAt(
                    5,
                    "var __nr"
                        + member.Id
                        + " = "
                        + NestedField(member)
                        + ".RebaseOnto(__cur."
                        + esc
                        + ", options?.Nest("
                        + lit
                        + "));"
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
                        + ".Rebased.IsEmpty ? null : __nr"
                        + member.Id
                        + ".Rebased;"
                );
                code.AppendLineAt(4, "    }");
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
                var reconciler = strat;
                var equality =
                    strat
                    + ".AreEqual(__curM"
                    + member.Id
                    + ".Value, __reb"
                    + member.Id
                    + ".Value)";
                var reasonFallback = "The custom merge strategy could not rebase the member.";
                if (member.RebasePolicyType is not null)
                {
                    reconciler =
                        "Fragment."
                        + SparseFragmentPatchEmitter.GetRebasePolicyField(dialect, member);
                    equality =
                        reconciler
                        + ".AreEqual(__curM"
                        + member.Id
                        + ".Value, __reb"
                        + member.Id
                        + ".Value)";
                    reasonFallback = "The custom rebase policy could not rebase the member.";
                }
                SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
                    code,
                    member,
                    lit,
                    runtime,
                    conflict,
                    dialect,
                    HasField(member),
                    SparseChangeSetMemberRebaseEmitter.ScalarPassthrough(member, esc)
                );
                code.AppendLineAt(4, "if (" + HasField(member) + " && !__red" + member.Id + ")");
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
                SparseChangeSetMemberRebaseEmitter.AppendReconcilerAttempt(
                    code,
                    member,
                    lit,
                    reconciler,
                    equality,
                    reasonFallback,
                    conflict,
                    conflictKind
                );
                code.AppendLineAt(4, "}");
            }
            else if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
            {
                SparseChangeSetMemberRebaseEmitter.AppendMergeCollectionRebase(
                    code,
                    member,
                    esc,
                    lit,
                    runtime,
                    dialect
                );
            }
            else
            {
                SparseChangeSetMemberRebaseEmitter.AppendRedactedGuard(
                    code,
                    member,
                    lit,
                    runtime,
                    conflict,
                    dialect,
                    HasField(member),
                    SparseChangeSetMemberRebaseEmitter.ScalarPassthrough(member, esc)
                );
                code.AppendLineAt(4, "if (" + HasField(member) + " && !__red" + member.Id + ")");
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
                if (member.RebasePolicyType is not null)
                {
                    var policy =
                        "Fragment."
                        + SparseFragmentPatchEmitter.GetRebasePolicyField(dialect, member);
                    SparseChangeSetMemberRebaseEmitter.AppendReconcilerAttempt(
                        code,
                        member,
                        lit,
                        policy,
                        policy
                            + ".AreEqual(__curM"
                            + member.Id
                            + ".Value, __reb"
                            + member.Id
                            + ".Value)",
                        "The custom rebase policy could not rebase the member.",
                        conflict,
                        conflictKind
                    );
                }
                else
                {
                    SparseChangeSetMemberRebaseEmitter.AppendScalarModeRebase(
                        code,
                        member,
                        lit,
                        conflictKind,
                        conflict,
                        modeType
                    );
                }
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
            SparseChangeSetModelRebaseEmitter.AppendModelRebase(
                shell,
                code,
                modelType,
                optionalFragment,
                rebaseResult,
                optionsType,
                target
            );
            AppendModelTryApply(
                shell,
                code,
                modelType,
                optionalFragment,
                dialect.ConflictType,
                dialect.ConflictKindType,
                dialect.InPlaceWriteUnavailableKindMemberName,
                runtime,
                members,
                optionsType,
                ignoredSettablePropertyNames,
                canApplyInPlace,
                target
            );
        }
    }

    private static void AppendModelTryApply(
        SharedIndentedBuilder shell,
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        string conflictType,
        string conflictKindType,
        string? inPlaceWriteUnavailableKindMemberName,
        string runtime,
        ImmutableArray<SparseMemberModel> members,
        string optionsType,
        ImmutableArray<string> ignoredSettablePropertyNames,
        bool canApplyInPlace,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            shell.AppendLineAt(
                2,
                "/// <summary>Applies this change to an ordinary model if it can be rebased without conflicts.</summary>"
            );
            shell.AppendLineAt(
                2,
                "public bool TryApplyTo("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".TryApplyTo(this, current, out updated, options);"
            );
            shell.AppendLineAt(
                2,
                "/// <summary>Applies this change to an ordinary model and returns structured conflicts when rebasing fails.</summary>"
            );
            shell.AppendLineAt(
                2,
                "public bool TryApplyTo("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".TryApplyTo(this, current, out updated, out conflicts, options);"
            );
            code.AppendLineAt(
                2,
                "/// <summary>Applies a change to an ordinary model if it can be rebased without conflicts.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal static bool TryApplyTo("
                    + "ChangeSet self, "
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, "
                    + optionsType
                    + "? options = null)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "return TryApplyTo(self, current, out updated, out _, options);");
            code.AppendLineAt(2, "}");
            code.AppendLineAt(
                2,
                "/// <summary>Applies a change to an ordinary model and returns structured conflicts when rebasing fails.</summary>"
            );
            code.AppendLineAt(
                2,
                "internal static bool TryApplyTo("
                    + "ChangeSet self, "
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out "
                    + modelType
                    + "? updated, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null)"
            );
            AppendTryApplyBody(
                code,
                optionalFragment,
                ignoredSettablePropertyNames,
                selfName: "self"
            );
        }
        else
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
                    + "? updated, "
                    + optionsType
                    + "? options = null)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "return TryApplyTo(current, out updated, out _, options);");
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
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null)"
            );
            AppendTryApplyBody(
                code,
                optionalFragment,
                ignoredSettablePropertyNames,
                selfName: null
            );
        }
        if (
            canApplyInPlace
            && (
                members.All(static member =>
                    !member.Property.IsReadOnly && !member.Property.IsInitOnly
                ) || inPlaceWriteUnavailableKindMemberName is not null
            )
        )
        {
            AppendModelTryApplyInPlace(
                shell,
                code,
                modelType,
                conflictType,
                conflictKindType,
                inPlaceWriteUnavailableKindMemberName,
                runtime,
                members,
                optionsType,
                target
            );
        }
    }

    private static void AppendTryApplyBody(
        SharedIndentedBuilder code,
        string optionalFragment,
        ImmutableArray<string> ignoredSettablePropertyNames,
        string? selfName
    )
    {
        var self = selfName is null ? string.Empty : selfName + ".";
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var __state = " + optionalFragment + ".Present(Fragment.From(current));"
        );
        code.AppendLineAt(3, "ChangeSet __toApply;");
        code.AppendLineAt(3, "if (options is null && " + self + "__SparseBeforeMatches(__state))");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "__toApply = " + (selfName ?? "this") + ";");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            selfName is null
                ? "var __rebase = RebaseOnto(__state, options);"
                : "var __rebase = RebaseOnto(" + selfName + ", __state, options);"
        );
        code.AppendLineAt(4, "if (__rebase.HasConflicts)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "updated = null;");
        code.AppendLineAt(5, "conflicts = __rebase.Conflicts;");
        code.AppendLineAt(5, "return false;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__toApply = __rebase.Rebased;");
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

    private static void AppendModelTryApplyInPlace(
        SharedIndentedBuilder shell,
        SharedIndentedBuilder code,
        string modelType,
        string conflictType,
        string conflictKindType,
        string? inPlaceWriteUnavailableKindMemberName,
        string runtime,
        ImmutableArray<SparseMemberModel> members,
        string optionsType,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            shell.AppendLineAt(
                2,
                "/// <summary>Applies this change to an existing model after checking its before-state.</summary>"
            );
            shell.AppendLineAt(
                2,
                "/// <remarks>Returns an in-place write conflict when the change includes an immutable member.</remarks>"
            );
            shell.AppendLineAt(
                2,
                "/// <param name=\"current\">The existing model instance to update.</param>"
            );
            shell.AppendLineAt(
                2,
                "/// <param name=\"conflicts\">Structured conflicts when rebasing fails or an immutable member cannot be written.</param>"
            );
            shell.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
            shell.AppendLineAt(
                2,
                "/// <returns><see langword=\"true\"/> when the update was applied.</returns>"
            );
            shell.AppendLineAt(
                2,
                "public bool TryApplyInPlace("
                    + modelType
                    + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".TryApplyInPlace(this, current, out conflicts, options);"
            );
            shell.AppendLineAt(
                2,
                "/// <summary>Applies this change to an existing model or throws when its before-state conflicts or it includes an immutable member.</summary>"
            );
            shell.AppendLineAt(
                2,
                "/// <param name=\"current\">The existing model instance to update.</param>"
            );
            shell.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
            shell.AppendLineAt(
                2,
                "public void ApplyInPlace("
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".ApplyInPlace(this, current, options);"
            );
        }
        code.AppendLineAt(
            2,
            "/// <summary>Applies this change to an existing model after checking its before-state.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Returns an in-place write conflict when the change includes an immutable member.</remarks>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"current\">The existing model instance to update.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"conflicts\">Structured conflicts when rebasing fails or an immutable member cannot be written.</param>"
        );
        code.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLineAt(
            2,
            "/// <returns><see langword=\"true\"/> when the update was applied.</returns>"
        );
        code.AppendLineAt(
            2,
            (
                target is null
                    ? "public bool TryApplyInPlace("
                    : "internal static bool TryApplyInPlace(ChangeSet self, "
            )
                + modelType
                + " current, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts, "
                + optionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (current is null) throw new global::System.ArgumentNullException(nameof(current));"
        );
        code.AppendLineAt(
            3,
            "var __state = " + runtime + "Optional<Fragment?>.Present(Fragment.From(current));"
        );
        code.AppendLineAt(3, "ChangeSet __toApply;");
        code.AppendLineAt(
            3,
            target is null
                ? "if (options is null && __SparseBeforeMatches(__state))"
                : "if (options is null && self.__SparseBeforeMatches(__state))"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, target is null ? "__toApply = this;" : "__toApply = self;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "else");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            target is null
                ? "var __rebase = RebaseOnto(__state, options);"
                : "var __rebase = RebaseOnto(self, __state, options);"
        );
        code.AppendLineAt(4, "if (__rebase.HasConflicts)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "conflicts = __rebase.Conflicts;");
        code.AppendLineAt(5, "return false;");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "__toApply = __rebase.Rebased;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var __patch = __toApply.ToPatch();");
        code.AppendLineAt(3, "var __applied = __patch.Apply(__state);");
        code.AppendLineAt(
            3,
            "if (!__applied.IsPresent || __applied.Value is null) throw new global::System.InvalidOperationException(\"The rebased change does not produce a non-null model root. Use the presence-aware Fragment/Optional API for root presence transitions.\");"
        );
        code.AppendLineAt(3, "var __updatedModel = __applied.Value.ToModel();");
        code.AppendLineAt(3, "var __sparse_inPlaceResult = __patch.ApplyInPlace(current);");
        code.AppendLineAt(3, "if (!__sparse_inPlaceResult.Succeeded)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "var __sparse_inPlaceConflicts = new global::System.Collections.Generic.List<"
                + conflictType
                + ">();"
        );
        foreach (
            var member in members.Where(static member =>
                member.Property.IsReadOnly || member.Property.IsInitOnly
            )
        )
        {
            var propertyName = SparseNaming.EscapeIdentifier(member.Property.Name);
            var property = "nameof(" + modelType + "." + propertyName + ")";
            code.AppendLineAt(
                4,
                "if (global::System.Linq.Enumerable.Contains(__sparse_inPlaceResult.UnsupportedMembers, "
                    + property
                    + "))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "__sparse_inPlaceConflicts.Add(new "
                    + conflictType
                    + "(new string[] { "
                    + property
                    + " }, "
                    + conflictKindType
                    + "."
                    + inPlaceWriteUnavailableKindMemberName
                    + ", "
                    + runtime
                    + "Optional<object?>.Present((object?)current."
                    + propertyName
                    + "), "
                    + runtime
                    + "Optional<object?>.Present((object?)__updatedModel."
                    + propertyName
                    + "), "
                    + runtime
                    + "Optional<object?>.Present((object?)current."
                    + propertyName
                    + "), \"The member cannot be changed in place because it is immutable.\"));"
            );
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(
            4,
            "conflicts = global::System.Array.AsReadOnly(__sparse_inPlaceConflicts.ToArray());"
        );
        code.AppendLineAt(4, "return false;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "conflicts = null;");
        code.AppendLineAt(3, "return true;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Applies this change to an existing model or throws when its before-state conflicts or it includes an immutable member.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"current\">The existing model instance to update.</param>"
        );
        code.AppendLineAt(2, "/// <param name=\"options\">Optional rebase behavior.</param>");
        code.AppendLineAt(
            2,
            (
                target is null
                    ? "public void ApplyInPlace("
                    : "internal static void ApplyInPlace(ChangeSet self, "
            )
                + modelType
                + " current, "
                + optionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            target is null
                ? "if (!TryApplyInPlace(current, out var conflicts, options)) throw new global::System.InvalidOperationException(\"The change set cannot be applied in place because it conflicts or includes immutable members.\");"
                : "if (!TryApplyInPlace(self, current, out var conflicts, options)) throw new global::System.InvalidOperationException(\"The change set cannot be applied in place because it conflicts or includes immutable members.\");"
        );
        code.AppendLineAt(2, "}");
    }
}
