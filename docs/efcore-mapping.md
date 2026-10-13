# EF Core Mapping

`SparseTypes.EntityFrameworkCore` applies an accepted DTO `ChangeSet`
directly onto a tracked EF Core entity. The change set is both the strict write
mask and the value source: only members it marks changed execute their
setters, new values come from the typed transitions (`IsChanged`,
`After.IsPresent`, `After.Value`), and untouched EF properties stay
unmodified. There is no desired or root DTO argument. This page describes the
mapping delivered for issue #228: the API, the execution lifecycle, and the
supported/unsupported matrix.

## Lifecycle

The mapper owns one step of the endpoint flow. It can build the entity-to-DTO
projection from the write mapping, while `RebaseOnto`, authorization, query
execution and loading, `SaveChanges`, transactions, and retries stay
application-owned.

<!-- illustrative: mapper shape; ported from the EfCoreSample.Server OrderMapper -->
```csharp
[SparseFragmentMapper]
public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
{
    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
    {
        // Matching, safe scalar members map automatically via generated code.
        map.Member(dto => dto.Name, entity => entity.CustomerName);
        map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code == null ? null : code.ToUpperInvariant()
        );
        map.Read(dto => dto.Code, entity => entity.Code);
        map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        map.Ignore(dto => dto.DisplayTotal);
        map.Collection<OrderLineDto, OrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.Subtotal);
                });
                lines.Delete();
                lines.Order(entity => entity.SortOrder);
            }
        );
    }
}

var mapper = new OrderMapper();
var mapperInstance = SparseEntityMapper<OrderEntity, OrderDto>.Create(mapper, db.Model);
var current = mapperInstance.ToDto(trackedOrder);
var rebase = incoming.RebaseOnto(current);
if (rebase.HasConflicts)
{
    return Conflict();
}

var result = mapperInstance.ApplyWithResult(db, trackedOrder, rebase.Rebased); // no query, no SaveChanges
await db.SaveChangesAsync();
var authoritative = mapperInstance.ToDto(trackedOrder);
mapperInstance.AssignTemporaryKeys(db, result, current, authoritative);
```

`Create` runs `Configure` now, applies the source-generated conventional
defaults first, and validates the resulting plan against the EF `IModel`, so a
dormant unmapped member fails here even when the first change set never edits
it. Each `Create` call builds a fresh plan for that mapping instance: plans
are never shared across mapping instances, so constructor-injected
configuration cannot leak between instances. Reuse the returned mapper for
repeated applies against the same model.

For DI applications, `AddSparseEntityMappers` registers mapping types from the
generated registry and exposes `ISparseEntityMapper<TEntity, TDto>` services.
Mapping instances are singletons by default and can use constructor injection.
The first operation for a context model creates and caches one plan for that
`IModel`; a different model gets a separate plan. Call
`ValidateSparseEntityMappers<TContext>` during startup to build plans for
registered mappings present in that context's model and surface configuration
errors before serving requests.

`Projection` exposes an `Expression<Func<TEntity, TDto>>` for use with
`Select`, and `ToDto` evaluates the same mapping for an already loaded entity.
Mappings that cannot be inverted, such as custom writers and conversions
without a reverse expression, need an explicit `Read` registration. Projection
validation is deferred until either read API is requested.

Use `query.IncludeFor(mapper, changes)` to add the navigation paths needed by
an accepted change set. The method returns a query without executing it.
Membership or order changes load complete collections; edit-only keyed
transitions may use a filtered Include when the key is a mapped scalar property.
Other key shapes use an unfiltered Include. Owned references are omitted, and
callers can chain `AsSplitQuery()` when multiple collection includes warrant it.

`Apply` validates the whole change set before executing any queued write, so a
deterministic mapping failure leaves the entity graph and the `ChangeTracker`
untouched: no navigation is instantiated, nothing is attached or added, and no
collection setter runs during validation. Arbitrary custom callbacks
(converters, writers, factories) run during validation and execution; if one
throws after partial execution, the context must be treated as inconsistent
(discard it or roll back the surrounding transaction; the mapper performs no
rollback itself).

