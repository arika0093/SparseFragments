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
        var job = Job
            .Default.WithId(smoke ? "baseline-smoke-v1" : "baseline-v1")
            .WithLaunchCount(smoke ? 1 : 2)
            .WithWarmupCount(smoke ? 1 : 8)
            .WithIterationCount(smoke ? 1 : 15)
            .WithIterationTime(TimeInterval.FromMilliseconds(250));
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .AddJob(job)
            .AddExporter(JsonExporter.Full)
            .AddFilter(
                new SimpleFilter(item =>
                    item.Descriptor.Type == typeof(ThreeLayerOpsBenchmarks)
                    || item.Descriptor.Type == typeof(EditWorkflowBenchmarks)
                    || item.Descriptor.Type == typeof(BaselineGeneratorBenchmarks)
                )
            );
        BenchmarkSwitcher.FromAssembly(typeof(PerformanceBaseline).Assembly).Run(remaining, config);
    }
}
