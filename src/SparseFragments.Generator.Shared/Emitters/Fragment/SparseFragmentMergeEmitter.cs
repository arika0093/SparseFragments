using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits merge/diff/apply helpers for generated fragments.</summary>
internal sealed class SparseFragmentMergeEmitter
{
    private string Optional { get; }
    private string MergeStrategyFieldPrefix { get; }
    private string ReferenceComparer { get; }
    private SparseFragmentExpressions Expressions { get; }

    public SparseFragmentMergeEmitter(
        string optional,
        string mergeStrategyFieldPrefix,
        string referenceComparer,
        SparseFragmentExpressions expressions,
        string fieldQualifier = "",
        SparseFragmentOriginEmitter? originEmitter = null
    )
    {
        Optional = optional;
        MergeStrategyFieldPrefix = mergeStrategyFieldPrefix;
        ReferenceComparer = referenceComparer;
        Expressions = expressions;
        FieldQualifier = fieldQualifier;
        Origins = originEmitter;
    }

    private string FieldQualifier { get; }

    private SparseFragmentOriginEmitter? Origins { get; }

    private string MergeStrategyField(SparseMemberModel member) =>
        FieldQualifier
        + SparseFragmentEmitHelpers.MergeStrategyField(MergeStrategyFieldPrefix, member);

    public void AppendMerge(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? operationsType = null,
        string receiver = "this."
    )
    {
        // Stage 4 (#193): with an operations target the surface keeps a
        // one-line facade; the algorithm body moves to the operations class
        // with an explicit receiver. Origin fallbacks follow separately via
        // AppendOriginFallbackFacades so public facades precede internals.
        if (operationsType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
            );
            code.AppendLineAt(2, "/// <returns>The merged fragment.</returns>");
            code.AppendLineAt(
                2,
                "public Fragment Merge(Fragment higherPriority) => "
                    + operationsType
                    + ".Merge(this, higherPriority);"
            );

            return;
        }
        var hasCustomMergeStrategy = false;
        var replaceOnly = members.Length > 0;
        foreach (var member in members)
        {
            if (member.MergeStrategyType is not null)
            {
                hasCustomMergeStrategy = true;
                replaceOnly = false;
                continue;
            }

            if (
                SparseMergeModes.IsDeepMerge(member.MergeMode, member.ChildModel is not null)
                || member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion
            )
            {
                replaceOnly = false;
            }
        }

        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (Origins is null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
            );
            if (isOperationsBody)
            {
                code.AppendLineAt(
                    2,
                    "/// <param name=\"self\">The lower-priority fragment.</param>"
                );
            }

