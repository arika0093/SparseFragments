# EF Core Mapping (PoC)

`SparseFragments.EntityFrameworkCore` maps an accepted DTO `ChangeSet` onto a
tracked EF Core entity. The change set is the strict write mask: only members
it marks changed execute their setters, and untouched EF properties stay
unmodified. This page describes the proof of concept built for issue #224:
the API, the execution lifecycle, and what stays out of scope.

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
mapper.Apply(db, trackedEntity, desired, accepted); // no query, no SaveChanges
await db.SaveChangesAsync();
```

`Create` compiles and validates the plan once per EF `IModel` plus mapping
type and returns the cached mapper. `Apply` validates the change set first and
only then executes the queued writes, so a deterministic mapping failure
leaves scalar state untouched. Present values come from the change set's
after-state; `desired` supplies nested objects for recursion.

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

<!-- illustrative: scoped nested mapping; collections stay diagnostic in this PoC -->
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
map.Collection(dto => dto.Lines, entity => entity.Lines); // diagnosed, see below
```

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

Unmapped, ambiguous, protected, and collection changed paths all fail with
`SparseEntityMappingException` carrying the dotted DTO path. Changed
collection paths fail with `CollectionNotSupported`: keyed synchronization
continues in issue #225, and this PoC diagnoses collections (including
whole-value sequences) instead of syncing them.

## PoC limitations

* Targets `net10.0` only.
* No collection synchronization: any changed collection path fails. Element
  configuration arrives with issue #225.
* No source-generated Apply bodies yet: conventional mappings are compiled
  expression plans validated against EF metadata and cached per `IModel`
  plus mapping type. `Configure` must be deterministic for its mapping type.
  Generator emission stays a follow-up; the runtime plan is the validated
  source in this PoC.
* Reference navigations only for `Object` and flattened paths; value-type
  and collection intermediates are rejected.
* Nested materialization needs a public parameterless constructor.
* Root presence transitions (whole-DTO null or missing) are outside `Apply`
  scope; pass a non-null desired DTO with member-level changes.
* No auto-load, no `SaveChanges`, no transactions, no retries, no Mapperly
  dependency, and no NativeAOT support.
* The Contracts project (DTOs plus generated change sets) references only
  `SparseFragments`; EF references live in the Server layer and the mapping
  package. The `EfCoreSample.Contracts` and `EfCoreSample.Server` projects
  prove the split builds and runs.
