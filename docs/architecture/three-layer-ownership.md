# Three-layer code ownership

Scope: where reusable implementation lives across the compiled runtime,
compilation-scoped generated support, and per-model generated code.
This document defines the Runtime, Generated-Once, and Per-Model layers,
the criteria that place a helper in one of them, and the naming and
visibility rules that later refactors build on.

Related work: #176 landed model-facing facades, per-model placement, and
multi-source emission. #178 landed the emission infrastructure that moves
code between layers. #179 decoupled semantic roles from product-specific
generated names. #181 through #188 migrated individual helper families.
This record was written for #177; the criteria, call rules, visibility
rule, and naming scheme below remain the normative contract, while the
ownership table now describes the landed implementation. Where this
document and a migration issue disagreed during the work, the migration
issue recorded the deviation and its reason.

## Layers

Runtime holds assembly-level public contracts and reusable
implementations that consumers reference across assemblies. It ships in
the compiled `SparseFragments` assembly. Examples: `Optional<T>`,
`FragmentOperation<T>`, `MergeMode`, conflict and rebase-result types,
`SparseValueComparer`, `SparseCollectionMerger`,
`SparseCollectionRebase`, `SparseCollectionProvenance`,
`SparseKeyedCollection`, clone and cycle helpers, and the
`CompilerServices.SparseFragmentRuntime` facade.

Generated-Once holds model-independent implementation emitted once per
consumer compilation. It is ordinarily `internal` and uses the product's
runtime contracts through the configured dialects. It exists so generic
algorithms are not repeated per model and so a consumer compilation does
not carry compiled-runtime dependencies for code that is private to it.
Candidate residents: generic clone/collection helpers, payload DTO
machinery that needs no per-model specialization, and any helper moved
out of the runtime facade by #186 or out of per-model output by #178.

Per-Model holds type-safe state, transitions, and behavior specialized
for one analyzed model: typed `Fragment`/`Patch`/`ChangeSet` storage,
`Between`/`FromPatch`/`ToPatch`/`Invert`/`Compose`/`Rebase` cores, typed
transitions, JSON converters, and UI/editing types. It may call
Generated-Once helpers and reference Runtime contracts. It must not be
called by the other two layers.

## Placement criteria

Each criterion is a decision rule. Disputed placements cite the criterion
that settles them, and the test suite pins every criterion marked
testable below.

C1 Assembly-level type identity. When two assemblies must agree on one
CLR type, the type stays in Runtime. `Optional<T>`, operation payloads,
and conflict/result types cross assembly boundaries in public
signatures, so they cannot be compilation-scoped. Testable: a downstream
generator test emits against a foreign runtime namespace and asserts the
wire and contract types resolve there.

