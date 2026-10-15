using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Soft-delete order planning: retention order, duplicates, and validation.</summary>
/// <remarks>
/// Exercises the public mapper API over growing retained populations: active
/// rows stay first, planned removals follow, then previously deleted rows
/// omitted from the DTO. Each tracked instance appears exactly once.
/// </remarks>
public sealed partial class SoftDeleteOrderPlanningTests
{
    private static async Task<SoftOrderEntity> SeedLines(
        KeyedMappingContext context,
        params int[] quantities
    )
    {
        var entity = context
            .SoftOrders.Add(
                new SoftOrderEntity
                {
                    Lines = quantities
                        .Select(static qty => new SoftLineEntity { Qty = qty })
                        .ToList(),
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        return entity;
    }

    private static SoftOrderDto ReadActive(SoftOrderEntity entity) =>
        new()
        {
            Lines = entity
                .Lines.Where(static line => !line.IsDeleted)
                .OrderBy(static line => line.Id)
                .Select(static line => new SoftLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
        };

    private static SparseEntityMapper<SoftOrderEntity, SoftOrderDto> Mapper(
        KeyedMappingContext context
    ) =>
        SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
            new KeyedCollectionPolicyTests.SoftOrderMapper(),
            context.Model
        );

    [Test]
    public async Task RemoveAllRetainsEveryRowFlaggedInRemovalOrder()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedLines(context, 1, 2, 3);
        var current = ReadActive(entity);
        var incoming = ReadActive(entity);
        incoming.Lines.Clear();

        Mapper(context).Apply(context, entity, current.CreateChangeSet(incoming));

        entity.Lines.Count.ShouldBe(3);
        entity.Lines.All(static line => line.IsDeleted).ShouldBeTrue();
        // Planned removals append in plan order with no duplicates.
        entity.Lines.Select(static line => line.Qty).ShouldBe([1, 2, 3]);
        entity.Lines.Distinct().Count().ShouldBe(3);
    }

    [Test]
    public async Task PartialRemoveKeepsActivesFirstThenRetained()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedLines(context, 1, 2, 3, 4);
        var current = ReadActive(entity);
        var incoming = ReadActive(entity);
        incoming.Lines.RemoveAll(static line => line.Qty == 2 || line.Qty == 4);

        Mapper(context).Apply(context, entity, current.CreateChangeSet(incoming));

        entity.Lines.Select(static line => line.Qty).ShouldBe([1, 3, 2, 4]);
        entity.Lines.Where(static line => !line.IsDeleted).Count().ShouldBe(2);
        entity.Lines.Distinct().Count().ShouldBe(4);
    }

    [Test]
    public async Task LargeHistoryRetainsPreviouslyDeletedSurvivorsOnce()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedLines(context, Enumerable.Range(1, 200).ToArray());
        // A large previously deleted population leaves the DTO projection.
        foreach (var line in entity.Lines.Where(static line => line.Qty <= 100))
            line.IsDeleted = true;
        await context.SaveChangesAsync();
        var trackedBefore = entity.Lines.ToDictionary(static line => line.Qty);
        var current = ReadActive(entity);
        current.Lines.Count.ShouldBe(100);
        var incoming = ReadActive(entity);
        // Remove half of the remaining actives through the change set.
        incoming.Lines.RemoveAll(static line => line.Qty % 2 == 0);

        Mapper(context).Apply(context, entity, current.CreateChangeSet(incoming));

        entity.Lines.Count.ShouldBe(200);
        // No duplicate retained entities and tracked instances preserved.
        entity.Lines.Distinct().Count().ShouldBe(200);
        foreach (var line in entity.Lines)
            ReferenceEquals(line, trackedBefore[line.Qty]).ShouldBeTrue();
        // Stable order: surviving actives, then planned removals, then the
        // previously deleted rows in tracked order.
        var actives = entity.Lines.Take(50).Select(static line => line.Qty).ToList();
        actives.ShouldBe(Enumerable.Range(101, 100).Where(static qty => qty % 2 == 1).ToList());
        var removals = entity.Lines.Skip(50).Take(50).Select(static line => line.Qty).ToList();
        removals.ShouldBe(Enumerable.Range(101, 100).Where(static qty => qty % 2 == 0).ToList());
        entity.Lines.Skip(100).All(static line => line.IsDeleted).ShouldBeTrue();
        entity
            .Lines.Skip(100)
            .Select(static line => line.Qty)
            .ShouldBe(Enumerable.Range(1, 100).ToList());
    }

    [Test]
    public async Task OmittedActiveChildFailsWithoutMutating()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedLines(context, 1, 2, 3);
        var full = ReadActive(entity);
        // Stale baseline: the DTO never saw active row 2, so no removal item
        // covers it and coverage validation must refuse.
        var stale = new SoftOrderDto
        {
            Lines = full.Lines.Where(static line => line.Qty != 2).ToList(),
        };
        var incoming = new SoftOrderDto { Lines = stale.Lines.ToList() };
        incoming.Lines[0] = new SoftLineDto { Id = incoming.Lines[0].Id, Qty = 10 };
        // A membership change forces coverage validation, which must spot
        // the uncovered active row.
        incoming.Lines.Add(new SoftLineDto { Qty = 4 });

        var failure = Should.Throw<SparseEntityMappingException>(() =>
            Mapper(context).Apply(context, entity, stale.CreateChangeSet(incoming))
        );
        failure.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);

        entity.Lines.Count.ShouldBe(3);
        entity.Lines.All(static line => !line.IsDeleted).ShouldBeTrue();
    }

    [Test]
    public async Task SoftDeleteRoundTripPersistsFlags()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = await SeedLines(context, 1, 2, 3);
        var current = ReadActive(entity);
        var incoming = ReadActive(entity);
        incoming.Lines.RemoveAll(static line => line.Qty == 2);

        Mapper(context).Apply(context, entity, current.CreateChangeSet(incoming));
        await context.SaveChangesAsync();

        using var verify = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var reloaded = verify
            .SoftOrders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id);
        reloaded.Lines.Count.ShouldBe(3);
        reloaded.Lines.Single(static line => line.Qty == 2).IsDeleted.ShouldBeTrue();
        reloaded.Lines.Where(static line => !line.IsDeleted).Count().ShouldBe(2);
    }
}
