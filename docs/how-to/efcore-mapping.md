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

## Reconcile the client session after saving

Choose the reconciliation method based on which values the client must adopt. `TryReconcile` acknowledges the submitted change set, advances the session baseline to the authoritative model, and retargets temporary identities in the live model. It does not replace the live model with authoritative property values, so use it when the client already has the submitted values and only needs identity correlation.

`TryReconcileAndApply` adopts the authoritative model and rebases edits made after the submitted snapshot onto it. Use it when persistence can normalize values or when editing remains enabled while a save is in flight. A planning, correlation, or rebase conflict returns `false` without changing the live model or retained baseline. These conflict guarantees do not make arbitrary setters, property notifications, or callbacks transactional if they throw during the final in-place write.

## Handle client-assigned temporary keys

When clients create new child collection items before database primary keys exist, use `ApplyWithResult` and `AssignTemporaryKeys` to correlate client IDs with newly generated database keys:

<!-- illustrative: temporary key assignment workflow -->
```csharp
var result = mapper.ApplyWithResult(db, trackedOrder, rebase.Rebased);
await db.SaveChangesAsync();

mapper.AssignTemporaryKeys(db, result, current, authoritative);
```

## Coordinate an optional save pipeline

`SparseEntitySavePipeline.ExecuteAsync` is an optional orchestration helper for consumers that want the same explicit phase ordering outside the playground. The consumer supplies preparation (including its revision check, read, rebase, and mapper application), the single persistence attempt, the authoritative readback (including temporary-key assignment), and client synchronization (including `TryReconcileAndApply`). The helper performs no query, persistence, transaction, rollback, or retry on its own, and it never repeats a custom callback.

The returned stage distinguishes preparation failure, a concurrency conflict, an unknown save outcome, a failed authoritative readback, and a failed client synchronization after persistence. Treat the last three as requiring recovery rather than resubmitting the same change set: the write may already exist, and newly generated keys may already have been assigned. The pipeline reports callback exceptions but does not undo arbitrary EF writes or client-side setter effects; define transaction and recovery policy in the application.

### Recover from cancellation without losing the persistence status

`ExecuteAsync` reports cancellation instead of throwing it. When a callback honors the token and raises `OperationCanceledException`, the result keeps that exception in `Error` and the stage keeps what is known about persistence:

- Preparation cancellation returns `PreparationFailed`. The save callback never ran, so nothing was persisted and the same change set can be retried with a fresh token.
- Save cancellation returns `SaveOutcomeUnknown`. The write may have committed before cancellation surfaced, so query persisted state first instead of resubmitting. This also covers a callback that saves successfully and observes cancellation afterwards.
- Readback cancellation returns `ReadBackFailed`. Persistence completed, so re-read the authoritative state and continue without resubmitting.
- Synchronization cancellation returns `ClientSynchronizationFailed`. Persistence and readback completed, so finish client synchronization (for example `TryReconcileAndApply`) without resubmitting.

The pipeline performs no cancellation check between stages and never retries a stage. A callback that ignores the token runs to completion, and an already-canceled token surfaces at the first callback that honors it, normally as `PreparationFailed`. Detect cancellation by testing `Error is OperationCanceledException`:

<!-- illustrative: cancellation handling excerpt; pipeline and DTO names are defined elsewhere -->
```csharp
var outcome = await SparseEntitySavePipeline.ExecuteAsync(
    prepare, save, readBack, synchronize, cancellationToken);
if (outcome.Error is OperationCanceledException)
{
    // The stage tells whether persistence was not attempted
    // (PreparationFailed), is unknown (SaveOutcomeUnknown), or completed
    // (ReadBackFailed, ClientSynchronizationFailed).
}
```

## Guard the SQLite write race with an application-managed token

SQLite cannot store-generate a SQL Server style rowversion, so declare an
application-managed token (for example a `Guid`) and advance it on every
write that belongs to the aggregate, including child-only line changes:

1. Mark the token with `IsConcurrencyToken()` and keep it read-only for
   the mapper: `Map.Ignore` for writes, `Map.Read` for the projection.
2. Assign a new value in `SaveChanges`/`SaveChangesAsync` for every added
   or modified aggregate root, and for the parent of every added, modified,
   or deleted child. A child-only write leaves the parent row untouched by
   change tracking, so bump the parent explicitly.
3. Carry the token outside the change set: return it on reads, accept it
   as `If-Match` on writes, and set it as the entry's `OriginalValue`
   before saving.
4. Catch `DbUpdateConcurrencyException` and answer 412 with refresh
   guidance. Keep semantic `RebaseOnto` conflicts as 409. Never resubmit
   the same change set blindly; re-read, rebase pending edits onto the new
   current state, and submit with the fresh token. Let cancellation
   propagate instead of mapping it to 409 or 412.

<!-- illustrative: token plumbing excerpt; context and DTO names are defined elsewhere -->
```csharp
db.Entry(order).Property(static item => item.Version).OriginalValue = clientVersion;
try
{
    await db.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateConcurrencyException)
{
    return Results.Problem("The order changed since it was read.", statusCode: 412);
}
```

For full mapper configuration options, custom write handlers, and metadata constraints, see [EF Core mapping reference](../reference/efcore-mapping.md).