C2 Cross-library API contracts. Methods that hand-written code or a
second generated assembly calls directly stay public in Runtime. Methods
that only generated code in the same compilation calls are candidates
for Generated-Once. Testable: the runtime ownership audit (#186) maps
every `SparseFragmentRuntime` member to its call sites; members with no
external callers are relocation candidates.

C3 Parameterizability. A helper that can be expressed over generic type
parameters, delegates, or dialect-supplied type names does not need a
per-model copy. Equality, merge, rebase, and provenance helpers take
comparers and element types as parameters today, which is why they are
shared rather than specialized. Testable: emitter tests construct
`SparseFragmentExpressions` with downstream comparer, merger, and
optional type names and assert the output names only those contracts.

C4 Generated-code size. A helper emitted once per model that is
identical across models except for type arguments belongs in
Generated-Once or Runtime, not Per-Model. The cost signal is the
measured byte count of the duplicated output. Informative: size tables
in the #186 audit report.

C5 Compilation cost. Moving a helper to Generated-Once must not widen
incremental invalidation: per-model edits must not churn shared output,
and shared output must not depend on any one model. The existing
placement scheme derives names from stable model identity for this
reason. Testable: placement tests assert deterministic, collision-safe
containers and stable hint names.

C6 NativeAOT and trimming. Code that relies on runtime reflection or
source-generated `System.Text.Json` metadata keeps whatever visibility
and shape the trimmer and the JSON source generator require, even when
that conflicts with the default visibility rule. The payload DTO
exception below exists for this criterion.

## Call and reference rules

References point inward: Per-Model calls Generated-Once, and both
reference Runtime. Generated-Once never references Per-Model types, and
Runtime never references generated types. Generated code names runtime
types only through configuration: `SparseRuntimeDialect` for optional,
comparer, merger, and rebase helpers, and `SparsePatchDialect` for the
patch facade, conflict/result types, and payload versioning. Shared
emitters contain no `global::SparseFragments` literals; the product
generator supplies its own namespace and type names. The downstream
dialect fixture tests pin this rule by asserting emitted output with a
foreign runtime namespace contains no `global::SparseFragments` token.

Generated-Once algorithms obtain runtime behavior through generic
parameters or explicit supported contracts rather than fixed product
names. A downstream product reuses the analysis and the generic
algorithm while supplying its own primitives under the same contract
shape.

## Visibility rule

Generated-Once implementation types are ordinarily `internal` and must
not escape through public signatures. A public Per-Model API returns
public Per-Model or Runtime types only. Internal helpers stay out of
`public` return types, `protected` members, and interface
implementations so consumer assemblies never observe them.

Exceptions need a recorded justification against the criteria above:

E1 Payload implementation DTOs are public. A source-generated JSON
context in another assembly accesses the registered polymorphic types,
so trimming and source generation (C6) require public visibility. The
container keeps its `EditorBrowsable(Never)` marker and its stable-hash
name so the exception stays narrow.

E2 Any helper that crosses an assembly boundary in a public signature
must be public. Such a helper is evidence of a misplaced boundary: file
it as a candidate to move to Runtime (C1, C2) rather than widening
Generated-Once visibility silently.

## Naming

Compilation-scoped helpers live under one product-chosen namespace. The
standalone default is `SparseFragments.Generated`. The namespace is
configured, not inferred: `SparseGeneratorConfig` carries
`GeneratedImplementationNamespace`, `SparseFragmentsGenerator` sets it
to the default, and Shared provides no implicit fallback. A null or
empty value keeps single-file emission. All placement decisions flow
through `SparseGeneratedPlacement`: the per-model container derives from
the stable fully qualified model identity plus a stable hash, UI type
names dodge source member collisions, and surface/implementation hint
names stay stable and unique. The isolated consumer probe (a downstream
generator with no `SparseFragments` runtime reference) verifies that
Shared never assumes the default namespace.

Multi-generator coexistence falls out of this scheme. Each product sets
its own namespace (for example `Acme.Generated`), so two generators in
one compilation never share helper types. Containers are keyed by model
identity, so colliding short names in different namespaces stay
distinct, and unrelated model edits never churn other roots.

## Ownership and dependency table

Status reflects the landed implementation after the #190–#195 relocation
(all migration issues closed). The migration column is history: it records
which change landed each row, not pending work.

| Helper family | Current location | Target layer | Criteria | Migration |
| --- | --- | --- | --- | --- |
| `Optional<T>`, `FragmentOperation<T>`, `MergeMode`, conflict/result/rebase-option types | Runtime (public contracts) | Runtime | C1, C2 | none; stable seam |
| `SparseValueComparer`, `FragmentComparisonPrimitives` | Runtime | Runtime | C1, C2, C3 | none; confirmed by #186 |
| `SparseCollectionMerger`, `SparseCollectionRebase`, `SparseCollectionProvenance`, `SparseKeyedCollection` | Runtime | Runtime, with Generated-Once candidates per audit | C2, C3, C4 | #186 audited; #181–#183 landed |
| Clone/cycle contexts, keyed primitives behind `SparseFragmentRuntime` | Runtime facade over Runtime implementations | Runtime seam, narrowed; generic bodies are Generated-Once candidates | C2, C3 | #186 landed |
| `SparseFragmentRuntime` itself | Runtime (public facade) | Runtime, narrowed to the required generated-to-runtime contract | C2 | #186 landed |
| Generic clone/collection helpers emitted per model (`__Clone*`, materializers) | Generated-Once (`CloneKernels.g.cs`, read-only adapters, removal index) | Generated-Once | C3, C4 | #178, #176 landed (#190–#195) |
| Payload Core/Root/Change/item DTOs | Generated-Once implementation containers (`__Internal_<hash>`, E1 stays public) | Generated-Once implementation containers (E1 stays public) | C4, C6 | #176 landed (#192) |
| `Fragment`/`Patch`/`ChangeSet` algorithms (Between, Compose, Invert, Rebase, Apply, Enumerate) | Per-Model operations delegating to Generated-Once generics where C3 applies; bodies in `<Container>FragmentOperations`, `<Container>.PatchOperations`, `<Container>.ChangeSetOperations` (internal) | Per-Model operations, delegating to Generated-Once generics where C3 applies | C3, C4 | #176 landed (#193, #194) |
| JSON converters, descriptor factories | Per-Model facades over implementation-namespace bodies | Per-Model, under implementation namespace | C5 | #176 landed (#191, #192) |
| `Observable`, `ReadOnlyView`, `EditSession`, descriptor graphs | Implementation-namespace Per-Model types in `SparseFragments.Generated` | Generated-Once or implementation-namespace Per-Model types per #176 | C2, C5 | #176 landed (#190, #191) |
| `ChangePayload` envelope facade | Per-Model public facade over Generated-Once DTOs | Per-Model public facade over Generated-Once DTOs | C2 | #176 landed (#192) |
| EditSession core helpers | Compilation-scoped generated source | Generated-Once | C4, C5 | #178 landed |

## Stable interfaces for later tracks

Later tracks reference these names exactly as written. Renames go
through a deliberate Shared-surface change with a README update.

- `SparseGeneratorConfig.GeneratedImplementationNamespace` (explicit,
  no fallback) and `SparseGeneratorConfig.RuntimeDialect`,
  `PatchDialect`, `EditSessionDialect`, `DescriptorDialect`,
  `EmissionFeatures`.
- `SparseRuntimeDialect` members: `Namespace`, `OptionalType`,
  `MergeStrategyType`, `ReferenceComparer`, `ValueComparer`,
  `CollectionMerger`, `CollectionRebase`, `MergeStrategyFieldPrefix`,
  `RebasePolicyFieldPrefix`.
- `SparsePatchDialect` members: `RuntimeNamespace`, `RuntimeFacade`,
  `ConflictType`, `ConflictKindType`, `RebaseResult`, `ChildPatchName`,
  `ChildChangeSetName`, `PayloadImplementationContainerPrefix`,
  `ChangePayloadVersion`, rebase option/mode/policy entries, member
  policies, write contract.
- `SparseGeneratedPlacement`: `TryGetImplementationNamespace`,
  `GetImplementationContainer`, `GetImplementationTypeName`,
  `ResolveUiTypeName`, `SurfaceHintName`, `ImplementationHintName`.
- `SparseGenerationResult.AdditionalSources` carries implementation
  files alongside the surface file within per-model incremental
  isolation.
- `SparseEmissionFeatures` and its dependency validation select which
  families a product emits.

## Worked example

A runtime-facing interface with a Generated-Once implementation and a
Per-Model caller, using standalone names:

- Runtime: `global::SparseFragments.Optional<T>` (C1) and
  `CompilerServices.SparseFragmentRuntime.AreEqual` (C2) stay public in
  the compiled assembly.
- Generated-Once: `SparseFragments.Generated.<Model>_<hash>` holds an
  internal generic equality helper parameterized by a delegate, emitted
  once per compilation and callable from every model in it.
- Per-Model: `Order.Fragment.__SparseAreEqual` calls the Generated-Once
  helper with a model-specific element comparer, then exposes the typed
  result on the public `Fragment` API.

Public cross-assembly contracts (`Optional<T>`, operation and conflict
types) appear in signatures. Assembly-local helpers (the generic
implementation, clone contexts, member field names) never do, except
under E1.
