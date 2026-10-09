# Runtime ownership audit

Scope: method-level ownership of the generated-to-runtime seam. This
document inventories every `SparseFragmentRuntime` member, maps its
Shared emitter call sites, reviews the implementation families behind
the facade, and records why each method stays compiled, becomes
compilation-scoped support, or belongs to a Per-Model specialization.
It is the layer-wide companion to the three-layer design
(`three-layer-ownership.md`); the read-only, clone, and removal helper
migrations live in #181-#183.

## Seam status

Type-level coupling is already minimal. Shared emitters name runtime
types only through `SparseRuntimeDialect` (`Namespace`, `OptionalType`,
`MergeStrategyType`, `ReferenceComparer`, `ValueComparer`,
`CollectionMerger`, `CollectionRebase`) and `SparsePatchDialect`
(`RuntimeNamespace`, `RuntimeFacade`, conflict/result types). Shared
sources contain no `global::SparseFragments` literal. The downstream
dialect fixtures prove emitted patch, change-set, keyed, dictionary,
rebase, payload, equality, merge, clone-context, and UI-adjacent output
with a foreign runtime namespace names only that namespace.

The remaining seam is member names on dialect-owned types. A downstream
runtime implements the same member names under its own type names. The
stable member contract is listed per family below; renaming those
members is follow-up work for #181-#183, not a silent emitter change.

## Facade inventory

All 25 public `SparseFragmentRuntime` members are preserved with their
declared signatures. The public API approval file pins the surface, and
hand-written callers (benchmarks on rebase paths, NativeAOT smoke tests
on equality paths) exercise the facade directly, so removal is not
available in this track. Narrowing happens by relocating generic bodies
to Generated-Once behind the same facade names, owned by #178 with the
per-method targets below.

Equality (behind `SparseValueComparer`, `FragmentComparisonPrimitives`):

| Member | Generated call sites | Decision |
| --- | --- | --- |
| `AreEqual(object?, object?)`, `AreEqual<T>` | Between/compose/rebase/match emitters via `RuntimeFacade` | Runtime: cross-assembly identity semantics (C1, C2) |
| `AreSequenceEqual` (both overloads), `AreSetEqual`, `AreDictionaryEqual` (both overloads) | Fragment equality and dictionary algebra via `ValueComparer` | Runtime: shared semantics parameterized by delegates (C3) |

Merge (behind `SparseCollectionMerger`):

| Member | Generated call sites | Decision |
| --- | --- | --- |
| `MergeAppendList`, `MergeDistinctArray`, `MergeDistinctList`, `MergeSet` | Collection merge expressions via `CollectionMerger` | Runtime: generic over element type and comparer (C3); relocation candidate only if #178 finds a compilation-local caller set with no external users (benchmarks use the implementations today) |

Rebase (behind `SparseCollectionRebase`):

| Member | Generated call sites | Decision |
| --- | --- | --- |
| `TryRebaseAppend`, `TryRebaseSetUnion` (boxed) and the five generic `TryRebase*` overloads | Member/patch/collection rebase emitters via `RuntimeFacade` and `CollectionRebase` | Runtime: generic algorithms over comparers (C3); benchmarks call the facade directly, so the names stay public regardless of where bodies live |

Clone and cycle identity:

| Member | Generated call sites | Decision |
| --- | --- | --- |
| `CreateCloneContext` | Clone/conversion emitters via `ReferenceComparer` | Runtime contract; body is a one-line factory, no duplication to remove |
| `CreateFromCycleContext`, `CreateDiffCycleContext` | Conversion/merge emitters via `ReferenceComparer` | Runtime contract for the same reason |

Keyed primitives (behind `SparseKeyedCollection`):

| Member | Generated call sites | Decision |
| --- | --- | --- |
| `EnsureUniqueKeys`, `KeyOrderEquals` | Keyed-sequence and patch-sync emitters via `RuntimeFacade` | Runtime: key semantics shared across models (C2, C3) |

Provenance (behind `SparseCollectionProvenance`):

| Member | Generated call sites | Decision |
| --- | --- | --- |
| `TryExplainCollectionProvenance`, `TryExplainSetProvenance` | Merge/provenance paths | Runtime: generic over contribution lists (C3); largest relocation candidate by size if #178 scopes it compilation-local |

## Family review

`SparseCollectionMerger` (83 lines) and `SparseKeyedCollection`
(63 lines) are small, fully generic, and called from both generated and
hand-written code: they stay compiled. `SparseValueComparer`
(507 lines) and `FragmentComparisonPrimitives` (1090 lines) carry the
cross-assembly equality semantics: they stay compiled, with any
model-specific fast paths remaining Per-Model specializations.
`SparseCollectionRebase` (696 lines) and `SparseCollectionProvenance`
(718 lines) are the large generic bodies where Generated-Once
relocation pays if their caller set proves compilation-local; until
#178 moves them, the facade names above are the stable seam and no
algorithm is duplicated in generated output. `MixedChangeAlgebra`
(883 lines) and the observable collections are product runtime behavior
with hand-written callers: out of scope for relocation. Payload DTO
support, clone helpers, and UI/editing types follow #176 placement,
not this audit.

## Cost report

Measured implementation sizes at the base commit (bytes of source):

| Family | Lines | Bytes |
| --- | --- | --- |
| Equality (`SparseValueComparer`, `FragmentComparisonPrimitives`, pair/reference comparers) | 1647 | 51k |
| Rebase (`SparseCollectionRebase`, result, mode, options) | 967 | 33k |
| Provenance (`SparseCollectionProvenance`) | 718 | 26k |
| Merge (`SparseCollectionMerger`) | 83 | 3k |
| Keyed (`SparseKeyedCollection`) | 63 | 2k |
| Facade (`SparseFragmentRuntime`) | 232 | 9k |
| Contracts (`Optional`, `FragmentOperation`, `MergeMode`, payload endpoint) | 298 | 12k |
| Mixed algebra | 947 | 33k |
| Observable collections and descriptors | 1906 | 83k |

The facade itself is 9k of forwarding methods with no algorithm
content: narrowing it means relocating bodies, not deleting names.
Moving the rebase and provenance bodies (59k combined) to
Generated-Once would trade one compiled copy for one copy per consumer
compilation while removing the compiled-runtime dependency for
compilations that use only those paths; the per-model output does not
duplicate these algorithms today, so the saving is dependency scope,
not output size. Compilation-cost impact of relocation is bounded by
the existing placement scheme: shared output keys on stable identity,
and per-model edits must not churn it (#177, C5). AOT impact is
neutral: the algorithms use no runtime reflection, and the payload DTO
exception (E1) is unaffected by facade changes.

## Fixtures and gates

- `RuntimeOwnershipTests` pins the 25-method facade surface by name so
  #178 and #181-#183 change it deliberately, and proves clone-context,
  keyed-primitive, equality, and merge emission flows through injected
  dialect contracts with no product literal.
- `ThreeLayerOwnershipTests` pins the dialect-only comparer/merger
  contracts and the explicit payload prefix rule.
- `ChangeSetDialectFixtureTests` and `SemanticReferenceTests` prove the
  foreign-runtime and custom-vocabulary output properties.
- Representative feature tests remain green: the full unit suite passes
  with this audit's changes, and the solution builds with zero warnings
  and zero errors.
