using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits model/fragment conversion: <c>From</c>, <c>ToModel</c> and projection constructors.</summary>
internal sealed class SparseFragmentConversionEmitter
{
    private string Optional { get; }
    private string CloneContext { get; }
    private string ReferenceComparer { get; }
    private SparseFragmentExpressions Expressions { get; }

    public SparseFragmentConversionEmitter(
        string optional,
        string cloneContext,
        string referenceComparer,
        SparseFragmentExpressions expressions
    )
    {
        Optional = optional;
        CloneContext = cloneContext;
        ReferenceComparer = referenceComparer;
        Expressions = expressions;
    }

    public void AppendFromModel(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool usesPocoCloning,
        string? operationsType = null
    )
    {
        // Stage 4 (#193): facades delegate both From overloads; bodies move
        // verbatim (static, explicit receivers, Fragment aliases in scope).
        if (operationsType is not null)
        {
            code.AppendIndent(2)
                .Append("public static Fragment From(")
                .Append(modelType)
                .Append(" value) => ")
                .Append(operationsType)
                .AppendLine(".From(value);");
            code.AppendIndent(2)
                .Append("internal static Fragment From(")
                .Append(modelType)
                .Append(
                    " value, global::System.Collections.Generic.Dictionary<object, object> __sparse_clone_context, global::System.Collections.Generic.HashSet<object> __sparse_from_context, string __sparse_from_path) => "
                )
                .Append(operationsType)
                .AppendLine(
                    ".From(value, __sparse_clone_context, __sparse_from_context, __sparse_from_path);"
                );
            return;
        }
        var requiresContext = members.Any(static member =>
            member.ChildModel is not null
            || member.Property.Type.PocoCloneHelperName is not null
            || member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported
        );
        code.AppendIndent(2)
            .Append("public static Fragment From(")
            .Append(modelType)
            .AppendLine(" value)");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "value");
        }

        if (requiresContext)
        {
            SparseFragmentEmitHelpers.AppendCloneContext(code, 3, CloneContext, ReferenceComparer);
            code.AppendLineAt(
                3,
                "var __sparse_from_context = " + ReferenceComparer + ".CreateFromCycleContext();"
            );
            code.AppendLineAt(
                3,
                "return From(value, " + CloneContext + ", __sparse_from_context, \"\");"
            );
        }
        else
        {
            AppendFromModelBody(code, members, 3);
        }
        code.AppendLineAt(2, "}");
        code.AppendLine();
        code.AppendIndent(2)
            .Append("internal static Fragment From(")
            .Append(modelType)
            .Append(" value, global::System.Collections.Generic.Dictionary<object, object> ")
            .Append(CloneContext)
            .Append(
                ", global::System.Collections.Generic.HashSet<object> __sparse_from_context, string __sparse_from_path)"
            )
            .AppendLine("");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "value");
        }
        if (modelIsReferenceType)
        {
            code.AppendLineAt(3, "if (!__sparse_from_context.Add(value))");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "throw new global::System.NotSupportedException(\"Cyclic reference detected during From at '\" + __sparse_from_path + \"'. Fragment.From does not support cyclic object graphs; DeepClone preserves cycles.\");"
            );
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "try");
            code.AppendLineAt(3, "{");
            AppendFromModelBody(code, members, 4);
            code.AppendLineAt(3, "}");
            code.AppendLineAt(3, "finally");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "__sparse_from_context.Remove(value);");
            code.AppendLineAt(3, "}");
        }
        else
        {
            AppendFromModelBody(code, members, 3);
        }
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private void AppendFromModelBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        int indent
    )
    {
        code.AppendLineAt(indent, "return new Fragment");
        code.AppendLineAt(indent, "{");
        foreach (var member in members)
        {
            var access = "value." + SparseNaming.EscapeIdentifier(member.Property.Name);
            string value;
            if (member.ChildModel is null)
            {
                value = Expressions.CloneModelExpression(member, access);
            }
            else if (!member.ChildIsReferenceType)
            {
                value =
                    $"{member.ChildFragmentType}.From({access}, {CloneContext}, __sparse_from_context, {FromMemberPath(member)})";
            }
            else
            {
                value =
                    $"({access} is null ? null : {member.ChildFragmentType}.From({access}, {CloneContext}, __sparse_from_context, {FromMemberPath(member)}))";
            }

            code.AppendIndent(indent + 1)
                .Append(SparseNaming.EscapeIdentifier(member.Property.Name))
                .Append(" = ")
                .Append(Optional)
                .Append("<")
                .Append(SparseFragmentEmitHelpers.FragmentValueType(member))
                .Append(">.Present(")
                .Append(value)
                .AppendLine("),");
        }

        code.AppendLineAt(indent, "};");
    }

    private static string FromMemberPath(SparseMemberModel member)
    {
        var escaped = member.Property.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "(__sparse_from_path.Length == 0 ? \""
            + escaped
            + "\" : __sparse_from_path + \"."
            + escaped
            + "\")";
    }

    public static void AppendRootProjectionConstructor(
        SharedIndentedBuilder code,
        string modelName,
        ImmutableArray<SparseMemberModel> members,
        ModelConstructorBinding? constructor = null,
        string bridgeAccessibility = "private"
    )
    {
        if (
            ModelConstructionPlan.ForMembers(members).CanOverlayAfterConstruction
            && (constructor is null || constructor.Parameters.IsEmpty)
        )
            return;
        // Stage 4 (#193): split emission widens the token and constructor to
        // the narrowest CLR-mandated visibility so the operations class can
        // project through them; single-file keeps them private.
        code.AppendLineAt(1, bridgeAccessibility + " readonly struct __SparseProjectionToken { }");
        if (constructor?.IsImplicitParameterlessClassConstructor == true)
            code.AppendLineAt(1, "public " + modelName + "() { }");
        if (members.Any(static member => member.Property.IsRequired))
            code.AppendLineAt(1, "[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]");
        var arguments = constructor is null
            ? string.Empty
            : string.Join(
                ", ",
                constructor.Parameters.Select(parameter =>
                {
                    var member = members.FirstOrDefault(candidate =>
                        candidate.Property.Name == parameter.PropertyName
                    );
                    if (member.Property.Name is null)
                        return parameter.DefaultExpression;
                    var access =
                        "__sparse_projection."
                        + SparseNaming.EscapeIdentifier(parameter.PropertyName);
                    var value = access + ".Value!";
                    if (member.ChildModel is not null)
                        value = member.ChildIsReferenceType
                            ? access + ".Value?.ToModel()!"
                            : access + ".Value!.ToModel()";
                    return access + ".IsPresent ? " + value + " : " + parameter.DefaultExpression;
                })
            );
        code.AppendLineAt(
            1,
            bridgeAccessibility
                + " "
                + modelName
                + "(Fragment __sparse_projection, __SparseProjectionToken _) : this("
                + arguments
                + ")"
        );
        code.AppendLineAt(1, "{");
        foreach (var member in members.Where(static member => !member.Property.IsReadOnly))
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var access = "__sparse_projection." + name;
            var value = access + ".Value!";
            if (member.ChildModel is not null)
                value = member.ChildIsReferenceType
                    ? access + ".Value?.ToModel()!"
                    : access + ".Value!.ToModel()";
            if (member.Property.IsRequired)
                code.AppendLineAt(
                    2,
                    "this."
                        + name
                        + " = "
                        + access
                        + ".IsPresent ? "
                        + value
                        + " : this."
                        + name
                        + "!;"
                );
            else
                code.AppendLineAt(
                    2,
                    "if (" + access + ".IsPresent) this." + name + " = " + value + ";"
                );
        }
        code.AppendLineAt(1, "}");
    }

    public static void AppendToModel(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool hasRootProjectionConstructor = false,
        ModelConstructorBinding? constructor = null,
        string? operationsType = null,
        string receiver = ""
    )
    {
        // Stage 4 (#193): facades delegate both ToModel overloads; bodies move
        // with an explicit receiver (surface instance access is bare).
        if (operationsType is not null)
        {
            code.AppendIndent(2)
                .Append("public ")
                .Append(modelType)
                .Append(" ToModel(")
                .Append(modelType)
                .Append(" baseline) => ")
                .Append(operationsType)
                .AppendLine(".ToModel(this, baseline);");
            code.AppendIndent(2)
                .Append("public ")
                .Append(modelType)
                .AppendLine(" ToModel() => " + operationsType + ".ToModel(this);");
            return;
        }
        var isOperationsBody = receiver.Length != 0;
        if (isOperationsBody)
        {
            code.AppendIndent(2)
                .Append("public static ")
                .Append(modelType)
                .Append(" ToModel(Fragment fragment, ")
                .Append(modelType)
                .AppendLine(" baseline)");
        }
        else
        {
            code.AppendIndent(2)
                .Append("public ")
                .Append(modelType)
                .Append(" ToModel(")
                .Append(modelType)
                .AppendLine(" baseline)");
        }
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            isOperationsBody
                ? "return ToModel(Merge(From(baseline), fragment));"
                : "return From(baseline).Merge(this).ToModel();"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
        if (isOperationsBody)
            code.AppendIndent(2)
                .Append("public static ")
                .Append(modelType)
                .AppendLine(" ToModel(Fragment fragment)");
        else
            code.AppendIndent(2).Append("public ").Append(modelType).AppendLine(" ToModel()");
        code.AppendLineAt(2, "{");
        var construction = hasRootProjectionConstructor
            ? ModelConstructionPlan.ForMembers(members)
            : ModelConstructionPlan.ForStructuralMembers(members, constructor);
        if (
            hasRootProjectionConstructor
            && (
                !construction.CanOverlayAfterConstruction
                || (constructor is not null && !constructor.Parameters.IsEmpty)
            )
        )
        {
            // Stage 4 (#193): split emission widens the projection bridge so
            // the operations class can invoke it; single-file keeps it private.
            var token =
                receiver.Length == 0
                    ? "__SparseProjectionToken"
                    : modelType + ".__SparseProjectionToken";
            var source = receiver.Length == 0 ? "this" : "fragment";
            code.AppendLineAt(
                3,
                "return new " + modelType + "(" + source + ", default(" + token + "));"
            );
            code.AppendLineAt(2, "}");
            code.AppendLine();
            return;
        }
        if (construction.CanOverlayAfterConstruction)
        {
            var arguments = constructor is null
                ? string.Empty
                : string.Join(
                    ", ",
                    constructor.Parameters.Select(parameter =>
                    {
                        var member = members.FirstOrDefault(candidate =>
                            candidate.Property.Name == parameter.PropertyName
                        );
                        if (member.Property.Name is null)
                            return parameter.DefaultExpression;
                        var name = SparseNaming.EscapeIdentifier(parameter.PropertyName);
                        var access = receiver + name;
                        var projected = access + ".Value!";
                        if (member.ChildModel is not null)
                            projected = member.ChildIsReferenceType
                                ? access + ".Value?.ToModel()!"
                                : access + ".Value!.ToModel()";
                        return access
                            + ".IsPresent ? "
                            + projected
                            + " : "
                            + parameter.DefaultExpression;
                    })
                );
            code.AppendLineAt(3, "var value = new " + modelType + "(" + arguments + ");");
            foreach (
                var member in members.Where(static member =>
                    !member.Property.IsReadOnly && !member.Property.IsInitOnly
                )
            )
            {
                var name = SparseNaming.EscapeIdentifier(member.Property.Name);
                var access = receiver + name;
                var projected = access + ".Value!";
                if (member.ChildModel is not null)
                    projected = member.ChildIsReferenceType
                        ? access + ".Value?.ToModel()!"
                        : access + ".Value!.ToModel()";
                code.AppendLineAt(
                    3,
                    "if (" + access + ".IsPresent) value." + name + " = " + projected + ";"
                );
            }
            code.AppendLineAt(3, "return value;");
            code.AppendLineAt(2, "}");
            code.AppendLine();
            return;
        }
        if (!members.IsEmpty)
        {
            code.AppendIndent(3).Append("if (");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                code.Append(receiver)
                    .Append(SparseNaming.EscapeIdentifier(members[index].Property.Name))
                    .Append(".IsPresent");
            }

            code.AppendLine(")");
            code.AppendLineAt(3, "{");
            AppendModelInitializer(code, modelType, members, 4, false, receiver);
            code.AppendLineAt(3, "}");
        }

        code.AppendIndent(3).Append("var defaults = new ").Append(modelType).AppendLine("();");
        AppendModelInitializer(code, modelType, members, 3, true, receiver);
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private static void AppendModelInitializer(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        int indent,
        bool useDefaults,
        string receiver = ""
    )
    {
        code.AppendIndent(indent).Append("return new ").Append(modelType).AppendLine();
        code.AppendLineAt(indent, "{");
        foreach (var member in members)
        {
            var name = SparseNaming.EscapeIdentifier(member.Property.Name);
            var access = receiver + name;
            string value;
            if (member.ChildModel is null)
            {
                value = access + ".Value!";
            }
            else if (!member.ChildIsReferenceType)
            {
                value = access + ".Value!.ToModel()";
            }
            else
            {
                value = access + ".Value?.ToModel()!";
            }

            code.AppendIndent(indent + 1)
                .Append(name)
                .Append(" = ")
                .Append(
                    useDefaults ? access + ".IsPresent ? " + value + " : defaults." + name : value
                )
                .AppendLine(",");
        }

        code.AppendLineAt(indent, "};");
    }
}
