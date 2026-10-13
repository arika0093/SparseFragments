# SparsePath traversal and lookup budgets

Follow-up to #202, which requires no measurable pathological path
allocation or regression in representative traversal benchmarks.
Issue #222 adds the dedicated path-specific measurement that the
descriptor-access and ChangeSet-enumeration benchmarks did not isolate.
The cases live in
benchmarks/SparseTypes.Benchmarks/SparsePathTraversalBenchmarks.cs
and run through the default BenchmarkSwitcher with a filter. They are
outside the 45-case pinned baseline, which selects its classes
explicitly.

## Run

Full certification numbers need the standard warmed profile on an idle
pinned machine, not the Dry smoke below:

```powershell
dotnet run -c Release --project benchmarks/SparseTypes.Benchmarks -- --filter "*SparsePathTraversalBenchmarks*"
```

Smoke check used for this note (separate profile, not comparable with
full runs):

```powershell
dotnet run -c Release --project benchmarks/SparseTypes.Benchmarks -- --filter "*SparsePathTraversalBenchmarks*" --job dry
```

## Coverage

Construction uses only the public generated typed path API:
root, scalar (1 segment), nested 2-3 segments, keyed and indexed
3 segments, scalar-dict 2 segments, struct-dict 3 segments, deep
keyed 5 segments, escaped-key and composite-key paths. Lookup uses the
generic consumer interfaces: ChangeSet Find with typed, untyped and
parsed paths, descriptor Find through IDescriptorSet, and conflicts
Find. Comparison covers Equals on equal and different deep paths,
GetHashCode, ToString on scalar, deep and escaped paths, and Parse of
the same three shapes. Shape contrasts are sparse 1-property versus
wide 16-property models, keyed 1-change versus 16-change sets, and a
UI-like block of 100 repeated finds with a cached typed path against
re-parsing the path text every time.

## Smoke observation (Dry profile, not a budget)

Pinned smoke environment: Windows 11, Intel Core i7-14700F, .NET SDK
10.0.401, .NET 10.0.12 X64 RyuJIT, BenchmarkDotNet 0.15.8, Dry job
with 1 launch and 1 measured iteration. Dry timings sit on the harness
floor (about 170-340 us for every case, with MinIterationTime warnings)
and carry no Error interval, so they are not ns/op budgets. The
allocation column is the useful signal here:

| Group | Allocated per op (Dry) |
| --- | --- |
| Construct root / 1 / 2 / 3 / 5 segments | 32 / 224 / 456 / 752 / 1536 B |
| Construct escaped-key / composite-key | 456 / 784 B |
| Equals same / different, GetHashCode | 0 B in this profile |
| ToString scalar / deep / escaped | 136 / 496 / 456 B |
| Parse scalar / deep / escaped | 272 / 1064 / 488 B |
| ChangeSet Find sparse hit, typed cached / untyped cached | 368 / 368 B |
| ChangeSet Find sparse hit, cold / parsed baseline | 592 / 640 B |
| ChangeSet Find wide 16-prop hit / miss | 4496 / 4496 B |
| ChangeSet Find keyed 1-change / 16-change | 2768 / 21784 B |
| ChangeSet Find deep / escaped / composite | 3952 / 1808 / 2152 B |
| Descriptor Find nested cached / cold, keyed, struct-dict | 5064 / 5816 / 3848 / 3624 B |
| Conflicts Find hit / miss | 120 / 296 B |
| UI 100 finds, cached / parsed baseline | 36800 / 64000 B |

Supported-equivalent comparison inside the same profile: the typed
Find costs the same cached or untyped (368 B either way, so the typed
wrapper adds nothing measured), while rebuilding the path each lookup
adds exactly the construction cost (368 + 224 = 592 B) and re-parsing
the text each lookup adds exactly the parse cost (368 + 272 = 640 B).
The UI block scales linearly (100 x 368 = 36800 B cached against
100 x 640 = 64000 B parsed). Find allocation grows with the enumerated
change count (sparse 368 B against wide 4496 B; keyed 1-change 2768 B
against 16-change 21784 B) because Find enumerates entries per call.
Descriptor Find allocates per navigation (3.6-5.8 KB here, including a
cold delta of about 750 B for rebuilding the nested path).

## Tradeoffs and next step

No runtime or generator change was made for this note, so there is no
regression to fix and none is claimed. The recorded tradeoff is that
path reuse removes construction and parsing work but Find still
enumerates the change list on every call; indexing ChangeInfo entries
by path would be a separate optimization with its own measurements.
No zero-allocation guarantee is made: every construction, rendering,
parsing and lookup above measured above zero except Equals and
GetHashCode, which measured zero only in this Dry profile. Full warmed
runs on the pinned configuration are required before quoting ns/op or
certifying #202 acceptance.
