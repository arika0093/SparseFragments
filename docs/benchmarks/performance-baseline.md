# Measure and compare performance

Run the baseline on an idle machine with the SDK selected by `global.json`
and Python 3.10 or later. From the repository root:

```powershell
python benchmarks/run-baseline.py --output artifacts/baseline-first
python benchmarks/run-baseline.py --output artifacts/baseline-next --previous artifacts/baseline-first/baseline.json
```

Each output directory must be new. Keep the complete directory, including the
BenchmarkDotNet JSON and logs. `baseline.json` stores numeric values in ns and
bytes; `comparison.md` reports changes against the supplied previous run.
Missing results, failed setup, and non-finite measurements fail collection even
when BenchmarkDotNet exits successfully.

Use `--smoke` to check the harness with one launch and one measured iteration.
Smoke results have a separate profile and cannot be compared with full runs.
A full run uses two launches, eight warmups, fifteen measured iterations, and
a 250 ms target iteration duration. The suite contains 45 cases.

## Workloads and measurement boundaries

| Group | Inputs | Included work |
| --- | --- | --- |
| Single operations | 64 and 1,024 collection elements | One model/fragment clone, scalar and sequence equality, Between, EditSession batching, keyed/dictionary patches, invert and compose |
| Edit workflow | 64 and 1,024 keyed elements; replay and conflict | Model clone, EditSession creation, 64 child edits and one addition in BatchEdit, CreateChangeSet, ToPayload, UTF-8 JSON serialization/deserialization, ToChangeSet, remote model clone, RebaseOnto and patch ApplyTo |
| Fresh generation | 1, 4 and 16 collection models | New Compilation and generator driver, generation and UTF-8 output byte counting; excludes metadata-reference loading and assembly Emit |
| Incremental generation | Same model counts | A bounded comment edit, syntax parsing, Compilation replacement, existing-driver generation and UTF-8 byte counting |
| Compilation | Same model counts | New Compilation over generated syntax trees and references, binding, lowering and Release assembly Emit to a new MemoryStream; excludes generation and syntax parsing |

MemoryDiagnoser records allocated bytes and Gen0/Gen1/Gen2 collections for every
timed case. The normalized GC values are collections per 1,000 operations.
The size probe separately records generated UTF-8 bytes, file count, a SHA-256
fingerprint over ordered hint names and source bytes, and emitted assembly bytes.
Compilation time is the `BaselineGeneratorBenchmarks.Compile` measurement.
Build logs are retained for troubleshooting; MSBuild elapsed time is outside
the compilation measurement.

For payload-specific generator changes, `PayloadGeneratorBenchmarks` adds scalar
dictionaries, model-valued dictionaries, and keyed model collections. Each shape
contains 1, 4, or 16 parent models; model-valued shapes share one child model.
These exploratory cases measure fresh generation and compilation separately.
Their timed cases are outside the 45-case baseline. The baseline runner also
captures all nine shape/count size records in `payload-sizes.json` and includes
them in `baseline.json` and the comparison report. To capture those sizes alone:

```powershell
dotnet run -c Release --project benchmarks/SparseFragments.Benchmarks -- --payload-sizes artifacts/payload-sizes.json
```

The output identifies the shape and parent model count alongside the same byte
counts and source fingerprint used by the baseline size probe.

Workflow setup checks the applied model, preserved remote label, edited elements,
addition, conflict outcome, and unchanged prototype across repeated calls. Model
creation and JSON payload conversion are included in the workflow allocation.
Serializer metadata is warmed by setup. This measures a running application;
serializer startup needs a separate workload.

## Comparable inputs

Equal sequence and no-op Between cases use separately constructed, value-equal
inputs. Explicitly named `SameReference` controls retain the reference shortcut
as its own measurement. The sequence fixture also checks a difference in the
last element. Clone cases perform exactly one clone per operation. Keyed and
dictionary Between cases return the change set directly; patch construction
and hashing are outside those measurements.

Incremental shape benchmarks derive each edit from the original compilation
and alternate between two fixed-size added members. The certification generator
alternates fixed-size comments. Root invalidation benchmarks alternate bounded
property names. None accumulates comments or properties with invocation count.
Cold generation with a reused Compilation remains available in the exploratory
benchmarks; the baseline explicitly uses a new Compilation to avoid measuring
the assembly-scoped analysis cache as cold work.

## Compare successive runs

The runner records the revision, working-tree state, benchmark source digest,
SDK, BenchmarkDotNet version, runtime, OS, CPU and architecture. It compares
runs only when the schema, suite, profile, benchmark digest, host environment
and case set agree. Changes to benchmark inputs or measurement boundaries
require a new baseline. Changes to runtime or generator implementation can be
compared with the same benchmark source.

Schema 2 adds the nine payload generator size records. Missing, duplicate,
unexpected or invalid size records fail collection. Schema 1 runs start a new
baseline; their measurements are not compared with schema 2.

The report includes mean time changes, allocation and GC deltas, source and
assembly size deltas, and whether the 99.9% time intervals overlap. Interval
overlap is descriptive; it is not a significance test. Inspect the raw
measurements and rerun unexpected changes under the same machine load before
attributing them to a code change. Hosted CI machines can change CPU or runtime;
those runs start a new baseline.

The Performance baseline workflow runs on relevant main pushes, weekly, and
manual dispatch. It downloads the previous successful run's artifact, compares
compatible results, publishes the report in the job summary, and retains each
run for 90 days. Download artifacts you need to keep longer. The workflow records
performance changes without a percentage-based pass/fail threshold; benchmark
execution and result completeness are required.

## Existing benchmark collection

The default BenchmarkSwitcher still exposes the full collection for focused
investigations. The baseline selects single operations, workflow and generator
measurement classes explicitly. Specialized comparer, collection density,
serialization and generator-shape matrices remain available through `--filter`.

The [three-layer certification report](three-layer-certification.md) contains
historical numbers. Its old sequence and no-op comparisons used identical
references, and its clone methods performed two clones. Those numbers do not
serve as the baseline for the corrected methods.

## Recorded baseline

The [recorded runs](baselines/README.md) preserve the initial Windows and Linux
schema 1 captures. Start a schema 2 run with the current runner and pass its
`baseline.json` as `--previous` for later captures on a compatible machine and
SDK. Each run's artifact directory retains the raw measurements needed to
inspect variance and GC behavior.
