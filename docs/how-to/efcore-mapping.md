# EF Core Mapping

# Apply ChangeSets to EF Core entities

This guide shows how to map and apply incoming DTO `ChangeSet` modifications directly to tracked Entity Framework Core entities.

Only modified properties are written to the database. Untouched EF properties remain unmodified.

## Define a fragment mapper

Create a partial class annotated with `[SparseFragmentMapper<TEntity, TDto>]`. Matching same-name scalar properties map automatically. Configure overrides, renames, and child collections in `Configure()`:

<!-- illustrative: basic mapper declaration -->
```csharp
using SparseTypes.EntityFrameworkCore;

[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class OrderMapper
{
    private void Configure()
    {
        // Renamed properties
        Map.Member(dto => dto.CustomerName, entity => entity.Name);

        // Value conversions
        Map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code?.ToUpperInvariant()
        );

        // Suppress unpersisted fields
        Map.Ignore(dto => dto.CalculatedTotal);

        // Keyed collection mapping
        Map.Collection(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child => child.Ignore(line => line.Subtotal));
                lines.Delete();
                lines.Order(entity => entity.SortOrder);
            }
        );
    }
}
```

If your entity and DTO share identical member names and types, you can omit `Configure()` entirely:

<!-- illustrative: empty defaults-only mapper -->
```csharp
[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class DefaultsOnlyMapper
{
}
```

## Load navigations and apply changes

Use `IncludeFor` to load the navigations needed to apply the incoming change set. Some edit-only collection changes produce a filtered Include containing only the matching rows. That tracked navigation is suitable for applying the change set, but it is not a complete DTO projection.

When rebasing or returning the saved state, query the full `Projection` separately. Do not use `ToDto` on an entity whose collection was loaded through a filtered Include; omitted rows would be absent from that DTO.

<!-- illustrative: controller or endpoint apply flow -->
```csharp
// 1. Create the mapper instance from your DbContext model
var mapper = OrderMapper.Create(db.Model);

// 2. Read the complete current DTO for conflict detection
var current = await db.Orders
    .AsNoTracking()
    .Where(order => order.Id == orderId)
    .Select(mapper.Projection)
    .SingleAsync();
var rebase = incomingChanges.RebaseOnto(current);
if (rebase.HasConflicts)
{
    return Results.Conflict(rebase.Conflicts);
}

// 3. Load only the navigations required to apply the accepted changes
var trackedOrder = await db.Orders
    .IncludeFor(mapper, rebase.Rebased)
    .SingleAsync(order => order.Id == orderId);

// 4. Apply changes directly to the tracked entity
mapper.Apply(db, trackedOrder, rebase.Rebased);

// 5. Persist to database
await db.SaveChangesAsync();

// 6. Read the complete authoritative DTO for the response
var authoritative = await db.Orders.AsNoTracking()
    .Where(order => order.Id == orderId)
    .Select(mapper.Projection)
    .SingleAsync();
```

The same full projection is needed after saving a collection edit. This query is the canonical response read:

<!-- sample: efcore-authoritative-reload -->
```csharp
var authoritative = await db
    .Orders.AsNoTracking()
    .Where(order => order.Id == orderId)
    .Select(mapper.Projection)
    .SingleAsync();
```
<!-- /sample -->

## Handle client-assigned temporary keys

When clients create new child collection items before database primary keys exist, use `ApplyWithResult` and `AssignTemporaryKeys` to correlate client IDs with newly generated database keys:

<!-- illustrative: temporary key assignment workflow -->
```csharp
var result = mapper.ApplyWithResult(db, trackedOrder, rebase.Rebased);
await db.SaveChangesAsync();

mapper.AssignTemporaryKeys(db, result, current, authoritative);
```

For full mapper configuration options, custom write handlers, and metadata constraints, see [EF Core mapping reference](../reference/efcore-mapping.md).