A current DTO may still be used by the caller for `Rebase`; the mapper never
needs an after-state DTO and never reconstructs one internally.
`ApplyWithResult` additionally correlates created child entities by DTO
temporary identity, scoped by collection path (see below); plain `Apply`
discards that correlation.
The result also exposes `AppliedPaths`, an ordered list of successful writes
with DTO paths, operation kinds, and known entity targets. Only
`ApplyWithResult` records this list.

## Configuration

Conventional defaults are source-generated by the ordinary
`SparseTypes.Generator`, which discovers `[SparseFragmentMapper]`
Entity/DTO pairs (DTOs may live in a referenced Contracts assembly) and emits
an external `{Mapper}_SparseEfDefaults` companion: same-name exact-type
scalars via `Member`, same-name nested models via recursive `Object`, and
same-name collections via `DeclareCollection` markers. `Configure` declares
only the rest, and every explicit entry replaces its generated default exactly
once at the same DTO member path; the default setter never runs in addition
to a configured override. Collection element defaults arrive the same way:
the companion registers path-scoped conventional child mappings that the
explicit `Collection(..., lines => lines.Element(...))` composes underneath
its own overrides. Child element mappings stay scoped by parent collection
path: the same child CLR type pair may map differently under two
collections. A conventional `DeclareCollection` marker alone never counts as
coverage: declare the full keyed `Collection` mapping or `Ignore` the member,
or mapper creation fails even when no change set touches the collection.

<!-- illustrative: member-level registrations available on the builder -->
```csharp
map.Member(dto => dto.Name, entity => entity.CustomerName); // renamed
map.Member(dto => dto.ShipCity, entity => entity.Address!.City); // flattened
map.Member(dto => dto.Code, entity => entity.Code, code => Normalize(code)); // converted
map.Write(dto => dto.AuditNote, (entity, after) => AppendAudit(entity, after)); // specialized
map.Ignore(dto => dto.DisplayTotal); // suppressed
map.OnMissing(dto => dto.Notes, SparseEntityMissingPolicy.SetNull); // missing policy
map.AllowWrite(dto => dto.LegacyId); // protected member opt-in
map.MissingDefault(SparseEntityMissingPolicy.Ignore); // scope default
map.DeclareCollection(dto => dto.Tags, entity => entity.Tags); // known but never synced
```

Converters are expected to be pure: they run during validation, before any
write. `Write` handlers receive the presence-aware after-state
(`Optional<T>`: unchanged is never delivered, missing and present values are
distinguishable) and run only for changed members; missing policies do not
apply to them. Custom writes that need unchanged sibling DTO values are not
inferable: use current entity state inside the callback or fail explicitly.

Static mistakes fail at compile time: an incompatible same-name pair (both
sides exist, types differ) reports `SPF033` and points at the mapper, instead
of failing later through reflection. Explicit `Member` conversions and
`Ignore` entries suppress it for the members they provably cover;
helper-delegated `Configure` bodies stay silent there with runtime plan
validation as the backstop. A `[SparseFragmentMapper]` type that does not
implement `IEntityModelMapping<TEntity, TDto>` reports `SPF034`. Since
arbitrary `Configure` bodies cannot be evaluated statically, deeper omissions
fail fast at mapper creation with the DTO path and a corrective action.
A marked mapper whose companion is missing (generator not wired as an
analyzer) fails fast at creation instead of silently falling back to runtime
inference. Mappers without `[SparseFragmentMapper]` also fail at creation with
guidance to add the attribute; there is no runtime scalar-inference fallback.

