using EfCoreSample.Contracts;
using EfCoreSample.Server;
using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>SQLite-backed proof that accepted change sets drive tracked entities.</summary>
public sealed class OrderMappingTests
{
    internal static OrderDto ReadOrderDto(OrderEntity entity) =>
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
        };

    private static OrderEntity SeedOrder(SampleDbContext context) =>
        context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "aBc",
                    Address = new AddressEntity { Street = "Old Street", City = "Osaka" },
                }
            )
            .Entity;

    [Test]
    public async Task ScalarNestedFlattenedAndCallbackApply()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        await context.SaveChangesAsync();
        counter.Reset();

        var addressBefore = entity.Address;
        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Name = "Bob";
        incoming.Code = "xYz";
        incoming.ShipCity = "Kyoto";
        incoming.Address!.Street = "New Street";
        incoming.Address.City = "Ignored City";

        var submitted = current.CreateChangeSet(incoming);
        var rebased = submitted.RebaseOnto(current);
        rebased.HasConflicts.ShouldBeFalse();
        var accepted = rebased.Rebased;
        var desired = accepted.ToPatch().ApplyTo(current);

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        mapper.Apply(context, entity, desired, accepted);

        entity.CustomerName.ShouldBe("Bob");
        entity.Code.ShouldBe("XYZ");
        entity.Address.ShouldNotBeNull();
        entity.Address.Street.ShouldBe("New Street");
        entity.Address.City.ShouldBe("Kyoto");
        ReferenceEquals(entity.Address, addressBefore).ShouldBeTrue();

        var entry = context.Entry(entity);
        entry.Property(order => order.CustomerName).IsModified.ShouldBeTrue();
        entry.Property(order => order.Code).IsModified.ShouldBeTrue();
        entry.Property(order => order.Notes).IsModified.ShouldBeFalse();
        entry.Property(order => order.Id).IsModified.ShouldBeFalse();
        entry.Property(order => order.RowVersion).IsModified.ShouldBeFalse();
        context
            .Entry(entity.Address!)
            .Property(address => address.Street)
            .IsModified.ShouldBeTrue();
        context.Entry(entity.Address!).Property(address => address.City).IsModified.ShouldBeTrue();

        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();
        counter.Commands.ShouldBeGreaterThan(0);

        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using var verify = new SampleDbContext(verifyOptions);
        var reloaded = verify
            .Orders.Include(order => order.Address)
            .Single(order => order.Id == entity.Id);
        reloaded.CustomerName.ShouldBe("Bob");
        reloaded.Code.ShouldBe("XYZ");
        reloaded.Notes.ShouldBe("keep");
        reloaded.Address.ShouldNotBeNull();
        reloaded.Address!.Street.ShouldBe("New Street");
        reloaded.Address.City.ShouldBe("Kyoto");
    }

    [Test]
    public async Task UnchangedMembersAreNeverWritten()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        await context.SaveChangesAsync();
        counter.Reset();

        var current = ReadOrderDto(entity);
        var accepted = current.CreateChangeSet(current);
        accepted.IsEmpty.ShouldBeTrue();
        var desired = accepted.ToPatch().ApplyTo(current);

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, desired, accepted);

        context
            .Entry(entity)
            .Properties.Where(static property => property.IsModified)
            .ShouldBeEmpty();
        context
            .Entry(entity.Address!)
            .Properties.Where(static property => property.IsModified)
            .ShouldBeEmpty();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task IgnoredChangesAreSkipped()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        await context.SaveChangesAsync();
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.DisplayTotal = "$12.00";

        var accepted = current.CreateChangeSet(incoming);
        accepted.IsEmpty.ShouldBeFalse();
        var desired = accepted.ToPatch().ApplyTo(current);

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, desired, accepted);

        context
            .Entry(entity)
            .Properties.Where(static property => property.IsModified)
            .ShouldBeEmpty();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DetachedEntityAttachesWithoutQuery()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        await context.SaveChangesAsync();
        context.Entry(entity).State = EntityState.Detached;
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Name = "Detached Bob";
        var accepted = current.CreateChangeSet(incoming);
        var desired = accepted.ToPatch().ApplyTo(current);

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, desired, accepted);

        context.Entry(entity).State.ShouldBe(EntityState.Modified);
        entity.CustomerName.ShouldBe("Detached Bob");
        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();

        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using var verify = new SampleDbContext(verifyOptions);
        verify.Orders.Single(order => order.Id == entity.Id).CustomerName.ShouldBe("Detached Bob");
    }

    [Test]
    public async Task MissingNestedAddressIsMaterialized()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = new OrderEntity
        {
            CustomerName = "Ann",
            Notes = "keep",
            Code = "ABC",
            Address = null,
        };
        context.Orders.Add(entity);
        await context.SaveChangesAsync();
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Address = new AddressDto { Street = "Built", City = "Nara" };
        incoming.ShipCity = "Nara";

        var accepted = current.CreateChangeSet(incoming);
        var desired = accepted.ToPatch().ApplyTo(current);

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, desired, accepted);

        entity.Address.ShouldNotBeNull();
        entity.Address!.Street.ShouldBe("Built");
        entity.Address.City.ShouldBe("Nara");
        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();

        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using var verify = new SampleDbContext(verifyOptions);
        var reloaded = verify
            .Orders.Include(order => order.Address)
            .Single(order => order.Id == entity.Id);
        reloaded.Address.ShouldNotBeNull();
        reloaded.Address!.Street.ShouldBe("Built");
    }
}
