# Generator architecture

Audience: contributors to the generator and maintainers of downstream
generators built on `SparseFragments.Generator.Shared`. End users of the
library start at the repository [README](../../README.md) instead; nothing
here is needed to use the generated APIs.

## Current implementation

The generator is organized in three layers:

* `Runtime` ships in the compiled `SparseFragments` assembly: public
  contracts such as `Optional<T>`, `FragmentOperation<T>`, `MergeMode`,
  conflict and rebase-result types, plus the generic comparison, merge,
  rebase, provenance, keyed-collection, and clone helpers behind the
  `CompilerServices.SparseFragmentRuntime` facade.
* `Generated-Once` is model-independent implementation emitted once per
  consumer compilation, ordinarily `internal`: collection clone kernels,
  read-only adapters, removal-index helpers, patch kernels, and the
  edit-session cores. It names runtime behavior only through the
  configured dialects.
* `Per-Model` is type-safe state and behavior for one analyzed model:
  typed `Fragment`/`Patch`/`ChangeSet` storage with its `Between`,
  `FromPatch`, `ToPatch`, `Invert`, `Compose`, and `Rebase` cores, typed
  transitions, JSON converters, and the UI/editing types.

The model-facing surface after the relocation work (#190–#195, all
landed) is: `Fragment`, `FragmentBuilder`, `Patch`, `ChangeSet`, and the
`ChangePayload` facade stay nested in the annotated model, with
`CreateEditSession()`, `ToObservable()`, and `CreateChangeSet()` as the
entry points. Everything else generated per model lives in a stable
per-model container in `SparseFragments.Generated` (see
[Relocated generated types](../ui-frameworks.md#relocated-generated-types)).
There are no backwards-compatibility aliases. The surface is pinned by
`ModelFacingSurfaceTests` and `FragmentOperationsSplitTests`.

Shared emitters stay product-neutral: anything product-specific arrives
through `SparseGeneratorConfig` (including `FamilyNames`,
`GeneratedImplementationNamespace`, and `EmissionFeatures`) and the
`SparseRuntimeDialect` / `SparsePatchDialect` (/ session, descriptor,
and edit-session dialects). There is no implicit SparseFragments
fallback. The full contract lives in
[`src/SparseFragments.Generator.Shared/README.md`](../../src/SparseFragments.Generator.Shared/README.md),
which is the reference for downstream consumers.

## Documents and their status

| Document | Role | Status |
| --- | --- | --- |
| [Three-layer ownership](three-layer-ownership.md) | Normative layer contract: placement criteria C1–C6, call and reference rules, visibility, naming | Current. The ownership table describes the landed implementation; its migration column is history. |
| [Semantic roles](semantic-roles.md) | Role model separating generated-code meaning from product names, plus the binding mechanism | Current. The standalone vocabulary is this product's shipped binding; downstream products rebind through `FamilyNames`. |
| [Runtime ownership audit](runtime-ownership-audit.md) | Method-level inventory of the generated-to-runtime seam | Fixed record written for #186. Sizes and line counts are historical measurements, not current benchmarks. |

## Migration ledger

The layering design (#177), emission roles (#179), and helper audits
and migrations (#176–#188) are closed. The relocation follow-ups are
closed and landed as well: per-model `Observable`, `ReadOnlyView`, and
`EditSession` types (#190), `DescriptorFactory` relocation (#191),
payload DTOs and Fragment JSON converter bodies (#192), Fragment
operations and clone machinery (#193), Patch and ChangeSet algorithms
behind thin facades (#194), and the finalized model-facing surface with
consumer migration (#195). Statements in the older records that present
these as pending targets describe the plan as of their base commits, not
the current code.
