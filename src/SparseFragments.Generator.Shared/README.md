# SparseFragments.Generator.Shared

Source-only generator infrastructure shared by `SparseFragments.Generator` and downstream generators.

This package ships C# sources only — there is no runtime assembly and no `lib/` assets.
Referencing it compiles the shared sources into the consuming generator via
`build/SparseFragments.Generator.Shared.props` (direct consumer only; no
`buildTransitive/` propagation):

```xml
<PackageReference
  Include="SparseFragments.Generator.Shared"
  Version="0.1.0"
  PrivateAssets="all" />
```

Pin an explicit compatible version. Shared source changes alter the compiled
generator binary, so a floating version range is not recommended for
downstream generators.

`PrivateAssets="all"` is required: the sources are compiled into the
generator itself and must not flow transitively to generator consumers.

Consuming generators additionally need the same compile surface the
sources were written against:

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <LangVersion>preview</LangVersion>
  <PolyUseEmbeddedAttribute>true</PolyUseEmbeddedAttribute>
  <PolyArgumentExceptions>true</PolyArgumentExceptions>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.1" PrivateAssets="all" />
  <PackageReference Include="Polyfill" Version="11.4.1" PrivateAssets="all" />
</ItemGroup>
```

## Downstream generator configuration

The shared analysis and source emitter accept a product-owned
`SparseGeneratorConfig`. Keep the downstream attribute and interface metadata
names, merge enum values, reserved generated names, diagnostic IDs, and
hint/structural-host suffixes in that configuration. This includes the
redaction attribute metadata name: members carrying the downstream redaction
attribute emit a redacted before-state from `ToPayload()`. Merge enum values are
normalized to shared semantic modes before validation and emission; the
downstream enum does not need to reuse `SparseFragments.MergeMode`.
`SparseGeneratorConfig.FamilyNames` optionally rebinds the generated
state, operation, transition, payload, and view names
(`SparseFamilyNames`, defaulting to the standalone
`Fragment`/`Patch`/`ChangeSet` vocabulary). Child and nested references
resolve through `SparseSemanticReference` from those bindings; see
`docs/architecture/semantic-roles.md` for the role model and its limits,
and `docs/architecture/README.md` for the contributor-oriented
architecture overview (current layer placement, document status, and the
migration ledger).

`SparseRuntimeDialect` maps generated optional, comparer, collection, and merge
helpers to downstream-owned runtime types. `SparsePatchDialect` maps patch
runtime/facade, conflict/result types, nested member names, member
field/value types, and the provisional ChangePayload version token
(`ChangePayloadVersion`, default `"0.1"`). `ReservedGeneratedNames` is caller-owned and defaults to an
empty set; list only names the generator injects into the annotated model
itself (nested state/operation families and their member/method names).
Names that exist only in an external implementation container
(`Observable`, `ReadOnlyView`, `EditSession`, descriptor factories,
operation helpers, payload DTOs, converter bodies) must not be reserved on
the model: member reuse is legal there, and collisions resolve through the
central `SparseGeneratedPlacement` resolver. Pass the same config
through analysis and source emission; this lets a consuming generator emit
against runtime types it owns instead of adding a SparseFragments runtime
dependency. Full source emission requires both dialects, and the Patch/STJ
emitters require an explicit patch dialect; Shared provides no implicit
SparseFragments runtime fallback. The product generator owns and supplies its
runtime defaults.

Each model emits a unified `ChangePayload` envelope carrying baseline-aware
transitions and baseline-free commands side by side, with `missing`, `null`,
`value`, and `redacted` endpoint states. `ToChangeSet()` accepts only complete
histories; `ToPatch()` projects any envelope without a baseline, and
`FromPatch()` builds a command envelope without one.

Payload implementation DTOs are grouped under a nested container whose prefix
comes from `SparsePatchDialect.PayloadImplementationContainerPrefix`. Shared
appends a stable model hash to that prefix. The container and DTOs are marked
`EditorBrowsable(Never)`. Their names include a stable model hash because
`System.Text.Json` source generation can collide on nested types with identical
simple names across models. The DTOs remain public so a source-generated context
in another assembly can access the registered polymorphic types.

`SparseFragmentEmitter` retains the standalone `Fragment`/`Patch` API vocabulary.
It does not add product-specific model extension APIs unless the caller supplies
the optional `appendProductExtensions` callback. Downstream generators with a
different public vocabulary should not expose the standalone API shape; they
should reuse the shared analysis and focused emitters to write their own surface
and inject only product-owned extensions.

## Rebase policy configuration

Member-level rebase policies travel through the same configuration layer.
`SparseGeneratorConfig` accepts optional `RebasePolicyAttributeMetadataName`
and `RebasePolicyBaseMetadataName` values; when both are set, analysis reads
the named attribute, validates the policy type against the named
`FragmentRebasePolicy<T>` base (see `SPF027`), and emits per-member policy
dispatch ahead of the merge strategy's `TryRebase`. Leave both null to disable
the feature. `SparseRuntimeDialect` accepts an optional
`RebasePolicyFieldPrefix` for the generated policy fields.

`SparsePatchDialect` accepts optional `RebaseOptionsType`, `RebaseModeType`,
`RebasePolicyType`, and `RebasePolicyField` values. Each null entry derives
from the dialect's own runtime namespace (`ChangePayloadRebaseOptions`,
`SparseRebaseMode`, `FragmentRebasePolicy`), so generated rebase signatures
always name caller-owned types. The optional `PathType` entry names the
caller-owned canonical path type used by `ChangeInfo`, `EnumerateChangedPaths`,
rebase conflicts, and the generated fluent builders (defaulting to the runtime
namespace plus `SparsePath`). A downstream runtime that enables the
generated options overloads provides those types itself: an options record
with `RejectChangesWithRedactedBeforeValuesDuringRebase`,
`RedactedBeforePaths`, `DefaultRebaseMode`, `IsRedactedBefore`, and `Nest`;
a `SparseRebaseMode` enum (`Default`, `FailOnConflict`, `PreferIncoming`,
`PreferCurrent`); a `FragmentRebasePolicy<T>` base with presence-aware
`TryRebase`/`AreEqual`; and a `RedactedBefore` member on its conflict-kind
enum. Generated code calls only those members.

Path-based conflict resolution needs no new dialect entries. Every model with a
`ChangeSet` gains `BeginResolution` factories plus a nested `Resolution` state
backed by the runtime `SparseRebaseResolution<TChange>` base. The generated
fragment applier writes chosen values through `FragmentBuilder` copies and
cloned collection containers, so shared rebase inputs are never mutated. Keyed
and dictionary element leaves report `[Member, key, ...]` conflict paths, which
keeps per-element decisions unambiguous; keyed order conflicts share the member
path and resolve through the desired key list.

## Emission features

`SparseEmissionFeatures` selects which generated families appear for a model:
`Fragment`, `Patch`, `ChangeSet`, `ChangePayload`, `Observable`, and JSON
converters. Set it on `SparseGeneratorConfig.EmissionFeatures`. The standalone
default emits every family; downstream products opt out explicitly so unused
public APIs are not generated. Selections are validated before emission:
a patch requires its fragment, a change set requires its patch, and a payload
requires its change set. `GetEmittedTypeNames` lists the family root names used
for collision checks.

When a model emits a `ChangeSet`, it also exposes `EnumerateChanges()`, which
flattens nested, keyed, and dictionary transitions into `ChangeInfo` entries
carrying a canonical path. `Before` and `After` use the configured runtime's `Optional<object?>`
to preserve missing versus present-null values; `ChangeKind.Order` carries
keyed collection order as a collection-level entry. Consumers decide how to
format or display these entries.

Paths use the caller-owned path type from `SparsePatchDialect.PathType`
(defaulting to the dialect runtime namespace plus `SparsePath`). `ChangeInfo`
exposes the path in that type plus a wire-compatible text rendering; the
generated model also gains a thin `T.SparsePath` entrypoint with a fluent
`SparsePaths<TRoot>` root builder and per-member collection builders, as well
as exact-match `Find` overloads (untyped and compile-time typed) on the
`ChangeSet`. Rebase conflicts, descriptor paths, and session
`EnumerateChangedPaths()` use the same path type. Downstream runtimes provide
their own path type with member/key/index segments, equality, and text
rendering; Shared never names a `SparseFragments` runtime type.

## Edit-session configuration

`SparseEditSessionDialect` configures the namespace and hint names for the
generated edit-session helper types. The helper source uses the optional type
from `SparseRuntimeDialect` and the conflict and rebase-result types from
`SparsePatchDialect`, so Shared does not select product runtime types. A
consumer that emits edit sessions sets `EditSessionInterfaceMetadataName` and
`EditSessionDialect`, calls `SparseEditSessionEmitter.EmitCore` once when a
compilation contains session models, and calls
`SparseEditSessionEmitter.AppendModelEditSession` from its product-extension
callback. The product generator chooses its helper namespace; for example,
`SparseFragments.Generator` configures `SparseFragments.Generated`.
Setting the optional `EditSessionModelAccessorInterfaceMetadataName` additionally
implements a trusted model-accessor interface on each generated session, so
framework integrations can read the live model without invalidating the
session's observable-change cache. Consumers without that interface keep the
previous behavior: framework helpers fall back to the raw model.
`SparseEditSessionCapabilities` represents the reusable cores as explicit
Generated-Once capabilities with validated prerequisites
(`EditSessionDialect`, runtime/patch dialects, Fragment/Patch/ChangeSet/
Observable families). `SparseEditSessionRoles` names the generic role binding
(state, snapshot, command, transition, observable/current views) and
`SparseEditSessionAdapterContract` documents the per-model delegate binding
(FromModel, Between, ToPatch, advance baseline, projections, apply, invert,
rebase, notifications). Until the capability aggregation pipeline lands, call
`EmitCore`/`EmitCapability` once per compilation with the de-duplicated flags.

## Generated implementation placement

`SparseGeneratedCapabilityPlanner` derives one compilation-scoped plan from
the product feature selection, the analyzed model shapes (explicit and
promoted), the applicable collection families, and the target-framework
facts. `SparseGeneratedOnceEmitter` renders only the requested helper
families — collection clone kernels, read-only adapters, and removal-index
helpers — once per compilation as normally `internal` types. These helpers
are BCL-only and take no dialect parameters (unlike the edit-session core,
which consumes the runtime, patch, and session dialects). Hint names and
type identities derive from the owning generator namespaces, so coexisting
products stay distinct. Missing feature prerequisites report the configured
`InvalidEmissionPlan` diagnostic. Per-model output stays on the separate
`SparsePerModelEmitter` path: with an explicit implementation namespace it
calls the shared helpers through their qualified names, while the
null-namespace single-file path keeps legacy per-model private copies, so
shared copies on that path are emitted-but-uncalled.
TODO(generator maintainers, #178): finish the call-site rewiring — unify
the renderer implementations behind `SparseGeneratedOnceNames` and remove
the legacy per-model copies — then re-tighten this section.

`SparseGeneratorConfig.GeneratedImplementationNamespace` declares where
per-model generated implementations live. All placement decisions flow through
`SparseGeneratedPlacement`: the per-model container derives from the stable
fully qualified model identity (never filesystem paths or emission ordering),
UI type names dodge source member collisions, and surface/implementation hint
names stay stable and unique. The namespace is explicit with no Shared fallback;
null keeps the single-file emission. `SparseGenerationResult.AdditionalSources`
carries the implementation files with the surface file so per-model incremental
isolation covers both.

With an explicit namespace, per-model `Observable`, `ReadOnlyView`, and
`EditSession` types live in that namespace inside the model container. They are
emitted as separate additional sources (`.Observable.g.cs`, `.ReadOnlyView.g.cs`,
`.EditSession.g.cs`). The annotated model keeps no nested copy and no
compatibility alias. Child references resolve to the child container. Private
member attributes are read through an internal model bridge so the relocated
code never names a private type.

With an explicit namespace, each model also emits a payload/operations
implementation source, `<sanitized>_<hash>.Implementation.g.cs`, in that
namespace. It carries the typed `ChangePayload` DTO container
(`__Internal_<hash>` with its `Core`/`Root`/`Change`/item variants) and the
`FragmentJsonConverter` bodies. The surface keeps the public `ChangePayload`
facade and a thin private converter shell that derives from the implementation
converter, plus a file alias resolving the container name. Cross-model payload
references use the qualified `global::<ns>.__Internal_<hash>` form. The
`"0.1"` wire format, redacted/missing/null/value distinctions, validation, and
`JsonIgnore` rules are unchanged.

The same implementation file carries the per-model `Fragment` operations
class (`<container>FragmentOperations`, `internal static`). It owns the
model-specific conversion (`From`, `ToModel`, projection), merge, diff, and
deep-clone bodies plus POCO clone helpers; the model keeps one-line facades
on `Fragment`/`FragmentBuilder` and the root projection bridge. Reference
cycles, comparers, merge modes, and presence semantics are unchanged, and
collection kernels stay compilation-scoped under issue #182.
per-model generated implementations live once facade/implementation separation
lands. All placement decisions flow through `SparseGeneratedPlacement`: the
per-model container derives from the stable fully qualified model identity
(never filesystem paths or emission ordering), UI type names dodge source
member collisions, and surface/implementation hint names stay stable and
unique. The namespace is explicit with no Shared fallback; null keeps the
current single-file emission. `SparseGeneratorConfig.GeneratedImplementationSuffix`
overrides the implementation hint-name suffix (default
`".Implementation.g.cs"`). `SparseGenerationResult.AdditionalSources`
carries the implementation files with the surface file so per-model
incremental isolation covers both.

With an explicit implementation namespace, model-specific Patch algorithms
(apply, compose, invert, rebase and nested/keyed/dictionary operations) and
ChangeSet algorithms (Between, Compose, Invert, Rebase, Apply, enumeration,
payload projection and restoration) stream into the per-model
`PatchOperations`/`ChangeSetOperations` containers. The model keeps thin
typed facades: Patch retains its fields, typed `ref` member access, lazy
nested identity, constructors and whole operations; ChangeSet retains its
canonical sparse storage, constructor and typed transition API. Operation
bodies take the facade as an explicit parameter and reach state through
`internal` bridges, so `ref` identity, lazy aliasing, canonical sparsity
and ownership behave as in single-file emission. Bare `Patch`/`ChangeSet`/
`Fragment` references inside moved bodies resolve through file-level `using`
aliases, so emitters carry no product-specific qualification. A null
namespace keeps the legacy single-file emission for downstream dialects.

Compilation-scoped helpers live in the same namespace with stable hint names
(`ReadOnlyAdapters.g.cs`, `CloneKernels.g.cs`, `RemovalIndex.g.cs`).
`SparseGeneratedOnceNames` pins those identities; per-model output with an
explicit namespace instantiates them instead of redefining the generic
code. The product generator aggregates capabilities across explicit and
promoted models through the capability-driven plane and emits each family
once (single wiring; the interim manual aggregation was removed at merge
time). The clone family keeps the track-3 identity (`SparseCloneKernels`,
`<ns>.CloneKernels.g.cs`); track-2's `SparseCloneHelpers`/`CloneHelpers.g.cs`
was renamed to match.

## Descriptor configuration

`SparseDescriptorDialect` names the caller-owned descriptor contracts and their
generated implementations: `IDescriptor`/`IDescriptorSet` plus the
`IArrayDescriptor`, `IDictDescriptor`, and `ISetDescriptor` shapes with their
`SparseArrayDescriptorAccess`, `SparseDictionaryDescriptorAccess`, and
`SparseSetDescriptorAccess` bags, the `SparseDescriptorShape` static metadata,
and the `SparseDescriptorValue` conversion helper. The optional `PathType`
entry names the caller-owned descriptor path type (defaulting to the runtime
namespace plus `SparsePath`); descriptor sets expose exact-match `Find` over
that type. Set members (`HashSet<T>`,
`ISet<T>`, `IReadOnlySet<T>`) expose membership over live model values with no
positional semantics; the `IReadOnlySet<T>` reference is only named for members
declared with that type, so compilations without the type keep compiling. All
dialect entries are required: Shared provides no implicit runtime fallback.
`SparseDescriptorCapabilities` represents descriptor helpers as explicit
Generated-Once capabilities (static metadata, instance bridges, change
projection, collection/value helpers) with validated prerequisites
(descriptor dialect plus the Observable family). Static per-model metadata is
cached once per model and shared across accesses; generic implementations
stay shared once per compilation where contract identity permits (today via
the shared runtime). Per-model live bindings live in a `DescriptorFactory`
(`.DescriptorFactory.g.cs`) inside the model container. The relocated
`Observable` keeps a thin internal bridge delegating to its factory, so child
`current.__SparseGet_X(path)` call sites keep working without naming private
state.

## Member transport and rebase policies

`SparseMemberPolicy` assigns a transport to one member by name:
`Full` (default), `RedactedBefore`, or `WriteOnly`. Configure policies on
`SparsePatchDialect.MemberPolicies`. Redaction is a transport policy, not a
missing state: in-memory change sets stay complete and baseline-aware, while
the payload omits the undisclosed before-state and keeps the required
after-state. Only scalar members accept non-full transports. A redacted payload
cannot convert to a complete `ChangeSet`; the generated `ToPatchCore()` and
`ToPatch()` projection is the explicit baseline-discarding alternative.
Whole-root transitions and payloads are refused while any member transport
policy applies, so undisclosed before-state cannot leak through a whole
snapshot from a nested model.

`SparseRebasePolicy` selects how redacted-before operations project:
`Passthrough` (default) applies the requested after-state without historical
comparison, as for an explicit patch set; `StrictFail` refuses instead. The
wire version token stays `"0.1"`. Write-only members additionally stay out of
the fragment read projection.

## Write contracts

`SparseWriteContract` targets a write command separately from the read
projection. Set `SparsePatchDialect.WriteContract` with the fully qualified
write-command type and optional read-to-write member mappings; unmapped members
keep their name. Shared emits a `WriteTo` overload for that type which projects
sparse state into an existing instance. It never assumes the read and write
shapes are the same CLR model, and construction plus domain mapping stay
downstream. Without a contract no write overload is emitted.

## Product surface and name validation

Declared product type names go in
`SparseGeneratorConfig.ProductExtensionNames`. Analysis reports
`InvalidEmissionPlan` for incoherent feature selections and non-scalar
transport policies, `UnknownProductMember` for policy or mapping names that
match no analyzed member, and `GeneratedNameCollision` for product names that
shadow reserved or emitted family names. Product interfaces, methods,
metadata, hint names, and diagnostics travel through the existing facilities:
the `appendProductExtensions` callback plus the config-owned attribute,
diagnostic ID, hint, and structural-host names. No generic plugin mechanism or
free-form body replacement is provided. Plan failures report `SPF028`
(`InvalidEmissionPlan`) and unknown product members report `SPF029`
(`UnknownProductMember`).

`SparseFragments.Generator` itself keeps compiling the sibling Shared sources
directly in-repo via a `Compile` glob in
`src/SparseFragments.Generator/SparseFragments.Generator.csproj`. That in-repo
path remains canonical inside this repository; this package exists for
external/downstream generators (for example Configlue, which migrates from a
submodule-based Shared source transport to this package).

## Patch operation kernels

`SparsePatchKernelCapabilities` aggregates model-independent Patch/ChangeSet
kernels (presence composition, empty identities, keyed membership, order
transitions, canonical paths) as explicit Generated-Once families with one
stable hint name per compilation. `SparsePatchKernelInventory` records the
extracted subset versus model-specific specializations (typed nested
operations, sparse canonical storage, unassigned-key rules, ownership and
custom policies). `SparsePatchKernelEmitter.RenderHelperSource` emits BCL-only
generic helpers so downstream products reuse the semantics without a
SparseFragments runtime dependency; model emitters invoke them while keeping
typed transitions.

## Mixed redacted operations

Payload endpoints use the `ChangePayloadState.Redacted` token
(`"redacted"`) for before-states the sender could not disclose. A redacted
before-state paired with a concrete after-state is an explicit write-only
operation: generated payload readers route it to a baseline-free blind patch
instead of a baseline-aware transition. `FromPayload` and `ToChangeSet` fail
with a typed error naming the redacted paths, while `ToPatch()`,
`InvertReversibleChanges()`, and `TryApplyMixedTo()` handle the mixed
request. After-states must stay concrete; a redacted after-state is malformed.
The wire version stays `"0.1"`. No configuration is needed: the mixed surface
uses only sibling generated names, the configured runtime namespace, and the
configured conflict types.

## Edit-session recovery, observable access, and batching

Generated edit sessions expose `TryRevertChanges(out conflicts)`: `true`
after restoring the retained baseline in place with the live model instance
kept stable, `false` with structured conflicts when the revert cannot be
applied in place (immutable members or before-state conflicts), leaving the
model and the baseline untouched. `RevertChanges()` delegates to it and
throws an immutable-aware message on failure. When the live model is
temporarily invalid (duplicate keyed keys or unassigned keyed sentinels) it
cannot be diffed, so the revert restores the retained baseline directly
through the `BaselineToModel` configuration delegate (emitted as
`Fragment.ToModel()`); sessions built without that delegate report an
explicit recovery failure. `Reload` likewise refuses server states that move
init-only or getter-only members, returning structured conflicts without
touching the live model or the baseline.

`SparseObservableList<TModel, TView>` and
`SparseObservableDictionary<TKey, TModel, TView>` accept an optional
`onRawModelAccess` constructor callback, invoked on every `Model` read so the
owning session can drop its observable-change cache when raw mutations bypass
wrapper notifications. Trusted generated code that already accounts for the
access reads `UnsafeModel` instead; external callers should keep using
`Model`. Both views expose `NotifyReset(bool reportChange = true)` for bulk
backing-instance mutations: pass `false` when the owner already accounts for
the write (generated `__SparseRefresh` does this after in-place writes,
retiring views whose backing instance was replaced and emitting one `Reset`
for views wrapping the same instance), and use the default when reporting
your own bulk raw-model mutations. Backing collections that implement
`INotifyCollectionChanged` forward their own events and must not be duplicated
with this method.

Session-bound descriptor graphs are cached per session: `Descriptors`
resolves the root set once and reuses it, while nested accessors keep reading
live observable state so reorder, replacement, reload, and revert stay fresh.
`BatchEdit` defers intermediate snapshot and diff work: mutations inside the
batch only mark the session dirty, and the outermost exit publishes a single
entry-to-exit net transition (including when the edit delegate throws).
Batching is notification coalescing only and never rolls the model back;
likewise `TryApplyInPlace` detects conflicts atomically but runs arbitrary
model setters, so a throwing setter may partially mutate the model before the
session resynchronizes and the exception propagates.

## ChangeSet history and transport guarantees

`ChangeSet.Between` snapshots its inputs: whole-root fragments are
deep-cloned and member collection containers are copied at capture with
comparers preserved, so later caller-side mutation cannot alter retained
history. Element values are shared by reference. Typed `Before`/`After`
endpoints return fresh container snapshots for collection members.

`EnumerateChanges` reports a single root entry for whole-root presence
transitions and per-element `Added`/`Removed` entries for set members when
both sides are present. Keyed, dictionary, and set element paths carry the
typed key; the wire text quotes the key with JSON escaping. Key text keeps
simple forms for strings and invariant primitives and qualifies other keys by
runtime type name. Typed keys compare by value, so distinct keys with identical
display text stay distinct without disambiguation suffixes.

`ChangePayload` transport is lossless and version-gated: the `"0.1"`
envelope version is validated on `ToChangeSet`, `ToPatch`,
`InvertReversibleChanges`, and `TryApplyMixedTo` before any model work.
`ToPayloadCore` and patch-side cores throw naming the omitted paths when a
`JsonIgnore` member changed instead of exporting an empty change list.
Redacted endpoints must be value-free; an endpoint carrying a value is
rejected on every interpretation path.
