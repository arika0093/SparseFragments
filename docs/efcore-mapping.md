# EF Core Mapping (PoC)

`SparseFragments.EntityFrameworkCore` maps an accepted DTO `ChangeSet` onto a
tracked EF Core entity. The change set is the strict write mask: only members
it marks changed execute their setters, and untouched EF properties stay
unmodified. This page describes the proof of concept built for issues #224
(scalar/nested mapping) and #225 (keyed child collections): the API, the
execution lifecycle, and what stays out of scope.

## Lifecycle

The mapper owns one step of the endpoint flow. Everything around it stays
application-owned: the read-side entity to DTO projection, `RebaseOnto` and
authorization, entity loading and querying, `SaveChanges`, transactions, and
retries.

<!-- illustrative: mapper shape; ported from the EfCoreSample.Server OrderMapper -->
```csharp
[SparseFragmentMapper]
public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
{
    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
    {
        map.Member(dto => dto.Name, entity => entity.CustomerName);
        map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code == null ? null : code.ToUpperInvariant()
        );
        map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        map.Ignore(dto => dto.DisplayTotal);
        map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child =>
            {
                child.Ignore(address => address.City);
            }
        );
        map.Collection<OrderLineDto, OrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.Subtotal);
                });
                lines.Create(dto => new OrderLineEntity
                {
                    Quantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice,
                });
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}
```

<!-- illustrative: endpoint flow; mirrors the SQLite-backed tests -->
```csharp
var current = ReadOrderDto(trackedEntity); // application-owned read mapping
var submitted = current.CreateChangeSet(incoming);
var rebase = submitted.RebaseOnto(current);
if (rebase.HasConflicts)
{
    return Conflict();
}

var accepted = rebase.Rebased;
var desired = accepted.ToPatch().ApplyTo(current);
var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(new OrderMapper(), db.Model);
var applied = mapper.ApplyWithResult(db, trackedEntity, desired, accepted); // no query, no SaveChanges
await db.SaveChangesAsync();
```

`Create` compiles and validates the plan once per EF `IModel` plus mapping
type and returns the cached mapper. `Apply` validates the change set first and
only then executes the queued writes, so a deterministic mapping failure
leaves scalar state untouched. Present values come from the change set's
after-state; `desired` supplies nested objects for recursion. `ApplyWithResult`
additionally correlates created child entities by DTO temporary identity (see
below); plain `Apply` discards that correlation.

## Configuration

Same-name scalar members are inferred; `Configure` declares only the rest.
An explicitly configured DTO path replaces its inferred default.

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
```

Converters are expected to be pure: they run during validation, before any
write. `Write` handlers receive the presence-aware after-state and run only
for changed members; missing policies do not apply to them.

Nested objects stay inside the same mapper through `Object`, scoped to the
parent path so the same child type pair can map differently elsewhere:

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

## Keyed collections

Keyed child collections synchronize by stable DTO identity through the typed
keyed transition (`Added`/`Edited`/`Removed`, `BeforeOrder`/`AfterOrder`,
`OrderChanged`) emitted by the ordinary `SparseFragments.Generator`; no
separate EF generator exists. Child mapping, key projection, factory, removal,
and order policies are declared in the same parent mapper `Configure`, nested
under the collection member, and scoped to that parent path: two collections
with the same element CLR types keep independent configurations. A shared
private method taking the collection builder may deduplicate repeated
declarations; a standalone child mapper class is never required.

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
        lines.Create(dto => new OrderLineEntity { Quantity = dto.Quantity }); // optional factory
        lines.Delete(); // or Detach(), SoftDelete(mark), Remove(callback)
        lines.Order((entity, index) => entity.SortOrder = index); // opt-in order persistence
    }
);
```

* Added children are created (factory or parameterless constructor), written
  through the path-scoped child plan from the desired after-state, and attached
  to the parent collection; tracked instances of edited or untouched children
  are preserved, never replaced, and only their changed members execute
  setters.
* Edited children are located by key among the tracked children and recurse
  through their typed element transition; removed children follow the explicit
  policy. Collection removal is never physical deletion by default: `Delete`
  marks rows deleted, `Detach` unlinks them (optional relationships only),
  `SoftDelete` flags them through a callback while keeping the row, and
  `Remove` runs a custom callback. A missing policy, `Detach` on a required or
  owned relationship, and `SoftDelete` on an owned navigation all fail plan
  validation with an explicit diagnostic.
