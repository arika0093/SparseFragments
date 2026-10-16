using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Builds model-nested UI/editing implementation sources.</summary>
/// <remarks>UI/editing types (<c>Observable</c>.</remarks>
internal static class SparseModelImplementationEmitter
{
    /// <summary>Builds the model-nested observable implementation source.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="runtimeNamespace">Runtime helper namespace.</param>
    /// <param name="descriptorDialect">Descriptor contracts, or null when disabled.</param>
    /// <returns>Additional source with a distinct hint.</returns>
    public static SparseGeneratedSource BuildObservableSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        string runtimeNamespace,
        SparseDescriptorDialect? descriptorDialect
    )
    {
        using var innerBuilder = new SharedIndentedBuilder(cancellationToken);
        SparseObservableEmitter.AppendObservable(
            innerBuilder,
            model.ModelTypeName,
            members,
            runtimeNamespace,
            descriptorDialect
        );
        var inner = innerBuilder.ToString();
        if (
            descriptorDialect is null
            && config.DescriptorDialect is { } configured
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            // Thin bridge keeps child "$proxy.__SparseGet_X(path)" call sites
            // working after the graph moves to DescriptorFactory.
            var accessor = SparseObservableDescriptorEmitter.AccessorName(model.ModelTypeName);
            var factoryRef = SparseGeneratedPlacement.GetDescriptorFactoryReference(
                model,
                config,
                cancellationToken
            );
            var bridge =
                "        internal "
                + configured.DescriptorSetInterface
                + " "
                + accessor
                + "("
                + configured.EffectivePathType(runtimeNamespace)
                + " pathPrefix) => "
                + factoryRef
                + ".Create(this, pathPrefix);\n";
            var privateMarker = "\n        private ";
            var privateIndex = inner.IndexOf(privateMarker, System.StringComparison.Ordinal);
            if (privateIndex >= 0)
            {
                inner =
                    inner.Substring(0, privateIndex + 1)
                    + bridge
                    + inner.Substring(privateIndex + 1);
            }
            else
            {
                var closing = "\n    }\n";
                var index = inner.LastIndexOf(closing, System.StringComparison.Ordinal);
                if (index >= 0)
                {
                    inner = inner.Substring(0, index) + "\n" + bridge + inner.Substring(index + 1);
                }
            }
        }

        var source = WrapInModel(model, inner, cancellationToken);
        return new SparseGeneratedSource(
            SparseGeneratedPlacement.ObservableHintName(model, cancellationToken),
            source
        );
    }

    /// <summary>Builds the model-nested read-only-view implementation source.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="readOnlyViewModels">POCO view models.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="implementationNamespace">Explicit namespace for shared adapters.</param>
    /// <returns>Additional source with a distinct hint.</returns>
    public static SparseGeneratedSource BuildReadOnlyViewSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken,
        string? implementationNamespace
    )
    {
        using var innerBuilder = new SharedIndentedBuilder(cancellationToken);
        SparseReadOnlyViewEmitter.AppendReadOnlyView(
            innerBuilder,
            model.ModelTypeName,
            members,
            readOnlyViewModels,
            implementationNamespace
        );
        _ = readOnlyViewModels;
        var inner = innerBuilder.ToString();
        var source = WrapInModel(model, inner, cancellationToken);
        return new SparseGeneratedSource(
            SparseGeneratedPlacement.ReadOnlyViewHintName(model, cancellationToken),
            source
        );
    }

    /// <summary>Builds the model-nested per-model edit-session implementation source.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Additional source with a distinct hint.</returns>
    public static SparseGeneratedSource BuildEditSessionSource(
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var family = config.EffectiveFamilyNames;
        var observableSimple = SparseObservableEmitter.ObservableTypeName(members, family);
        var readOnlySimple = SparseReadOnlyViewEmitter.ReadOnlyViewTypeName(members, family);
        var observableRef = SparseGeneratedPlacement.GetObservableReference(
            model,
            observableSimple,
            config,
            cancellationToken
        );
        var readOnlyRef = SparseGeneratedPlacement.GetReadOnlyViewReference(
            model,
            readOnlySimple,
            config,
            cancellationToken
        );
        using var code = new SharedIndentedBuilder(cancellationToken);
        SparseRelocatedEditSessionCore.Append(
            code,
            model,
            members,
            config,
            observableRef,
            readOnlyRef
        );
        var source = WrapInModel(model, code.ToString(), cancellationToken);
        return new SparseGeneratedSource(
            SparseGeneratedPlacement.EditSessionHintName(model, cancellationToken),
            source
        );
    }

    private static string WrapInModel(
        SparseModelInfo model,
        string inner,
        CancellationToken cancellationToken
    )
    {
        // Mirrors the surface declaration keyword so the nested UI types land
        // in the annotated model partial (records/structs included).
        var accessibility = model.IsPublic ? "public " : "internal ";
        string keyword;
        if (model.IsStruct)
        {
            keyword =
                accessibility + (model.IsRecord ? "partial record struct " : "partial struct ");
        }
        else
        {
            keyword = accessibility + (model.IsRecord ? "partial record " : "partial class ");
        }

        using var code = new SharedIndentedBuilder(cancellationToken);
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        if (!model.IsGlobalNamespace)
        {
            code.Append("namespace ").Append(model.Namespace).AppendLine();
            code.AppendLine("{");
        }

        code.Append(keyword).Append(SparseNaming.EscapeIdentifier(model.Name)).AppendLine();
        code.AppendLine("{");
        code.Append(inner);
        code.AppendLine("}");
        if (!model.IsGlobalNamespace)
        {
            code.AppendLine("}");
        }

        return code.ToString();
    }

    /// <summary>Rewrites inline attribute arrays to model-local bridges.</summary>
    /// <remarks>UI bodies nest in the model again.</remarks>
    /// <param name="source">Emitted body.</param>
    /// <param name="modelType">Qualified model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The body calling back into model bridges.</returns>
    internal static string RelocateAttributeBridges(
        string source,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        CancellationToken cancellationToken
    )
    {
        // Private nested attributes stay inside the model bridge; relocated
        // descriptors call back instead of naming private types directly.
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(member.Property.AttributeExpressions))
            {
                var legacy =
                    "new global::System.Attribute[] { "
                    + member.Property.AttributeExpressions
                    + " }";
                var bridge = modelType + ".__SparseAttributes_" + member.Id + "()";
                source = SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
                    source,
                    legacy,
                    bridge,
                    cancellationToken
                );
            }
        }

        return source;
    }

    internal static string RelocateUiReferences(
        string source,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<SparseReadOnlyViewModel> readOnlyViewModels,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var root = SparseGeneratedPlacement.TryGetImplementationNamespace(config);
        if (root is null)
        {
            return source;
        }

        source = RelocateAttributeBridges(source, modelType, members, cancellationToken);

        // Collect distinct fragment identities from members and POCO views.
        var fragments = new System.Collections.Generic.Dictionary<string, SparseTypeModel>(
            System.StringComparer.Ordinal
        );
        void Add(SparseTypeModel type)
        {
            if (type.IsFragmentModel && !string.IsNullOrEmpty(type.NonNullableName))
            {
                fragments.TryAdd(type.NonNullableName, type);
            }
        }

        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.ChildModel is { } child)
            {
                Add(child);
            }

            Add(member.Collection.ElementType);
            if (member.Collection.ValueType is { } value)
            {
                Add(value);
            }
        }

        if (!readOnlyViewModels.IsDefaultOrEmpty)
        {
            foreach (var view in readOnlyViewModels)
            {
                foreach (var member in view.Members)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (member.ChildModel is { } child)
                    {
                        Add(child);
                    }

                    Add(member.Collection.ElementType);
                    if (member.Collection.ValueType is { } value)
                    {
                        Add(value);
                    }
                }
            }
        }

        // Longest names first so overlapping prefixes replace
        // deterministically.
        foreach (
            var pair in fragments.Values.OrderByDescending(static type =>
                type.NonNullableName.Length
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qualified = pair.NonNullableName;
            var observableSimple = pair.ObservableTypeName ?? "Observable";
            var readOnlySimple = pair.ReadOnlyViewTypeName ?? "ReadOnlyView";
            var container = SparseGeneratedPlacement.GetContainerForQualifiedName(
                qualified,
                cancellationToken
            );
            var observableQualified = "global::" + root + "." + container + "." + observableSimple;
            var readOnlyQualified = "global::" + root + "." + container + "." + readOnlySimple;
            source = SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
                source,
                qualified + "." + observableSimple,
                observableQualified,
                cancellationToken
            );
            source = SparseCodeRewrite.ReplaceOutsideLiteralsAndComments(
                source,
                qualified + "." + readOnlySimple,
                readOnlyQualified,
                cancellationToken
            );
        }

        return source;
    }
}
