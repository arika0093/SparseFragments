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

`SparseFragments.Generator` itself keeps compiling the sibling Shared sources
directly in-repo via a `Compile` glob in
`src/SparseFragments.Generator/SparseFragments.Generator.csproj`. That in-repo
path remains canonical inside this repository; this package exists for
external/downstream generators (for example Configlue, which migrates from a
submodule-based Shared source transport to this package).
