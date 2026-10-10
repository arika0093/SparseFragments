using BenchmarkDotNet.Running;

if (args.Length == 1 && args[0] == "--baseline-validate-inputs")
    BaselineInputValidation.Run();
else if (
    args.Contains("--baseline", StringComparer.Ordinal)
    || args.Contains("--baseline-sizes", StringComparer.Ordinal)
)
    PerformanceBaseline.Run(args);
else
    BenchmarkSwitcher.FromAssembly(typeof(SparseFragmentBenchmarks318).Assembly).Run(args);
