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
        return BuildSplitSource(
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
    /// the typed payload DTOs, Fragment JSON converter bodies and Fragment operations
    /// (reloc-2) alongside the Patch/ChangeSet operation containers (reloc-3, issue #194);
    /// without one the implementation is null and the surface is the legacy single file.</remarks>
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
        return BuildPromotedSplitSource(
            promoted,
            bclHashSetImplementsReadOnlySet,
            bclHashSetSupportsCapacity,
            cancellationToken,
            config
        ).Surface;
    }

    /// <summary>Builds split sources for one promoted model.</summary>
    /// <remarks>Union of the reloc-2 payload/Fragment-operations split and the
    /// reloc-3 Patch/ChangeSet operation split (issue #194).</remarks>
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

    /// <summary>Builds relocated UI/session/factory implementation sources for one model.</summary>
    /// <remarks>Reloc-1 family: per-model <c>Observable</c>, <c>ReadOnlyView</c>,
    /// <c>EditSession</c>, and descriptor-factory sources under distinct hints.
    /// Kept alongside the payload/Fragment-operations split implementation so
    /// both families stay under <c>AdditionalSources</c> with per-model isolation.</remarks>
    /// <param name="model">Model identity.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="readOnlyViewModels">Read-only view models.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>Additional sources, or empty for single-file emission.</returns>
    public static ImmutableArray<SparseGeneratedSource> BuildImplementationSources(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        CancellationToken cancellationToken,
        SparseGeneratorConfig config
    )
    {
        var implementationNamespace = SparseGeneratedPlacement.TryGetImplementationNamespace(
            config
        );
        if (implementationNamespace is null)
        {
            return ImmutableArray<SparseGeneratedSource>.Empty;
        }

        var features = config.EffectiveEmissionFeatures;
        if (!features.EmitFragment || !features.EmitObservable)
        {
            return ImmutableArray<SparseGeneratedSource>.Empty;
        }

        var runtime =
            config.RuntimeDialect
            ?? throw new ArgumentException(
                "A runtime dialect is required for source emission.",
                nameof(config)
            );
        var builder = ImmutableArray.CreateBuilder<SparseGeneratedSource>();
        if (!model.IsStruct)
        {
            builder.Add(
                SparseModelImplementationEmitter.BuildObservableSource(
                    model,
                    members,
                    config,
                    cancellationToken,
                    runtime.Namespace,
                    null
                )
            );
        }

        builder.Add(
            SparseModelImplementationEmitter.BuildReadOnlyViewSource(
                model,
                members,
                readOnlyViewModels,
                config,
                cancellationToken,
                implementationNamespace
            )
        );
        if (!model.IsStruct)
        {
            builder.Add(
                SparseModelImplementationEmitter.BuildEditSessionSource(
                    model,
                    members,
                    config,
                    cancellationToken
                )
            );
            if (config.DescriptorDialect is not null)
            {
                builder.Add(
                    SparseDescriptorFactoryEmitter.BuildSource(
                        model,
                        members,
                        config,
                        cancellationToken,
                        runtime.Namespace
                    )
                );
            }
        }

        return builder.ToImmutable();
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
            // Union aliases are written after the operation-target block below,
            // so a single implementation file carries one header plus the union
            // of reloc-2 (Fragment/FragmentBuilder/Patch/ChangeSet) and reloc-3
            // operation aliases with no duplicates and no interspersed usings.
        }
        // Issue #194 relocates Patch/ChangeSet algorithms into the per-model
        // operation container when the owning generator declares an
        // implementation namespace. The in-place flags feed both the surface
        // stubs and the implementation aliases, so they are hoisted here.
        var canApplyPatchInPlace = !model.IsStruct;
        var canApplyChangeSetInPlace =
            canApplyPatchInPlace
            && (
                patchDialect.InPlaceWriteUnavailableKindMemberName is not null
                || members.All(static member =>
                    !member.Property.IsReadOnly && !member.Property.IsInitOnly
                )
            );
        SharedIndentedBuilder? patchOperations = null;
        SharedIndentedBuilder? changeSetOperations = null;
        SparseOperationTarget? operationTarget = null;
        string? operationContainer = null;
        if (implementationNamespace is not null && features.EmitPatch)
        {
            operationContainer = SparseGeneratedPlacement.GetImplementationContainer(
                model,
                cancellationToken
            );
            patchOperations = new SharedIndentedBuilder(cancellationToken) { IndentOffset = 1 };
            changeSetOperations = new SharedIndentedBuilder(cancellationToken) { IndentOffset = 1 };
            operationTarget = new SparseOperationTarget(
                patchOperations,
                changeSetOperations,
                "global::"
                    + implementationNamespace
                    + "."
                    + operationContainer
                    + "."
                    + SparseGeneratedPlacement.PatchOperationsSimpleName,
                "global::"
                    + implementationNamespace
                    + "."
                    + operationContainer
                    + "."
                    + SparseGeneratedPlacement.ChangeSetOperationsSimpleName
            );
        }
        if (implementationBuilder is not null && implementationNamespace is not null)
        {
            // Single-file union: one header (above) plus the union of aliases,
            // then one namespace block co-hosting payload DTOs, Fragment
            // operations/converter (reloc-2) and the Patch/ChangeSet operation
            // container (reloc-3). File-level usings precede the namespace so
            // the file stays legal with no interspersed directives.
            var unionAliases = SparseModelOperationAliases.Collect(
                modelType,
                members,
                patchDialect,
                features,
                canApplyPatchInPlace
            );
            var seenAliases = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.Ordinal
            );
            // FragmentBuilder is reloc-2-only (Collect lacks it); Fragment is
            // provided by Collect when operations exist, otherwise by reloc-2.
            implementationBuilder.AppendLine(
                "using FragmentBuilder = global::" + TrimImplPrefix(modelType) + ".FragmentBuilder;"
            );
            seenAliases.Add("FragmentBuilder");
            if (operationTarget is null)
            {
                // No operations file: reloc-2 single-split aliases.
                implementationBuilder.AppendLine(
                    "using Fragment = global::" + TrimImplPrefix(modelType) + ".Fragment;"
                );
                seenAliases.Add("Fragment");
                if (features.EmitChangePayload)
                {
                    implementationBuilder.AppendLine(
                        "using Patch = global::" + TrimImplPrefix(modelType) + ".Patch;"
                    );
                    implementationBuilder.AppendLine(
                        "using ChangeSet = global::" + TrimImplPrefix(modelType) + ".ChangeSet;"
                    );
                    seenAliases.Add("Patch");
                    seenAliases.Add("ChangeSet");
                }
            }
            var payloadPrefix = patchDialect.PayloadImplementationContainerPrefix;
            foreach (
                var alias in unionAliases
                    .GroupBy(static a => a.Key)
                    .Select(static g => g.First())
                    .Where(a => !seenAliases.Contains(a.Key))
                    .Where(a =>
                        string.IsNullOrEmpty(payloadPrefix)
                        || !a.Key.StartsWith(payloadPrefix, System.StringComparison.Ordinal)
                    )
            )
            {
                // Split emission relocates payload DTO containers to the
                // implementation namespace with fully qualified child refs, so
                // model-nested container aliases from Collect would misresolve
                // (CS0426); they are omitted here while all operation aliases
                // (Patch/ChangeSet/Fragment/ChangePayload/ApplyInPlaceResult,
                // ChangeKind/ChangeInfo, collection patches, transitions) stay.
                implementationBuilder.AppendLine(
                    "using " + alias.Key + " = global::" + TrimAliasPrefix(alias.Value) + ";"
                );
            }
            if (HasNullableKeyedMember(members))
            {
                implementationBuilder.AppendLine(
                    "#pragma warning disable CS8714 // Present nullable values are valid dictionary keys."
                );
            }
            implementationBuilder.AppendLine();
            implementationBuilder.Append("namespace ").Append(implementationNamespace).AppendLine();
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
                operationsType: operationsType,
                operationTarget: operationTarget,
                canApplyPatchInPlace: canApplyPatchInPlace,
                canApplyChangeSetInPlace: canApplyChangeSetInPlace
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
        if (features.EmitFragment && features.EmitObservable && implementationNamespace is null)
        {
            // Relocated stage: UI/editing types live in AdditionalSources under
            // the configured namespace; the surface keeps only Fragment state.
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

        // Relocated descriptors call back into these bridges instead of
        // naming private attribute arrays directly. Emit them whenever
        // relocation is active (the same gate as the rewrite side in
        // SparseModelImplementationEmitter), not only when a descriptor
        // dialect is present, so the reference always resolves.
        if (implementationNamespace is not null && features.EmitFragment && features.EmitObservable)
        {
            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(member.Property.AttributeExpressions))
                {
                    code.AppendLineAt(
                        1,
                        "internal static global::System.Attribute[] __SparseAttributes_"
                            + member.Id
                            + "() => new global::System.Attribute[] { "
                            + member.Property.AttributeExpressions
                            + " };"
                    );
                }
            }
        }

        code.AppendLine("}");
        appendProductExtensions?.Invoke(code, model, members);
        if (!model.IsGlobalNamespace)
        {
            code.AppendLine("}");
        }

        if (implementationBuilder is null)
        {
            return (code.ToString(), null);
        }

        // Single-file union: payload DTOs, Fragment operations/converter
        // (reloc-2) already streamed into the implementation builder inside
        // its namespace block; co-host the Patch/ChangeSet operation container
        // (reloc-3) as a sibling partial class in the same namespace so one
        // hint carries coexisting containers under the fixed identity.
        if (
            operationTarget is not null
            && operationContainer is not null
            && patchOperations is not null
        )
        {
            var containerAccessibility = model.IsPublic ? "public" : "internal";
            implementationBuilder.AppendLineAt(
                1,
                "/// <summary>Per-model generated operations for '"
                    + modelType
                    + "'. Backs the model-facing Patch and ChangeSet facades.</summary>"
            );
            implementationBuilder.AppendLineAt(
                1,
                containerAccessibility + " static partial class " + operationContainer
            );
            implementationBuilder.AppendLineAt(1, "{");
            SparseModelOperationFileEmitter.OpenOperations(
                implementationBuilder,
                SparseGeneratedPlacement.PatchOperationsSimpleName,
                "Patch"
            );
            implementationBuilder.Append(patchOperations.ToString());
            SparseModelOperationFileEmitter.CloseOperations(implementationBuilder);
            if (features.EmitChangeSet && changeSetOperations is not null)
            {
                SparseModelOperationFileEmitter.OpenOperations(
                    implementationBuilder,
                    SparseGeneratedPlacement.ChangeSetOperationsSimpleName,
                    "ChangeSet"
                );
                implementationBuilder.Append(changeSetOperations.ToString());
                SparseModelOperationFileEmitter.CloseOperations(implementationBuilder);
            }
            implementationBuilder.AppendLineAt(1, "}");
        }
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
        string? operationsType = null,
        SparseOperationTarget? operationTarget = null,
        bool canApplyPatchInPlace = false,
        bool canApplyChangeSetInPlace = false
    )
    {
        SparseFragmentCoreEmitter.AppendDeclaration(
            code,
            string.Empty,
            string.Empty,
            accessibility: generatedAccessibility
        );
        // Public-first ordering: the empty singleton precedes the member
        // slots so no public member trails the internal strategy caches.
        code.AppendLineAt(2, "/// <summary>The empty fragment.</summary>");
        code.AppendLineAt(2, "public static Fragment Empty { get; } = new();");
        core.AppendMembers(
            code,
            members,
            runtime.MergeStrategyType,
            appendMemberAttributes: null,
            rebasePolicyBase: SparseFragmentPatchEmitter.GetRebasePolicyType(patchDialect),
            rebasePolicyField: patchDialect.RebasePolicyField
        );
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
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace,
            patchDialect.WriteContract,
            features
        );
        code.AppendLineAt(
            2,
            "/// <summary>Creates a mutable builder seeded from this fragment.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The seeded builder.</returns>");
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
        // Internal helpers trail the public surface (ToBuilder/JsonConverter).
        AppendFragmentEquality(code, members, runtime.OptionalType, expressions, core);
        if (canWriteInPlace || (features.EmitPatch && canApplyPatchInPlace))
        {
            AppendWritableMemberWriter(code, modelType, writableMembers);
        }
        code.AppendLineAt(1, "}");
        core.AppendBuilder(code, members, generatedAccessibility);
        SparseFragmentPatchEmitter.AppendPatch(
            code,
            modelType,
            members,
            patchDialect,
            ignoredSettablePropertyNames,
            canApplyPatchInPlace,
            features,
            generatedAccessibility,
            implementationNamespace,
            implementationBuilder,
            operationTarget,
            canApplyChangeSetInPlace
        );
        if (features.EmitChangeSet)
        {
            SparsePathBuilderEmitter.Append(code, modelType, members, patchDialect);
        }
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

    private static string TrimImplPrefix(string qualified) =>
        qualified.StartsWith("global::", System.StringComparison.Ordinal)
            ? qualified.Substring("global::".Length)
            : qualified;

    private static string TrimAliasPrefix(string qualified) => TrimImplPrefix(qualified);

    private static bool HasNullableKeyedMember(ImmutableArray<SparseMemberModel> members) =>
        members.Any(static member =>
            SparseKeyedCollectionEmitter.IsKeyedSequence(member)
            && member.Collection.KeyTypeName?.EndsWith("?", System.StringComparison.Ordinal) == true
        );
}
