using BenchmarkDotNet.Running;

if (args.Length == 1 && args[0] == "--baseline-validate-inputs")
    BaselineInputValidation.Run();
else if (args.Length == 1 && args[0] == "--temporary-payload-validate-inputs")
    TemporaryKeyPayloadBenchmarks.ValidateInputs();
else if (args.Length == 2 && args[0] == "--payload-sizes")
    PayloadGeneratorBenchmarks.WriteSizes(args[1]);
else if (
    args.Contains("--baseline", StringComparer.Ordinal)
    || args.Contains("--baseline-sizes", StringComparer.Ordinal)
)
    PerformanceBaseline.Run(args);
else
    BenchmarkSwitcher.FromAssembly(typeof(SparseFragmentBenchmarks318).Assembly).Run(args);
