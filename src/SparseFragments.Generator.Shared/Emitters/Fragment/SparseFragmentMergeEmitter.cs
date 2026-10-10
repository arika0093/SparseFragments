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
        string fieldQualifier = ""
    )
    {
        Optional = optional;
        MergeStrategyFieldPrefix = mergeStrategyFieldPrefix;
        ReferenceComparer = referenceComparer;
        Expressions = expressions;
        FieldQualifier = fieldQualifier;
    }

    private string FieldQualifier { get; }

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
        // with an explicit receiver.
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

        code.AppendLineAt(
            2,
            "/// <summary>Merges a higher-priority fragment over this fragment.</summary>"
        );
        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (isOperationsBody)
            code.AppendLineAt(2, "/// <param name=\"self\">The lower-priority fragment.</param>");
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

        code.AppendLineAt(3, "return new Fragment");
        code.AppendLineAt(3, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var lower = receiver + name;
            var higher = "higherPriority." + name;
            string expression;
            if (member.MergeStrategyType is not null)
            {
                expression = $"{MergeStrategyField(member)}.Merge({lower}, {higher})";
            }
            else if (SparseMergeModes.IsDeepMerge(member.MergeMode, member.ChildModel is not null))
            {
                // Default resolves shape-aware: nested models deep-merge,
                // otherwise whole-value replacement below. Missing high
                // preserves low; present null replaces; non-null merges.
                expression =
                    $"{higher}.IsPresent ? {Optional}<{SparseFragmentEmitHelpers.FragmentValueType(member)}>.Present(({lower}.IsPresent && (object?){lower}.Value is not null && (object?){higher}.Value is not null) ? {lower}.Value!.Merge({higher}.Value!) : {higher}.Value) : {lower}";
            }
            else if (member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion)
            {
                var merged = Expressions.BuildCollectionMerge(
                    member,
                    lower + ".Value!",
                    higher + ".Value!"
                );
                expression =
                    $"{higher}.IsPresent ? ({lower}.IsPresent && (object?){lower}.Value is not null && (object?){higher}.Value is not null ? {Optional}<{SparseFragmentEmitHelpers.FragmentValueType(member)}>.Present({merged}) : {higher}) : {lower}";
            }
            else
            {
                expression = $"{higher}.IsPresent ? {higher} : {lower}";
            }

            code.AppendIndent(4).Append(name).Append(" = ").Append(expression).AppendLine(",");
        }

        code.AppendLineAt(3, "};");
        code.AppendLineAt(2, "}");
        code.AppendLine();
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
        code.AppendLineAt(
            2,
            "/// <summary>Applies a sparse semantic diff to this contribution.</summary>"
        );
        var isOperationsBody = !string.Equals(receiver, "this.", StringComparison.Ordinal);
        if (isOperationsBody)
            code.AppendLineAt(2, "/// <param name=\"self\">The fragment to update.</param>");
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
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var type = SparseFragmentEmitHelpers.FragmentValueType(member);
            var self = receiver + name;
            var expression = member.ChildModel is null
                ? $"changes.{name}.IsPresent ? changes.{name} : {self}"
                : $"changes.{name}.IsPresent ? {Optional}<{type}>.Present(({self}.IsPresent && (object?){self}.Value is not null && (object?)changes.{name}.Value is not null) ? {self}.Value!.ApplyChanges(changes.{name}.Value!) : changes.{name}.Value) : {self}";
            code.AppendIndent(4).Append(name).Append(" = ").Append(expression).AppendLine(",");
        }

        code.AppendLineAt(3, "};");
        code.AppendLineAt(2, "}");
        code.AppendLine();
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