Ownership split: the generator statically discovers every conventional
default it can resolve (same-name scalars, nested objects, collection
markers) and reports incompatible pairs at compile time. Missing/null guards,
keyed lookup, relationship handling, and order persistence live once in the
shared generic runtime algorithms instead of being duplicated per model, and
execute only through the typed transitions above. Collection element children
are configured explicitly in `Configure` (identity, factory, removal, and
order policies cannot be inferred); same-name child scalars resolve within
that explicit path scope. Nothing is ever discovered by scanning DTO members
at runtime once the companion exists.

Nested objects stay inside the same mapper through `Object`, scoped to the
parent path:

<!-- illustrative: scoped nested mapping; keyed collections follow the same scoping -->
```csharp
map.Object(
    dto => dto.Address,
    entity => entity.Address,
    child =>
    {
        child.Ignore(address => address.City);
    },
    SparseEntityMissingPolicy.Throw
);
```

## Direct value semantics

* Scalars read the new value from the transition. Unchanged means no
  operation; `Present(null)` writes an explicit null; changed-to-`Missing`
  follows the explicit policy (`Ignore`/`SetDefault`/`SetNull`, default
  `Throw`), never an accidental null.
* Renamed and flattened paths, typed conversions, and custom writers work
  without a full desired DTO.
* Nested edits recurse through the child change set. Nested adds use the
  complete After snapshot carried by the whole-object record (converting the
  generated Fragment shape when that is what the transition projects) and a
  dedicated new-object path; the added object is validated preflight on a
  detached prospective instance and only then created, linked, and tracked.
  Null and removal respect EF relationship, owned, and complex semantics.
* A current DTO used for `Rebase` is a concurrency input, not a mapper input.

## Keyed collections

Keyed child collections synchronize by stable DTO identity through the typed
keyed transition (`Added`/`Edited`/`Removed`, `BeforeOrder`/`AfterOrder`,
`OrderChanged`, temporary lookup) emitted by the ordinary generator; no
separate EF generator exists. Operations source exclusively from the
transition: additions from item `After` snapshots, edits from item `Edit`
transitions, removals from keyed before-identity, and order from the
transition order plus temporary-identity metadata. No desired or
full-collection DTO is required. Child mapping, key projection, factory,
removal, and order policies are declared in the same parent mapper
`Configure` and scoped to that parent path.

Added children use a public parameterless constructor by default. Configure
`Create` only when construction needs values outside the mapped after-state or
a domain factory; the child plan writes mapped members from the after-state
after the factory returns.

<!-- illustrative: keyed collection registrations available on the collection builder -->
```csharp
map.Collection<OrderLineDto, OrderLineEntity>(
    dto => dto.Lines,
    entity => entity.Lines,
    lines =>
    {
        lines.Element(child =>
        {
            child.Ignore(line => line.Subtotal);
        });
        lines.Key(dto => dto.Key, entity => ValueTuple.Create(entity.TenantId, entity.LineNo)); // computed/composite
        lines.UnassignedKey(0); // explicit sentinel; never inferred from default(TKey)
        lines.Delete(); // or Detach(), SoftDelete(mark), Remove(callback)
        lines.Order((entity, index) => entity.SortOrder = index); // opt-in order persistence
    }
);
```

* Added children are created (factory or parameterless constructor), written
  through the path-scoped child plan from their `Added` After snapshot, and
  attached to the parent collection; tracked instances of edited or untouched
  children are preserved, never replaced, and only their changed members
  execute setters. Whole-collection replace is not a primitive: it arrives as
  semantic added/removed/edited transitions, never as clear plus re-add.
* Edited children are located by key within the addressed parent collection
  only: a matching key under a different parent never resolves here. Removals
  follow the explicit policy. Collection removal is never physical deletion by
  default: `Delete` marks rows deleted, `Detach` unlinks them (optional
  relationships only, checked against `IForeignKey.IsRequired` rather than
  per-property nullability), `SoftDelete` flags them through a callback while
  keeping the row, and `Remove` runs a custom callback. `DeleteBehavior` alone
  never authorizes physical deletion.
