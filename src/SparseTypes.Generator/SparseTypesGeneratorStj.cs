using System;
using Microsoft.CodeAnalysis;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Generator;

/// <summary>System.Text.Json capability wiring for the standalone generator.</summary>
/// <remarks>Split from <c>SparseTypesGenerator</c> to keep the entry file within the size guideline.</remarks>
public sealed partial class SparseTypesGenerator
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
            .WithTrackingName("SparseTypesGenerator.StjCapabilities");

    /// <summary>Reports an actionable diagnostic when the surface is partial but JSON is requested.</summary>
    /// <remarks>Core-only consumers need no JSON diagnostic.</remarks>
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
            .WithTrackingName("SparseTypesGenerator.StjCapabilitiesValidation");
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
    /// <remarks>Shares probed capabilities across config and patch dialects.</remarks>
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
