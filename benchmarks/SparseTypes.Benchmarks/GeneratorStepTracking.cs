using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

/// <summary>Incremental step-tracking probe shared by the generator benchmarks Counts tracked outputs that are reused or recomputed.</summary>
internal static class GeneratorStepTracking
{
    public static readonly string[] TrackedStageNames =
    [
        "SparseTypesGenerator.Analysis",
        "SparseTypesGenerator.BclSetSupport",
        "SparseTypesGenerator.BlazorEditSessions",
        "SparseTypesGenerator.Output",
        "SparseTypesGenerator.PromotedPerRoot",
        "SparseTypesGenerator.PromotedContributions",
        "SparseTypesGenerator.ExplicitRoots",
        "SparseTypesGenerator.PromotedDedup",
        "SparseTypesGenerator.PromotedDistinct",
        "SparseTypesGenerator.Promoted",
        "SparseTypesGenerator.PromotedConflicts",
        "SparseTypesGenerator.IsExternalInit",
    ];

    public static GeneratorDriver CreateTrackedDriver() =>
        CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new SparseTypesGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );

    public static int CachedOutputs(GeneratorRunResult result)
    {
        var cached = 0;
        foreach (var stage in TrackedStageNames)
        {
            if (!result.TrackedSteps.TryGetValue(stage, out var steps))
            {
                continue;
            }

            foreach (var step in steps)
            {
                foreach (var output in step.Outputs)
                {
                    if (output.Reason == IncrementalStepRunReason.Cached)
                    {
                        cached++;
                    }
                }
            }
        }

        return cached;
    }

    public static int RecomputedOutputs(GeneratorRunResult result)
    {
        var recomputed = 0;
        foreach (var stage in TrackedStageNames)
        {
            if (!result.TrackedSteps.TryGetValue(stage, out var steps))
            {
                continue;
            }

            foreach (var step in steps)
            {
                foreach (var output in step.Outputs)
                {
                    if (output.Reason != IncrementalStepRunReason.Cached)
                    {
                        recomputed++;
                    }
                }
            }
        }

        return recomputed;
    }

    public static int TotalSourceBytes(GeneratorRunResult result)
    {
        var bytes = 0;
        foreach (var source in result.GeneratedSources)
        {
            bytes += System.Text.Encoding.UTF8.GetByteCount(source.SourceText.ToString());
        }

        return bytes;
    }
}
