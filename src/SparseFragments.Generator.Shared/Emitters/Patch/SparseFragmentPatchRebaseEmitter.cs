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
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        _ = modelType;
        var runtime = dialect.RuntimeNamespace;
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var optionalFragment = runtime + "Optional<Fragment?>";
        var kind = runtime + "FragmentOperationKind";
        var conflict = dialect.ConflictType;
        var conflictKind = dialect.ConflictKindType;
        var conflictList = "global::System.Collections.Generic.List<" + dialect.ConflictType + ">";
        var rebaseResult = dialect.RebaseResult("Patch");

        AppendRebaseStateHelpers(code, runtime);
        AppendRebaseHeader(
            code,
            prefix,
            rebaseResult,
            optionalFragment,
            kind,
            conflict,
            conflictKind,
            conflictList
        );

        code.AppendLineAt(3, "var baseFragment = baseState.Value!;");
        code.AppendLineAt(3, "var currentFragment = currentState.Value!;");

        foreach (var member in members)
        {
            code.AppendLineAt(3, "{");
            if (member.ChildModel is not null)
                AppendNestedMemberRebase(code, member, conflict, conflictKind, dialect);
            else if (SparseFragmentPatchEmitter.IsCollectionPatch(member))
                AppendCollectionMemberRebase(code, member, conflict, conflictKind, dialect);
            else
                AppendScalarMemberRebase(code, member, kind, conflict, conflictKind, dialect);
            code.AppendLineAt(3, "}");
        }

        code.AppendLineAt(3, "return new " + rebaseResult + "(result, conflicts);");
        code.AppendLineAt(2, "}");
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
        string conflictList
    )
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
                + " currentState)"
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
        code.AppendLineAt(3, "var desiredState = local.Apply(baseState);");
        code.AppendLineAt(3, "if (local.__sparse_whole.Kind != " + kind + ".Unchanged)");
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
                + "(new string[0], "
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
                + "(new string[0], "
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

        code.AppendLineAt(
            4,
            "if (local." + field + " is not null && !local." + field + ".__SparseIsEmpty())"
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
                + "(new string[] { "
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + " }, "
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
        code.AppendLineAt(7, "result." + field + " = nested.Patch;");
        code.AppendLineAt(7, "foreach (var nestedConflict in nested.Conflicts)");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "conflicts.Add(nestedConflict.WithPathPrefix("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "));"
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
                + "(new string[] { "
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + " }, "
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

        code.AppendLineAt(
            4,
            "if (local." + field + " is not null && !local." + field + ".__SparseIsEmpty())"
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
                + ");"
        );
        code.AppendLineAt(6, "result." + field + " = nested.Patch;");
        code.AppendLineAt(6, "foreach (var nestedConflict in nested.Conflicts)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "conflicts.Add(nestedConflict.WithPathPrefix("
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + "));"
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
                + "(new string[] { "
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + " }, "
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
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
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
        if (member.MergeStrategyType is not null)
        {
            AppendCustomStrategyMemberRebase(
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
            code.AppendLineAt(4, "if (local." + field + ".Kind != " + kind + ".Unchanged)");
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
                AppendTypedSetUnionRebase(code, member, field, operation, dialect);
            }
            else if (
                member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
                && member.Collection.CloneKind
                    is SparseCloneCollectionKind.Array
                        or SparseCloneCollectionKind.List
                && member.Collection.ElementType.UsesDefaultScalarEquality
            )
            {
                AppendTypedSequenceRebase(code, member, field, operation, dialect);
            }
            else
            {
                AppendBoxedCollectionRebase(code, member, field, operation, dialect);
            }
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "conflicts.Add(new "
                    + conflict
                    + "(new string[] { "
                    + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                    + " }, "
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
            EmitScalarRebase(
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
            code.AppendLineAt(4, "if (local." + field + ".Kind != " + kind + ".Unchanged)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "var " + baseMember + " = baseFragment." + name + ";");
            code.AppendLineAt(5, "var " + currentMember + " = currentFragment." + name + ";");
            code.AppendLineAt(
                5,
                "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
            );
            EmitScalarRebase(
                code,
                field,
                member.Property.Name,
                baseMember,
                desiredMember,
                currentMember,
                equality,
                scalarKind,
                conflict,
                5
            );
            code.AppendLineAt(4, "}");
        }
    }

    private static void AppendTypedSetUnionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string operation,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLineAt(6, "var beforeValues = baseMember.Value!;");
        code.AppendLineAt(6, "var currentValues = currentMember.Value!;");
        code.AppendLineAt(6, "var desiredValues = desiredMember.Value!;");
        code.AppendLineAt(
            6,
            "if ("
                + dialect.RuntimeFacade
                + ".TryRebaseSetUnion<"
                + member.Collection.ElementType.Name
                + ">(beforeValues, desiredValues, currentValues, out var rebasedValues, out var reason))"
        );
        code.AppendLineAt(6, "{");
        var operationType =
            operation + "<" + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member) + ">";
        code.AppendLineAt(7, "result." + field + " = " + operationType + ".Set(rebasedValues);");
    }

    private static void AppendTypedSequenceRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string operation,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var elementType = member.Collection.ElementType.Name;
        var valueType = SparseFragmentPatchEmitter.GetMemberValueType(dialect, member);
        var listType = $"global::System.Collections.Generic.List<{elementType}>";
        var readOnlyType = $"global::System.Collections.Generic.IReadOnlyList<{elementType}>";
        var typedMethod =
            member.MergeMode == SparseMergeModes.Append
                ? "TryRebaseSequenceAppend"
                : "TryRebaseSequenceSetUnion";
        if (member.Collection.CloneKind == SparseCloneCollectionKind.Array)
        {
            typedMethod += "Array";
        }
        var boxedMethod =
            member.MergeMode == SparseMergeModes.Append ? "TryRebaseAppend" : "TryRebaseSetUnion";
        string NativeInput(string state, string variable) =>
            $"(object?){state}.Value is {readOnlyType} {variable} && ({variable} is {elementType}[] || {variable}.GetType() == typeof({listType}))";

        code.AppendLineAt(6, $"{valueType} __rebasedCollection = default!;");
        code.AppendLineAt(6, "string? reason;");
        code.AppendLineAt(6, "bool __rebaseSucceeded;");
        code.AppendLineAt(
            6,
            "if ("
                + NativeInput("baseMember", "beforeValues")
                + " && "
                + NativeInput("desiredMember", "desiredValues")
                + " && "
                + NativeInput("currentMember", "currentValues")
                + ")"
        );
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            $"__rebaseSucceeded = {dialect.RuntimeFacade}.{typedMethod}<{elementType}>(beforeValues, desiredValues, currentValues, null, out var __typedValues, out reason);"
        );
        code.AppendLineAt(7, "if (__rebaseSucceeded) __rebasedCollection = __typedValues;");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else");
        code.AppendLineAt(6, "{");
        foreach (var state in new[] { "before", "current", "desired" })
        {
            var memberState = state == "before" ? "baseMember" : state + "Member";
            code.AppendLineAt(
                7,
                $"var {state}Boxed = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable){memberState}.Value));"
            );
        }
        code.AppendLineAt(
            7,
            $"__rebaseSucceeded = {dialect.RuntimeFacade}.{boxedMethod}(beforeBoxed, desiredBoxed, currentBoxed, (object? left, object? right) => {dialect.RuntimeFacade}.AreEqual(left, right), out var __boxedValues, out reason);"
        );
        var boxedResult = SparseFragmentExpressions.MaterializeCollection(
            member,
            $"global::System.Linq.Enumerable.Cast<{elementType}>(__boxedValues)"
        );
        code.AppendLineAt(7, $"if (__rebaseSucceeded) __rebasedCollection = {boxedResult};");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "if (__rebaseSucceeded)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            $"result.{field} = {operation}<{valueType}>.Set(__rebasedCollection);"
        );
    }

    private static void AppendBoxedCollectionRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string operation,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        code.AppendLineAt(
            6,
            "var beforeValues = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)baseMember.Value));"
        );
        code.AppendLineAt(
            6,
            "var currentValues = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)currentMember.Value));"
        );
        code.AppendLineAt(
            6,
            "var desiredValues = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)desiredMember.Value));"
        );
        code.AppendLineAt(
            6,
            "if ("
                + dialect.RuntimeFacade
                + "."
                + (
                    member.MergeMode == SparseMergeModes.Append
                        ? "TryRebaseAppend"
                        : "TryRebaseSetUnion"
                )
                + "(beforeValues, desiredValues, currentValues, (object? left, object? right) => "
                + dialect.RuntimeFacade
                + ".AreEqual(left, right), out var rebasedValues, out var reason))"
        );
        code.AppendLineAt(6, "{");
        var operationType =
            operation + "<" + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member) + ">";
        var materialized = SparseFragmentExpressions.MaterializeCollection(
            member,
            "global::System.Linq.Enumerable.Cast<"
                + member.Collection.ElementType.Name
                + ">(rebasedValues)"
        );
        code.AppendLineAt(
            7,
            "result." + field + " = " + operationType + ".Set(" + materialized + ");"
        );
    }

    private static void AppendCustomStrategyMemberRebase(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string field,
        string kind,
        string baseMember,
        string desiredMember,
        string currentMember,
        string conflict,
        string conflictKind,
        int indent,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var operationType =
            SparseFragmentPatchEmitter.Operation(dialect)
            + "<"
            + SparseFragmentPatchEmitter.GetMemberValueType(dialect, member)
            + ">";
        // Custom strategies observe every member edit (Set and Unset): the presence-aware
        // TryRebase(Optional<T>, ...) SPI can represent a missing rebased state, so unlike the
        // previous T?-based SPI there is no need to route Unset through the scalar fallback.
        code.AppendLineAt(indent, "if (local." + field + ".Kind != " + kind + ".Unchanged)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "var " + baseMember + " = baseFragment." + name + ";");
        code.AppendLineAt(indent + 1, "var " + currentMember + " = currentFragment." + name + ";");
        code.AppendLineAt(
            indent + 1,
            "var " + desiredMember + " = local." + field + ".Apply(" + baseMember + ");"
        );
        var strategyField =
            "Fragment." + SparseFragmentPatchEmitter.GetMergeStrategyField(dialect, member);
        code.AppendLineAt(
            indent + 1,
            "if ("
                + strategyField
                + ".TryRebase("
                + baseMember
                + ", "
                + desiredMember
                + ", "
                + currentMember
                + ", out var rebasedValue, out var reason))"
        );
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "if (!((!rebasedValue.IsPresent && !"
                + currentMember
                + ".IsPresent) || (rebasedValue.IsPresent && "
                + currentMember
                + ".IsPresent && "
                + strategyField
                + ".AreEqual("
                + currentMember
                + ".Value, rebasedValue.Value))))"
        );
        code.AppendLineAt(indent + 2, "{");
        code.AppendLineAt(indent + 3, "if (rebasedValue.IsPresent)");
        code.AppendLineAt(indent + 3, "{");
        code.AppendLineAt(
            indent + 4,
            "result." + field + " = " + operationType + ".Set(rebasedValue.Value);"
        );
        code.AppendLineAt(indent + 3, "}");
        code.AppendLineAt(indent + 3, "else");
        code.AppendLineAt(indent + 3, "{");
        code.AppendLineAt(indent + 4, "result." + field + " = " + operationType + ".Unset;");
        code.AppendLineAt(indent + 3, "}");
        code.AppendLineAt(indent + 2, "}");
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent + 1, "else");
        code.AppendLineAt(indent + 1, "{");
        code.AppendLineAt(
            indent + 2,
            "conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + SymbolDisplay.FormatLiteral(member.Property.Name, true)
                + " }, "
                + conflictKind
                + ".CustomStrategy, __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), reason ?? \"The custom merge strategy could not rebase the member.\"));"
        );
        code.AppendLineAt(indent + 1, "}");
        code.AppendLineAt(indent, "}");
    }

    private static void EmitScalarRebase(
        SharedIndentedBuilder code,
        string field,
        string memberName,
        string baseMember,
        string desiredMember,
        string currentMember,
        string equality,
        string conflictKind,
        string conflict,
        int indent
    )
    {
        code.AppendLineAt(
            indent,
            "if (" + equality + "(" + baseMember + ", " + currentMember + "))"
        );
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(indent + 1, "result." + field + " = local." + field + ";");
        code.AppendLineAt(indent, "}");
        code.AppendLineAt(
            indent,
            "else if (!"
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
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "conflicts.Add(new "
                + conflict
                + "(new string[] { "
                + SymbolDisplay.FormatLiteral(memberName, true)
                + " }, "
                + conflictKind
                + ", __SparseMember("
                + baseMember
                + "), __SparseMember("
                + desiredMember
                + "), __SparseMember("
                + currentMember
                + "), \"The member conflicts with a concurrent change.\"));"
        );
        code.AppendLineAt(indent, "}");
    }
}
