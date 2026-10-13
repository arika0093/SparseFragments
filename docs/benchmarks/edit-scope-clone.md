# Edit scope open cost

Scope: cost of opening path-addressed child drafts (`EditSession.OpenScope`),
which forks the session. Issue: #239. Benchmarks:
`benchmarks/SparseTypes.Benchmarks/EditScopeCloneBenchmarks.cs`.
Validation tests:
`tests/SparseTypes.Tests/Patch/EditSessionScopeTests.cs`.

All timing numbers below are machine-dependent and are not asserted by
tests. Allocation behavior for invalid inputs is pinned by
`InvalidOpenScopeDoesNotCloneTheRoot`.

## Reproduce

```powershell
dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release -- --filter "*EditScopeCloneBenchmarks.OpenSmallChildScope*" --job short
dotnet run --project benchmarks/SparseTypes.Benchmarks/SparseTypes.Benchmarks.csproj --configuration Release -- --filter "*EditScopeClone*" --job Dry
```

Environment for the numbers below: Windows 11, Intel Core i7-14700F,
.NET SDK 10.0.401, .NET 10.0.12 X64 RyuJIT.

## Opening a small child scales with the root

`OpenSmallChildScope` opens a draft of a 4-item child group while the root
carries 0, 1024, or 8192 sibling items (`--job short`, 3 iterations):

| Siblings | Mean | Allocated |
| --- | --- | --- |
| 0 | 2.43 us | 7.55 KB |
| 1024 | 168.84 us | 247.67 KB |
| 8192 | 2630.56 us | 4132.04 KB |

Cost per sibling is about 250 bytes and 0.3 us. The edited subtree stays
the same size in every row, so the growth comes from the fork: one fragment
snapshot plus one deep clone of the whole live root.

## Nested and simultaneous scopes multiply the fork

Indicative single-sample figures (`--job Dry`, N = 1) for the rest of the
matrix:

| Benchmark | Siblings | Mean | Allocated |
| --- | --- | --- | --- |
| OpenSiblingScopeAndCancel | 8192 | 8.34 ms | 6816 KB |
| OpenNestedScope | 1024 | 5.45 ms | 421 KB |
| OpenNestedScope | 8192 | 7.42 ms | 6636 KB |
| OpenManyScopes, 1 scope | 8192 | 5.16 ms | 4143 KB |
| OpenManyScopes, 8 scopes | 1024 | 7.01 ms | 2867 KB |
| OpenManyScopes, 8 scopes | 8192 | 19.81 ms | 23066 KB |

A nested scope forks twice (once per level), so it pays about twice the
single-scope cost. Simultaneous scopes each hold their own fork, so eight
scopes hold about eight root clones. The mid-list sibling case also pays a
linear key scan on top of the clone.

## Invalid inputs no longer pay for a clone

Before this change, root `OpenScope` forked first and validated second, so
an invalid path paid a full root clone before throwing (and a null path
threw `NullReferenceException`). Validation now runs before `Fork`, and the
fork-side resolve stays as a backstop. A unit probe on a 5000-item model
(`InvalidOpenScopeDoesNotCloneTheRoot`) measures the difference:

| Input | Cost after the change |
| --- | --- |
| `OpenScope(null)` | 304 bytes (exception only) |
| Missing key | key-scan resolution only, below a successful open |
| Successful open, same model | about 732 KB (one full clone) |

The missing-key figure is not zero because keyed misses scan the collection
linearly, which is pre-existing resolution behavior, not a clone. Failed
opens also register no draft: the parent reports no changes and no open
drafts.

## Why the root clone stays

Removing the clone for valid opens would require subtree-only drafts with
their own commit path. Commit (`TryCommit`) is defined on whole-root fork
changesets: it diffs the fork, rebases onto the parent model, applies to a
candidate parent fork, and writes the updated child back into the existing
target instance. That path carries isolation, lineage checks, conflict
handling, no-op commits, nested invalidation, and keyed/temporary-key
correlation, all pinned by the scope and regression tests. A parallel
subtree algebra would duplicate those semantics for the most
correctness-sensitive operation in the session, which trades a linear and
predictable open cost for ongoing semantic risk. The validation reorder
above removes the only case that paid the clone for nothing (invalid
input). Revisit only with a design that reuses the existing changeset and
rebase semantics rather than adding a second one.
