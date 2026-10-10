# Three-layer certification report

Historical measurements: sequence equality and no-op Between used identical
references, and clone methods performed two clones per call. The generator
incremental input also grew with invocation count. Use the corrected suite in
[Measure and compare performance](performance-baseline.md) for new comparisons.

Scope: end-to-end benchmark and certification for the Runtime,
Generated-Once, and Per-Model layers defined in
[three-layer-ownership.md](../architecture/three-layer-ownership.md).
Issue: #189. The executable gates live in
`tests/SparseFragments.Tests/Shared/ThreeLayerCertificationTests.cs`;
the operation and generator matrices live in
`benchmarks/SparseFragments.Benchmarks/ThreeLayerCertificationBenchmarks.cs`.

All timing numbers below are machine-dependent and are not asserted by
tests. Byte counts, hint sets, and compilation results are deterministic
and are pinned by the certification tests.

## Reproduce

```powershell
dotnet build SparseFragments.slnx --configuration Release
dotnet test tests/SparseFragments.Tests/SparseFragments.Tests.csproj --configuration Release
dotnet run --project benchmarks/SparseFragments.Benchmarks/SparseFragments.Benchmarks.csproj --configuration Release -- --filter "*ThreeLayerOps*"
dotnet run --project benchmarks/SparseFragments.Benchmarks/SparseFragments.Benchmarks.csproj --configuration Release -- --filter "*ThreeLayerGenerator*" --job short
dotnet publish tests/SparseFragments.NativeAotSmoke/SparseFragments.NativeAotSmoke.csproj --configuration Release --framework net10.0
```

Environment for the numbers below: Windows 11, Intel Core i7-14700F,
.NET SDK 10.0.401, .NET 10.0.12 X64 RyuJIT.

## Generator size matrix

Collection models with one scalar and one `List<string>` member,
measured with a throwaway Roslyn driver (cold generation, then
`Emit` to memory):

| Models | Emitted files | Total bytes | Shared bytes | Assembly bytes |
| --- | --- | --- | --- | --- |
| 1 | 5 | 200,028 | 10,692 | 82,944 |
| 4 | 8 | 610,995 | 10,692 | 194,048 |
| 16 | 20 | 2,255,577 | 10,692 | 688,128 |

Shared helper bytes stay constant at 10,692 while totals grow with the
per-model output (about 137,000 bytes per collection model at this
shape). Four models cost 610,995 bytes against 800,112 for four times
the single-model total, a 24 percent saving that widens with model
count. The saving equals the shared bytes times (N - 1): each extra
model reuses the one compilation-scoped copy instead of carrying its
own. `SizeMatrix_SharedBytesStayConstantAsModelsGrow` pins the
sublinear growth and the byte-identical shared sources across runs.

When Generated-Once does not help: two independent compilations each
emit their own copy under the same hint name. Cross-assembly sharing
would need a compiled-runtime dependency for code that is private to
each consumer, which the design rejects (criterion C2). Replication
across assemblies is accepted; duplication within one compilation is
removed.

Incremental behavior: an unrelated per-model edit leaves every shared
source byte-identical with a stable hint set
(`UnrelatedEdit_PreservesHelperBytesAndHintSet` in the driver suite
covers this; the BDN incremental benchmarks return the same totals).
A capability-changing edit adds only the newly requested families and
leaves the unaffected ones untouched
(`CapabilityEdit_UpdatesOnlyAffectedFamilies`).

## Operation matrix

Full BDN matrix, 14 benchmarks at collection sizes 64 and 1024, over
generated leaf, keyed-holder, and dictionary-holder models:

| Operation | Size 64 | Size 1024 | Allocated 64 | Allocated 1024 |
| --- | --- | --- | --- | --- |
| Clone: model DeepClone | 166 ns | 655 ns | 1,200 B | 8,880 B |
| Clone: fragment DeepClone | 152 ns | 692 ns | 1,168 B | 8,848 B |
| Comparison: runtime scalar equality | ~0 ns | ~1 ns | 0 B | 0 B |
| Comparison: runtime sequence equality | ~0 ns | ~0 ns | 0 B | 0 B |
| Comparison: no-op Between | 16 ns | 15 ns | 136 B | 136 B |
| EditSession: 64 edits, no batching | 2.51 ms | 50.4 ms | 7,123,152 B | 126,033,216 B |
| EditSession: 64 edits, one BatchEdit | 64 µs | 967 µs | 175,648 B | 2,361,832 B |
| Keyed: Between, one edit plus one add | 18.1 µs | 369 µs | 52,448 B | 999,800 B |
| Keyed: patch apply | 2.25 µs | 37.9 µs | 5,352 B | 110,624 B |
| Dictionary: Between, edit plus append | 1.05 µs | 21.4 µs | 1,648 B | 9,328 B |
| Dictionary: patch apply | 595 ns | 8.5 µs | 2,120 B | 31,016 B |
| Patch/ChangeSet: ToPatch then apply | 33 ns | 31 ns | 72 B | 72 B |
| Patch/ChangeSet: invert then apply | 49 ns | 51 ns | 208 B | 208 B |
| Patch/ChangeSet: compose then apply | 137 ns | 1.30 µs | 208 B | 208 B |