* The DTO `[SparseKey]` may be computed (tuples, value objects) and may differ
  from EF identity. Without `Key()`, the DTO key must equal the single-property
  EF primary key type; composite EF keys and alternate identities always need
  an explicit projection to the same key space. Generated child mappings
  exclude `[SparseKey]` and `[SparseTemporaryKey]` members by default. Use an
  explicit child `AllowWrite` when a client-assigned key must be written during
  creation.
* No unassigned sentinel is ever inferred from `default(TKey)`: without an
  explicit `[SparseKey(Unassigned = ...)]` or `UnassignedKey(...)`
  declaration, a stable default key is a valid assigned identity. Multiple
  unassigned children may share the declared sentinel when they carry distinct
  `[SparseTemporaryKey]` values. Assigned keys always take precedence over
  temporary lookup.
* `ApplyWithResult` correlates every `[SparseTemporaryKey]` Guid to its created
  child entity, scoped by collection path and keyed parent identity (nested
  scopes additionally qualify with the added parent's assigned or temporary
  identity): duplicate Guids within one logical collection fail, reuse across
  distinct scopes resolves independently. After `SaveChanges`, call
  `AssignTemporaryKeys` with the baseline and authoritative DTOs. It copies
  temporary identities to newly created DTO elements without replacing
  identities already present in the baseline, ready for the client
  `Reconcile` flow.
* `ApplyWithResult` also records each successful write in `AppliedPaths`, with
  the DTO path, operation kind, and known entity target. `Apply` does not
  allocate or populate this operation list.
  Previously soft-deleted rows stay tracked but leave the DTO projection:
  later applies retain them only when the `SoftDelete` `isDeleted` predicate
  proves the omission, and their positions persist through `Order()` like any
  other element; without the predicate (or for an active row) the omission
  fails completeness validation.
* Order persistence is opt-in through `Order()`. A pure reorder without a
  setter fails explicitly, as do reorders whose transition evidence is
  incomplete.
* The navigation must be loaded with an unfiltered `Include`/`Load`:
  unloaded collections fail with `CollectionNotLoaded` instead of
  auto-loading, querying, or inferring empty. A loaded navigation is not proof
  of completeness: a filtered `Include` marks the navigation loaded while
  omitting elements. Pure addressed-key edits without membership or order
  changes validate only addressed state and stay safe on partial loads;
  additions, removals, and order persistence require complete evidence (every
  surviving assigned key needs an `AfterOrder` slot and vice versa) and fail
  explicitly otherwise. Filtered includes are unsupported for
  addition/removal/reorder-bearing transitions.
* Unknown edited/removed keys, foreign-parent keys, colliding additions, and
  duplicated keys fail during preflight, before mutating. `Apply` never
  queries, never calls `SaveChanges`, and never rebases. Reflection covers EF
  metadata, relationship resolution, and configured callbacks only, never
  fallback implicit mapping.
* Skip navigations (many-to-many), unkeyed sequences, and scalar sequences
  without a compatible EF primitive-collection property fail explicitly
  instead of syncing.

## Missing versus null

`Missing` is never silently interpreted as null. The default policy throws
`MissingNotHandled` and leaves the entity untouched; each member can opt into
`Ignore`, `SetDefault`, or `SetNull` through `OnMissing` or `MissingDefault`.
`Present(null)` writes null directly and needs no policy, but fails with
`NullNotAllowed` when the entity member does not accept null. A global
`MissingDefault` that a member cannot honor fails lazily: only when that
member actually goes missing. Whole-object transitions follow the same split:
a missing nested object uses the `objectMissing` policy, a present null sets
the navigation to null, and a null to object transition writes every mapped
member from the After snapshot.

## EF metadata

Plans are validated against `DbContext.Model` and writes go through EF
property entries, so persistence behavior follows the model rather than CLR
reflection:

* scalar versus navigation, primary and alternate keys, concurrency tokens,
  `ValueGenerated`, ownership, and shadow properties are read from
  `IEntityType`; relationship requiredness comes from
  `IForeignKey.IsRequired`;
