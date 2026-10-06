using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(SparseFragmentBenchmarks318).Assembly).Run(args);
