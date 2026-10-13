using Microsoft.EntityFrameworkCore;
using SparseFragments.EntityFrameworkCore.Mapping;
using SparseFragments.EntityFrameworkCore.Tests.Sample;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>Direct ChangeSet-to-entity regressions for the review findings behind #228.</summary>
/// <remarks>
/// Every test applies the accepted ChangeSet alone: no desired DTO is rebuilt
/// between Rebase and Apply. Preflight validation must leave the entity graph
/// and the ChangeTracker untouched, temporary identities stay scoped by
/// collection path, and order/removal evidence must be complete.
/// </remarks>
public sealed class DirectApplyRegressionTests
{
    private static Optional<OrderDto.Fragment?> Present(OrderDto.Fragment fragment) =>
        Optional<OrderDto.Fragment?>.Present(fragment);

    private static OrderDto ReadOrderDto(OrderEntity entity) =>
        KeyedCollectionMappingTests.ReadOrderDto(entity);

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
                        new OrderLineEntity
                        {
                            Quantity = 3,
                            UnitPrice = 30m,
                            SortOrder = 2,
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
    public async Task FlattenedPreflightLeavesGraphUntouched()
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
                    Address = null,
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        var trackedBefore = context.ChangeTracker.Entries().Count();
        counter.Reset();

        // ShipCity flattens through the null Address navigation while Notes goes
        // missing (Throw by default). Validation must fail before any write, so
        // the null navigation is never materialized during preflight.
        var current = ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Notes = Optional<string?>.Missing;
        builder.ShipCity = Optional<string>.Present("Kyoto");
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MissingNotHandled);
        entity.Address.ShouldBeNull();
        entity.Notes.ShouldBe("keep");
        context.ChangeTracker.Entries().Count().ShouldBe(trackedBefore);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DetachedRootAndNestedStayDetachedOnFailure()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);

        // Fully detached graph: validation must not attach the root or the
        // owned nested instance when a later member fails deterministically.
        var entity = new OrderEntity
        {
            CustomerName = "Ann",
            Notes = "keep",
            Code = "ABC",
            Address = new AddressEntity { Street = "Old Street", City = "Osaka" },
        };
        var current = ReadOrderDto(entity);
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        builder.Notes = Optional<string?>.Missing;
        var addressBuilder = builder.Address.Value!.ToBuilder();
        addressBuilder.Street = Optional<string>.Present("New Street");
        builder.Address = Optional<AddressDto.Fragment?>.Present(addressBuilder.Build());
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));
        counter.Reset();

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MissingNotHandled);
        context.ChangeTracker.Entries().ShouldBeEmpty();
        context.Entry(entity).State.ShouldBe(EntityState.Detached);
        context.Entry(entity.Address!).State.ShouldBe(EntityState.Detached);
        entity.Notes.ShouldBe("keep");
        entity.Address!.Street.ShouldBe("Old Street");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NullCollectionPreflightLeavesNavigationUntouched()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        counter.Reset();

        // Null out the loaded navigation but keep it marked loaded, then submit
        // a removal for a key the (now invisible) collection cannot resolve.
        // Planning must fail before the execution phase assigns a list.
        var lineId = entity.Lines[0].Id;
        entity.Lines = null!;
        context.Entry(entity).Collection(static order => order.Lines).IsLoaded = true;

        var current = new OrderDto
        {
            Lines =
            [
                new OrderLineDto
                {
                    Id = lineId,
                    Quantity = 1,
                    UnitPrice = 10m,
                },
            ],
        };
        var incoming = new OrderDto { Lines = [] };
        var accepted = current.CreateChangeSet(incoming);

        // A null but settable navigation validates against an empty preflight
        // stand-in: the removal resolves to no tracked identity without ever
        // assigning the navigation.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        context.Entry(entity).Collection(static order => order.Lines).CurrentValue.ShouldBeNull();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NullSettableCollectionAcceptsPureAddition()
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
                    Lines = null!,
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        context.Entry(entity).Collection(static order => order.Lines).IsLoaded = true;
        counter.Reset();

        // Pure additions never need existing members, so the execution phase
        // assigns the list that preflight refused to touch.
        var current = new OrderDto { Lines = [] };
        var incoming = new OrderDto
        {
            Lines =
            [
                new OrderLineDto
                {
                    Quantity = 1,
                    UnitPrice = 10m,
                    TemporaryId = Guid.NewGuid(),
                },
            ],
        };
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.ShouldNotBeNull();
        entity.Lines.Count.ShouldBe(1);
        entity.Lines[0].Quantity.ShouldBe(1);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task TemporaryIdsAreScopedByCollectionPath()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        counter.Reset();

        // The same temporary value reused under two collection paths resolves
        // independently; duplicates are only invalid within one scope.
        var shared = Guid.NewGuid();
        static DuoDto Read(DuoEntity owner) =>
            new()
            {
                Actives = owner
                    .Actives.Select(static line => new DuoLineDto
                    {
                        Id = line.Id,
                        Qty = line.Qty,
                        Note = line.Note,
                    })
                    .ToList(),
                Archived = owner
                    .Archived.Select(static line => new DuoLineDto
                    {
                        Id = line.Id,
                        Qty = line.Qty,
                        Note = line.Note,
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Actives.Add(new DuoLineDto { Qty = 1, TemporaryId = shared });
        incoming.Archived.Add(new DuoLineDto { Qty = 2, TemporaryId = shared });
        var accepted = current.CreateChangeSet(incoming);

        var result = SparseEntityMapper<DuoEntity, DuoDto>
            .Create(new KeyedCollectionMappingTests.DuoDivergentMapper(), context.Model)
            .ApplyWithResult(context, entity, accepted);

        var active = (DuoLineEntity)result.CreatedByPath["Actives"][shared];
        var archived = (DuoLineEntity)result.CreatedByPath["Archived"][shared];
        ReferenceEquals(active, archived).ShouldBeFalse();
        active.Qty.ShouldBe(1);
        archived.Qty.ShouldBe(2);
        // The cross-path lookup stays ambiguous by design; path lookups are exact.
        result.TryGetCreated<DuoLineEntity>(shared, out _).ShouldBeFalse();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task DuplicateTemporaryWithinScopeFailsWithoutMutation()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Duos.Add(new DuoEntity { Actives = [], Archived = [] }).Entity;
        await context.SaveChangesAsync();
        var trackedBefore = context.ChangeTracker.Entries().Count();
        counter.Reset();

        var shared = Guid.NewGuid();
        var current = new DuoDto { Actives = [], Archived = [] };
        var incoming = new DuoDto
        {
            Actives =
            [
                new DuoLineDto { Qty = 1, TemporaryId = shared },
                new DuoLineDto { Qty = 2, TemporaryId = shared },
            ],
            Archived = [],
        };

        // The core keyed transition itself rejects a duplicate temporary
        // identity within one collection scope at ChangeSet construction, so
        // the invalid state never reaches the mapper or the tracked graph.
        var error = Should.Throw<InvalidOperationException>(() =>
            current.CreateChangeSet(incoming)
        );
        error.Message.ShouldContain("temporary", Case.Insensitive);
        entity.Actives.ShouldBeEmpty();
        context.ChangeTracker.Entries().Count().ShouldBe(trackedBefore);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task SharedSentinelWithDistinctTemporariesCorrelates()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        counter.Reset();

        // Two unassigned children share the declared sentinel (0) with distinct
        // temporary identities; both persist with distinct database identities.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Lines.Add(
            new OrderLineDto
            {
                Quantity = 7,
                UnitPrice = 70m,
                TemporaryId = first,
            }
        );
        incoming.Lines.Add(
            new OrderLineDto
            {
                Quantity = 8,
                UnitPrice = 80m,
                TemporaryId = second,
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        var result = SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .ApplyWithResult(context, entity, accepted);
        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();

        result.TryGetCreated<OrderLineEntity>(first, out var firstEntity).ShouldBeTrue();
        result.TryGetCreated<OrderLineEntity>(second, out var secondEntity).ShouldBeTrue();
        firstEntity!.Id.ShouldNotBe(0);
        secondEntity!.Id.ShouldNotBe(0);
        firstEntity.Id.ShouldNotBe(secondEntity.Id);

        var authoritative = ReadOrderDto(entity);
        var createdIds = result
            .CreatedByPath["Lines"]
            .ToDictionary(
                static entry => ((OrderLineEntity)entry.Value).Id,
                static entry => entry.Key
            );
        foreach (var line in authoritative.Lines)
        {
            if (createdIds.TryGetValue(line.Id, out var temp))
            {
                line.TemporaryId = temp;
            }
        }

        authoritative.Lines.Single(static line => line.Quantity == 7).TemporaryId.ShouldBe(first);
        authoritative.Lines.Single(static line => line.Quantity == 8).TemporaryId.ShouldBe(second);
    }

    [Test]
    public async Task CompositeDefaultKeyIsAssignedWithoutSentinel()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .CompositeOrders.Add(
                new CompositeOrderEntity
                {
                    Title = "T",
                    Lines =
                    [
                        new CompositeLineEntity
                        {
                            TenantId = "t1",
                            LineNo = 1,
                            Qty = 1,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        counter.Reset();

        // CompositeLineDto declares no unassigned sentinel: the default-shaped
        // key ("", 0) is a valid assigned identity, never inferred unassigned.
        static CompositeOrderDto Read(CompositeOrderEntity order) =>
            new()
            {
                Title = order.Title,
                Lines = order
                    .Lines.Select(static line => new CompositeLineDto
                    {
                        TenantId = line.TenantId,
                        LineNo = line.LineNo,
                        Qty = line.Qty,
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Add(
            new CompositeLineDto
            {
                TenantId = "",
                LineNo = 0,
                Qty = 5,
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<CompositeOrderEntity, CompositeOrderDto>
            .Create(new CompositeOrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(2);
        entity.Lines.Single(static line => line.LineNo == 0).Qty.ShouldBe(5);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task FilteredIncludeReorderFailsSafely()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();

        // Full caller state with stable identities, built from a complete load.
        OrderDto current;
        OrderDto incoming;
        int orderId;
        using (var seed = TestDatabases.CreateSample(connection, counter))
        {
            var seeded = SeedOrder(seed);
            orderId = seeded.Id;
            current = ReadOrderDto(seeded);
            incoming = ReadOrderDto(seeded);
            var reordered = new List<OrderLineDto>
            {
                incoming.Lines[1],
                incoming.Lines[0],
                incoming.Lines[2],
            };
            incoming.Lines.Clear();
            incoming.Lines.AddRange(reordered);
        }

        // A filtered Include reports the navigation loaded while one row stays
        // invisible. The full caller order then references a child with no
        // tracked identity here and must fail instead of reordering around
        // unseen state. IsLoaded alone never proves completeness.
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Include(static order => order.Lines.Where(static line => line.Quantity > 1))
            .Single(order => order.Id == orderId);
        context.Entry(entity).Collection(static order => order.Lines).IsLoaded.ShouldBeTrue();
        var before = entity.Lines.Select(static line => line.Quantity).ToList();
        counter.Reset();

        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        entity.Lines.Select(static line => line.Quantity).ShouldBe(before);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task PartialLoadEditTouchesOnlyAddressedKeys()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();

        OrderDto current;
        OrderDto incoming;
        int orderId;
        using (var seed = TestDatabases.CreateSample(connection, counter))
        {
            var seeded = SeedOrder(seed);
            orderId = seeded.Id;
            current = ReadOrderDto(seeded);
            incoming = ReadOrderDto(seeded);
            incoming.Lines.Single(static line => line.Quantity == 2).Quantity = 5;
        }

        // Partial load without any order change: the edit addresses one
        // tracked key, so validation covers only addressed state and the apply
        // succeeds without complete membership evidence (contrast the reorder
        // case above, which requires it).
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = context
            .Orders.Include(static order => order.Lines.Where(static line => line.Quantity > 1))
            .Single(order => order.Id == orderId);
        counter.Reset();

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Single(static line => line.Quantity == 5).ShouldNotBeNull();
        entity.Lines.Single(static line => line.Quantity == 3).UnitPrice.ShouldBe(30m);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task MapperInstancesDoNotSharePlans()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var strict = context
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
        var lenient = context
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

        static OrderDto Current(OrderEntity order) =>
            new()
            {
                Name = order.CustomerName,
                Notes = order.Notes,
                Code = order.Code,
                ShipCity = order.Address?.City ?? string.Empty,
                Address = order.Address is null
                    ? null
                    : new AddressDto { Street = order.Address.Street, City = order.Address.City },
            };
        static global::SparseFragments.ISparseChangeSet MissingNotes(OrderDto current)
        {
            var basis = Present(OrderDto.Fragment.From(current));
            var builder = basis.Value!.ToBuilder();
            builder.Notes = Optional<string?>.Missing;
            return OrderDto.ChangeSet.Between(basis, Present(builder.Build()));
        }

        // Two instances of the same mapper CLR type carry different
        // constructor-injected missing policies: per-instance plans cannot leak
        // across instances (the former static type-keyed cache did exactly that).
        var strictMapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new FlagMapper(false),
            context.Model
        );
        var lenientMapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new FlagMapper(true),
            context.Model
        );

        var strictAccepted = MissingNotes(Current(strict));
        var strictError = Should.Throw<SparseEntityMappingException>(() =>
            strictMapper.Apply(context, strict, strictAccepted)
        );
        strictError.Failure.ShouldBe(SparseEntityMappingFailure.MissingNotHandled);

        var lenientAccepted = MissingNotes(Current(lenient));
        lenientMapper.Apply(context, lenient, lenientAccepted);
        lenient.Notes.ShouldBeNull();
        strict.Notes.ShouldBe("keep");
        counter.Commands.ShouldBe(0);
    }

    /// <summary>Same CLR type, constructor-injected missing policy.</summary>
    public sealed class FlagMapper : IEntityModelMapping<OrderEntity, OrderDto>
    {
        public FlagMapper(bool lenient)
        {
            Lenient = lenient;
        }

        /// <summary>Gets whether missing Notes clears instead of throwing.</summary>
        public bool Lenient { get; }

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
            map.Collection<OrderLineDto, OrderLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                        child.Ignore(line => line.TemporaryId);
                        child.Ignore(line => line.Subtotal);
                    });
                    lines.Delete();
                    lines.Order((entity, index) => entity.SortOrder = index);
                }
            );
            if (Lenient)
            {
                map.MissingDefault(SparseEntityMissingPolicy.SetNull);
            }
        }
    }

    [Test]
    public void ExplicitDuplicateRegistrationFails()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        // Two explicit entries for one path stay a duplicate: generated
        // defaults replace exactly once, explicit entries never twice.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<WidgetEntity, WidgetDto>.Create(
                new DuplicateWidgetMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Duplicate);
        error.DtoPath.ShouldBe("Name");
    }

    public sealed class DuplicateWidgetMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
    {
        public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
        {
            map.Ignore(dto => dto.Id);
            map.Ignore(dto => dto.RowVersion);
            map.Ignore(dto => dto.Display);
            map.Member(dto => dto.Name, entity => entity.Name);
            map.Member(dto => dto.Name, entity => entity.Name);
        }
    }

    [Test]
    public async Task NestedMapperCompanionResolves()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateMapping(connection, counter);
        var entity = context.Widgets.Add(new WidgetEntity("Gizmo", "active")).Entity;
        await context.SaveChangesAsync();
        counter.Reset();

        // Nested mapper classes resolve their namespace-level companion
        // through the same enclosing-name rule the emitter uses.
        var current = new WidgetDto
        {
            Id = entity.Id,
            Name = "Gizmo",
            Status = "active",
            RowVersion = entity.RowVersion,
        };
        var incoming = new WidgetDto
        {
            Id = entity.Id,
            Name = "Renamed",
            Status = "active",
            RowVersion = entity.RowVersion,
        };
        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<WidgetEntity, WidgetDto>
            .Create(new NestedWidgetMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Name.ShouldBe("Renamed");
        counter.Commands.ShouldBe(0);
    }

    [SparseFragmentMapper]
    public sealed class NestedWidgetMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
    {
        public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
        {
            map.Ignore(dto => dto.Id);
            map.Ignore(dto => dto.RowVersion);
            map.Ignore(dto => dto.Display);
        }
    }

    [Test]
    public void RequiresGeneratedDefaultsReflectsAttribute()
    {
        // Wiring proof complement: attributed mappers demand a companion at
        // creation (the suite passing is the end-to-end wiring fixture), while
        // plain mappings keep the scoped inference fallback.
        GeneratedDefaults.RequiresGeneratedDefaults(typeof(WidgetMapper)).ShouldBeTrue();
        GeneratedDefaults.RequiresGeneratedDefaults(typeof(DeviceMapper)).ShouldBeFalse();
    }

    [Test]
    public void GeneratedMarkerAloneDoesNotCoverCollections()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        // The companion emits a DeclareCollection marker for Lines, but the
        // marker carries no synchronization behavior: creation must fail even
        // though no change set touches the collection. Only a full
        // Collection() mapping or Ignore() covers the path.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>.Create(
                new UnconfiguredLinesMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.Unmapped);
        error.DtoPath.ShouldBe("Lines");
    }

    [SparseFragmentMapper]
    public sealed class UnconfiguredLinesMapper : IEntityModelMapping<OrderEntity, OrderDto>
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

    [Test]
    public void FailuresCarryMapperTypePathAndCategory()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<StudentEntity, StudentDto>.Create(new StudentMapper(), context.Model)
        );
        (error.Mapper ?? string.Empty).ShouldContain("StudentMapper");
        error.DtoPath.ShouldBe("Courses");
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionNotSupported);
        error.InnerException.ShouldBeOfType<SparseEntityMappingException>();
    }

    [Test]
    public async Task KeyedAdditionWithNestedReferencePersists()
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
                            ShipTo = new ParcelAddressEntity { City = "Osaka" },
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static parcel => parcel.Lines).LoadAsync();
        counter.Reset();

        // Wholesale creation of an added element must project the nested
        // snapshot through the DTO selector: a blind cast of the element
        // snapshot previously threw InvalidCastException here.
        static ParcelDto Read(ParcelEntity parcel) =>
            new()
            {
                Lines = parcel
                    .Lines.Select(static line => new ParcelLineDto
                    {
                        Id = line.Id,
                        Qty = line.Qty,
                        ShipTo = line.ShipTo is null
                            ? null
                            : new ParcelAddressDto { City = line.ShipTo.City },
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Single(static line => line.Qty == 1).ShipTo!.City = "Kyoto";
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 2,
                TemporaryId = Guid.NewGuid(),
                ShipTo = new ParcelAddressDto { City = "Nara" },
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<ParcelEntity, ParcelDto>
            .Create(new ParcelMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Single(static line => line.Qty == 1).ShipTo!.City.ShouldBe("Kyoto");
        var created = entity.Lines.Single(static line => line.Qty == 2);
        created.ShipTo.ShouldNotBeNull();
        created.ShipTo!.City.ShouldBe("Nara");
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        using var verify = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var reloaded = verify
            .Parcels.Include(static parcel => parcel.Lines)
                .ThenInclude(static line => line.ShipTo)
            .Single(parcel => parcel.Id == entity.Id);
        reloaded.Lines.Single(static line => line.Qty == 2).ShipTo!.City.ShouldBe("Nara");
    }

    public sealed class ParcelMapper : IEntityModelMapping<ParcelEntity, ParcelDto>
    {
        public void Configure(SparseEntityMapBuilder<ParcelEntity, ParcelDto> map)
        {
            map.Collection<ParcelLineDto, ParcelLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                        child.Ignore(line => line.TemporaryId);
                        child.Object(line => line.ShipTo, entity => entity.ShipTo, address => { });
                        child.Collection<ParcelTagDto, ParcelTagEntity>(
                            dto => dto.Tags,
                            entity => entity.Tags,
                            tags =>
                            {
                                tags.Element(tag =>
                                {
                                    tag.Ignore(tag => tag.Id);
                                    tag.Ignore(tag => tag.TemporaryId);
                                });
                                tags.Delete();
                            }
                        );
                    });
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public async Task WholesaleCreationReplacesFactoryChildren()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Parcels.Add(new ParcelEntity { Lines = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static parcel => parcel.Lines).LoadAsync();
        counter.Reset();

        // The factory pre-populates nested tags absent from the After
        // snapshot: wholesale creation must replace, never append, and an
        // empty snapshot must clear.
        static ParcelDto Read(ParcelEntity parcel) =>
            new()
            {
                Lines = parcel
                    .Lines.Select(static line => new ParcelLineDto
                    {
                        Id = line.Id,
                        Qty = line.Qty,
                        Tags = line
                            .Tags.Select(static tag => new ParcelTagDto { Label = tag.Label })
                            .ToList(),
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 4,
                TemporaryId = Guid.NewGuid(),
                Tags = [new ParcelTagDto { Label = "keep" }],
            }
        );
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 5,
                TemporaryId = Guid.NewGuid(),
                Tags = [],
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<ParcelEntity, ParcelDto>
            .Create(new ParcelJunkMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(2);
        entity
            .Lines.Single(static line => line.Qty == 4)
            .Tags.Select(static tag => tag.Label)
            .ShouldBe(["keep"]);
        entity.Lines.Single(static line => line.Qty == 5).Tags.ShouldBeEmpty();
        counter.Commands.ShouldBe(0);
    }

    /// <summary>Factory pre-populates nested tags to prove wholesale replacement.</summary>
    public sealed class ParcelJunkMapper : IEntityModelMapping<ParcelEntity, ParcelDto>
    {
        public void Configure(SparseEntityMapBuilder<ParcelEntity, ParcelDto> map)
        {
            map.Collection<ParcelLineDto, ParcelLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                        child.Ignore(line => line.TemporaryId);
                        child.Object(line => line.ShipTo, entity => entity.ShipTo, address => { });
                        child.Collection<ParcelTagDto, ParcelTagEntity>(
                            dto => dto.Tags,
                            entity => entity.Tags,
                            tags =>
                            {
                                tags.Element(tag =>
                                {
                                    tag.Ignore(tag => tag.Id);
                                    tag.Ignore(tag => tag.TemporaryId);
                                });
                                tags.Delete();
                            }
                        );
                    });
                    lines.Create(dto => new ParcelLineEntity
                    {
                        Qty = dto.Qty,
                        Tags =
                        [
                            new ParcelTagEntity { Label = $"junk-{dto.Qty}-1" },
                            new ParcelTagEntity { Label = $"junk-{dto.Qty}-2" },
                        ],
                    });
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public async Task ReorderWithEditAndWithoutOrderSetterFails()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var entity = SeedOrder(context);
        counter.Reset();

        // Reordering alongside an edit still cannot persist positions without
        // Order(): the in-memory list must not move either.
        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Lines.Single(static line => line.Quantity == 2).Quantity = 5;
        var reordered = new List<OrderLineDto>
        {
            incoming.Lines[1],
            incoming.Lines[0],
            incoming.Lines[2],
        };
        incoming.Lines.Clear();
        incoming.Lines.AddRange(reordered);
        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new KeyedCollectionMappingTests.OrderNoOrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionNotSupported);
        entity.Lines.Select(static line => line.Quantity).ShouldBe([1, 2, 3]);
        context
            .Entry(entity.Lines[1])
            .Properties.Where(static property => property.IsModified)
            .ShouldBeEmpty();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task StaleOmissionUnderDeleteFailsPreflight()
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
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        var trackedBefore = context.ChangeTracker.Entries().Count();
        counter.Reset();

        // Stale caller DTO: tracked line "b" is absent without a removal, so
        // the membership evidence is incomplete and the apply must fail before
        // the addition mutates anything.
        var current = new SkuOrderDto { Lines = [new SkuLineDto { Sku = "a", Qty = 1 }] };
        var incoming = new SkuOrderDto
        {
            Lines = [new SkuLineDto { Sku = "a", Qty = 1 }, new SkuLineDto { Sku = "c", Qty = 3 }],
        };
        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SkuOrderEntity, SkuOrderDto>
                .Create(new SkuOrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        entity.Lines.Count.ShouldBe(2);
        context.ChangeTracker.Entries().Count().ShouldBe(trackedBefore);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task SecondApplyOnSoftDeleteCollectionSucceeds()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .SoftOrders.Add(
                new SoftOrderEntity
                {
                    Lines = [new SoftLineEntity { Qty = 1 }, new SoftLineEntity { Qty = 2 }],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();

        static SoftOrderDto Read(SoftOrderEntity order) =>
            new()
            {
                Lines = order
                    .Lines.Where(static line => !line.IsDeleted)
                    .OrderBy(static line => line.Id)
                    .Select(static line => new SoftLineDto { Id = line.Id, Qty = line.Qty })
                    .ToList(),
            };
        var first = Read(entity);
        var second = Read(entity);
        second.Lines.RemoveAll(static line => line.Qty == 1);
        var removed = first.CreateChangeSet(second);
        SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
            .Create(new KeyedCollectionPolicyTests.SoftOrderMapper(), context.Model)
            .Apply(context, entity, removed);
        await context.SaveChangesAsync();
        counter.Reset();

        // The flagged row stays tracked but leaves the DTO projection. The
        // next addition must retain it instead of failing on incomplete order
        // evidence.
        var current = Read(entity);
        current.Lines.Count.ShouldBe(1);
        var incoming = Read(entity);
        // Assigned key (no sentinel hit, no temporary needed): the flagged row
        // stays invisible to the DTO while the addition resolves by key.
        incoming.Lines.Add(new SoftLineDto { Id = 100, Qty = 3 });
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
            .Create(new KeyedCollectionPolicyTests.SoftOrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(3);
        entity.Lines.Single(static line => line.Qty == 1).IsDeleted.ShouldBeTrue();
        entity.Lines.Single(static line => line.Qty == 3).IsDeleted.ShouldBeFalse();
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task LateMissingDefaultPropagatesToChildScope()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateSample(connection, counter);
        var strict = context
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
        var lenient = context
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

        static global::SparseFragments.ISparseChangeSet MissingStreet(OrderDto current)
        {
            var basis = Present(OrderDto.Fragment.From(current));
            var builder = basis.Value!.ToBuilder();
            var addressBuilder = builder.Address.Value!.ToBuilder();
            addressBuilder.Street = Optional<string>.Missing;
            builder.Address = Optional<AddressDto.Fragment?>.Present(addressBuilder.Build());
            return OrderDto.ChangeSet.Between(basis, Present(builder.Build()));
        }

        // FlagMapper declares Object(Address) before MissingDefault: the root
        // default still governs the child scope (resolved at plan build).
        // Street is non-nullable, so SetNull surfaces as NullNotAllowed while
        // Throw surfaces as MissingNotHandled.
        var strictError = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new FlagMapper(false), context.Model)
                .Apply(context, strict, MissingStreet(Current(strict)))
        );
        strictError.Failure.ShouldBe(SparseEntityMappingFailure.MissingNotHandled);
        strictError.DtoPath.ShouldBe("Address.Street");

        var lenientError = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new FlagMapper(true), context.Model)
                .Apply(context, lenient, MissingStreet(Current(lenient)))
        );
        lenientError.Failure.ShouldBe(SparseEntityMappingFailure.NullNotAllowed);
        lenientError.DtoPath.ShouldBe("Address.Street");
        counter.Commands.ShouldBe(0);

        static OrderDto Current(OrderEntity order) =>
            new()
            {
                Name = order.CustomerName,
                Notes = order.Notes,
                Code = order.Code,
                ShipCity = order.Address?.City ?? string.Empty,
                Address = order.Address is null
                    ? null
                    : new AddressDto { Street = order.Address.Street, City = order.Address.City },
            };
    }

    [Test]
    public async Task ExplicitChildDefaultWinsOverRoot()
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

        var current = new OrderDto
        {
            Name = entity.CustomerName,
            Notes = entity.Notes,
            Code = entity.Code,
            ShipCity = entity.Address!.City,
            Address = new AddressDto { Street = "Old", City = "Osaka" },
        };
        var basis = Present(OrderDto.Fragment.From(current));
        var builder = basis.Value!.ToBuilder();
        var addressBuilder = builder.Address.Value!.ToBuilder();
        addressBuilder.Street = Optional<string>.Missing;
        builder.Address = Optional<AddressDto.Fragment?>.Present(addressBuilder.Build());
        var accepted = OrderDto.ChangeSet.Between(basis, Present(builder.Build()));

        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new ExplicitChildMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Address!.Street.ShouldBe("Old");
        counter.Commands.ShouldBe(0);
    }

    public sealed class ExplicitChildMapper : IEntityModelMapping<OrderEntity, OrderDto>
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
            map.Ignore(dto => dto.Lines);
            map.Object(
                dto => dto.Address,
                entity => entity.Address,
                child =>
                {
                    child.Ignore(address => address.City);
                    child.MissingDefault(SparseEntityMissingPolicy.Ignore);
                }
            );
        }
    }

    [Test]
    public async Task StaleActiveOmissionUnderSoftDeleteStillFails()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .SoftOrders.Add(
                new SoftOrderEntity
                {
                    Lines = [new SoftLineEntity { Qty = 1 }, new SoftLineEntity { Qty = 2 }],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        var trackedBefore = context.ChangeTracker.Entries().Count();
        counter.Reset();

        // The predicate proves nothing here: the omitted row was never
        // flagged, so a stale DTO omitting an active child fails even though
        // previously deleted rows would be tolerated.
        static SoftOrderDto Read(SoftOrderEntity order) =>
            new()
            {
                Lines = order
                    .Lines.Where(static line => !line.IsDeleted)
                    .OrderBy(static line => line.Id)
                    .Select(static line => new SoftLineDto { Id = line.Id, Qty = line.Qty })
                    .ToList(),
            };
        var current = Read(entity);
        current.Lines.RemoveAll(static line => line.Qty == 2);
        var incoming = Read(entity);
        incoming.Lines.RemoveAll(static line => line.Qty == 2);
        incoming.Lines.Add(new SoftLineDto { Id = 100, Qty = 3 });
        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
                .Create(new KeyedCollectionPolicyTests.SoftOrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        entity.Lines.Count.ShouldBe(2);
        context.ChangeTracker.Entries().Count().ShouldBe(trackedBefore);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NestedTemporaryIdsAreScopedByParentIdentity()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context.Parcels.Add(new ParcelEntity { Lines = [] }).Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static parcel => parcel.Lines).LoadAsync();
        counter.Reset();

        // The same temporary value under two new siblings' nested collections
        // resolves independently once scopes qualify with the parent identity;
        // without qualification the second record falsely conflicts.
        var firstLine = Guid.NewGuid();
        var secondLine = Guid.NewGuid();
        var sharedTag = Guid.NewGuid();
        static ParcelDto Read(ParcelEntity parcel) =>
            new()
            {
                Lines = parcel
                    .Lines.Select(static line => new ParcelLineDto { Id = line.Id, Qty = line.Qty })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 1,
                TemporaryId = firstLine,
                Tags = [new ParcelTagDto { TemporaryId = sharedTag, Label = "one" }],
            }
        );
        incoming.Lines.Add(
            new ParcelLineDto
            {
                Qty = 2,
                TemporaryId = secondLine,
                Tags = [new ParcelTagDto { TemporaryId = sharedTag, Label = "two" }],
            }
        );
        var accepted = current.CreateChangeSet(incoming);

        var result = SparseEntityMapper<ParcelEntity, ParcelDto>
            .Create(new ParcelJunkMapper(), context.Model)
            .ApplyWithResult(context, entity, accepted);

        result.TryGetCreated<ParcelLineEntity>(firstLine, out _).ShouldBeTrue();
        result.TryGetCreated<ParcelLineEntity>(secondLine, out _).ShouldBeTrue();
        var tagScopes = result
            .CreatedByPath.Where(entry => entry.Key.EndsWith(".Tags", StringComparison.Ordinal))
            .ToList();
        tagScopes.Count.ShouldBe(2);
        tagScopes.SelectMany(static scope => scope.Value.Keys).ShouldBe([sharedTag, sharedTag]);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NullObservableCollectionMaterializesDeclaredType()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .ObservableOrders.Add(new ObservableOrderEntity { Lines = null! })
            .Entity;
        await context.SaveChangesAsync();
        context.Entry(entity).Collection(static order => order.Lines).IsLoaded = true;
        counter.Reset();

        var current = new ObservableOrderDto { Lines = [] };
        var incoming = new ObservableOrderDto { Lines = [new SkuLineDto { Sku = "a", Qty = 1 }] };
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<ObservableOrderEntity, ObservableOrderDto>
            .Create(new ObservableOrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.ShouldNotBeNull();
        entity.Lines.ShouldBeOfType<System.Collections.ObjectModel.ObservableCollection<SkuLineEntity>>();
        entity.Lines.Count.ShouldBe(1);
        entity.Lines[0].Sku.ShouldBe("a");
        counter.Commands.ShouldBe(0);
    }

    public sealed class ObservableOrderMapper
        : IEntityModelMapping<ObservableOrderEntity, ObservableOrderDto>
    {
        public void Configure(SparseEntityMapBuilder<ObservableOrderEntity, ObservableOrderDto> map)
        {
            map.Collection<SkuLineDto, SkuLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.AllowWrite(line => line.Sku);
                    });
                    lines.Key(dto => dto.Sku, entity => entity.Sku);
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public void RigidCollectionWithoutConstructorFailsAtBuild()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<RigidOrderEntity, RigidOrderDto>.Create(
                new RigidOrderMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MetadataMismatch);
        error.DtoPath.ShouldBe("Lines");
        error.Message.ShouldContain("parameterless constructor");
    }

    public sealed class RigidOrderMapper : IEntityModelMapping<RigidOrderEntity, RigidOrderDto>
    {
        public void Configure(SparseEntityMapBuilder<RigidOrderEntity, RigidOrderDto> map)
        {
            map.Collection<SkuLineDto, SkuLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.AllowWrite(line => line.Sku);
                    });
                    lines.Key(dto => dto.Sku, entity => entity.Sku);
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public void SkipNavigationFailsExplicitlyAtBuild()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        // Skip navigations live outside FindNavigation, so resolution checks
        // them first and rejects many-to-many members with guidance instead of
        // a missing-navigation report.
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<StudentEntity, StudentDto>.Create(new StudentMapper(), context.Model)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionNotSupported);
        error.DtoPath.ShouldBe("Courses");
        error.Message.ShouldContain("skip", Case.Insensitive);
    }

    public sealed class StudentMapper : IEntityModelMapping<StudentEntity, StudentDto>
    {
        public void Configure(SparseEntityMapBuilder<StudentEntity, StudentDto> map)
        {
            map.Collection<CourseRefDto, CourseEntity>(
                dto => dto.Courses,
                entity => entity.Courses,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(course => course.Id);
                    });
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public async Task DetachAllowedOnOptionalCompositeForeignKey()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .Batches.Add(
                new BatchEntity
                {
                    Tenant = "t1",
                    BatchNo = 7,
                    Lines =
                    [
                        new BatchLineEntity
                        {
                            Tenant = "t1",
                            BatchNo = 7,
                            Qty = 1,
                        },
                        new BatchLineEntity
                        {
                            Tenant = "t1",
                            BatchNo = 7,
                            Qty = 2,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static batch => batch.Lines).LoadAsync();
        counter.Reset();

        // The composite FK is optional even though BatchNo is non-nullable:
        // requiredness comes from IForeignKey.IsRequired, never from
        // per-property nullability inference. Detach must plan-build here.
        static BatchDto Read(BatchEntity batch) =>
            new()
            {
                Lines = batch
                    .Lines.Select(static line => new BatchLineDto { Id = line.Id, Qty = line.Qty })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.RemoveAll(static line => line.Qty == 1);
        var accepted = current.CreateChangeSet(incoming);

        SparseEntityMapper<BatchEntity, BatchDto>
            .Create(new BatchMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(1);
        entity.Lines.Single().Qty.ShouldBe(2);
        counter.Commands.ShouldBe(0);
    }

    public sealed class BatchMapper : IEntityModelMapping<BatchEntity, BatchDto>
    {
        public void Configure(SparseEntityMapBuilder<BatchEntity, BatchDto> map)
        {
            map.Collection<BatchLineDto, BatchLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                        child.Ignore(line => line.TemporaryId);
                    });
                    lines.Detach();
                }
            );
        }
    }
}
