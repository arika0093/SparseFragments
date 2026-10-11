using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Perfolizer.Horology;

internal static class PerformanceBaseline
{
    internal static void Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--baseline-sizes")
        {
            BaselineGeneratorBenchmarks.WriteSizes(args[1]);
            return;
        }
        var smoke = args.Contains("--smoke", StringComparer.Ordinal);
        var remaining = args.Where(arg => arg is not "--baseline" and not "--smoke").ToArray();
        var runtimeJob = CreateJob("runtime", smoke).WithWarmupCount(smoke ? 1 : 20);
        var generatorJob = CreateJob("generator", smoke).WithWarmupCount(smoke ? 1 : 50);
        var compilationJob = CreateJob("compile", smoke);
        compilationJob = smoke
            ? compilationJob.WithWarmupCount(1)
            : compilationJob.WithMinWarmupCount(100).WithMaxWarmupCount(250);
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(runtimeJob)
            .AddJob(generatorJob)
            .AddJob(compilationJob)
            .AddExporter(JsonExporter.Full)
            .AddFilter(
                new SimpleFilter(item =>
                {
                    if (item.Descriptor.Type == typeof(BaselineGeneratorBenchmarks))
                    {
                        var job =
                            item.Descriptor.WorkloadMethod.Name
                            == nameof(BaselineGeneratorBenchmarks.Compile)
                                ? compilationJob
                                : generatorJob;
                        return item.Job.Id == job.Id;
                    }
                    return item.Job.Id == runtimeJob.Id
                        && (
                            item.Descriptor.Type == typeof(ThreeLayerOpsBenchmarks)
                            || item.Descriptor.Type == typeof(EditWorkflowBenchmarks)
                        );
                })
            );
        BenchmarkSwitcher.FromAssembly(typeof(PerformanceBaseline).Assembly).Run(remaining, config);
    }

    private static Job CreateJob(string group, bool smoke) =>
        Job
            .Default.WithId("baseline-" + (smoke ? "smoke-" : string.Empty) + group + "-v2")
            .WithLaunchCount(smoke ? 1 : 2)
            .WithIterationCount(smoke ? 1 : 15)
            .WithIterationTime(TimeInterval.FromMilliseconds(250));
}
