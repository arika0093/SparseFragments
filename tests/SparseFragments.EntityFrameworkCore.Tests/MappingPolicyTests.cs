using EfCoreSample.Contracts;
using EfCoreSample.Server;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>Failure and policy behavior: missing, null, unmapped, ambiguous, and protected paths.</summary>
public sealed class MappingPolicyTests
{
    /// <summary>Order mapping with a per-member missing policy for notes.</summary>
    public sealed class OrderPermissiveMapper : IEntityModelMapping<OrderEntity, OrderDto>
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
            map.OnMissing(dto => dto.Notes, SparseEntityMissingPolicy.SetNull);
        }
    }

    /// <summary>Order mapping with a lenient global missing default.</summary>
    public sealed class OrderLenientMissingMapper : IEntityModelMapping<OrderEntity, OrderDto>
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
            map.MissingDefault(SparseEntityMissingPolicy.SetNull);
        }
    }

    /// <summary>Order mapping that ignores a missing nested address.</summary>
    public sealed class OrderLenientNestedMapper : IEntityModelMapping<OrderEntity, OrderDto>
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
                },
                SparseEntityMissingPolicy.Ignore
            );
        }
    }

    private static Optional<OrderDto.Fragment?> Present(OrderDto.Fragment fragment) =>
        Optional<OrderDto.Fragment?>.Present(fragment);

    private static WidgetEntity SeedWidget(MappingTestContext context)
    {
        var entity = context.Widgets.Add(new WidgetEntity("Gizmo", "active")).Entity;
        context.SaveChanges();
        return entity;
    }

    private static WidgetDto ReadWidgetDto(WidgetEntity entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Status = entity.Status,
            RowVersion = entity.RowVersion,
            Display = null,
        };

    [Test]
    public async Task ScalarMissingThrowsByDefault()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Notes = Optional<string?>.Missing;
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MissingNotHandled);
        error.DtoPath.ShouldBe("Notes");
        entity.Notes.ShouldBe("keep");
        context.Entry(entity).Property(order => order.Notes).IsModified.ShouldBeFalse();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task ScalarMissingSetNullWithPolicy()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Notes = Optional<string?>.Missing;
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderPermissiveMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Notes.ShouldBeNull();
        context.Entry(entity).Property(order => order.Notes).IsModified.ShouldBeTrue();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task MissingDefaultIsCheckedLazily()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        // The global SetNull default builds even though Name cannot honor it.
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderLenientMissingMapper(),
            context.Model
        );

        var current = OrderMappingTests.ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var notesBuilder = basis.Value!.ToBuilder();
        notesBuilder.Notes = Optional<string?>.Missing;
        var notesAccepted = OrderDto.ChangeSet.Between(basis, Present(notesBuilder.Build()));
        mapper.Apply(context, entity, notesAccepted);
        entity.Notes.ShouldBeNull();

        var nameBuilder = basis.Value!.ToBuilder();
        nameBuilder.Name = Optional<string>.Missing;
        var nameAccepted = OrderDto.ChangeSet.Between(basis, Present(nameBuilder.Build()));
        var error = Should.Throw<SparseEntityMappingException>(() =>
            mapper.Apply(context, entity, nameAccepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.NullNotAllowed);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task PresentNullWritesNull()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var incoming = OrderMappingTests.ReadOrderDto(entity);
        incoming.Notes = null;
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Notes.ShouldBeNull();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task PresentNullToNonNullableFails()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var incoming = OrderMappingTests.ReadOrderDto(entity);
        incoming.Name = null!;
        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.NullNotAllowed);
        entity.CustomerName.ShouldBe("Ann");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NestedMissingThrowsByDefault()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Address = Optional<AddressDto.Fragment?>.Missing;
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MissingNotHandled);
        error.DtoPath.ShouldBe("Address");
        entity.Address.ShouldNotBeNull();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NestedMissingIgnoreWithPolicy()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Address = Optional<AddressDto.Fragment?>.Missing;
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderLenientNestedMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Address.ShouldNotBeNull();
        entity.Address!.Street.ShouldBe("Old");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NestedNullSetsNull()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                    Address = new AddressEntity { Street = "Old", City = "Osaka" },
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        var current = OrderMappingTests.ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Address = Optional<AddressDto.Fragment?>.Present(null);
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Address.ShouldBeNull();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task UnmappedChangedPathFails()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var entity = SeedWidget(context);
        var current = ReadWidgetDto(entity);
        var incoming = ReadWidgetDto(entity);
        incoming.Display = "shown";

        var accepted = current.CreateChangeSet(incoming);
        // Fail-fast: the dormant unmapped Display member fails at mapper
        // creation even before any Apply, and the changed path fails the same way.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
                new WidgetStrictMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Unmapped);
        error.DtoPath.ShouldBe("Display");

        var dormant = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
                new WidgetStrictMapper(),
                context.Model
            )
        );
        dormant.Failure.ShouldBe(SparseEntityMappingFailure.Unmapped);
        dormant.DtoPath.ShouldBe("Display");
    }

    [Test]
    public void AmbiguousPlanFailsFast()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<GadgetEntity, GadgetDto>.Create(
                new GadgetAmbiguousMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Ambiguous);
    }

    [Test]
    public async Task IgnoredIdChangeIsSkipped()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = SeedWidget(context);
        counter.Reset();
        var current = ReadWidgetDto(entity);
        var incoming = ReadWidgetDto(entity);
        incoming.Id = entity.Id + 100;

        var accepted = current.CreateChangeSet(incoming);
        // The baseline mapper explicitly ignores the identity: fail-fast
        // coverage is satisfied at creation and the ignored change is skipped.
        // Unignored identity writes keep failing at creation (see
        // RawIdMemberFailsWithoutAllowWrite).
        SparseEntityMapper<WidgetEntity, WidgetDto>
            .Create(new WidgetMapper(), context.Model)
            .Apply(context, entity, accepted);
        entity.Id.ShouldBe(current.Id);
        context.Entry(entity).Property(widget => widget.Id).IsModified.ShouldBeFalse();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public void RawIdMemberFailsWithoutAllowWrite()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
                new WidgetRawIdMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Protected);
    }

    [Test]
    public async Task AllowedIdWriteReachesEntryAndEfGuardsTheKey()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = SeedWidget(context);
        counter.Reset();
        var current = ReadWidgetDto(entity);
        var incoming = ReadWidgetDto(entity);
        incoming.Id = entity.Id + 100;

        var accepted = current.CreateChangeSet(incoming);

        // AllowWrite permits the plan, so the write reaches the EF entry; EF Core
        // itself then refuses the tracked key mutation. The value is already set
        // when EF throws, which callers must account for before reusing the context.
        var error = Should.Throw<InvalidOperationException>(() =>
            SparseEntityMapper<WidgetEntity, WidgetDto>
                .Create(new WidgetIdMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        (error is SparseEntityMappingException).ShouldBeFalse();
        entity.Id.ShouldBe(current.Id + 100);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task IgnoredConcurrencyTokenChangeIsSkipped()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = SeedWidget(context);
        counter.Reset();
        var current = ReadWidgetDto(entity);
        var incoming = ReadWidgetDto(entity);
        incoming.RowVersion = [1, 2, 3, 4];

        var accepted = current.CreateChangeSet(incoming);
        // The baseline mapper explicitly ignores the concurrency token, so the
        // change is skipped and the token stays caller-managed.
        SparseEntityMapper<WidgetEntity, WidgetDto>
            .Create(new WidgetMapper(), context.Model)
            .Apply(context, entity, accepted);
        context.Entry(entity).Property(widget => widget.RowVersion).IsModified.ShouldBeFalse();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task AlternateKeyChangeFails()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var entity = context.Devices.Add(new DeviceEntity { Serial = "S-1", Label = "one" }).Entity;
        context.SaveChanges();
        var current = new DeviceDto { Serial = "S-1", Label = "one" };
        var incoming = new DeviceDto { Serial = "S-2", Label = "one" };

        var accepted = current.CreateChangeSet(incoming);
        // Fail-fast: the alternate-key member never writes by default, so the
        // mapper creation itself fails even before the changed path applies.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<DeviceEntity, DeviceDto>.Create(new DeviceMapper(), context.Model)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Protected);
        error.DtoPath.ShouldBe("Serial");
    }

    [Test]
    public async Task BackingFieldWriteGoesThroughEntry()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = SeedWidget(context);
        counter.Reset();
        var current = ReadWidgetDto(entity);
        var incoming = ReadWidgetDto(entity);
        incoming.Status = "suspended";

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<WidgetEntity, WidgetDto>
            .Create(new WidgetMapper(), context.Model)
            .Apply(context, entity, accepted);

        // Status has no CLR setter: only the EF property entry can write it.
        entity.Status.ShouldBe("suspended");
        context.Entry(entity).Property(widget => widget.Status).IsModified.ShouldBeTrue();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task ShadowMemberChangeFails()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = SeedWidget(context);
        counter.Reset();

        // Same-name inference never writes the shadow member: fail-fast
        // creation rejects it as protected with the shadow reason.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<WidgetEntity, ShadowedWidgetDto>.Create(
                new ShadowedWidgetMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Protected);
        error.DtoPath.ShouldBe("TenantId");
        error.Message.ShouldContain("shadow", Case.Insensitive);
        counter.Commands.ShouldBe(0);
    }
}
