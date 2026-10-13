# SparseTypes.Generator.Shared

Source-only generator infrastructure shared by `SparseTypes.Generator` and downstream generators.

This package ships C# sources only — there is no runtime assembly and no `lib/` assets.
Referencing it compiles the shared sources into the consuming generator via
`build/SparseTypes.Generator.Shared.props` (direct consumer only; no
`buildTransitive/` propagation):

```xml
<PackageReference
  Include="SparseTypes.Generator.Shared"
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
downstream enum does not need to reuse `SparseTypes.MergeMode`.
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
helpers to downstream-owned runtime types. Its optional `CloneContextScopeFactory`
names a parameterless method returning an `IDisposable` scope with a `Context`
property of type `Dictionary<object, object>`. Each scope must own an empty,
reference-identity dictionary exclusively until disposal. Generated `From` and
`DeepClone` operations dispose the scope on success and on exceptions. Leave the
factory unset to keep using `ReferenceComparer.CreateCloneContext()`.

`SparsePatchDialect.KeyValidationScopeFactory` optionally names a generic method
called with the key type, required capacity, and a `Func<int, HashSet<TKey>>`
constructor. The constructor uses the target framework's capacity overload when
available. The factory returns an `IDisposable` scope whose `Keys` property is an
empty `HashSet<TKey>` using default key equality. The scope owns that set
exclusively until disposal. Aligned
keyed comparisons dispose it after validating keys, before comparing child
values. Leave the factory unset to allocate validation storage per comparison.

`SparsePatchDialect` maps patch
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
against runtime types it owns instead of adding a SparseTypes runtime
dependency. Full source emission requires both dialects, and the Patch/STJ
emitters require an explicit patch dialect; Shared provides no implicit
SparseTypes runtime fallback. The product generator owns and supplies its
runtime defaults.

Structural collection values and keyed sequence elements must expose
`ChangeSet.Between(TModel before, TModel after)`. Shared emits this overload
when the model type is supplied. Collection comparisons call it directly so
models whose members need no cloning can avoid intermediate fragments. Models
that need projection or cloning retain the fragment-based comparison path.

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
namespace plus `SparsePath`). Set `PathIsValueType` to `true` when both canonical
and typed paths are value types. This emits readonly struct builders and omits
path null checks; the default remains compatible with reference-type paths.
A downstream runtime that enables the
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
default emits every family except fragment origins; downstream products opt out
explicitly so unused public APIs are not generated. Selections are validated before emission:
a patch requires its fragment, a change set requires its patch, and a payload
requires its change set. `GetEmittedTypeNames` lists the family root names used
for collision checks.

`EmitFragmentOrigins` stays opt-in because it names runtime contracts a
downstream product may not carry. When enabled, fragments gain an optional
origin (`new Fragment(origin)`, null means Unknown), ordinary `Merge` and
`ApplyChanges` propagate attribution by the existing merge rules, and the
effective values can be read back with `GetOrigin`/`TryGetOrigin`,
`EnumerateOrigins`, `GetByOrigin`, and `SplitByOrigin` over the caller-owned
path type (the same `SparsePath` contract used by `ChangeSet`, so typed
per-model navigation binds without extra holders). Nested fragments inherit
the enclosing default unless explicitly originated; custom strategies report
Unknown. Origins never affect equality, `Diff`, Patch/ChangeSet creation, or
the wire format. A downstream runtime that enables this family provides
`FragmentOriginEntry`, `FragmentOriginGroup<TFragment>`, and the
`MergeAppendOrigins`/`MergeSetUnionOrigins`/`MergeSetOrigins` helpers on its
collection-merger facade.

When a model emits a `ChangeSet`, it also exposes `EnumerateChanges()`, which
flattens nested, keyed, and dictionary transitions into `ChangeInfo` entries
carrying a canonical path. `Before` and `After` use the configured runtime's `Optional<object?>`
to preserve missing versus present-null values; `ChangeKind.Order` carries
keyed collection order as a collection-level entry. Consumers decide how to
format or display these entries.

`SparsePatchDialect` accepts an optional `TransitionContracts` record naming
the caller-owned read-only transition contracts. When set, every generated
`ChangeSet` implements the change-set contract (`IsChanged`,
`EnumerateChangeRecords`, and the `TryGet*` member traversal entry points),
scalar and set transitions implement the value-change contract, keyed
transitions and their items implement the keyed contracts (plus the temporary
contract when the element declares temporary identity), and dictionary
transitions and their entries implement the dictionary contracts. Generated
members forward to the existing typed surface; no parallel representation is
introduced. Leave `TransitionContracts` null to disable the surface: products
without the runtime contracts keep byte-identical output. Shared never names a
`SparseTypes` runtime type; every contract name arrives through the record.

`SparseTransitionContracts.TypedChangeSetInterfaceType` optionally names an
open generic interface for model-typed change sets. Set it to a fully qualified
type name without type arguments, such as
`global::MyProduct.ISparseChangeSet`, and make that interface derive from
`ChangeSetInterfaceType`. Keep its model parameter invariant when downstream
APIs need exact model matching. When a model type is available, Shared closes
the configured interface over that model for each root and nested `ChangeSet`.
Downstream APIs can use this closed interface as a parameter type, so a change
set for another model fails to compile. Leave this property unset to retain the
non-generic `ChangeSetInterfaceType`; leave `TransitionContracts` null to
disable transition contracts.

Paths use the caller-owned path type from `SparsePatchDialect.PathType`
(defaulting to the dialect runtime namespace plus `SparsePath`). `ChangeInfo`
exposes the path in that type plus a wire-compatible text rendering; the
generated model also gains a thin `T.SparsePath` entrypoint with a fluent
`SparsePaths<TRoot>` root builder and per-member collection builders, as well
as exact-match `Find` overloads (untyped and compile-time typed) on the
`ChangeSet`. Rebase conflicts, descriptor paths, and session
`EnumerateChangedPaths()` use the same path type. Downstream runtimes provide
their own path type with member/key/index segments, equality, and text
rendering; Shared never names a `SparseTypes` runtime type.

Scalar `Find` lookups inspect the path's `Depth`, `RootType`, and segment `Name`
before reading the matching transition. Custom path types must expose these
members along with equality and the path factories described above. Segment
`Name` is empty for non-member segments.

JSON artifacts additionally require a compatible `System.Text.Json` API set in
the consuming compilation. `SparseStjCapabilities` snapshots the referenced
symbols: the converter base, property attributes, polymorphic and discriminator
attributes, unknown-member handling, the UTF-8 reader and writer surface, and
type-info lookup. `SparseStjCapabilityDetector.Probe` computes the snapshot
from the compilation itself, never from the target framework name or the
generator host's dependencies. `SparseGeneratorConfig.StjCapabilities` carries
it (null keeps the historical full surface for products that never probe), and
`SparsePatchDialect.StjCapabilities` propagates it to emitters that only
receive the dialect. `EffectiveEmitJson` intersects the snapshot with
`EmitJsonConverters`. When the surface is absent or partial, emitters drop
every `System.Text.Json` token and keep the serializer-independent `Fragment`,
`Patch`, `ChangeSet`, and payload CLR projections. A present-but-partial
surface with JSON families requested reports the missing APIs through the
InvalidEmissionPlan diagnostic. The `ChangePayload` v1 endpoint and item-kind
mapping lives in generated per-model converters that read tokens from the
runtime wire constants; the runtime endpoint and enum types carry no serializer
attributes.

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
`SparseTypes.Generator` configures `SparseTypes.Generated`.
Setting the optional `EditSessionModelAccessorInterfaceMetadataName` additionally
implements a trusted model-accessor interface on each generated session, so
framework integrations can read the live model without invalidating the
session's observable-change cache. Consumers without that interface keep the
previous behavior: framework helpers fall back to the raw model.
Set `OpenDraftStateInterfaceMetadataName` to the consumer-owned interface
metadata name when generated sessions should report open child drafts. Shared
implements that contract on both sessions and scopes; a session reports its
active root and nested scopes, while a scope reports its active descendants.
Closing or invalidating a scope removes it and its descendants from that state.
Setting the optional `EditSessionWithPathsInterfaceMetadataName` implements a
typed path-provider contract on each generated root session and adds a cached
`Paths` property returning the existing generated `SparsePaths<TModel>`
builder, so consumers navigate `session.Paths.Items.Key(id).Title` without a
separate root builder. Reading `Paths` never touches the mutable model, the
change cache, or edit notifications. Collection member builders then also
implement the caller-owned typed-path interface over the collection value
type (element `Key`/`At`/`TemporaryKey`, dictionary, and set navigation are
unchanged), which keeps nested, indexed, keyed, nullable, and relocated child
navigation compile-time typed. Child scopes keep only the minimal session
contract because a generic child scope cannot promise the root's concrete
builder. Leave the entry null to disable the surface: products without the
contract keep byte-identical output.

`SparsePatchDialect` accepts an optional `StateContracts` record naming the
caller-owned semantic state types: the field state, the collection state view,
and the static state queries. When set (together with `TransitionContracts`
and the typed-path interface), every generated `EditSession` gains live
`FieldState` overloads (compile-time typed and model-agnostic paths) plus a
path-addressed `CollectionState` view over root keyed and dictionary members
for state inspection only; the surface is named `CollectionState` so a future
path-addressed editing facade can keep the distinct `Collection` name. Setting
the optional `ObservedFieldType` and `ObservedFieldHooksType` entries adds
opt-in `ObserveField`/`WatchField` factories returning a disposable
`INotifyPropertyChanged` watcher per path; the watcher refreshes on the
session's `PropertyChanged` notification, which covers baseline-only accepts
that never publish a transition, and notifies only on effective state changes.
Queries derive from the session's retained baseline or change set on each call and keep
no per-field caches or subscriptions; change-set queries are point-in-time
while session queries re-diff the live model. Row states reuse the typed
keyed/dictionary transitions, and field states project the flattened change
records with missing versus present-null endpoints preserved. Leave
`StateContracts` null to disable the surface: products without the runtime
state types keep byte-identical output. Shared never names a `SparseTypes`
runtime type; every state name arrives through the record.

`SparseEditSessionDialect` optionally binds an opt-in history surface through
`HistoryType`, `HistoryOptionsType`, and `HistoryHooksType`. When all three
name caller-owned types, each generated session gains a typed `CreateHistory`
factory that wires the concrete change set (`IsEmpty`, `Invert`, `Compose`,
in-place apply, transition subscription) into the reusable history core, plus
baseline-lifecycle invalidation that clears registered histories only after
`AcceptChanges`, `Reload`, `TryReconcile`, or `RevertChanges` succeeds. Leave
any entry null to keep sessions byte-identical without the factory. The
factory is an advanced multi-observer escape hatch and is hidden from routine
IntelliSense; each factory call subscribes an independent disposable history
owned by its caller.

Setting `HistoryContractType` to a caller-owned non-generic history control
contract (with a parameterless options type exposing `MaxEntries`) adds the
single-owned surface on top of that binding: a nullable `History` accessor,
`EnableHistory` returning the session-owned control, `DisableHistory`
releasing it, and a `HistoryChanged` availability event. The control exposes
`CanUndo`, `CanRedo`, `Undo`, `Redo`, `TryUndo`, `TryRedo`, counts, grouping,
`Clear`, and a `StateChanged` event raised by edits, grouped commits, replays,
and lifecycle clears; failed replays raise nothing. The view is not
disposable: only the session releases it. Repeating `EnableHistory` with the
same options returns the existing history without resetting it; repeating with
different options throws, so reconfiguration goes through `DisableHistory`
first. `Fork` and new sessions start without history. Leave
`HistoryContractType` null to keep the legacy factory only, so existing
consumers stay byte-identical.

Outside `BatchEdit`, observable edits validate and publish their transitions
immediately; a batch publishes its net transition at exit. The baseline
comparison for `HasChanges` is deferred until its first read after an edit,
which reuses the observed snapshot, or an explicit `CreateChangeSet` call.
Notification handlers can read `HasChanges` synchronously. Custom `Between`
delegates must allow the baseline comparison, including any exceptions it
produces, to occur at that read. Raw model access disables this cache so
subsequent reads capture the live model.

`SparseEditSessionCapabilities` represents the reusable cores as explicit
Generated-Once capabilities with validated prerequisites
(`EditSessionDialect`, runtime/patch dialects, Fragment/Patch/ChangeSet/
Observable families). `SparseEditSessionRoles` names the generic role binding
(state, snapshot, command, transition, observable/current views) and
`SparseEditSessionAdapterContract` documents the per-model delegate binding
(FromModel, Between, ToPatch, advance baseline, projections, apply, invert,
rebase, notifications). Until the capability aggregation pipeline lands, call
`EmitCore`/`EmitCapability` once per compilation with the de-duplicated flags.
Each emitted `EditSession` also carries speculative branching: `Fork()` copies
the live model through the fragment snapshot with the current state as its
baseline, and `TryMergeFrom()` rebases a same-lineage fork onto the receiver
and returns a new session keeping the receiver baseline. Branching reuses the
existing adapter delegates and adds no configuration knob; sessions built
without rebase or baseline materialization report `NotSupportedException`.

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
UI/session/builder/paths names dodge source member collisions, and
surface/implementation hint names stay stable and unique. The namespace is
explicit with no Shared fallback; null keeps the single-file emission.
`SparseGenerationResult.AdditionalSources` carries the implementation files
with the surface file so per-model incremental isolation covers both.

With an explicit namespace, the UI/editing types (`Observable`,
`ReadOnlyView`, `EditSession`) nest in the annotated model; only operation,
payload, builder, paths, and descriptor-factory machinery lives in the
per-model container. Child observable/view references stay model-nested, so
no cross-container rewrite applies to them. Private member attributes are
read through an internal model bridge so the factory never names a private
type. A same-named member pushes the generated type behind a `Sparse`
prefix (`SparseObservable`, `SparseEditSession`); a user-nested
`EditSession` still collides and fails compilation.

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
class (`<container>FragmentOperations`). It owns the model-specific
conversion (`From`, `ToModel`, projection), merge, diff, and deep-clone
bodies plus POCO clone helpers; the model keeps one-line facades on
`Fragment` and the root projection bridge. The class is `file`-scoped when
`SparseGeneratorConfig.AllowFileScopedTypes` is set (consumer language C#
11+; the owning generator derives it from the compilation), otherwise
`internal static`, because only the owning model file references it.
Reference cycles, comparers, merge modes, and presence semantics are
unchanged, and collection kernels stay compilation-scoped under issue #182.
The container also hosts the relocated `FragmentBuilder` and
`SparsePaths<TRoot>` classes; the model keeps the `ToBuilder()` facade and
the static `SparsePath` entry point forwarding to them.
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
payload projection and restoration) live in the private nested
`Patch.__Core`/`ChangeSet.__Core` with direct private-state access. The model
keeps thin typed facades: Patch retains private fields, typed `ref` member
access, lazy nested identity, constructors and whole operations; ChangeSet
retains private canonical sparse storage, a private constructor and the typed
transition API. Facades call their nested core; the core reads and writes
private state without an internal bridge. Cross-family use goes through typed
facades or narrow internal `__Sparse*` bridges, so `ref` identity, lazy
aliasing, canonical sparsity and ownership behave as in single-file emission.
The per-model implementation container hosts only public `FragmentBuilder`
and paths. A null namespace keeps the legacy single-file emission for
downstream dialects.

Compilation-scoped helpers live in the same namespace with stable hint names
(`Generated.ReadOnlyAdapters.g.cs`, `Generated.CloneKernels.g.cs`,
`Generated.RemovalIndex.g.cs`).
`SparseGeneratedOnceNames` pins those identities; per-model output with an
explicit namespace instantiates them instead of redefining the generic
code. The product generator aggregates capabilities across explicit and
promoted models through the capability-driven plane and emits each family
once (single wiring; the interim manual aggregation was removed at merge
time). The clone family keeps the track-3 identity (`SparseCloneKernels`,
`Generated.CloneKernels.g.cs`); track-2's `SparseCloneHelpers`/`CloneHelpers.g.cs`
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
(`.DescriptorFactory.g.cs`) inside the model container. The model-nested
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

`SparseTypes.Generator` itself keeps compiling the sibling Shared sources
directly in-repo via a `Compile` glob in
`src/SparseTypes.Generator/SparseTypes.Generator.csproj`. That in-repo
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
SparseTypes runtime dependency; model emitters invoke them while keeping
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
