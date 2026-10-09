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

`SparseRuntimeDialect` maps generated optional, comparer, collection, and merge
helpers to downstream-owned runtime types. `SparsePatchDialect` maps patch
runtime/facade, conflict/result types, nested member names, member
field/value types, and the provisional ChangePayload version token
(`ChangePayloadVersion`, default `"0.1"`). `ReservedGeneratedNames` is caller-owned and defaults to an
empty set; include all names reserved by the generated API. Pass the same config
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
always name caller-owned types. A downstream runtime that enables the
generated options overloads provides those types itself: an options record
with `RejectChangesWithRedactedBeforeValuesDuringRebase`,
`RedactedBeforePaths`, `DefaultRebaseMode`, `IsRedactedBefore`, and `Nest`;
a `SparseRebaseMode` enum (`Default`, `FailOnConflict`, `PreferIncoming`,
`PreferCurrent`); a `FragmentRebasePolicy<T>` base with presence-aware
`TryRebase`/`AreEqual`; and a `RedactedBefore` member on its conflict-kind
enum. Generated code calls only those members.

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
flattens nested, keyed, and dictionary transitions into path-based `ChangeInfo`
entries. `Before` and `After` use the configured runtime's `Optional<object?>`
to preserve missing versus present-null values; `ChangeKind.Order` carries
keyed collection order as a collection-level entry. Consumers decide how to
format or display these entries.

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

## ChangeSet history and transport guarantees

`ChangeSet.Between` snapshots its inputs: whole-root fragments are
deep-cloned and member collection containers are copied at capture with
comparers preserved, so later caller-side mutation cannot alter retained
history. Element values are shared by reference. Typed `Before`/`After`
endpoints return fresh container snapshots for collection members.

`EnumerateChanges` reports a single `$root` entry for whole-root presence
transitions and per-element `Added`/`Removed` entries for set members when
both sides are present. Keyed, dictionary, and set element paths quote the
key with JSON escaping; key text keeps simple forms for strings and
invariant primitives and qualifies other keys by runtime type name, with
per-enumeration disambiguation on residual collisions.

`ChangePayload` transport is lossless and version-gated: the `"0.1"`
envelope version is validated on `ToChangeSet`, `ToPatch`,
`InvertReversibleChanges`, and `TryApplyMixedTo` before any model work.
`ToPayloadCore` and patch-side cores throw naming the omitted paths when a
`JsonIgnore` member changed instead of exporting an empty change list.
Redacted endpoints must be value-free; an endpoint carrying a value is
rejected on every interpretation path.
