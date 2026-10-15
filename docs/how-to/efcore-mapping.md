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

Use `IncludeFor` to load only the navigations touched by the incoming change set. Then apply the changes directly to the tracked entity and save:

<!-- illustrative: controller or endpoint apply flow -->
```csharp
// 1. Create the mapper instance from your DbContext model
var mapper = OrderMapper.Create(db.Model);

// 2. Load the tracked entity with only necessary navigations
var trackedOrder = await db.Orders
    .IncludeFor(mapper, incomingChanges)
    .SingleAsync(o => o.Id == orderId);

// 3. Optional: reconcile concurrent changes if needed
var current = mapper.ToDto(trackedOrder);
var rebase = incomingChanges.RebaseOnto(current);
if (rebase.HasConflicts)
{
    return Results.Conflict(rebase.Conflicts);
}

// 4. Apply changes directly to the tracked entity
mapper.Apply(db, trackedOrder, rebase.Rebased);

// 5. Persist to database
await db.SaveChangesAsync();
```

## Handle client-assigned temporary keys

When clients create new child collection items before database primary keys exist, use `ApplyWithResult` and `AssignTemporaryKeys` to correlate client IDs with newly generated database keys:

<!-- illustrative: temporary key assignment workflow -->
```csharp
var result = mapper.ApplyWithResult(db, trackedOrder, rebase.Rebased);
await db.SaveChangesAsync();

var authoritative = mapper.ToDto(trackedOrder);
mapper.AssignTemporaryKeys(db, result, current, authoritative);
```

For full mapper configuration options, custom write handlers, and metadata constraints, see [EF Core mapping reference](../reference/efcore-mapping.md).
