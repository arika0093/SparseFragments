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
hint/structural-host suffixes in that configuration. Merge enum values are
normalized to shared semantic modes before validation and emission; the
downstream enum does not need to reuse `SparseFragments.MergeMode`.

`SparseRuntimeDialect` maps generated optional, comparer, collection, and merge
helpers to downstream-owned runtime types. `SparsePatchDialect` maps patch
runtime/facade, conflict/result types, nested member names, member
field/value types, and the provisional ChangeSet payload version token
(`ChangeSetPayloadVersion`, default `"0.1"`). `ReservedGeneratedNames` is caller-owned and defaults to an
empty set; include all names reserved by the generated API. Pass the same config
through analysis and source emission; this lets a consuming generator emit
against runtime types it owns instead of adding a SparseFragments runtime
dependency. Full source emission requires both dialects, and the Patch/STJ
emitters require an explicit patch dialect; Shared provides no implicit
SparseFragments runtime fallback. The product generator owns and supplies its
runtime defaults.

`SparseFragmentEmitter` retains the standalone `Fragment`/`Patch` API vocabulary.
It does not add product-specific model extension APIs unless the caller supplies
the optional `appendProductExtensions` callback. Downstream generators with a
different public vocabulary should not expose the standalone API shape; they
should reuse the shared analysis and focused emitters to write their own surface
and inject only product-owned extensions.

## Emission features

`SparseEmissionFeatures` selects which generated families appear for a model:
`Fragment`, `Patch`, `ChangeSet`, `ChangePayload`, `Observable`, and JSON
converters. Set it on `SparseGeneratorConfig.EmissionFeatures`. The standalone
default emits every family; downstream products opt out explicitly so unused
public APIs are not generated. Selections are validated before emission:
a patch requires its fragment, a change set requires its patch, and a payload
requires its change set. `GetEmittedTypeNames` lists the family root names used
for collision checks.

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
free-form body replacement is provided.

`SparseFragments.Generator` itself keeps compiling the sibling Shared sources
directly in-repo via a `Compile` glob in
`src/SparseFragments.Generator/SparseFragments.Generator.csproj`. That in-repo
path remains canonical inside this repository; this package exists for
external/downstream generators (for example Configlue, which migrates from a
submodule-based Shared source transport to this package).
