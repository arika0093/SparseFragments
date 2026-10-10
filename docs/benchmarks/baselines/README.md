# Recorded performance baselines

The [Windows x64 baseline](windows-x64.json) records the 45-case full run of
`3b0d68f` on 2026-10-10: Windows 11, Intel Core i7-14700F, SDK 10.0.401 and
.NET 10.0.12. Each case includes time, the 99.9% interval, allocated bytes and
Gen0/Gen1/Gen2 collections per 1,000 operations. The size entries contain UTF-8
generated source bytes, file counts, source fingerprints and assembly bytes.

The [Linux x64 baseline](linux-x64.json) records the full 45-case CI capture of
`e015e8f` on Ubuntu 24.04.5 with an AMD EPYC 9V74, the same SDK and runtime.
The [successful CI run](https://github.com/arika0093/SparseFragments/actions/runs/38023044656)
retains its raw reports and logs as the `performance-baseline` artifact.

These observations apply to that machine and workload. Some time intervals are
wide; use the intervals and repeated measurements when assessing a later change.
The runner checks environment and workload compatibility before reporting ratios.

To compare a later revision on the same environment:

```powershell
python benchmarks/run-baseline.py --output artifacts/baseline-next --previous docs/benchmarks/baselines/windows-x64.json
```

Use `linux-x64.json` for a matching Linux environment. Each file records its
own environment; comparisons across the two environments are unavailable.

See [Measure and compare performance](../performance-baseline.md) for workload
boundaries, the fixed job configuration and CI artifact retention. Full raw
reports from the initial local capture are in
`BenchmarkDotNet.Artifacts/baseline-v1` in the performance worktree. Subsequent
CI captures are available as the `performance-baseline` artifact of each
Performance baseline workflow run.