* identity, generated, concurrency-token, and shadow members are never
  written unless explicitly allowed with `AllowWrite`, and same-name DTO
  members colliding with them fail as protected instead of mapping silently;
* shadow members have no CLR accessor; explicit mappings still need
  `AllowWrite`, and same-name DTO members fail as protected with the shadow
  reason;
* null reference navigations are materialized in the execution phase through
  their public parameterless constructor and enter the context as added; null
  collection navigations are assigned in the execution phase only;
* a pre-existing but detached root entity is attached as unchanged (explicit,
  documented handling); graph-wide updates are never issued.

Unmapped, ambiguous, protected, keyed-sync, and declared-only collection
changed paths all fail with `SparseEntityMappingException` carrying the
mapper type, the dotted DTO path (including nested keyed scopes), the entity
target, the failure category, and a corrective action.

## Supported / unsupported matrix

| Scenario | Status |
| --- | --- |
| Scalar edits from the accepted ChangeSet, renamed/flattened members, conversions, custom writers | Supported |
| `Present(null)` vs changed-to-`Missing` policies | Supported |
| Nested edits via child change sets; nested adds via After snapshots | Supported |
| Owned navigations and field-backed/shadow members via property entries | Supported |
| Keyed add/edit/remove without a final DTO, with Delete/Detach/SoftDelete/Remove policies | Supported |
| Composite, alternate, and computed identities via explicit `Key()` | Supported |
| Path-scoped `[SparseTemporaryKey]` correlation and authoritative-DTO `Reconcile` | Supported |
| `AssignTemporaryKeys` after persistence and `AppliedPaths` from `ApplyWithResult` | Supported |
| Entity-to-DTO `Projection` and `ToDto` for readable mappings | Supported |
| ChangeSet-derived navigation loading with `IncludeFor` | Supported |
| EF primitive collections as whole-value replacements | Supported |
| Opt-in order persistence with complete order evidence | Supported |
| Compile-time conventional defaults plus fail-fast mapper creation | Supported |
| Rebase, `SaveChanges`, authorization, loading, transactions | Application-owned (out of scope) |
| NativeAOT | Not required; EF model inspection may reflect |
| Unkeyed sequences, unsupported scalar sequences, skip navigations | Explicit failure |
| Filtered includes for removal/reorder transitions | Explicit failure |
| Transformations needing unchanged sibling DTO values | Explicit custom logic or unsupported-operation diagnostic |
| Detached nested targets | Attached as unchanged (documented) or rejected per member |

## Notes and limits

* Targets `net10.0` only.
* Keyed synchronization covers `IList` collection navigations with a generated
  keyed transition (`[SparseKey]` element with generator support).
* Primitive collection updates replace the complete collection instance. The
  DTO and EF property must expose compatible element types; null and `Missing`
  follow the configured nullability and missing policies.
* Custom callbacks are caller code: failures after partial execution leave the
  context inconsistent; do not reuse it outside a rolled-back transaction.
* Conversions run during validation and should be pure.
* Reference navigations only for `Object` and flattened paths; value-type
  and collection intermediates are rejected.
* Nested materialization needs a public parameterless constructor (or `Create`).
* Root presence transitions (whole-DTO null or missing) are outside `Apply`
  scope; pass member-level changes.
* Cross-parent foreign identities are detected without querying where feasible
  (unknown tracked keys fail); true cross-parent conflicts surface at the
  application-owned `SaveChanges`. Editing an unassigned (temporary-only)
  identity is rejected: the server cannot resolve pending identities to
  persisted rows.
* The Contracts project (DTOs plus generated change sets) references only
  `SparseTypes`. Client references Contracts and calls the Server API;
  Server owns EF and the mapping package. Aspire AppHost starts both projects
  for the sample. The EF Core tests use their own model fixtures and do not
  reference the sample projects.
