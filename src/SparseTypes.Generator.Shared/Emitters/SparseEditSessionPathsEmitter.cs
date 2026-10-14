using System.Threading;

namespace SparseTypes.Generator.Shared;

/// <summary>Typed path-navigation surface for generated root edit sessions (issue #270).</summary>
/// <remarks>
/// Emits one cached <c>Paths</c> property per generated session returning the
/// existing generated <c>SparsePaths&lt;TModel&gt;</c> builder, plus the
/// opt-in <c>ISparseEditSessionWithPaths&lt;TModel, TPaths&gt;</c> base.
/// Path-only access never touches the mutable model, the change cache, or
/// edit notifications: the builder is cached per session from the root path.
/// Child sessions use the same emission, with their builder rooted at the
/// child model type.
/// </remarks>
internal static class SparseEditSessionPathsEmitter
{
    /// <summary>Whether the typed Paths surface can be emitted for this configuration.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="dialect">Product patch dialect.</param>
    /// <returns>True when the provider contract and the typed-path surface both exist.</returns>
    internal static bool HasPathsSurface(
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    ) =>
        config.EditSessionWithPathsInterfaceMetadataName is { Length: > 0 }
        && dialect.TypedPathInterfaceType is { Length: > 0 };

    /// <summary>Resolves the session Paths builder type for one model.</summary>
    /// <remarks>
    /// Relocated builders live in the per-model implementation container;
    /// single-file emission keeps them nested in the model.
    /// </remarks>
    /// <param name="model">Owning model.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The qualified root builder type.</returns>
    internal static string GetPathsType(
        SparseModelInfo model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var root = SparseGeneratedPlacement.TryGetImplementationNamespace(config);
        if (root is null)
        {
            return "SparsePaths<" + model.ModelTypeName + ">";
        }

        return "global::"
            + root
            + "."
            + SparseGeneratedPlacement.GetImplementationContainer(model, cancellationToken)
            + ".SparsePaths<"
            + model.ModelTypeName
            + ">";
    }

    /// <summary>Resolves the opt-in provider base for one session, or empty when disabled.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="modelType">Qualified model type name.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="dialect">Product patch dialect.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The interface base fragment, or empty.</returns>
    internal static string GetPathsInterfaceBase(
        SparseModelInfo model,
        string modelType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        CancellationToken cancellationToken
    )
    {
        if (!HasPathsSurface(config, dialect))
        {
            return string.Empty;
        }

        return ", global::"
            + config.EditSessionWithPathsInterfaceMetadataName
            + "<"
            + modelType
            + ", "
            + GetPathsType(model, config, cancellationToken)
            + ">";
    }

    /// <summary>Appends the public Paths property for one root session.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="model">Owning model.</param>
    /// <param name="modelType">Qualified model type name.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="dialect">Product patch dialect.</param>
    internal static void AppendPathsProperty(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        string modelType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasPathsSurface(config, dialect))
        {
            return;
        }

        var pathsType = GetPathsType(model, config, code.CancellationToken);
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed path navigation rooted at this session's model.</summary>"
        );
        code.AppendLineAt(
            2,
            "/// <remarks>Path-only access: returns the cached builder without reading the mutable model, invalidating the change cache, or subscribing to edit notifications. For example <c>Paths.Customer.Name</c>.</remarks>"
        );
        code.AppendLineAt(2, "public " + pathsType + " Paths => __paths;");
    }

    /// <summary>Appends the cached Paths backing field for one root session.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="model">Owning model.</param>
    /// <param name="modelType">Qualified model type name.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="dialect">Product patch dialect.</param>
    internal static void AppendPathsField(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        string modelType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (!HasPathsSurface(config, dialect))
        {
            return;
        }

        var pathsType = GetPathsType(model, config, code.CancellationToken);
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        code.AppendLineAt(
            2,
            "private readonly "
                + pathsType
                + " __paths = new "
                + pathsType
                + "("
                + pathType
                + ".Root(typeof("
                + modelType
                + ")));"
        );
    }
}
