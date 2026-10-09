using System.Collections.Generic;
using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Central generated type-location and name resolver.</summary>
/// <remarks>
/// <para>
/// Per-model generated implementations (operations, state holders, payload
/// DTOs, converters, UI and editing types) must live under deterministic,
/// collision-safe names instead of being constructed independently in each
/// emitter as nested <c>ChildModel.Observable</c> style references. All
/// placement decisions flow through this resolver.
/// </para>
/// <para>
/// Placement is product-neutral: the implementation namespace comes from
/// <see cref="SparseGeneratorConfig"/> and there is no implicit fallback.
/// A null namespace keeps the current single-file emission; stages that
/// relocate types require an explicit namespace from the owning generator.
/// </para>
/// </remarks>
internal static class SparseGeneratedPlacement
{
    /// <summary>Gets the configured implementation namespace, or null when disabled.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <returns>The explicit namespace, or null for single-file emission.</returns>
    public static string? TryGetImplementationNamespace(SparseGeneratorConfig config) =>
        string.IsNullOrEmpty(config.GeneratedImplementationNamespace)
            ? null
            : config.GeneratedImplementationNamespace;

    /// <summary>Gets the deterministic per-model implementation container name.</summary>
    /// <remarks>
    /// The container derives from the stable fully qualified model identity,
    /// not from filesystem paths or emission ordering, so unrelated model
    /// edits never churn other roots. Sanitized-name collisions resolve
    /// through the stable hash suffix. Global-namespace and internal models
    /// share the same scheme: naming never depends on accessibility.
    /// </remarks>
    /// <param name="model">Model identity.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A valid C# type name unique per model identity.</returns>
    public static string GetImplementationContainer(
        SparseModelInfo model,
        CancellationToken cancellationToken
    )
    {
        var identity = FullyQualifiedIdentity(model);
        var container =
            SparseNaming.Sanitize(identity, cancellationToken)
            + "_"
            + SparseNaming.GetStableTypeHash(identity, cancellationToken);
        return char.IsLetter(container[0]) || container[0] == '_' ? container : "_" + container;
    }

    /// <summary>Gets the fully qualified implementation type name, or null when disabled.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="simpleName">Simple type name inside the model container.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The qualified name, or null for single-file emission.</returns>
    public static string? GetImplementationTypeName(
        SparseModelInfo model,
        SparseGeneratorConfig config,
        string simpleName,
        CancellationToken cancellationToken
    )
    {
        var root = TryGetImplementationNamespace(config);
        return root is null
            ? null
            : root + "." + GetImplementationContainer(model, cancellationToken) + "." + simpleName;
    }

    /// <summary>Resolves a collision-aware generated UI type name.</summary>
    /// <remarks>
    /// Unifies the previously duplicated <c>Observable</c> and
    /// <c>ReadOnlyView</c> loops: a source member with the same name pushes
    /// the generated type behind a <c>Sparse</c> prefix.
    /// </remarks>
    /// <param name="baseName">Preferred type name.</param>
    /// <param name="memberNames">Source member names competing for the name.</param>
    /// <returns>The usable type name.</returns>
    public static string ResolveUiTypeName(string baseName, IEnumerable<string> memberNames)
    {
        var taken = new HashSet<string>(memberNames, StringComparer.Ordinal);
        var prefix = new System.Text.StringBuilder();
        while (taken.Contains(prefix.ToString() + baseName))
        {
            prefix.Append("Sparse");
        }

        return prefix.ToString() + baseName;
    }

    /// <summary>Gets the model-facing surface hint name.</summary>
    /// <param name="model">Model identity.</param>
    /// <returns>The existing per-model hint name.</returns>
    public static string SurfaceHintName(SparseModelInfo model) => model.HintName;

    /// <summary>Gets a stable unique implementation hint name.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="implementationSuffix">Owning generator implementation suffix.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A hint name distinct from the surface file.</returns>
    public static string ImplementationHintName(
        SparseModelInfo model,
        string implementationSuffix,
        CancellationToken cancellationToken
    )
    {
        var identity = FullyQualifiedIdentity(model);
        return SparseNaming.Sanitize(identity, cancellationToken)
            + "_"
            + SparseNaming.GetStableTypeHash(identity, cancellationToken)
            + implementationSuffix;
    }

    private static string FullyQualifiedIdentity(SparseModelInfo model)
    {
        // Display names arrive global::-qualified in production
        // ("global::Ns.Model"). Strip the alias before hashing so the
        // identity can never double to "Ns.global::Ns.Model". Test doubles
        // may carry a simple name instead; those keep the legacy namespace
        // qualification so distinct namespaces stay distinct.
        if (model.ModelTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            return model.ModelTypeName.Substring("global::".Length);
        }

        return model.IsGlobalNamespace
            ? "global::" + model.ModelTypeName
            : model.Namespace + "." + model.ModelTypeName;
    }

    /// <summary>Gets the container name for a qualified child type name.</summary>
    /// <remarks>
    /// Mirrors <see cref="FullyQualifiedIdentity"/> so parent references to a
    /// child resolve to the same container the child's own file declares.
    /// </remarks>
    /// <param name="qualifiedTypeName">Child <c>NonNullableName</c>.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A container name matching the model's own scheme.</returns>
    public static string GetContainerForQualifiedName(
        string qualifiedTypeName,
        CancellationToken cancellationToken
    )
    {
        var identity = QualifiedIdentity(qualifiedTypeName);
        var container =
            SparseNaming.Sanitize(identity, cancellationToken)
            + "_"
            + SparseNaming.GetStableTypeHash(identity, cancellationToken);
        return char.IsLetter(container[0]) || container[0] == '_' ? container : "_" + container;
    }