            code.AppendLineAt(
                2,
                "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
            );
            code.AppendLineAt(2, "/// <returns>The merged fragment.</returns>");
            if (isOperationsBody)
                code.AppendLineAt(
                    2,
                    "public static Fragment Merge(Fragment self, Fragment higherPriority)"
                );
            else
                code.AppendLineAt(2, "public Fragment Merge(Fragment higherPriority)");
            code.AppendLineAt(2, "{");
            if (isOperationsBody)
                SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "higherPriority");
            AppendMergeFastPaths(code, members, receiver, hasCustomMergeStrategy, replaceOnly);
            code.AppendLineAt(3, "return new Fragment");
            code.AppendLineAt(3, "{");
            for (var index = 0; index < members.Length; index++)
            {
                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.AppendIndent(4)
                    .Append(name)
                    .Append(" = ")
                    .Append(BuildMergeValueExpression(members[index], receiver, index))
                    .AppendLine(",");
            }

            code.AppendLineAt(3, "};");
            code.AppendLineAt(2, "}");
            code.AppendLine();
            return;
        }

        if (isOperationsBody)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"self\">The lower-priority fragment.</param>");
            code.AppendLineAt(
                2,
                "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"selfFallback\">The fallback origin for the lower contribution.</param>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"higherFallback\">The fallback origin for the higher contribution.</param>"
            );
            code.AppendLineAt(2, "/// <returns>The merged fragment.</returns>");
            code.AppendLineAt(
                2,
                "public static Fragment Merge(Fragment self, Fragment higherPriority, string? selfFallback = null, string? higherFallback = null)"
            );
            code.AppendLineAt(2, "{");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "higherPriority");
            AppendMergeFastPaths(code, members, receiver, hasCustomMergeStrategy, replaceOnly);
            SparseFragmentMergeOriginEmitter.AppendMergeWithOrigins(
                code,
                members,
                receiver,
                BuildMergeValueExpression,
                Origins
            );
            code.AppendLineAt(2, "}");
            code.AppendLine();
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
        );
        code.AppendLineAt(2, "/// <returns>The merged fragment.</returns>");
        code.AppendLineAt(
            2,
            "public Fragment Merge(Fragment higherPriority) => __SparseMergeWithFallback(higherPriority, null, null);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Merges with explicit fallback origins for nested attribution.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"higherPriority\">The higher-priority contribution.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"selfFallback\">The fallback origin for this contribution.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"higherFallback\">The fallback origin for the higher contribution.</param>"
        );
        code.AppendLineAt(2, "/// <returns>The merged fragment.</returns>");
        code.AppendLineAt(
            2,
            "internal Fragment __SparseMergeWithFallback(Fragment higherPriority, string? selfFallback, string? higherFallback)"
        );
        code.AppendLineAt(2, "{");
        SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "higherPriority");
        AppendMergeFastPaths(code, members, receiver, hasCustomMergeStrategy, replaceOnly);
        SparseFragmentMergeOriginEmitter.AppendMergeWithOrigins(
            code,
            members,
            receiver,
            BuildMergeValueExpression,
            Origins
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendMergeFastPaths(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string receiver,
        bool hasCustomMergeStrategy,
        bool replaceOnly
    )
    {
        if (!hasCustomMergeStrategy)
        {
            code.AppendLineAt(3, "if (higherPriority.IsEmpty)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "return " + receiver.TrimEnd('.') + ";");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "if (" + receiver + "IsEmpty)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "return higherPriority;");
            code.AppendLineAt(3, "}");
        }

        if (replaceOnly)
        {
            code.AppendIndent(3).Append("if (");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                code.Append("higherPriority.")
                    .Append(SparseNaming.EscapeIdentifier(members[index].Property.Name))
                    .Append(".IsPresent");
            }

            code.AppendLine(")");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "return higherPriority;");
            code.AppendLineAt(3, "}");
        }
    }

    private string BuildMergeValueExpression(
        SparseMemberModel member,
        string receiver,
        int position
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var lower = receiver + name;
        var higher = "higherPriority." + name;
        if (member.MergeStrategyType is not null)
        {
            return $"{MergeStrategyField(member)}.Merge({lower}, {higher})";
        }

        if (SparseMergeModes.IsDeepMerge(member.MergeMode, member.ChildModel is not null))
        {
            // Default resolves shape-aware: nested models deep-merge,
            // otherwise whole-value replacement below. Missing high
            // preserves low; present null replaces; non-null merges.
            // With origins, nested fragments inherit the enclosing default
            // through the fallback seam unless explicitly originated.
            var merge =
                Origins is not null && SparseFragmentOriginEmitter.IsFragmentDeep(member)
                    ? $"{lower}.Value!.__SparseMergeWithFallback({higher}.Value!, {SparseFragmentOriginEmitter.MemberAttribution(receiver, position, "selfFallback")}, {SparseFragmentOriginEmitter.MemberAttribution("higherPriority.", position, "higherFallback")})"
                    : $"{lower}.Value!.Merge({higher}.Value!)";
            return $"{higher}.IsPresent ? {Optional}<{SparseFragmentEmitHelpers.FragmentValueType(member)}>.Present(({lower}.IsPresent && (object?){lower}.Value is not null && (object?){higher}.Value is not null) ? {merge} : {higher}.Value) : {lower}";
        }

        if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
        {
            var merged = Expressions.BuildCollectionMerge(
                member,
                lower + ".Value!",
                higher + ".Value!"
            );
            return $"{higher}.IsPresent ? ({lower}.IsPresent && (object?){lower}.Value is not null && (object?){higher}.Value is not null ? {Optional}<{SparseFragmentEmitHelpers.FragmentValueType(member)}>.Present({merged}) : {higher}) : {lower}";
        }

        return $"{higher}.IsPresent ? {higher} : {lower}";
    }

    public void AppendApplyChanges(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string? operationsType = null,
        string receiver = "this."
    )
    {
        if (operationsType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"changes\">The diff to apply.</param>");
            code.AppendLineAt(2, "/// <returns>The fragment with the diff applied.</returns>");
            code.AppendLineAt(
                2,
                "public Fragment ApplyChanges(Fragment changes) => "
                    + operationsType
                    + ".ApplyChanges(this, changes);"
            );

            return;
        }

        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (Origins is null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
            );
            if (isOperationsBody)
            {
                code.AppendLineAt(2, "/// <param name=\"self\">The fragment to update.</param>");
            }

            code.AppendLineAt(2, "/// <param name=\"changes\">The diff to apply.</param>");
            code.AppendLineAt(2, "/// <returns>The fragment with the diff applied.</returns>");
            if (isOperationsBody)
                code.AppendLineAt(
                    2,
                    "public static Fragment ApplyChanges(Fragment self, Fragment changes)"
                );
            else
                code.AppendLineAt(2, "public Fragment ApplyChanges(Fragment changes)");
            code.AppendLineAt(2, "{");
            if (isOperationsBody)
                SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "changes");
            code.AppendLineAt(3, "return new Fragment");
            code.AppendLineAt(3, "{");
            for (var index = 0; index < members.Length; index++)
            {
                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.AppendIndent(4)
                    .Append(name)
                    .Append(" = ")
                    .Append(BuildApplyValueExpression(members[index], receiver, index))
                    .AppendLine(",");
            }

            code.AppendLineAt(3, "};");
            code.AppendLineAt(2, "}");
            code.AppendLine();
            return;
        }

        if (isOperationsBody)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"self\">The fragment to update.</param>");
            code.AppendLineAt(2, "/// <param name=\"changes\">The diff to apply.</param>");
            code.AppendLineAt(
                2,
                "/// <param name=\"selfFallback\">The fallback origin for the updated fragment.</param>"
            );
            code.AppendLineAt(
                2,
                "/// <param name=\"changesFallback\">The fallback origin for the diff.</param>"
            );
            code.AppendLineAt(2, "/// <returns>The fragment with the diff applied.</returns>");
            code.AppendLineAt(
                2,
                "public static Fragment ApplyChanges(Fragment self, Fragment changes, string? selfFallback = null, string? changesFallback = null)"
            );
            code.AppendLineAt(2, "{");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "changes");
            SparseFragmentMergeOriginEmitter.AppendApplyChangesWithOrigins(
                code,
                members,
                receiver,
                BuildApplyValueExpression
            );
            code.AppendLineAt(2, "}");
            code.AppendLine();
            return;
        }

        code.AppendLineAt(
            2,
            "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"changes\">The diff to apply.</param>");
        code.AppendLineAt(2, "/// <returns>The fragment with the diff applied.</returns>");
        code.AppendLineAt(
            2,
            "public Fragment ApplyChanges(Fragment changes) => __SparseApplyChangesWithFallback(changes, null, null);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Applies a diff with explicit fallback origins for nested attribution.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"changes\">The diff to apply.</param>");
        code.AppendLineAt(
            2,
            "/// <param name=\"selfFallback\">The fallback origin for the updated fragment.</param>"
        );
        code.AppendLineAt(
            2,
            "/// <param name=\"changesFallback\">The fallback origin for the diff.</param>"
        );
        code.AppendLineAt(2, "/// <returns>The fragment with the diff applied.</returns>");
        code.AppendLineAt(
            2,
            "internal Fragment __SparseApplyChangesWithFallback(Fragment changes, string? selfFallback, string? changesFallback)"
        );
        code.AppendLineAt(2, "{");
        SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "changes");
        SparseFragmentMergeOriginEmitter.AppendApplyChangesWithOrigins(
            code,
            members,
            receiver,
            BuildApplyValueExpression
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits the internal origin-fallback facades for split emission.</summary>
    /// <remarks>Called with the internal caches so public facades precede them.</remarks>
    /// <param name="code">Surface target builder.</param>
    /// <param name="operationsType">Operations class qualifier.</param>
    public void AppendOriginFallbackFacades(SharedIndentedBuilder code, string operationsType)
    {
        if (Origins is null)
        {
            return;
        }

        code.AppendLineAt(
            2,
            "internal Fragment __SparseMergeWithFallback(Fragment higherPriority, string? selfFallback, string? higherFallback) => "
                + operationsType
                + ".Merge(this, higherPriority, selfFallback, higherFallback);"
        );
        code.AppendLineAt(
            2,
            "internal Fragment __SparseApplyChangesWithFallback(Fragment changes, string? selfFallback, string? changesFallback) => "
                + operationsType
                + ".ApplyChanges(this, changes, selfFallback, changesFallback);"
        );
    }

    private string BuildApplyValueExpression(
        SparseMemberModel member,
        string receiver,
        int position
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var type = SparseFragmentEmitHelpers.FragmentValueType(member);
        var self = receiver + name;
        if (member.ChildModel is null)
        {
            return $"changes.{name}.IsPresent ? changes.{name} : {self}";
        }

        // With origins, nested fragments inherit the enclosing default
        // through the fallback seam unless explicitly originated.
        var apply =
            Origins is not null && SparseFragmentOriginEmitter.IsFragmentDeep(member)
                ? $"{self}.Value!.__SparseApplyChangesWithFallback(changes.{name}.Value!, {SparseFragmentOriginEmitter.MemberAttribution(receiver, position, "selfFallback")}, {SparseFragmentOriginEmitter.MemberAttribution("changes.", position, "changesFallback")})"
                : $"{self}.Value!.ApplyChanges(changes.{name}.Value!)";
        return $"changes.{name}.IsPresent ? {Optional}<{type}>.Present(({self}.IsPresent && (object?){self}.Value is not null && (object?)changes.{name}.Value is not null) ? {apply} : changes.{name}.Value) : {self}";
    }

    public void AppendDiff(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        string? operationsType = null
    )
    {
        // Stage 4 (#193): facades delegate both Diff overloads; the core and
        // per-member helpers move with no surface counterpart. Public-first
        // order keeps the public facade with the public surface; the internal
        // overload moves via AppendDiffInternalFacade.
        if (operationsType is not null)
        {
            code.AppendLineAt(
                2,
                "/// <summary>Creates a sparse semantic diff between two ordinary model values.</summary>"
            );
            code.AppendLineAt(2, "/// <param name=\"before\">The baseline value.</param>");
            code.AppendLineAt(2, "/// <param name=\"after\">The updated value.</param>");
            code.AppendLineAt(
                2,
                "/// <returns>The sparse diff from baseline to updated.</returns>"
            );
            code.AppendIndent(2)
                .Append("public static Fragment Diff(")
                .Append(modelType)
                .Append(" before, ")
                .Append(modelType)
                .Append(" after) => ")
                .Append(operationsType)
                .AppendLine(".Diff(before, after);");
            return;
        }
        const string diffContextType =
            "global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>>";
        // Public-first ordering: buffer the per-member private helpers and
        // append them after the public/internal overloads below.
        var memberHelpers = new SharedIndentedBuilder(code.CancellationToken);
        var surface = code;
        code = memberHelpers;
        foreach (var member in members.Where(static member => member.ChildModel is not null))
        {
            var type = member.ChildModel!.Value.NonNullableName;
            var fragment = member.ChildFragmentType!;
            code.AppendIndent(2)
                .Append("private static ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> __Diff_")
                .Append(member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('(');
            if (!member.ChildIsReferenceType)
            {
                code.Append(type)
                    .Append(" before, ")
                    .Append(type)
                    .Append(" after, ")
                    .Append(diffContextType)
                    .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
                code.AppendLineAt(2, "{");
                code.AppendIndent(3)
                    .Append("if (global::System.Collections.Generic.EqualityComparer<")
                    .Append(type)
                    .AppendLine(">.Default.Equals(before!, after!)) { return default; }");
                code.AppendIndent(3)
                    .Append("return ")
                    .Append(Optional)
                    .Append("<")
                    .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                    .Append(">.Present(")
                    .Append(fragment)
                    .AppendLine(
                        ".Diff(before, after, __sparse_diff_context, __sparse_diff_path));"
                    );
                code.AppendLineAt(2, "}");
                continue;
            }

            code.Append(type)
                .Append("? before, ")
                .Append(type)
                .Append("? after, ")
                .Append(diffContextType)
                .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(before, after)) { return default; }"
            );
            code.AppendIndent(3)
                .Append("if (before is null || after is null) { return ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append(">.Present(after is null ? null : ")
                .Append(fragment)
                .AppendLine(".From(after)); }");
            code.AppendIndent(3)
                .Append("var difference = ")
                .Append(fragment)
                .AppendLine(".Diff(before, after, __sparse_diff_context, __sparse_diff_path);");
            code.AppendIndent(3)
                .Append("return difference.IsEmpty ? default : ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .AppendLine(">.Present(difference);");
            code.AppendLineAt(2, "}");
        }

        code = surface;
        code.AppendLineAt(
            2,
            "/// <summary>Creates a sparse semantic diff between two ordinary model values.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"before\">The baseline value.</param>");
        code.AppendLineAt(2, "/// <param name=\"after\">The updated value.</param>");
        code.AppendLineAt(2, "/// <returns>The sparse diff from baseline to updated.</returns>");
        code.AppendIndent(2)
            .Append("public static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .AppendLine(" after)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "after");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        var requiresDiffContext = members.Any(static member =>
            member.ChildModel is not null && member.MergeStrategyType is null
        );
        if (requiresDiffContext)
        {
            code.AppendLineAt(
                3,
                "var __sparse_diff_context = " + ReferenceComparer + ".CreateDiffCycleContext();"
            );
            code.AppendLineAt(
                3,
                "return __SparseDiffCore(before, after, __sparse_diff_context, \"\");"
            );
        }
        else
        {
            // Internal calls retain their ancestor guard even for leaf projections.
            AppendDiffBody(code, members, 3);
        }
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendIndent(2)
            .Append("internal static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after, ")
            .Append(diffContextType)
            .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "after");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        code.AppendLineAt(
            3,
            "return __SparseDiffCore(before, after, __sparse_diff_context, __sparse_diff_path);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.Append(memberHelpers.ToString());
        code.AppendLine();
        code.AppendIndent(2)
            .Append("private static Fragment __SparseDiffCore(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after, ")
            .Append(diffContextType)
            .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            code.AppendLineAt(
                3,
                "var __sparse_diff_pair = new global::System.Collections.Generic.KeyValuePair<object, object>((object)before, (object)after);"
            );
            code.AppendLineAt(3, "if (!__sparse_diff_context.Add(__sparse_diff_pair))");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "throw new global::System.NotSupportedException(\"Cyclic reference detected during Diff at '\" + __sparse_diff_path + \"'. Fragment.Diff does not support cyclic object graphs; DeepClone preserves cycles.\");"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "try");
            code.AppendLineAt(3, "{");
            AppendDiffBody(code, members, 4);
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "finally");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__sparse_diff_context.Remove(__sparse_diff_pair);");
            code.AppendLineAt(3, "}");
        }
        else
        {
            AppendDiffBody(code, members, 3);
        }
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits the public Diff body preceding internal helpers.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendDiffPublicBody(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        code.AppendLineAt(
            2,
            "/// <summary>Creates a sparse semantic diff between two ordinary model values.</summary>"
        );
        code.AppendLineAt(2, "/// <param name=\"before\">The baseline value.</param>");
        code.AppendLineAt(2, "/// <param name=\"after\">The updated value.</param>");
        code.AppendLineAt(2, "/// <returns>The sparse diff from baseline to updated.</returns>");
        code.AppendIndent(2)
            .Append("public static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .AppendLine(" after)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "after");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        var requiresDiffContext = members.Any(static member =>
            member.ChildModel is not null && member.MergeStrategyType is null
        );
        if (requiresDiffContext)
        {
            code.AppendLineAt(
                3,
                "var __sparse_diff_context = " + ReferenceComparer + ".CreateDiffCycleContext();"
            );
            code.AppendLineAt(
                3,
                "return __SparseDiffCore(before, after, __sparse_diff_context, \"\");"
            );
        }
        else
        {
            AppendDiffBody(code, members, 3);
        }
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits the internal Diff body trailing public operations.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public static void AppendDiffInternalBody(
        SharedIndentedBuilder code,
        string modelType,
        bool modelIsReferenceType
    )
    {
        const string diffContextType =
            "global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>>";
        code.AppendIndent(2)
            .Append("internal static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after, ")
            .Append(diffContextType)
            .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "before");
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "after");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(before, after)) { return new Fragment(); }"
            );
        }

        code.AppendLineAt(
            3,
            "return __SparseDiffCore(before, after, __sparse_diff_context, __sparse_diff_path);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits private Diff helpers trailing internal operations.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    public void AppendDiffPrivateBodies(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        const string diffContextType =
            "global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>>";
        foreach (var member in members.Where(static member => member.ChildModel is not null))
        {
            var type = member.ChildModel!.Value.NonNullableName;
            var fragment = member.ChildFragmentType!;
            code.AppendIndent(2)
                .Append("private static ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append("> __Diff_")
                .Append(member.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('(');
            if (!member.ChildIsReferenceType)
            {
                code.Append(type)
                    .Append(" before, ")
                    .Append(type)
                    .Append(" after, ")
                    .Append(diffContextType)
                    .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
                code.AppendLineAt(2, "{");
                code.AppendIndent(3)
                    .Append("if (global::System.Collections.Generic.EqualityComparer<")
                    .Append(type)
                    .AppendLine(">.Default.Equals(before!, after!)) { return default; }");
                code.AppendIndent(3)
                    .Append("return ")
                    .Append(Optional)
                    .Append("<")
                    .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                    .Append(">.Present(")
                    .Append(fragment)
                    .AppendLine(
                        ".Diff(before, after, __sparse_diff_context, __sparse_diff_path));"
                    );
                code.AppendLineAt(2, "}");
                continue;
            }

            code.Append(type)
                .Append("? before, ")
                .Append(type)
                .Append("? after, ")
                .Append(diffContextType)
                .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "if (global::System.Object.ReferenceEquals(before, after)) { return default; }"
            );
            code.AppendIndent(3)
                .Append("if (before is null || after is null) { return ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append(">.Present(after is null ? null : ")
                .Append(fragment)
                .AppendLine(".From(after)); }");
            code.AppendIndent(3)
                .Append("var difference = ")
                .Append(fragment)
                .AppendLine(".Diff(before, after, __sparse_diff_context, __sparse_diff_path);");
            code.AppendIndent(3)
                .Append("return difference.IsEmpty ? default : ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .AppendLine(">.Present(difference);");
            code.AppendLineAt(2, "}");
        }

        code.AppendLine();
        code.AppendIndent(2)
            .Append("private static Fragment __SparseDiffCore(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(" after, ")
            .Append(diffContextType)
            .AppendLine(" __sparse_diff_context, string __sparse_diff_path)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            code.AppendLineAt(
                3,
                "var __sparse_diff_pair = new global::System.Collections.Generic.KeyValuePair<object, object>((object)before, (object)after);"
            );
            code.AppendLineAt(3, "if (!__sparse_diff_context.Add(__sparse_diff_pair))");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "throw new global::System.NotSupportedException(\"Cyclic reference detected during Diff at '\" + __sparse_diff_path + \"'. Fragment.Diff does not support cyclic object graphs; DeepClone preserves cycles.\");"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "try");
            code.AppendLineAt(3, "{");
            AppendDiffBody(code, members, 4);
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "finally");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__sparse_diff_context.Remove(__sparse_diff_pair);");
            code.AppendLineAt(3, "}");
        }
        else
        {
            AppendDiffBody(code, members, 3);
        }
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private void AppendDiffBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        int indent
    )
    {
        code.AppendLineAt(indent, "return new Fragment");
        code.AppendLineAt(indent, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var before = "before." + name;
            var after = "after." + name;
            var valueType = SparseFragmentEmitHelpers.FragmentValueType(member);
            string condition;
            if (member.MergeStrategyType is not null)
            {
                condition =
                    $"{MergeStrategyField(member)}.AreEqual({before}, {after}) ? default : {Optional}<{valueType}>.Present({after})";
            }
            else if (member.ChildModel is null)
            {
                condition =
                    $"{Expressions.ValueEqualityExpression(member, before, after)} ? default : {Optional}<{valueType}>.Present({after})";
            }
            else
            {
                condition =
                    $"__Diff_{member.Id}({before}, {after}, __sparse_diff_context, {DiffMemberPath(member)})";
            }

            code.AppendIndent(indent + 1)
                .Append(name)
                .Append(" = ")
                .Append(condition)
                .AppendLine(",");
        }

        code.AppendLineAt(indent, "};");
    }

    private static string DiffMemberPath(SparseMemberModel member)
    {
        var escaped = member.Property.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "(__sparse_diff_path.Length == 0 ? \""
            + escaped
            + "\" : __sparse_diff_path + \"."
            + escaped
            + "\")";
    }

    /// <summary>Emits the internal Diff facade trailing the public fragment surface.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="operationsType">Operations class qualifying the delegate.</param>
    public static void AppendDiffInternalFacade(
        SharedIndentedBuilder code,
        string modelType,
        string operationsType
    )
    {
        code.AppendIndent(2)
            .Append("internal static Fragment Diff(")
            .Append(modelType)
            .Append(" before, ")
            .Append(modelType)
            .Append(
                " after, global::System.Collections.Generic.HashSet<global::System.Collections.Generic.KeyValuePair<object, object>> __sparse_diff_context, string __sparse_diff_path) => "
            )
            .Append(operationsType)
            .AppendLine(".Diff(before, after, __sparse_diff_context, __sparse_diff_path);");
    }
}
