using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Builds standalone generated source for a <c>[SparseFragmentModel]</c>.</summary>
internal static class SparseFragmentEmitter
{
    public static string BuildSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseStructuralModel> structuralModels,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config,
        Action<
            SharedIndentedBuilder,
            SparseModelInfo,
            ImmutableArray<SparseMemberModel>
        >? appendProductExtensions = null
    )
    {
        return BuildSourceInternal(
            model,
            members,
            pocoCloneModels,
            structuralModels,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config,
            appendProductExtensions
        );
    }

    public static string BuildPromotedSource(
        SparsePromotedModel promoted,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config
    )
    {
        return BuildSourceInternal(
            promoted.Model,
            promoted.Members,
            promoted.PocoCloneModels,
            promoted.StructuralModels,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config,
            appendProductExtensions: null
        );
    }

    public static string GetPromotedHintName(
        SparseModelInfo model,
        string suffix,
        CancellationToken cancellationToken
    )
    {
        var fullyQualifiedName = model.ModelTypeName;
        return SparseNaming.Sanitize(fullyQualifiedName, cancellationToken)
            + "_"
            + SparseNaming.GetStableTypeHash(fullyQualifiedName, cancellationToken)
            + suffix;
    }

    private static string BuildSourceInternal(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseStructuralModel> structuralModels,
        bool bclHashSetImplementsReadOnlySet,
        bool bclHashSetSupportsCapacity,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config,
        Action<
            SharedIndentedBuilder,
            SparseModelInfo,
            ImmutableArray<SparseMemberModel>
        >? appendProductExtensions
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime =
            config.RuntimeDialect
            ?? throw new ArgumentException(
                "A runtime dialect is required for source emission.",
                nameof(config)
            );
        var patchDialect =
            config.PatchDialect
            ?? throw new ArgumentException(
                "A patch dialect is required for source emission.",
                nameof(config)
            );
        patchDialect = patchDialect with
        {
            HashSetSupportsCapacity = bclHashSetSupportsCapacity,
            MergeStrategyField =
                patchDialect.MergeStrategyField
                ?? (member => runtime.MergeStrategyFieldPrefix + member.Id),
            RebasePolicyField =
                patchDialect.RebasePolicyField
                ?? (
                    member =>
                        (
                            runtime.RebasePolicyFieldPrefix
                            ?? SparseWellKnownNames.RebasePolicyFieldPrefix
                        ) + member.Id
                ),
        };
        var expressions = new SparseFragmentExpressions(
            "__sparse_clone_context",
            runtime.ValueComparer,
            runtime.CollectionMerger,
            runtime.OptionalType
        );
        var core = new SparseFragmentCoreEmitter(
            runtime.OptionalType,
            runtime.MergeStrategyFieldPrefix,
            "__sparse_clone_context",
            runtime.ReferenceComparer,
            expressions,
            runtime.RebasePolicyFieldPrefix
        );
        _ = structuralModels;
        var portableSetView = SparseFragmentCoreEmitter.RequiresPortableSetView(
            bclHashSetImplementsReadOnlySet,
            members
                .Select(static member => member.Collection.NamedTypeDefinition)
                .Concat(
                    pocoCloneModels.SelectMany(static poco =>
                        poco.Members.Select(static member => member.Collection.NamedTypeDefinition)
                    )
                )
        );
        if (portableSetView)
        {
            members = ApplyPortableSetView(members);
            pocoCloneModels = pocoCloneModels
                .Select(static poco => poco with { Members = ApplyPortableSetView(poco.Members) })
                .ToImmutableArray();
        }

        var modelType = model.ModelTypeName;
        var generatedType = ModelDeclarationKeyword(model);
        var name = SparseNaming.EscapeIdentifier(model.Name);
        var code = new SharedIndentedBuilder(cancellationToken);
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        // Block-scoped namespace: file-scoped namespaces need C# 10, while the
        // generated surface targets C# 9 as its minimum language version.
        if (!model.IsGlobalNamespace)
        {
            code.Append("namespace ").Append(model.Namespace).AppendLine();
            code.AppendLine("{");
        }

        code.Append(generatedType).Append(name).AppendLine();
        code.AppendLine("{");
        SparseFragmentCoreEmitter.AppendRootProjectionConstructor(
            code,
            name,
            members,
            model.Constructor
        );
        core.AppendDeepClone(
            code,
            modelType,
            members,
            !pocoCloneModels.IsEmpty,
            model.Constructor,
            !model.IsStruct
        );
        foreach (var poco in pocoCloneModels)
            core.AppendPocoCloneHelper(
                code,
                poco.Model.ModelTypeName,
                poco.CloneHelperName,
                poco.Members,
                poco.Model.Constructor
            );
        SparseFragmentCoreEmitter.AppendCollectionCloneHelpers(
            code,
            portableSetView,
            bclHashSetSupportsCapacity
        );
        AppendFragment(
            code,
            modelType,
            members,
            !model.IsStruct,
            !pocoCloneModels.IsEmpty,
            core,
            expressions,
            runtime,
            patchDialect,
            constructor: model.Constructor,
            ignoredSettablePropertyNames: model.IgnoredSettablePropertyNames
        );
        if (!model.IsStruct)
        {
            SparseObservableEmitter.AppendObservable(code, modelType, members, runtime.Namespace);
        }

        code.AppendLine("}");
        appendProductExtensions?.Invoke(code, model, members);
        if (!model.IsGlobalNamespace)
        {
            code.AppendLine("}");
        }

        return code.ToString();
    }

    private static ImmutableArray<SparseMemberModel> ApplyPortableSetView(
        ImmutableArray<SparseMemberModel> members
    ) => members.Select(static member => member with { PortableSetView = true }).ToImmutableArray();

    private static string ModelDeclarationKeyword(SparseModelInfo model)
    {
        if (model.IsStruct)
        {
            return model.IsRecord ? "partial record struct " : "partial struct ";
        }

        // Short-form record keeps record-class models on the C# 9 floor:
        // "record class" spelling needs C# 10, and record structs need it anyway.
        return model.IsRecord ? "partial record " : "partial class ";
    }

    private static void AppendFragment(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool usesPocoCloning,
        SparseFragmentCoreEmitter core,
        SparseFragmentExpressions expressions,
        SparseRuntimeDialect runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        bool isRootModel = true,
        ModelConstructorBinding? constructor = null,
        ImmutableArray<string> ignoredSettablePropertyNames = default
    )
    {
        SparseFragmentCoreEmitter.AppendDeclaration(code, string.Empty, string.Empty);
        core.AppendMembers(
            code,
            members,
            runtime.MergeStrategyType,
            appendMemberAttributes: null,
            rebasePolicyBase: SparseFragmentPatchEmitter.GetRebasePolicyType(patchDialect),
            rebasePolicyField: patchDialect.RebasePolicyField
        );
        code.AppendLineAt(2, "/// <summary>The empty fragment.</summary>");
        code.AppendLineAt(2, "public static Fragment Empty { get; } = new();");
        AppendFragmentEquality(code, members, runtime.OptionalType, expressions, core);
        core.AppendFromModel(code, modelType, members, modelIsReferenceType, usesPocoCloning);
        SparseFragmentCoreEmitter.AppendToModel(code, modelType, members, isRootModel, constructor);
        core.AppendMerge(code, members);
        core.AppendApplyChanges(code, members);
        core.AppendDiff(code, modelType, members, modelIsReferenceType);
        core.AppendFragmentClone(code, members, usesPocoCloning);
        var canWriteInPlace =
            modelIsReferenceType
            && members.All(static member =>
                !member.Property.IsReadOnly && !member.Property.IsInitOnly
            );
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace
        );
        code.AppendLineAt(2, "public FragmentBuilder ToBuilder() => new(this);");
        SparseFragmentJsonEmitter.AppendStandaloneFragmentJson(code, members, runtime.OptionalType);
        code.AppendLineAt(1, "}");
        core.AppendBuilder(code, members);
        SparseFragmentPatchEmitter.AppendPatch(
            code,
            modelType,
            members,
            patchDialect,
            ignoredSettablePropertyNames,
            canWriteInPlace
        );
    }

    private static void AppendFragmentEquality(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optionalType,
        SparseFragmentExpressions expressions,
        SparseFragmentCoreEmitter core
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "internal static bool __SparseAreEqual("
                + optionalType
                + "<Fragment?> left, "
                + optionalType
                + "<Fragment?> right)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (!left.IsPresent) return !right.IsPresent;");
        code.AppendLineAt(3, "if (!right.IsPresent) return false;");
        code.AppendLineAt(
            3,
            "if (global::System.Object.ReferenceEquals(left.Value, right.Value)) return true;"
        );
        code.AppendLineAt(3, "if (left.Value is null || right.Value is null) return false;");
        if (members.IsEmpty)
        {
            code.AppendLineAt(3, "return true;");
        }
        else
        {
            code.AppendIndent(3).Append("return ");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.Append("__SparseEqual_")
                    .Append(members[index].Id)
                    .Append("(left.Value.")
                    .Append(name)
                    .Append(", right.Value.")
                    .Append(name)
                    .Append(")");
            }
            code.AppendLine(";");
        }
        code.AppendLineAt(2, "}");
        foreach (var member in members)
        {
            var valueType = SparseFragmentEmitHelpers.FragmentValueType(member);
            code.AppendIndent(2)
                .Append("internal static bool __SparseEqual_")
                .Append(member.Id)
                .Append("(")
                .Append(optionalType)
                .Append("<")
                .Append(valueType)
                .Append("> left, ")
                .Append(optionalType)
                .Append("<")
                .Append(valueType)
                .AppendLine("> right)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "if (!left.IsPresent) return !right.IsPresent;");
            code.AppendLineAt(3, "if (!right.IsPresent) return false;");
            string equality;
            if (member.ChildModel is not null)
            {
                equality = member.ChildFragmentType + ".__SparseAreEqual(left, right)";
            }
            else if (member.MergeStrategyType is not null)
            {
                equality = core.MergeStrategyField(member) + ".AreEqual(left.Value, right.Value)";
            }
            else
            {
                equality = expressions.ValueEqualityExpression(member, "left.Value", "right.Value");
            }
            code.AppendLineAt(3, "return " + equality + ";");
            code.AppendLineAt(2, "}");
        }
        code.AppendLine();
    }
}
