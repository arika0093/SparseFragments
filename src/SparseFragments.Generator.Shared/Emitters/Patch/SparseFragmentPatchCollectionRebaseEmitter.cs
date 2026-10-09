namespace SparseFragments.Generator.Shared;

/// <summary>Typed and boxed collection rebase for standalone patch members.</summary>
internal static class SparseFragmentPatchCollectionRebaseEmitter
{
    internal static void AppendTypedSetUnionRebase(
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

    internal static void AppendTypedSequenceRebase(
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

    internal static void AppendBoxedCollectionRebase(
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
}