Readings:

- No-op `Between` costs about 16 ns at either size: the comparison
  short-circuits before touching elements.
- `ToPatch` plus apply stays near 32 ns independent of size for a
  scalar transition; invert and compose add a bounded constant.
- One `BatchEdit` around 64 edits runs 39 times faster than separate
  edits at size 64 (64 µs against 2.51 ms) and 52 times faster at
  size 1024, with 40 to 53 times fewer bytes allocated.
- Clone scales about 4 times for a 16 times larger collection.
- Scalar and sequence runtime equality measures at the noise floor
  with no allocation.

## Layer ownership certification

`ThreeLayerCertificationTests` locks the ownership table:

- Every one of the 37 public `SparseFragmentRuntime` methods
  classifies into a documented Runtime family (equality, merge,
  rebase, clone/cycle, keyed, provenance) across 21 distinct names.
- The CloneKernels, ReadOnlyAdapters, and RemovalIndex families are
  emitted exactly once per compilation, are `internal`, and name only
  the owning namespace plus BCL types. No runtime or dialect token
  appears in them.
- Per-model call sites are live on the explicit-namespace path: the
  product generator sets `SparseFragments.Generated`, and per-model
  output calls the qualified shared helpers with no legacy
  per-model copies left behind.
- The null-namespace single-file path keeps legacy per-model private
  copies and names no shared helper. This matches the merge-commit
  honesty note (option b): no rewiring was assumed, and the test
  certifies the actual state of each path.
- Shared implementations never appear in exported types; the payload
  DTO public-visibility exception (E1) stays in per-model nested
  containers, outside these families.

## Dialect, framework, and AOT coverage

- The same clone, adapter, and removal behavior executes identically
  under a standard-shaped and a renamed downstream namespace with no
  `SparseFragments` reference
  (`StandardAndDownstream_DialectsAgreeOnSemantics`).
- An external assembly consumes `ChangeSet.Between`, `ToPatch`,
  `ApplyTo`, `CreateEditSession`, `Descriptors`, and `IDescriptorSet`
  without naming any generated implementation type, then runs
  (`CrossAssembly_ExternalConsumerUsesContractsWithoutImplTypes`).
- `IReadOnlySet` gating follows d7011db: the default render names no
  `IReadOnlySet` at all, the portable variant gates it in a bridge,
  and every render parses under C# 9 with no errors.
- Fixture builds pass with zero warnings and zero errors:
  netstandard2.0 consumer, net48 consumer, and the minimum-language
  consumer. The netstandard2.0 and net48 lanes are the behavioral
  compatibility gates for the old-TFM fallback.
- NativeAOT publish of `SparseFragments.NativeAotSmoke` succeeds with
  no trim or AOT analyzer warnings; the native binary runs 29 of 29
  tests. The Windows executable is 21.54 MB.

## Regression analysis and residual gaps

No functional regression was found; certifying required no product
change, so this track is tests, benchmarks, and this report only.

Known tradeoffs recorded here rather than fixed:

- Patch operation kernels keep their own stable hint as an unwired
  seam (noted in the generator wiring comment). They are small and
  stable; wiring them into the capability plane is follow-up work.
- The null-namespace path duplicates helpers per model. It exists for
  downstream dialects without a placement namespace; products that
  set `GeneratedImplementationNamespace` never pay it.
- Rebase and provenance bodies stay in the compiled runtime because
  hand-written benchmarks call the facade directly. Moving them would
  trade one compiled copy for one copy per consumer compilation with
  no per-model output saving today; the audit sizes this at 59 KB
  combined.
- BDN timings above are single-machine observations. Only the byte
  counts, hint sets, and pass/fail gates run in CI.