    /// <summary>Gets the qualified observable reference, relocating when enabled.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="simpleName">Collision-resolved simple name.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Nested reference for single-file emission, qualified otherwise.</returns>
    public static string GetObservableReference(
        SparseModelInfo model,
        string simpleName,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var root = TryGetImplementationNamespace(config);
        return root is null
            ? model.ModelTypeName + "." + simpleName
            : "global::"
                + root
                + "."
                + GetImplementationContainer(model, cancellationToken)
                + "."
                + simpleName;
    }

    /// <summary>Gets the qualified read-only-view reference, relocating when enabled.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="simpleName">Collision-resolved simple name.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Nested reference for single-file emission, qualified otherwise.</returns>
    public static string GetReadOnlyViewReference(
        SparseModelInfo model,
        string simpleName,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => GetObservableReference(model, simpleName, config, cancellationToken);

    /// <summary>Gets the qualified edit-session reference, relocating when enabled.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Nested reference for single-file emission, qualified otherwise.</returns>
    public static string GetEditSessionReference(
        SparseModelInfo model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => GetObservableReference(model, "EditSession", config, cancellationToken);

    /// <summary>Gets the qualified child observable reference.</summary>
    /// <param name="child">Child type model.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Nested reference for single-file emission, qualified otherwise.</returns>
    public static string GetChildObservableReference(
        SparseTypeModel child,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var simple = child.ObservableTypeName ?? "Observable";
        var root = TryGetImplementationNamespace(config);
        return root is null
            ? child.NonNullableName + "." + simple
            : "global::"
                + root
                + "."
                + GetContainerForQualifiedName(child.NonNullableName, cancellationToken)
                + "."
                + simple;
    }

    /// <summary>Gets the qualified child read-only-view reference.</summary>
    /// <param name="child">Child type model.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Nested reference for single-file emission, qualified otherwise.</returns>
    public static string GetChildReadOnlyViewReference(
        SparseTypeModel child,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var simple = child.ReadOnlyViewTypeName ?? "ReadOnlyView";
        var root = TryGetImplementationNamespace(config);
        return root is null
            ? child.NonNullableName + "." + simple
            : "global::"
                + root
                + "."
                + GetContainerForQualifiedName(child.NonNullableName, cancellationToken)
                + "."
                + simple;
    }

    /// <summary>Gets the qualified descriptor-factory reference for a model.</summary>
    /// <param name="model">Owning model.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Nested reference for single-file emission, qualified otherwise.</returns>
    public static string GetDescriptorFactoryReference(
        SparseModelInfo model,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    ) => GetObservableReference(model, "DescriptorFactory", config, cancellationToken);

    /// <summary>Gets the qualified child descriptor-factory reference.</summary>
    /// <param name="childQualifiedName">Child <c>NonNullableName</c>.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Factory reference matching the child's own container.</returns>
    public static string GetChildDescriptorFactoryReference(
        string childQualifiedName,
        SparseGeneratorConfig config,
        CancellationToken cancellationToken
    )
    {
        var root = TryGetImplementationNamespace(config);
        return root is null
            ? childQualifiedName + ".DescriptorFactory"
            : "global::"
                + root
                + "."
                + GetContainerForQualifiedName(childQualifiedName, cancellationToken)
                + ".DescriptorFactory";
    }

    /// <summary>Gets the observable implementation hint name.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A hint distinct from the surface file.</returns>
    public static string ObservableHintName(
        SparseModelInfo model,
        CancellationToken cancellationToken
    ) => ImplementationHintName(model, ".Observable.g.cs", cancellationToken);

    /// <summary>Gets the read-only-view implementation hint name.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A hint distinct from the surface file.</returns>
    public static string ReadOnlyViewHintName(
        SparseModelInfo model,
        CancellationToken cancellationToken
    ) => ImplementationHintName(model, ".ReadOnlyView.g.cs", cancellationToken);

    /// <summary>Gets the edit-session implementation hint name.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A hint distinct from the surface file.</returns>
    public static string EditSessionHintName(
        SparseModelInfo model,
        CancellationToken cancellationToken
    ) => ImplementationHintName(model, ".EditSession.g.cs", cancellationToken);

    /// <summary>Gets the descriptor-factory implementation hint name.</summary>
    /// <param name="model">Model identity.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A hint distinct from the surface file.</returns>
    public static string DescriptorFactoryHintName(
        SparseModelInfo model,
        CancellationToken cancellationToken
    ) => ImplementationHintName(model, ".DescriptorFactory.g.cs", cancellationToken);

    private static string QualifiedIdentity(string qualifiedTypeName)
    {
        // Child NonNullableName display names normalize exactly like the
        // child's own identity above: strip one global:: alias and never
        // re-prefix the namespace, so nested "global::Ns.Outer.Inner" hashes
        // whole on both sides instead of diverging per side.
        if (qualifiedTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            return qualifiedTypeName.Substring("global::".Length);
        }

        return qualifiedTypeName;
    }
}
