using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the standalone three-way rebase with structured conflicts.</summary>
internal static class SparseFragmentPatchRebaseEmitter
{
    private static string EqualMethod(SparseMemberModel member) =>
        "Fragment.__SparseEqual_" + member.Id;

    public static void AppendPatchRebase(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        var into = target?.PatchOperations ?? code;
        var runtime = dialect.RuntimeNamespace;
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var optionalFragment = runtime + "Optional<Fragment?>";
        var kind = runtime + "FragmentOperationKind";
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
        var rebaseResult = dialect.RebaseResult("Patch");
        var optionsType = SparseRebaseOptionEmitter.OptionsType(dialect);
        var modeType = SparseRebaseOptionEmitter.ModeType(dialect);

        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Rebases a local patch onto a newer sparse state and reports structured conflicts.</summary>"
            );
            code.AppendLineAt(
                2,
                "public static "
                    + rebaseResult
                    + " "
                    + prefix
                    + "Rebase("
                    + optionalFragment
                    + " baseState, Patch local, "
                    + optionalFragment
                    + " currentState, "
                    + optionsType
                    + "? options = null) => "
                    + target.PatchOperationsType
                    + "."
                    + prefix
                    + "Rebase(baseState, local, currentState, options);"
            );
        }
        AppendRebaseStateHelpers(into, runtime);
        into.AppendLineAt(
            2,
            "/// <summary>Model-rooted path factory for rebase conflicts.</summary>"
        );
        into.AppendLineAt(
            2,
            "internal static "
                + pathType
                + " __SparseRootPath => "
                + pathType
                + ".Root(typeof("
                + (string.IsNullOrEmpty(modelType) ? "global::System.Object" : modelType)
                + "));"
        );
        SparseRebaseOptionEmitter.AppendHelpers(into, dialect);
        AppendRebaseHeader(
            into,
            prefix,
            rebaseResult,
            optionalFragment,
            kind,
            conflict,
            conflictKind,
            conflictList,
            optionsType,
            runtime,
            target is not null
        );

        into.AppendLineAt(3, "var baseFragment = baseState.Value!;");
        into.AppendLineAt(3, "var currentFragment = currentState.Value!;");

        foreach (var member in members)
        {
            into.AppendLineAt(3, "{");
            if (member.ChildModel is not null)
                AppendNestedMemberRebase(into, member, conflict, conflictKind, dialect);
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
                AppendCollectionMemberRebase(into, member, conflict, conflictKind, dialect);
            else
                AppendScalarMemberRebase(
                    into,
                    member,
                    kind,
                    conflict,
                    conflictKind,
                    dialect,
                    modeType
                );
            into.AppendLineAt(3, "}");
        }

        into.AppendLineAt(3, "return new " + rebaseResult + "(result, conflicts);");
        into.AppendLineAt(2, "}");
    }

    private static void AppendRebaseStateHelpers(SharedIndentedBuilder code, string runtime)
    {
        code.AppendIndent(2)
            .Append(
                "private static "
                    + runtime
                    + "Optional<object?> __SparseState("
                    + runtime
                    + "Optional<Fragment?>"
                    + " state) => state.IsPresent ? "
                    + runtime
                    + "Optional<object?>.Present((object?)state.Value) : "
                    + runtime
                    + "Optional<object?>.Missing;"
            )
            .AppendLine();
        code.AppendIndent(2)
            .Append(
                "private static "
                    + runtime
                    + "Optional<object?> __SparseMember<T>("
                    + runtime
                    + "Optional<T> value) => value.IsPresent ? "
                    + runtime
                    + "Optional<object?>.Present((object?)value.Value) : "
                    + runtime
                    + "Optional<object?>.Missing;"
            )
            .AppendLine();
    }

    private static void AppendRebaseHeader(
        SharedIndentedBuilder code,
        string prefix,
        string rebaseResult,
        string optionalFragment,
        string kind,
        string conflict,
        string conflictKind,
        string conflictList,
        string optionsType,
        string runtime,
        bool isRelocated = false
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Rebases a local patch onto a newer sparse state and reports structured conflicts.</summary>"
        );
        code.AppendLineAt(
            2,
            (isRelocated ? "internal static " : "public static ")
                + rebaseResult
                + " "
                + prefix
                + "Rebase("
                + optionalFragment
                + " baseState, Patch local, "
                + optionalFragment
                + " currentState, "
                + optionsType
                + "? options = null)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (local is null) throw new global::System.ArgumentNullException(nameof(local));"
        );
        code.AppendLineAt(
            3,
            "if (local.__SparseIsEmpty()) return " + rebaseResult + ".Success(new Patch());"
        );
        code.AppendLineAt(3, "var result = new Patch();");
        code.AppendLineAt(3, "var conflicts = new " + conflictList + "();");
        code.AppendLineAt(3, "if (__SparseIsRedacted(options, \"\"))");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__SparseRejectsRedacted(options))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".RedactedBefore, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, "
                + runtime
                + "Optional<object?>.Missing, \""
                + SparseRebaseOptionEmitter.RedactedReason
                + "\"));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "    result = local." + prefix + "Compose(new Patch());");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return new " + rebaseResult + "(result, conflicts);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "var desiredState = local.Apply(baseState);");
        code.AppendLineAt(3, "if (local.__sparse_whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (Fragment.__SparseAreEqual(baseState, currentState))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "    result = local." + prefix + "Compose(new Patch());");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (!Fragment.__SparseAreEqual(desiredState, currentState))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            4,
            "    conflicts.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".WholeContribution, __SparseState(baseState), __SparseState(desiredState), __SparseState(currentState), \"The whole contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return new " + rebaseResult + "(result, conflicts);");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!baseState.IsPresent || !currentState.IsPresent || baseState.Value is null || currentState.Value is null || !desiredState.IsPresent || desiredState.Value is null)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (Fragment.__SparseAreEqual(baseState, currentState))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(4, "    result = local." + prefix + "Compose(new Patch());");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (!Fragment.__SparseAreEqual(desiredState, currentState))");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            4,
            "    conflicts.Add(new "
                + conflict
                + "(__SparseRootPath, "
                + conflictKind
                + ".WholeContribution, __SparseState(baseState), __SparseState(desiredState), __SparseState(currentState), \"The contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "return new " + rebaseResult + "(result, conflicts);");
        code.AppendLineAt(3, "}");
    }

    private static void AppendCollectionMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var field = dialect.MemberField(member);
        const string baseMember = "baseMember";
        const string currentMember = "currentMember";
        const string desiredMember = "desiredMember";
        var equality = EqualMethod(member);

        var pathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
        code.AppendLineAt(
            4,
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && __SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (__SparseRejectsRedacted(options))");
        code.AppendLineAt(5, "{");
        SparseRebaseOptionEmitter.AppendRedactedConflict(
            code,
            6,
            dialect.RuntimeNamespace,
            conflict,
            dialect,
            "__SparseRootPath.Member(" + pathLit + ")",
            "conflicts"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "result." + field + " = local." + field + ";");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && !__SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLineAt(5, "var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLineAt(5, "var " + desiredMember + " = " + baseMember + ";");
        code.AppendLineAt(5, "var __applyFailed" + member.Id + " = false;");
        code.AppendLineAt(5, "try");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, desiredMember + " = local." + field + ".Apply(" + baseMember + ");");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "catch (global::System.InvalidOperationException ex) { conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + baseMember
                + "), __SparseMember("
                + currentMember
                + "), ex.Message)); __applyFailed"
                + member.Id
                + " = true; }"
        );
        code.AppendLineAt(5, "if (!__applyFailed" + member.Id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "if (" + equality + "(" + baseMember + ", " + currentMember + "))");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(7, "result." + field + " = local." + field + ";");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else if (" + baseMember + ".IsPresent && " + currentMember + ".IsPresent)"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "var nested = "
                + SparseFragmentPatchEmitter.GetCollectionPatchName(dialect, member)
                + ".Rebase("
                + baseMember
                + ", local."
                + field
                + ", "
                + currentMember
                + ");"
        );
        code.AppendLineAt(7, "result." + field + " = nested.Rebased;");
        code.AppendLineAt(7, "foreach (var nestedConflict in nested.Conflicts)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "conflicts.Add(nestedConflict.WithPathPrefix(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + ")));"
        );
        code.AppendLineAt(7, "}");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else if (!" + equality + "(" + desiredMember + ", " + currentMember + "))"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The collection contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }

    private static void AppendNestedMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var field = dialect.MemberField(member);
        const string baseMember = "baseMember";
        const string currentMember = "currentMember";
        const string desiredMember = "desiredMember";
        var equality = EqualMethod(member);
        var pathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);

        code.AppendLineAt(
            4,
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && __SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "if (__SparseRejectsRedacted(options))");
        code.AppendLineAt(5, "{");
        SparseRebaseOptionEmitter.AppendRedactedConflict(
            code,
            6,
            dialect.RuntimeNamespace,
            conflict,
            dialect,
            "__SparseRootPath.Member(" + pathLit + ")",
            "conflicts"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "result." + field + " = local." + field + ";");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "if (local."
                + field
                + " is not null && !local."
                + field
                + ".__SparseIsEmpty() && !__SparseIsRedacted(options, "
                + pathLit
                + "))"
        );
        code.AppendLineAt(4, "{");
        code.AppendLineAt(5, "var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLineAt(5, "var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLineAt(
            5,
            "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
        );
        code.AppendLineAt(5, "if (" + equality + "(" + baseMember + ", " + currentMember + "))");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "result." + field + " = local." + field + ";");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "else if (" + baseMember + ".IsPresent && " + currentMember + ".IsPresent)"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "var nested = "
                + dialect.ChildPatchName(member)
                + "."
                + member.ChildModel!.Value.PatchApiPrefix
                + "Rebase("
                + baseMember
                + ", local."
                + field
                + ", "
                + currentMember
                + ", options?.Nest("
                + pathLit
                + "));"
        );
        code.AppendLineAt(6, "result." + field + " = nested.Rebased;");
        code.AppendLineAt(6, "foreach (var nestedConflict in nested.Conflicts)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "conflicts.Add(nestedConflict.WithPathPrefix(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + ")));"
        );
        code.AppendLineAt(6, "}");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "else if (!" + equality + "(" + desiredMember + ", " + currentMember + "))"
        );
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "conflicts.Add(new "
                + conflict
                + "(__SparseRootPath.Member("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "), "
                + conflictKind
                + ".Nested, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The nested contribution conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(4, "}");
    }

    private static void AppendScalarMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string kind,
        string conflict,
        string conflictKind,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modeType
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var field = dialect.MemberField(member);
        const string baseMember = "baseMember";
        const string currentMember = "currentMember";
        const string desiredMember = "desiredMember";
        var equality = EqualMethod(member);
        var operation = runtime + "FragmentOperation";
        string scalarKind;
        if (member.MergeMode == SparseMergeModes.Append)
        {
            scalarKind = conflictKind + ".CollectionAppend";
        }
        else if (member.MergeMode == SparseMergeModes.SetUnion)
        {
            scalarKind = conflictKind + ".CollectionSetUnion";
        }
        else
        {
            scalarKind = conflictKind + ".Scalar";
        }
        if (member.MergeStrategyType is not null || member.RebasePolicyType is not null)
        {
            SparseFragmentPatchScalarRebaseEmitter.AppendCustomStrategyMemberRebase(
                code,
                member,
                field,
                kind,
                baseMember,
                desiredMember,
                currentMember,
                conflict,
                conflictKind,
                4,
                dialect
            );
        }
        else if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
        {
            var mergePathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLineAt(
                4,
                "if (local."
                    + field
                    + ".Kind != "
                    + kind
                    + ".Keep && __SparseIsRedacted(options, "
                    + mergePathLit
                    + "))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "if (__SparseRejectsRedacted(options))");
            code.AppendLineAt(5, "{");
            SparseRebaseOptionEmitter.AppendRedactedConflict(
                code,
                6,
                dialect.RuntimeNamespace,
                conflict,
                dialect,
                "__SparseRootPath.Member(" + mergePathLit + ")",
                "conflicts"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "result." + field + " = local." + field + ";");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
            code.AppendLineAt(
                4,
                "if (local."
                    + field
                    + ".Kind != "
                    + kind
                    + ".Keep && !__SparseIsRedacted(options, "
                    + mergePathLit
                    + "))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "var " + baseMember + " = baseFragment." + name + ";");
            code.AppendLineAt(5, "var " + currentMember + " = currentFragment." + name + ";");
            code.AppendLineAt(
                5,
                "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
            );
            code.AppendLineAt(5, "var handled = false;");
            code.AppendLineAt(
                5,
                "if ("
                    + baseMember
                    + ".IsPresent && "
                    + baseMember
                    + ".Value is not null && "
                    + currentMember
                    + ".IsPresent && "
                    + currentMember
                    + ".Value is not null && "
                    + desiredMember
                    + ".IsPresent && "
                    + desiredMember
                    + ".Value is not null)"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "handled = true;");
            if (
                member.MergeMode == SparseMergeModes.SetUnion
                && member.Collection.CloneKind == SparseCloneCollectionKind.Set
            )
            {
                SparseFragmentPatchCollectionRebaseEmitter.AppendTypedSetUnionRebase(
                    code,
                    member,
                    field,
                    operation,
                    dialect
                );
            }
            else if (
                member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
                && member.Collection.CloneKind
                    is SparseCloneCollectionKind.Array
                        or SparseCloneCollectionKind.List
                && member.Collection.ElementType.UsesDefaultScalarEquality
            )
            {
                SparseFragmentPatchCollectionRebaseEmitter.AppendTypedSequenceRebase(
                    code,
                    member,
                    field,
                    operation,
                    dialect
                );
            }
            else
            {
                SparseFragmentPatchCollectionRebaseEmitter.AppendBoxedCollectionRebase(
                    code,
                    member,
                    field,
                    operation,
                    dialect
                );
            }
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                    + "), "
                    + scalarKind
                    + ", __SparseMember("
                    + baseMember
                    + "), __SparseMember("
                    + desiredMember
                    + "), __SparseMember("
                    + currentMember
                    + "), reason));"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "if (!handled)");
            code.AppendLineAt(5, "{");
            SparseFragmentPatchScalarRebaseEmitter.EmitScalarRebase(
                code,
                field,
                member.Property.Name,
                baseMember,
                desiredMember,
                currentMember,
                equality,
                scalarKind,
                conflict,
                6
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
        }
        else
        {
            var plainPathLit = SymbolDisplay.FormatLiteral(member.Property.Name, true);
            code.AppendLineAt(4, "if (local." + field + ".Kind != " + kind + ".Keep)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "var " + baseMember + " = baseFragment." + name + ";");
            code.AppendLineAt(5, "var " + currentMember + " = currentFragment." + name + ";");
            code.AppendLineAt(
                5,
                "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
            );
            code.AppendLineAt(
                5,
                "var __mode"
                    + member.Id
                    + " = options is null ? "
                    + modeType
                    + ".Default : options.DefaultRebaseMode;"
            );
            code.AppendLineAt(5, "if (__SparseIsRedacted(options, " + plainPathLit + "))");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "if (__SparseRejectsRedacted(options))");
            code.AppendLineAt(6, "{");
            SparseRebaseOptionEmitter.AppendRedactedConflict(
                code,
                7,
                dialect.RuntimeNamespace,
                conflict,
                dialect,
                "__SparseRootPath.Member(" + plainPathLit + ")",
                "conflicts"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(7, "result." + field + " = local." + field + ";");
            code.AppendLineAt(6, "}");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else if (__mode"
                    + member.Id
                    + " == "
                    + modeType
                    + ".PreferIncoming && !"
                    + equality
                    + "("
                    + desiredMember
                    + ", "
                    + currentMember
                    + "))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "result." + field + " = local." + field + ";");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else if (__mode"
                    + member.Id
                    + " != "
                    + modeType
                    + ".PreferCurrent && "
                    + equality
                    + "("
                    + baseMember
                    + ", "
                    + currentMember
                    + "))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "result." + field + " = local." + field + ";");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(
                5,
                "else if (__mode"
                    + member.Id
                    + " != "
                    + modeType
                    + ".PreferCurrent && !"
                    + equality
                    + "("
                    + desiredMember
                    + ", "
                    + currentMember
                    + ") && !"
                    + equality
                    + "("
                    + desiredMember
                    + ", "
                    + baseMember
                    + "))"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "conflicts.Add(new "
                    + conflict
                    + "(__SparseRootPath.Member("
                    + plainPathLit
                    + "), "
                    + scalarKind
                    + ", __SparseMember("
                    + baseMember
                    + "), __SparseMember("
                    + desiredMember
                    + "), __SparseMember("
                    + currentMember
                    + "), \"The member conflicts with a concurrent change.\"));"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
        }
    }
}
