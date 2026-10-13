using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Verifies IncludeFor query paths from accepted change sets.</summary>
public sealed class IncludeForTests
{
    [Test]
    public async Task ScalarChangeAddsNoReferenceOrCollectionInclude()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "ABC",
                    Address = new AddressEntity { City = "Osaka" },
                    Lines = [new OrderLineEntity { Quantity = 1, UnitPrice = 2m }],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var current = new OrderDto { Name = "Ann" };
        var incoming = new OrderDto { Name = "Bob" };
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        counter.Reset();

        var query = context
            .Orders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var sql = query.ToQueryString();

        sql.ShouldNotContain("OrderLineEntity");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public void UntypedChangeSetFromAnotherDtoIsRejected()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        ISparseChangeSet unrelated = new AddressDto { City = "Kyoto" }.CreateChangeSet(
            new AddressDto()
        );

        var error = Should.Throw<ArgumentException>(() =>
            context.Orders.IncludeFor(mapper, unrelated)
        );

        error.ParamName.ShouldBe("changes");
    }

    [Test]
    public async Task FlattenedReferenceChangeIncludesItsNavigation()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<ReqNoteContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        using (var seed = new ReqNoteContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Orders.Add(
                new ReqNoteOrderEntity
                {
                    CustomerName = "Ann",
                    Note = new ReqNoteEntity { Text = "keep" },
                }
            );
            await seed.SaveChangesAsync();
        }

        using var context = new ReqNoteContext(options);
        var tracked = await context.Orders.SingleAsync();
        var current = new ReqNoteFlatDto { Notes = "keep" };
        var incoming = new ReqNoteFlatDto { Notes = "updated" };
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<ReqNoteOrderEntity, ReqNoteFlatDto>.Create(
            new ReqNoteFlatMapper(),
            context.Model
        );
        counter.Reset();

        var query = context
            .Orders.Where(order => order.Id == tracked.Id)
            .IncludeFor(mapper, changes);
        var sql = query.ToQueryString();
        var entity = await query.SingleAsync();

        sql.ShouldContain("ReqNoteEntity");
        context.Entry(entity).Reference(order => order.Note).IsLoaded.ShouldBeTrue();
        counter.Reset();
        mapper.Apply(context, entity, changes);
        entity.Note!.Text.ShouldBe("updated");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task MembershipChangeIncludesTheCompleteCollection()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "ABC",
                    Lines =
                    [
                        new OrderLineEntity { Quantity = 1, UnitPrice = 2m },
                        new OrderLineEntity { Quantity = 2, UnitPrice = 3m },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var current = ReadOrder(entity);
        var incoming = ReadOrder(entity);
        incoming.Lines.Add(
            new OrderLineDto
            {
                Quantity = 3,
                UnitPrice = 4m,
                TemporaryId = Guid.NewGuid(),
            }
        );
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        counter.Reset();

        var query = context
            .Orders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var sql = query.ToQueryString();
        var loaded = await query.SingleAsync();

        sql.ShouldContain("OrderLineEntity");
        context.Entry(loaded).Collection(order => order.Lines).IsLoaded.ShouldBeTrue();
        loaded.Lines.Count.ShouldBe(2);
        counter.Reset();
        mapper.Apply(context, loaded, changes);
        loaded.Lines.Count.ShouldBe(3);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task EditOnlyChangeUsesFilteredCollectionInclude()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Code = "ABC",
                    Lines =
                    [
                        new OrderLineEntity { Quantity = 1, UnitPrice = 2m },
                        new OrderLineEntity { Quantity = 2, UnitPrice = 3m },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var current = ReadOrder(entity);
        var incoming = ReadOrder(entity);
        incoming.Lines.Single(line => line.Quantity == 2).Quantity = 5;
        var editedId = current.Lines.Single(line => line.Quantity == 2).Id;
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );

        using var fresh = TestDatabases.CreateSample(connection, counter);
        counter.Reset();
        var query = fresh.Orders.Where(order => order.Id == entity.Id).IncludeFor(mapper, changes);
        var sql = query.ToQueryString();
        var loaded = await query.SingleAsync();

        sql.ShouldContain(editedId.ToString());
        fresh.Entry(loaded).Collection(order => order.Lines).IsLoaded.ShouldBeTrue();
        loaded.Lines.Count.ShouldBe(1);
        loaded.Lines[0].Id.ShouldBe(editedId);
        counter.Reset();
        mapper.Apply(fresh, loaded, changes);
        loaded.Lines[0].Quantity.ShouldBe(5);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task CompositeKeyFallsBackToUnfilteredInclude()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .CompositeOrders.Add(
                new CompositeOrderEntity
                {
                    Title = "batch",
                    Lines =
                    [
                        new CompositeLineEntity
                        {
                            TenantId = "a",
                            LineNo = 1,
                            Qty = 1,
                        },
                        new CompositeLineEntity
                        {
                            TenantId = "a",
                            LineNo = 2,
                            Qty = 2,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var current = new CompositeOrderDto
        {
            Lines =
            [
                new CompositeLineDto
                {
                    TenantId = "a",
                    LineNo = 1,
                    Qty = 1,
                },
                new CompositeLineDto
                {
                    TenantId = "a",
                    LineNo = 2,
                    Qty = 2,
                },
            ],
        };
        var incoming = new CompositeOrderDto
        {
            Lines =
            [
                new CompositeLineDto
                {
                    TenantId = "a",
                    LineNo = 1,
                    Qty = 1,
                },
                new CompositeLineDto
                {
                    TenantId = "a",
                    LineNo = 2,
                    Qty = 5,
                },
            ],
        };
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<CompositeOrderEntity, CompositeOrderDto>.Create(
            new CompositeOrderMapper(),
            context.Model
        );
        counter.Reset();

        var query = context
            .CompositeOrders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var loaded = await query.SingleAsync();

        loaded.Lines.Count.ShouldBe(2);
        counter.Reset();
        mapper.Apply(context, loaded, changes);
        loaded.Lines.Single(line => line.LineNo == 2).Qty.ShouldBe(5);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task ExplicitScalarEntityKeyFiltersCollectionInclude()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .SkuOrders.Add(
                new SkuOrderEntity
                {
                    Lines =
                    [
                        new SkuLineEntity { Sku = "a", Qty = 1 },
                        new SkuLineEntity { Sku = "b", Qty = 2 },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var current = new SkuOrderDto
        {
            Lines = [new SkuLineDto { Sku = "a", Qty = 1 }, new SkuLineDto { Sku = "b", Qty = 2 }],
        };
        var incoming = new SkuOrderDto
        {
            Lines = [new SkuLineDto { Sku = "a", Qty = 1 }, new SkuLineDto { Sku = "b", Qty = 5 }],
        };
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<SkuOrderEntity, SkuOrderDto>.Create(
            new SkuOrderMapper(),
            context.Model
        );
        using var fresh = TestDatabases.CreateKeyed(connection, counter);
        counter.Reset();

        var query = fresh
            .SkuOrders.Where(order => order.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var loaded = await query.SingleAsync();

        loaded.Lines.Count.ShouldBe(1);
        loaded.Lines[0].Sku.ShouldBe("b");
        counter.Reset();
        mapper.Apply(fresh, loaded, changes);
        loaded.Lines[0].Qty.ShouldBe(5);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NestedKeyedChangesBuildThenIncludePaths()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .Parcels.Add(
                new ParcelEntity
                {
                    Lines =
                    [
                        new ParcelLineEntity
                        {
                            Qty = 1,
                            Tags = [new ParcelTagEntity { Label = "old" }],
                        },
                        new ParcelLineEntity
                        {
                            Qty = 2,
                            Tags = [new ParcelTagEntity { Label = "other" }],
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var current = ReadParcel(entity);
        var incoming = ReadParcel(entity);
        incoming.Lines[0].Tags[0].Label = "new";
        var editedLineId = entity.Lines[0].Id;
        var editedTagId = entity.Lines[0].Tags[0].Id;
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<ParcelEntity, ParcelDto>.Create(
            new DirectApplyRegressionTests.ParcelMapper(),
            context.Model
        );
        using var fresh = TestDatabases.CreateKeyed(connection, counter);
        counter.Reset();

        var query = fresh
            .Parcels.Where(parcel => parcel.Id == entity.Id)
            .IncludeFor(mapper, changes);
        var sql = query.ToQueryString();
        var loaded = await query.SingleAsync();

        sql.ShouldContain("ParcelLineEntity");
        sql.ShouldContain("ParcelTagEntity");
        loaded.Lines.Count.ShouldBe(1);
        loaded.Lines[0].Id.ShouldBe(editedLineId);
        loaded.Lines[0].Tags.Count.ShouldBe(1);
        loaded.Lines[0].Tags[0].Id.ShouldBe(editedTagId);
        counter.Reset();
        mapper.Apply(fresh, loaded, changes);
        loaded.Lines[0].Tags[0].Label.ShouldBe("new");
        counter.Commands.ShouldBe(0);
    }

    private static OrderDto ReadOrder(OrderEntity entity) =>
        new()
        {
            Name = entity.CustomerName,
            Code = entity.Code,
            ShipCity = entity.Address?.City ?? string.Empty,
            Lines = entity
                .Lines.Select(line => new OrderLineDto
                {
                    Id = line.Id,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                })
                .ToList(),
        };

    private static ParcelDto ReadParcel(ParcelEntity entity) =>
        new()
        {
            Lines = entity
                .Lines.Select(line => new ParcelLineDto
                {
                    Id = line.Id,
                    Qty = line.Qty,
                    Tags = line
                        .Tags.Select(tag => new ParcelTagDto { Id = tag.Id, Label = tag.Label })
                        .ToList(),
                })
                .ToList(),
        };
}
