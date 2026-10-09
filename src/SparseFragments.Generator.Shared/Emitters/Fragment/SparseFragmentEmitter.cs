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
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
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
            readOnlyViewModels,
            structuralModels,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config,
            appendProductExtensions
        ).Surface;
    }

    /// <summary>Builds the split surface and per-model implementation sources.</summary>
    /// <remarks>With an explicit implementation namespace the second source carries
    /// the typed payload DTOs and Fragment JSON converter bodies; without one the
    /// implementation is null and the surface is the legacy single file.</remarks>
    public static (string Surface, string? Implementation) BuildSplitSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
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
            readOnlyViewModels,
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
            promoted.ReadOnlyViewModels,
            promoted.StructuralModels,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config,
            appendProductExtensions: null
        ).Surface;
    }

    /// <summary>Builds split sources for one promoted model.</summary>
    public static (string Surface, string? Implementation) BuildPromotedSplitSource(
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
            promoted.ReadOnlyViewModels,
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

    private static (string Surface, string? Implementation) BuildSourceInternal(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparsePocoCloneModel> pocoCloneModels,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
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
            HashSetImplementsReadOnlySet = bclHashSetImplementsReadOnlySet,
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
        var features = config.EffectiveEmissionFeatures;
        var planErrors = features.ValidateDependencies();
        if (!planErrors.IsDefaultOrEmpty && planErrors.Length > 0)
            throw new ArgumentException(planErrors[0], nameof(config));
        SparseDownstreamPolicy.ThrowOnInvalidTransport(members, patchDialect);
        // Generated-Once (#182): with an explicit namespace the collection
        // clone kernels live once per compilation; null keeps the legacy
        // per-model private helpers for downstream dialects.
        var implementationNamespace = SparseGeneratedPlacement.TryGetImplementationNamespace(
            config
        );
        var cloneKernelsPrefix = implementationNamespace is null
            ? null
            : SparseGeneratedOnceNames.QualifiedCloneKernels(implementationNamespace);
        // Stage 4 (#193): split emission moves Fragment algorithms to an
        // operations class. The surface expressions qualify relocated POCO
        // helpers (the staying clone bridge calls them); the operations
        // expressions resolve helpers and strategy fields in-class.
        var splitOperations = implementationNamespace is not null && features.EmitFragment;
        string? operationsType = null;
        if (splitOperations)
            operationsType =
                "global::"
                + implementationNamespace
                + "."
                + SparseGeneratedPlacement.GetFragmentOperationsSimpleName(
                    model,
                    cancellationToken
                );
        var expressions = new SparseFragmentExpressions(
            "__sparse_clone_context",
            runtime.ValueComparer,
            runtime.CollectionMerger,
            runtime.OptionalType,
            config.EffectiveFamilyNames,
            cloneKernelsPrefix,
            memberFieldQualifier: "",
            pocoHelperQualifier: operationsType is null ? "" : operationsType + "."
        );
        var core = new SparseFragmentCoreEmitter(
            runtime.OptionalType,
            runtime.MergeStrategyFieldPrefix,
            "__sparse_clone_context",
            runtime.ReferenceComparer,
            expressions,
            runtime.RebasePolicyFieldPrefix
        );
        SparseFragmentCoreEmitter? operationsCore = null;
        if (splitOperations)
        {
            var operationsExpressions = new SparseFragmentExpressions(
                "__sparse_clone_context",
                runtime.ValueComparer,
                runtime.CollectionMerger,
                runtime.OptionalType,
                config.EffectiveFamilyNames,
                cloneKernelsPrefix,
                memberFieldQualifier: "Fragment."
            );
            operationsCore = new SparseFragmentCoreEmitter(
                runtime.OptionalType,
                runtime.MergeStrategyFieldPrefix,
                "__sparse_clone_context",
                runtime.ReferenceComparer,
                operationsExpressions,
                runtime.RebasePolicyFieldPrefix,
                fieldQualifier: "Fragment."
            );
        }
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
        var generatedAccessibility = model.IsPublic ? "public" : "internal";
        // Stage 3 (#192): with an explicit implementation namespace the typed
        // payload DTOs and Fragment JSON converter bodies move to a per-model
        // implementation source; the surface keeps facades plus using aliases
        // so simple container references resolve without protocol changes.
        var splitImplementation = implementationNamespace is not null && features.EmitFragment;
        SharedIndentedBuilder? implementationBuilder = null;
        string? jsonConverterSimpleName = null;
        string? jsonConverterQualifiedName = null;
        if (splitImplementation)
        {
            jsonConverterSimpleName = SparseGeneratedPlacement.GetFragmentJsonConverterSimpleName(
                model,
                cancellationToken
            );
            jsonConverterQualifiedName =
                "global::" + implementationNamespace + "." + jsonConverterSimpleName;
            implementationBuilder = new SharedIndentedBuilder(cancellationToken);
            implementationBuilder.AppendLine("// <auto-generated />");
            implementationBuilder.AppendLine("#nullable enable");
            // Aliases resolve simple references identically to single-file output.
            // Fragment/Builder back every operations body; Patch/ChangeSet back
            // payload cores only, so split files never carry unused usings.
            implementationBuilder.AppendLine("using Fragment = " + modelType + ".Fragment;");
            implementationBuilder.AppendLine(
                "using FragmentBuilder = " + modelType + ".FragmentBuilder;"
            );
            if (features.EmitChangePayload)
            {
                // Payload core blocks reference sibling facades by simple name.
                implementationBuilder.AppendLine("using Patch = " + modelType + ".Patch;");
                implementationBuilder.AppendLine("using ChangeSet = " + modelType + ".ChangeSet;");
            }
            implementationBuilder
                .Append("namespace ")
                .Append(implementationNamespace!)
                .AppendLine();
            implementationBuilder.AppendLine("{");
        }
        var code = new SharedIndentedBuilder(cancellationToken);
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        if (splitImplementation && features.EmitChangePayload)
        {
            // Child payload references stay fully qualified; only the current
            // container resolves through a file alias (stage 3, issue #192).
            var payloadContainerAlias = SparseChangeSetPayloadEmitter.RequirePayloadContainerName(
                patchDialect,
                modelType
            );
            code.AppendLine(
                "using "
                    + payloadContainerAlias
                    + " = global::"
                    + implementationNamespace
                    + "."
                    + payloadContainerAlias
                    + ";"
            );
        }
        if (
            members.Any(static member =>
                SparseKeyedCollectionEmitter.IsKeyedSequence(member)
                && member.Collection.KeyTypeName?.EndsWith("?", StringComparison.Ordinal) == true
            )
        )
        {
            code.AppendLine(
                "#pragma warning disable CS8714 // Present nullable values are valid dictionary keys."
            );
        }
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
            model.Constructor,
            bridgeAccessibility: splitOperations ? "internal" : "private"
        );
        core.AppendDeepClone(
            code,
            modelType,
            members,
            !pocoCloneModels.IsEmpty,
            model.Constructor,
            !model.IsStruct,
            operationsType: operationsType
        );
        foreach (var poco in pocoCloneModels)
            core.AppendPocoCloneHelper(
                code,
                poco.Model.ModelTypeName,
                poco.CloneHelperName,
                poco.Members,
                poco.Model.Constructor,
                operationsType: operationsType
            );
        // Shared kernels (#182) are emitted once per compilation; only the
        // legacy single-file path redefines them per model.
        if (implementationNamespace is null)
        {
            SparseFragmentCoreEmitter.AppendCollectionCloneHelpers(
                code,
                portableSetView,
                bclHashSetSupportsCapacity
            );
        }
        if (features.EmitFragment)
        {
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
                features,
                generatedAccessibility,
                constructor: model.Constructor,
                ignoredSettablePropertyNames: model.IgnoredSettablePropertyNames,
                implementationNamespace: implementationNamespace,
                implementationBuilder: implementationBuilder,
                jsonConverterQualifiedName: jsonConverterQualifiedName,
                jsonConverterSimpleName: jsonConverterSimpleName,
                operationsType: operationsType
            );
            // Stage 4 (#193): moved algorithms live in the operations class in
            // the same implementation file (hints stay per-model via the shared
            // resolver, so incremental isolation is unchanged).
            if (splitOperations && implementationBuilder is not null && operationsCore is not null)
                SparseFragmentOperationsEmitter.AppendOperations(
                    implementationBuilder,
                    model,
                    members,
                    pocoCloneModels,
                    operationsCore
                );
        }
        if (features.EmitFragment && features.EmitObservable)
        {
            if (!model.IsStruct)
            {
                SparseObservableEmitter.AppendObservable(
                    code,
                    modelType,
                    members,
                    runtime.Namespace,
                    config.DescriptorDialect
                );
            }
            SparseReadOnlyViewEmitter.AppendReadOnlyView(
                code,
                modelType,
                members,
                readOnlyViewModels,
                implementationNamespace
            );
        }

        code.AppendLine("}");
        appendProductExtensions?.Invoke(code, model, members);
        if (!model.IsGlobalNamespace)
        {
            code.AppendLine("}");
        }

        if (implementationBuilder is null)
            return (code.ToString(), null);

        implementationBuilder.AppendLine("}");
        return (code.ToString(), implementationBuilder.ToString());
    }

    private static ImmutableArray<SparseMemberModel> ApplyPortableSetView(
        ImmutableArray<SparseMemberModel> members
    ) => members.Select(static member => member with { PortableSetView = true }).ToImmutableArray();

    private static string ModelDeclarationKeyword(SparseModelInfo model)
    {
        var accessibility = model.IsPublic ? "public " : "internal ";
        if (model.IsStruct)
        {
            return accessibility + (model.IsRecord ? "partial record struct " : "partial struct ");
        }

        // Short-form record keeps record-class models on the C# 9 floor:
        // "record class" spelling needs C# 10, and record structs need it anyway.
        return accessibility + (model.IsRecord ? "partial record " : "partial class ");
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
        SparseEmissionFeatures features,
        string generatedAccessibility,
        bool isRootModel = true,
        ModelConstructorBinding? constructor = null,
        ImmutableArray<string> ignoredSettablePropertyNames = default,
        string? implementationNamespace = null,
        SharedIndentedBuilder? implementationBuilder = null,
        string? jsonConverterQualifiedName = null,
        string? jsonConverterSimpleName = null,
        string? operationsType = null
    )
    {
        SparseFragmentCoreEmitter.AppendDeclaration(
            code,
            string.Empty,
            string.Empty,
            accessibility: generatedAccessibility
        );
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
        core.AppendFromModel(
            code,
            modelType,
            members,
            modelIsReferenceType,
            usesPocoCloning,
            operationsType: operationsType
        );
        SparseFragmentCoreEmitter.AppendToModel(
            code,
            modelType,
            members,
            isRootModel,
            constructor,
            operationsType: operationsType
        );
        core.AppendMerge(code, members, operationsType: operationsType);
        core.AppendApplyChanges(code, members, operationsType: operationsType);
        core.AppendDiff(code, modelType, members, modelIsReferenceType, operationsType);
        core.AppendFragmentClone(code, members, usesPocoCloning);
        var writableMembers = members
            .Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly)
            .ToImmutableArray();
        var canWriteInPlace = modelIsReferenceType && writableMembers.Length == members.Length;
        var canApplyInPlace = modelIsReferenceType;
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace,
            patchDialect.WriteContract,
            features
        );
        if (canWriteInPlace || (features.EmitPatch && canApplyInPlace))
        {
            AppendWritableMemberWriter(code, modelType, writableMembers);
        }
        code.AppendLineAt(2, "public FragmentBuilder ToBuilder() => new(this);");
        if (features.EmitJsonConverters)
        {
            if (
                implementationBuilder is not null
                && jsonConverterQualifiedName is not null
                && jsonConverterSimpleName is not null
            )
            {
                // Stage 3 (#192): converter bodies live in the implementation
                // source; the model keeps the accessor plus a thin shell.
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJsonFacade(
                    code,
                    jsonConverterQualifiedName
                );
                SparseFragmentJsonEmitter.AppendConverter(
                    implementationBuilder,
                    members,
                    runtime.OptionalType,
                    isStandalone: true,
                    converterClassName: jsonConverterSimpleName,
                    converterAccessibility: generatedAccessibility,
                    sealedConverter: false
                );
                implementationBuilder.AppendLine();
            }
            else
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJson(
                    code,
                    members,
                    runtime.OptionalType
                );
            }
        }
        code.AppendLineAt(1, "}");
        core.AppendBuilder(code, members, generatedAccessibility);
        SparseFragmentPatchEmitter.AppendPatch(
            code,
            modelType,
            members,
            patchDialect,
            ignoredSettablePropertyNames,
            canApplyInPlace,
            features,
            generatedAccessibility,
            implementationNamespace,
            implementationBuilder
        );
    }

    private static void AppendWritableMemberWriter(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLineAt(2, "internal void __SparseWriteWritableTo(" + modelType + " model)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (model is null) throw new global::System.ArgumentNullException(nameof(model));"
        );
        if (!members.IsEmpty)
        {
            code.AppendLineAt(3, "var __sparse_updated = ToModel();");
        }
        foreach (var member in members)
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (
                member.Collection.Kind == SparseCollectionKind.List
                && member.Collection.CloneKind == SparseCloneCollectionKind.List
            )
            {
                var listType =
                    "global::System.Collections.Generic.List<"
                    + member.Collection.ElementType.Name
                    + ">";
                code.AppendLineAt(
                    3,
                    "if (model."
                        + property
                        + " is "
                        + listType
                        + " __sparse_list"
                        + member.Id
                        + " && __sparse_updated."
                        + property
                        + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "__sparse_list" + member.Id + ".Clear();");
                code.AppendLineAt(
                    4,
                    "__sparse_list" + member.Id + ".AddRange(__sparse_updated." + property + ");"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else model." + property + " = __sparse_updated." + property + "!;"
                );
            }
            else if (member.Collection.IsDictionary)
            {
                var dictionaryType =
                    "global::System.Collections.Generic.Dictionary<"
                    + member.Collection.ElementType.Name
                    + ", "
                    + member.Collection.ValueType!.Value.Name
                    + ">";
                code.AppendLineAt(
                    3,
                    "if (model."
                        + property
                        + " is "
                        + dictionaryType
                        + " __sparse_dict"
                        + member.Id
                        + " && __sparse_updated."
                        + property
                        + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "__sparse_dict" + member.Id + ".Clear();");
                code.AppendLineAt(
                    4,
                    "foreach (var __sparse_pair"
                        + member.Id
                        + " in __sparse_updated."
                        + property
                        + ") __sparse_dict"
                        + member.Id
                        + ".Add(__sparse_pair"
                        + member.Id
                        + ".Key, __sparse_pair"
                        + member.Id
                        + ".Value);"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else model." + property + " = __sparse_updated." + property + "!;"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "model." + property + " = __sparse_updated." + property + "!;"
                );
            }
        }
        code.AppendLineAt(2, "}");
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
            else if (member.ComparisonComparerType is not null)
            {
                equality =
                    SparseFragmentEmitHelpers.ComparisonComparerField(member)
                    + ".Equals(left.Value, right.Value)";
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
