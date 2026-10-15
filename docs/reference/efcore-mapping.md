# EF Core Mapping Reference

Technical reference for `SparseTypes.EntityFrameworkCore`, covering mapper configuration APIs, plan lifecycle, metadata handling, and execution constraints.

## Configuration APIs

The mapper builder (`Map`) is available inside the synchronous `Configure` method of a `[SparseFragmentMapper<TEntity, TDto>]` class.

<!-- illustrative: configuration builder API overview -->
```csharp
[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class OrderMapper
{
    private void Configure()
    {
        // Property mappings
        Map.Member(dto => dto.Name, entity => entity.CustomerName);
        Map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        Map.Member(dto => dto.Code, entity => entity.Code, code => code?.ToUpperInvariant());

        // Custom read projection for non-invertible writes
        Map.Read(dto => dto.Code, entity => entity.Code);

        // Custom write handler
        Map.Write(dto => dto.AuditNote, (entity, after) => entity.AuditTrail.Add(after.Value!));

        // Member suppression
        Map.Ignore(dto => dto.DisplayTotal);

        // Missing value policies
        Map.OnMissing(dto => dto.Notes, SparseEntityMissingPolicy.SetNull);
        Map.MissingDefault(SparseEntityMissingPolicy.Ignore);

        // Protected member opt-in
        Map.AllowWrite(dto => dto.LegacyId);

        // Keyed collection mapping
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child => child.Ignore(line => line.Subtotal));
                lines.Key(dto => dto.Key, entity => ValueTuple.Create(entity.TenantId, entity.LineNo));
                lines.UnassignedKey(0);
                lines.Delete(); // Options: Detach(), SoftDelete(predicate), Remove(callback)
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );

        // Scoped nested object mapping
        Map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child => child.Ignore(a => a.City),
            SparseEntityMissingPolicy.Throw
        );
    }
}
```

### Member mapping methods

| Method | Description |
|---|---|
| `Map.Member(dto, entity)` | Maps a DTO property to an entity property (renamed or flattened). |
| `Map.Member(dto, entity, converter)` | Applies a value converter during mapping. The converter runs during validation. |
| `Map.Read(dto, entity)` | Registers an explicit projection for non-invertible conversions. |
| `Map.Write(dto, handler)` | Executes a custom write callback receiving the presence-aware `Optional<T>` after-state. |
| `Map.Ignore(dto)` | Suppresses mapping for a DTO property. |
| `Map.OnMissing(dto, policy)` | Sets the behavior when a DTO property changes to `Missing` (`Throw`, `Ignore`, `SetDefault`, `SetNull`). |
| `Map.AllowWrite(dto)` | Permits writing to protected EF properties (primary keys, generated values, shadow properties). |

### Keyed collections

| Method | Description |
|---|---|
| `lines.Key(dtoKey, entityKey)` | Defines composite, computed, or custom key projections between DTO and entity. |
| `lines.Element(builder)` | Configures path-scoped element mappings and overrides. |
| `lines.UnassignedKey(sentinel)` | Declares an explicit unassigned key sentinel (e.g., `0`). |
| `lines.Delete()` | Marks removed entities as deleted (`EntityState.Deleted`). |
| `lines.Detach()` | Unlinks removed entities from parent (optional foreign keys only). |
| `lines.SoftDelete(callback)` | Invokes a callback to mark records deleted without removing the row. |
| `lines.Order(callback)` | Enables explicit collection order synchronization. |

## Execution lifecycle

1. **Plan construction**: `OrderMapper.Create(db.Model)` compiles source-generated conventional defaults, executes `Configure()`, and validates the plan against `IModel`.
2. **Navigation loading**: `query.IncludeFor(mapper, changes)` inspects the accepted `ChangeSet` and includes only navigations required by changed paths.
3. **Preflight validation**: `Apply` or `ApplyWithResult` validates all operations before modifying any entity state. A failure leaves `ChangeTracker` untouched.
4. **Direct mutation**: Only modified properties execute setters. Unchanged EF properties remain untouched.
5. **Temporary key correlation**: `ApplyWithResult` correlates client-generated `[SparseTemporaryKey]` values to new entities. Call `mapper.AssignTemporaryKeys(db, result, baseline, authoritative)` after `SaveChangesAsync()`.

## Missing versus null semantics

* `Missing`: Never converted to null automatically. The default policy throws `MissingNotHandled`. Configure `OnMissing` or `MissingDefault` to change this to `Ignore`, `SetDefault`, or `SetNull`.
* `Present(null)`: Explicitly writes `null` to the entity property. Throws `NullNotAllowed` if the entity property is non-nullable.

## EF metadata integration

Mappings run against EF property entries:
* Identity, generated, concurrency-token, and shadow properties are protected by default.
* Unmapped changed paths throw `SparseEntityMappingException` with the mapper type, target path, and recommended fix.
* Compile-time diagnostics: `SPF033` flags incompatible default mappings; `SPF034` flags invalid mapper declarations; `SPF035` flags unsupported member shapes.

## Supported / unsupported matrix

| Feature | Support |
|---|---|
| Scalar edits, renamed/flattened properties, conversions | Supported |
| Explicit null vs changed-to-Missing policies | Supported |
| Nested models via child change sets | Supported |
| Owned navigations, shadow properties (with `AllowWrite`) | Supported |
| Keyed add/edit/remove with Delete/Detach/SoftDelete | Supported |
| Composite and computed keys via `Key()` | Supported |
| Path-scoped `[SparseTemporaryKey]` correlation | Supported |
| Entity-to-DTO `Projection` and `ToDto` | Supported |
| Query navigation pruning via `IncludeFor` | Supported |
| Primitive collections (whole value replacement) | Supported |
| Unkeyed collections, skip navigations (many-to-many) | Unsupported (fail explicitly) |
| Filtered includes for collections with additions/removals | Unsupported (unfiltered required) |
| Rebase, authorization, `SaveChanges` | Application-owned |
| Optimistic concurrency tokens | Application-owned (`IsConcurrencyToken` plus a per-write advance covering child writes) |
