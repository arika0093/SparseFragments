using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Case 5 save lifecycle through production EF mapping plus the session contract (#266).</summary>
/// <remarks>
/// <para>
/// Each test captures the exact immutable submitted <c>ChangeSet</c> from a generated edit
/// session, applies it with production <c>ApplyWithResult</c>, saves, projects the authoritative
/// DTO, restores temporary identities with production <c>AssignTemporaryKeys</c>, and acknowledges
/// with <c>TryReconcile</c> while edits made after submit stay pending. No manual positional or
/// helper-based retargeting takes part.
/// </para>
/// <para>All temporary identities are fixed so the tests are deterministic.</para>
/// </remarks>
public sealed class Case5EfSessionLifecycleTests
{
    private static readonly Guid TempA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TempB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TempLate = new("44444444-4444-4444-4444-444444444444");

    private static OrderLineDto PendingLine(int quantity, Guid temp) =>
        new()
        {
            Quantity = quantity,
            UnitPrice = 10m,
            Subtotal = quantity * 10m,
            TemporaryId = temp,
        };

    private static OrderDto ReadOrderDto(OrderEntity entity) =>
        new()
        {
            Name = entity.CustomerName,
            Notes = entity.Notes,
            Code = entity.Code,
            ShipCity = entity.Address?.City ?? string.Empty,
            DisplayTotal = null,
            Address = entity.Address is null
                ? null
                : new AddressDto { Street = entity.Address.Street, City = entity.Address.City },
            Lines = entity
                .Lines.OrderBy(static line => line.SortOrder)
                .Select(static line => new OrderLineDto
                {
                    Id = line.Id,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Subtotal = line.Quantity * line.UnitPrice,
                })
                .ToList(),
        };

    private static OrderDto CloneOrder(OrderDto source) =>
        new()
        {
            Name = source.Name,
            Notes = source.Notes,
            Code = source.Code,
            ShipCity = source.ShipCity,
            DisplayTotal = source.DisplayTotal,
            Address = source.Address is null
                ? null
                : new AddressDto { Street = source.Address.Street, City = source.Address.City },
            Lines = source
                .Lines.Select(static line => new OrderLineDto
                {
                    Id = line.Id,
                    TemporaryId = line.TemporaryId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Subtotal = line.Subtotal,
                })
                .ToList(),
        };

    private static OrderEntity SeedOrder(SampleDbContext context)
    {
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old Street", City = "Osaka" },
                    Lines =
                    [
                        new OrderLineEntity
                        {
                            Quantity = 1,
                            UnitPrice = 10m,
                            SortOrder = 0,
                        },
                        new OrderLineEntity
                        {
                            Quantity = 2,
                            UnitPrice = 20m,
                            SortOrder = 1,
                        },
                    ],
                }
            )
            .Entity;
        context.SaveChanges();
        context.Entry(entity).Collection(static order => order.Lines).Load();
        return entity;
    }

    [Test]
    public async Task ProductionSaveReconcilesMultipleRowsReordersAndPostSubmitEdits()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        // Client edit session: one edit, two temporary additions, and a reorder.
        // The second addition carries a stale Subtotal; the server recomputes it on write.
        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        draft.Lines.Single(static line => line.Quantity == 1).Quantity = 10;
        draft.Lines.Add(PendingLine(9, TempA));
        var staleSubtotal = PendingLine(8, TempB);
        staleSubtotal.Subtotal = 0m;
        draft.Lines.Add(staleSubtotal);
        var first = draft.Lines[0];
        draft.Lines.RemoveAt(0);
        draft.Lines.Add(first);

        // The exact immutable submission captured before the awaited save.
        var submitted = session.CreateChangeSet();

        // Overlapping edits continue while the save is in flight.
        draft.Lines.Single(line => line.TemporaryId == TempA).Quantity = 99;
        draft.Lines.Add(PendingLine(7, TempLate));
        var moved = draft.Lines[0];
        draft.Lines.RemoveAt(0);
        draft.Lines.Insert(1, moved);

        // Server: apply the submitted transition only, then save explicitly.
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        counter.Reset();
        var applied = mapper.ApplyWithResult(context, entity, submitted);
        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();

        var authoritative = ReadOrderDto(entity);
        mapper.AssignTemporaryKeys(context, applied, before, authoritative);

        // Both submitted rows correlate by temporary identity, never by position.
        // The untouched row adopts the server-recomputed Subtotal.
        authoritative.Lines.Count.ShouldBe(4);
        var persistedA = authoritative.Lines.Single(line => line.TemporaryId == TempA);
        var persistedB = authoritative.Lines.Single(line => line.TemporaryId == TempB);
        persistedA.Id.ShouldNotBe(0);
        persistedB.Id.ShouldNotBe(0);
        persistedB.Subtotal.ShouldBe(80m);

        var reconciled = session.TryReconcile(submitted, authoritative, out var error);
        reconciled.ShouldBeTrue();
        error.ShouldBeNull();

        // Assigned keys landed on the submitted rows; the post-submit quantity replayed onto
        // the assigned identity while the untouched row adopted the server-recomputed value.
        var liveA = draft.Lines.Single(line => line.TemporaryId == TempA);
        liveA.Id.ShouldBe(persistedA.Id);
        liveA.Quantity.ShouldBe(99);
        var liveB = draft.Lines.Single(line => line.TemporaryId == TempB);
        liveB.Id.ShouldBe(persistedB.Id);
        liveB.Subtotal.ShouldBe(80m);

        // The late row stays pending under its own temporary identity.
        var late = draft.Lines.Single(line => line.TemporaryId == TempLate);
        late.Id.ShouldBe(0);
        var pending = session.CreateChangeSet();
        pending.IsChanged.ShouldBeTrue();
        pending.Lines.GetTemporaryChange(TempLate).IsAdded.ShouldBeTrue();
    }

    [Test]
    public async Task ProductionSaveRejectsFilteredRowWithoutPartialWriteback()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        draft.Lines.Add(PendingLine(9, TempA));
        draft.Lines.Add(PendingLine(8, TempB));
        var submitted = session.CreateChangeSet();

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var applied = mapper.ApplyWithResult(context, entity, submitted);
        await context.SaveChangesAsync();

        var authoritative = ReadOrderDto(entity);
        mapper.AssignTemporaryKeys(context, applied, before, authoritative);

        // The server filtered one submitted row: its temporary identity is absent downstream.
        var filtered = CloneOrder(authoritative);
        filtered.Lines.RemoveAll(line => line.TemporaryId == TempB);

        session.TryReconcile(submitted, filtered, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();

        // No partial key writeback: both rows stay unassigned and the submission stays pending.
        draft.Lines.Single(line => line.TemporaryId == TempA).Id.ShouldBe(0);
        draft.Lines.Single(line => line.TemporaryId == TempB).Id.ShouldBe(0);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task ProductionSaveRejectsRepeatedTemporaryIdentity()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        draft.Lines.Add(PendingLine(9, TempA));
        var submitted = session.CreateChangeSet();

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var applied = mapper.ApplyWithResult(context, entity, submitted);
        await context.SaveChangesAsync();

        var authoritative = ReadOrderDto(entity);
        mapper.AssignTemporaryKeys(context, applied, before, authoritative);

        // A duplicated temporary identity across two assigned rows is rejected, never split.
        var ambiguous = CloneOrder(authoritative);
        ambiguous.Lines.Add(
            new OrderLineDto
            {
                Id = 999,
                Quantity = 1,
                UnitPrice = 10m,
                Subtotal = 10m,
                TemporaryId = TempA,
            }
        );

        session.TryReconcile(submitted, ambiguous, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        draft.Lines.Single(line => line.TemporaryId == TempA).Id.ShouldBe(0);
    }

    [Test]
    public async Task StaleSubmissionIsRejectedAfterBaselineMovedOn()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        draft.Lines.Add(PendingLine(9, TempA));
        var submitted = session.CreateChangeSet();

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var applied = mapper.ApplyWithResult(context, entity, submitted);
        await context.SaveChangesAsync();

        var authoritative = ReadOrderDto(entity);
        mapper.AssignTemporaryKeys(context, applied, before, authoritative);

        // An overlapping acknowledgement moves the baseline past the submission.
        draft.Name = "intervening";
        session.AcceptChanges(session.CreateChangeSet());

        session.TryReconcile(submitted, authoritative, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
    }
}