* The DTO `[SparseKey]` may be computed (tuples, value objects) and may differ
  from EF identity. Without `Key()`, the DTO key must equal the single-property
  EF primary key type; composite EF keys and alternate identities always need
  an explicit projection to the same key space. Key members that EF protects
  (primary, alternate, or composite keys) are written on creation only through
  an explicit child `AllowWrite`, matching scalar protection elsewhere.
* `ApplyWithResult` correlates every `[SparseTemporaryKey]` Guid to its created
  child entity, scoped by collection path. Permanent keys always win when
  assigned; unassigned additions without a temporary value fail explicitly. The
  application reads database-generated keys off the tracked instances after
  its own `SaveChanges`, builds the authoritative DTO (preserving temporary
  values for new rows), and hands it to the existing client `Reconcile` flow;
  existing Reconcile issues (#216, #218) stay separately tracked.
* Order persistence is opt-in through `Order()`. Membership changes reorder the
  tracked in-memory list to the desired DTO order; the setter additionally
  writes the persisted position when the key order changed. A pure reorder
  without a setter fails explicitly, as do partial orders whose transition
  does not cover the full desired assigned keys.
* The navigation must be loaded (`Include`/`Load`): unloaded collections fail
  with `CollectionNotLoaded` instead of auto-loading or inferring empty.
  Unknown edited/removed keys, colliding additions, duplicated keys, and
  desired states that do not cover the full tracked collection all fail before
  mutating. `Apply` never queries, never calls `SaveChanges`, and never
  rebases; reflection covers EF metadata, relationship resolution, and
  configured callbacks only, never implicit child mapping discovery.

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
member from the desired state (the generated transition carries no leaves for
that case).

## EF metadata

Plans are validated against `DbContext.Model` and writes go through EF
property entries, so persistence behavior follows the model rather than CLR
reflection:

* scalar versus navigation, primary and alternate keys, concurrency tokens,
  `ValueGenerated`, and shadow properties are read from `IEntityType`;
* identity, generated, concurrency-token, and shadow members are never
  written unless explicitly allowed with `AllowWrite`, and same-name DTO
  members colliding with them fail as protected instead of mapping silently;
* shadow members have no CLR accessor; explicit mappings still need
  `AllowWrite`, and same-name DTO members fail as protected with the shadow
  reason;
* reference navigations that are null are materialized through their public
  parameterless constructor and enter the context as added; collection
  navigations are rejected;
* a pre-existing but untracked instance is attached as unchanged for that
  single entity, never through a graph-wide update.

Unmapped, ambiguous, protected, keyed-sync, and marker-only collection changed
paths all fail with `SparseEntityMappingException` carrying the dotted DTO
path. Changed marker-only collection paths (the two-argument `Collection`
overload, kept for diagnostics) fail with `CollectionNotSupported`; keyed
synchronization is configured through the three-argument overload above.

## PoC limitations

* Targets `net10.0` only.
* Keyed synchronization covers `IList` collection navigations with a generated
  keyed transition (`[SparseKey]` element with generator support). Scalar or
  unkeyed sequences, many-to-many skip navigations, and whole-collection
  replacement outside the keyed transition fail explicitly instead of syncing.
* No source-generated mapper Apply bodies yet: conventional mappings are
  compiled expression plans validated against EF metadata and cached per
  `IModel` plus mapping type. `Configure` must be deterministic for its mapping
  type. Dispatch runs through the typed keyed-transition contracts emitted by
  the ordinary generator; no separate EF generator exists. Generator emission
  stays a follow-up; the runtime plan is the validated source in this PoC.
* Added-child member validation (for example nullability) surfaces while the
  collection action executes, after relationship, key, and policy validation;
  every other deterministic failure precedes any write.
* Reference navigations only for `Object` and flattened paths; value-type
  and collection intermediates are rejected.
* Nested materialization needs a public parameterless constructor (or `Create`).
* Root presence transitions (whole-DTO null or missing) are outside `Apply`
  scope; pass a non-null desired DTO with member-level changes.
* Cross-parent foreign identities are detected only where feasible without
  querying (stale-current collisions, unknown tracked keys); true
  cross-parent conflicts surface at the application-owned `SaveChanges`.
  Editing an unassigned (temporary-only) identity is rejected: the server
  cannot resolve pending identities to persisted rows.
* No auto-load, no `SaveChanges`, no transactions, no retries, no Mapperly
  dependency, and no NativeAOT support.
* The Contracts project (DTOs plus generated change sets) references only
  `SparseFragments`; EF references live in the Server layer and the mapping
  package. The `EfCoreSample.Contracts` and `EfCoreSample.Server` projects
  prove the split builds and runs.
