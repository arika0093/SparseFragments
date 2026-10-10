using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Builds relocated UI/editing implementation sources outside the model partial.</summary>
/// <remarks>
/// Stage 2 of the placement track reuses the existing nested emitters for the
/// class bodies, then rewrites nested <c>Model.Observable</c> style references
/// to the configured generated-implementation namespace. Replacement targets
/// fully qualified <c>NonNullableName.Simple</c> pairs outside string literals
/// and comments; bare model names and accessor hashes are untouched.
/// </remarks>
internal static class SparseModelImplementationEmitter
{
    /// <summary>Builds the relocated observable implementation source.</summary>
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
        var innerBuilder = new SharedIndentedBuilder(cancellationToken);
        SparseObservableEmitter.AppendObservable(
            innerBuilder,
            model.ModelTypeName,
            members,
            runtimeNamespace,
            descriptorDialect
        );
        var inner = RelocateUiReferences(
            innerBuilder.ToString(),
            model.ModelTypeName,
            members,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            config,
            cancellationToken
        );
        if (
            descriptorDialect is null
            && config.DescriptorDialect is { } configured
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            // Thin bridge keeps child "$proxy.__SparseGet_X(path)" call sites
            // working after the graph moves to DescriptorFactory.
            var accessor = SparseObservableDescriptorEmitter.AccessorName(model.ModelTypeName);
            var bridge =
                "        internal "
                + configured.DescriptorSetInterface
                + " "
                + accessor
                + "("
                + configured.EffectivePathType(runtimeNamespace)
                + " pathPrefix) => DescriptorFactory.Create(this, pathPrefix);\n";
            var closing = "\n    }\n";
            var index = inner.LastIndexOf(closing, System.StringComparison.Ordinal);
            if (index >= 0)
            {
                inner = inner.Substring(0, index) + "\n" + bridge + inner.Substring(index + 1);
            }
        }

        var source = WrapInContainer(model, inner, config, cancellationToken);
        return new SparseGeneratedSource(
            SparseGeneratedPlacement.ObservableHintName(model, cancellationToken),
            source
        );
    }

    /// <summary>Builds the relocated read-only-view implementation source.</summary>
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
        var innerBuilder = new SharedIndentedBuilder(cancellationToken);
        SparseReadOnlyViewEmitter.AppendReadOnlyView(
            innerBuilder,
            model.ModelTypeName,
            members,
            readOnlyViewModels,
            implementationNamespace
        );
        var inner = RelocateUiReferences(
            innerBuilder.ToString(),
            model.ModelTypeName,
            members,
            readOnlyViewModels,
            config,
            cancellationToken
        );
        var source = WrapInContainer(model, inner, config, cancellationToken);
        return new SparseGeneratedSource(
            SparseGeneratedPlacement.ReadOnlyViewHintName(model, cancellationToken),
            source
        );
    }

    /// <summary>Builds the relocated per-model edit-session implementation source.</summary>
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
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseRelocatedEditSessionCore.Append(
            code,
            model,
            members,
            config,
            observableRef,
            readOnlyRef
        );
        var source = WrapInContainer(model, code.ToString(), config, cancellationToken);
        return new SparseGeneratedSource(
            SparseGeneratedPlacement.EditSessionHintName(model, cancellationToken),
            source
        );
    }

    private static string WrapInContainer(
        SparseModelInfo model,
        string inner,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var root =
            SparseGeneratedPlacement.TryGetImplementationNamespace(config)
            ?? throw new System.ArgumentException(
                "An implementation namespace is required.",
                nameof(config)
            );
        var container = SparseGeneratedPlacement.GetImplementationContainer(
            model,
            cancellationToken
        );
        var accessibility = model.IsPublic ? "public" : "internal";
        var code = new SharedIndentedBuilder(cancellationToken);
        code.AppendLine("// <auto-generated />");
        code.AppendLine("#nullable enable");
        code.Append("namespace ").Append(root).AppendLine();
        code.AppendLine("{");
        code.AppendLineAt(1, "/// <summary>Relocated per-model implementations.</summary>");
        code.AppendLineAt(1, accessibility + " partial class " + container);
        code.AppendLineAt(1, "{");
        code.Append(inner);
        code.AppendLineAt(1, "}");
        code.AppendLine("}");
        return code.ToString();
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

        // Private nested attributes stay inside the model bridge; relocated
        // descriptors call back instead of naming private types directly.
        // Replacement skips string literals and comments so user content
        // (for example attribute string arguments) is never rewritten.
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

        // Longest names first so overlapping prefixes replace deterministically.
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
