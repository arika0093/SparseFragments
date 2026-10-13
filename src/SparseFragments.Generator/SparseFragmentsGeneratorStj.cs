using System;
using Microsoft.CodeAnalysis;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Generator;

/// <summary>System.Text.Json capability wiring for the standalone generator.</summary>
/// <remarks>
/// Split from <c>SparseFragmentsGenerator</c> to keep the entry file within
/// the size guideline; it owns only the compilation capability probe, the
/// per-compilation configuration derivation, and the partial-surface
/// diagnostic (issues #242/#243) with no analysis changes.
/// </remarks>
public sealed partial class SparseFragmentsGenerator
{
    /// <summary>Probes the consuming compilation for usable System.Text.Json APIs.</summary>
    /// <param name="context">Generator initialization context.</param>
    /// <returns>Deterministic capability snapshot provider.</returns>
    private static IncrementalValueProvider<SparseStjCapabilities> CreateStjCapabilitiesProvider(
        IncrementalGeneratorInitializationContext context
    ) =>
        context
            .CompilationProvider.Select(
                static (compilation, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return SparseStjCapabilityDetector.Probe(compilation);
                }
            )
            .WithComparer(EqualityComparer<SparseStjCapabilities>.Default)
            .WithTrackingName("SparseFragmentsGenerator.StjCapabilities");

    /// <summary>Reports an actionable diagnostic when the surface is partial but JSON is requested.</summary>
    /// <remarks>
    /// A fully absent surface stays silent so core-only consumers that never
    /// opted out of the default JSON families still build cleanly. Partial
    /// surfaces omit the JSON artifacts in the emitters; this diagnostic names
    /// the missing APIs instead of failing silently.
    /// </remarks>
    /// <param name="context">Generator initialization context.</param>
    /// <param name="stjCapabilities">Capability snapshot provider.</param>
    /// <param name="hasModels">Whether the compilation declares any model.</param>
    private static void RegisterStjSurfaceValidation(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<SparseStjCapabilities> stjCapabilities,
        IncrementalValueProvider<bool> hasModels
    )
    {
        var stjDiagnostics = stjCapabilities
            .Combine(hasModels)
            .Select(
                static (input, _) =>
                    input.Right
                    && (
                        Configuration.EffectiveEmissionFeatures.EmitJsonConverters
                        || Configuration.EffectiveEmissionFeatures.EmitChangePayload
                    )
                    && input.Left.HasSystemTextJson
                    && !input.Left.SupportsV1Wire
                        ? "The referenced System.Text.Json API surface lacks "
                            + input.Left.DescribeMissing()
                            + " required for the ChangePayload v1 contract; JSON serialization artifacts are omitted. Reference a compatible System.Text.Json version to re-enable JSON support."
                        : string.Empty
            )
            .WithComparer(EqualityComparer<string>.Default)
            .WithTrackingName("SparseFragmentsGenerator.StjCapabilitiesValidation");
        context.RegisterSourceOutput(
            stjDiagnostics,
            static (productionContext, message) =>
            {
                if (message.Length != 0)
                    productionContext.ReportDiagnostic(
                        Diagnostic.Create(InvalidEmissionPlan, Location.None, message)
                    );
            }
        );
    }

    /// <summary>Derives the per-compilation configuration from probed capabilities.</summary>
    /// <remarks>
    /// The probed snapshot travels through both the config layer (top-level
    /// decisions) and the patch dialect (deep emitters that only receive the
    /// dialect), keeping one capability source for the whole render.
    /// </remarks>
    private static SparseGeneratorConfig WithCapabilities(
        SparseGeneratorConfig configuration,
        bool allowFileScopedTypes,
        SparseStjCapabilities stjCapabilities
    )
    {
        var withCapabilities = configuration with
        {
            AllowFileScopedTypes = allowFileScopedTypes,
            StjCapabilities = stjCapabilities,
        };
        if (withCapabilities.PatchDialect is { } patchDialect)
            withCapabilities = withCapabilities with
            {
                PatchDialect = patchDialect with { StjCapabilities = stjCapabilities },
            };
        return withCapabilities;
    }
}
