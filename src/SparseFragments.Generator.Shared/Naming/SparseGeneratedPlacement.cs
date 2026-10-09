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

    private static string FullyQualifiedIdentity(SparseModelInfo model) =>
        model.IsGlobalNamespace
            ? "global::" + model.ModelTypeName
            : model.Namespace + "." + model.ModelTypeName;
}
