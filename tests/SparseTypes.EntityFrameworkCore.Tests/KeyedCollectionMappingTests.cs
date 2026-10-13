using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>SQLite-backed keyed add/edit/remove over the Contracts+Server order sample.</summary>
public sealed partial class KeyedCollectionMappingTests
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

    private static SampleDbContext CreateSample(SqliteConnection connection, QueryCounter counter)
    {
        return TestDatabases.CreateSample(connection, counter);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void CollectionApplyPreservesChangeDetectionSettingOnSuccessAndFailure(
        bool detectChanges
    )
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = CreateSample(connection, new QueryCounter());
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);
        var after = ReadOrderDto(entity);
        after.Lines[0].Quantity++;
        var changes = before.CreateChangeSet(after);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        entity.Lines[1].Quantity = 88;
        mapper.Apply(context, entity, changes);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        context
            .Entry(entity.Lines[1])
            .Property(line => line.Quantity)
            .IsModified.ShouldBe(detectChanges);
        context.Entry(entity).Collection(order => order.Lines).IsLoaded = false;
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        Should.Throw<SparseEntityMappingException>(() => mapper.Apply(context, entity, changes));
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
    }

    [Test]
    public async Task KeyedAddEditRemovePreservesTrackedIdentity()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = CreateSample(connection, counter);
        var entity = SeedOrder(context);
        var lineA = entity.Lines[0];
        var lineB = entity.Lines[1];
        var lineC = entity.Lines[2];
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Lines[1].Quantity = 5;
        incoming.Lines[1].Subtotal = 999m;
        incoming.Lines.RemoveAt(2);
        var temporary = Guid.NewGuid();
        incoming.Lines.Add(
            new OrderLineDto
            {
                Quantity = 7,
                UnitPrice = 70m,
                Subtotal = 490m,
                TemporaryId = temporary,
            }
        );

        var submitted = current.CreateChangeSet(incoming);
        var rebased = submitted.RebaseOnto(current);
        rebased.HasConflicts.ShouldBeFalse();
        var accepted = rebased.Rebased;

        var result = SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .ApplyWithResult(context, entity, accepted);

        entity.Lines.Count.ShouldBe(3);
        ReferenceEquals(entity.Lines[0], lineA).ShouldBeTrue();
        ReferenceEquals(entity.Lines[1], lineB).ShouldBeTrue();
        entity.Lines[1].Quantity.ShouldBe(5);
        entity.Lines[1].UnitPrice.ShouldBe(20m);
        entity.Lines.ShouldNotContain(lineC);
        context.Entry(lineC).State.ShouldBe(EntityState.Deleted);
        var created = entity.Lines[2];
        created.Quantity.ShouldBe(7);
        created.UnitPrice.ShouldBe(70m);
        context.Entry(created).State.ShouldBe(EntityState.Added);

        var lineBEntry = context.Entry(lineB);
        lineBEntry.Property(static line => line.Quantity).IsModified.ShouldBeTrue();
        lineBEntry.Property(static line => line.UnitPrice).IsModified.ShouldBeFalse();
        context
            .Entry(lineA)
            .Properties.Where(static property => property.IsModified)
            .ShouldBeEmpty();

        result.TryGetCreated<OrderLineEntity>(temporary, out var correlated).ShouldBeTrue();
        ReferenceEquals(correlated, created).ShouldBeTrue();
        ReferenceEquals(result.CreatedByPath, result.CreatedByPath).ShouldBeTrue();
        var createdByPath = result.GetCreated<OrderLineEntity>("Lines");
        createdByPath.Count.ShouldBe(1);
        ReferenceEquals(createdByPath[temporary], created).ShouldBeTrue();
        var expressionPath = result.GetCreated<OrderLineEntity, OrderDto>(dto => dto.Lines);
        ReferenceEquals(expressionPath[temporary], created).ShouldBeTrue();
        result
            .TryGetCreated<OrderLineEntity>("Lines", temporary, out var pathCorrelated)
            .ShouldBeTrue();
        ReferenceEquals(pathCorrelated, created).ShouldBeTrue();
        result.GetCreated("missing").ShouldBeEmpty();
        result
            .CreatedByPath["Lines"]
            .ShouldBeOfType<System.Collections.ObjectModel.ReadOnlyDictionary<Guid, object>>();
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();

        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using var verify = new SampleDbContext(verifyOptions);
        var reloaded = verify
            .Orders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id);
        reloaded.Lines.Count.ShouldBe(3);
        reloaded.Lines.Single(line => line.Quantity == 5).UnitPrice.ShouldBe(20m);
        reloaded.Lines.Any(static line => line.Quantity == 3).ShouldBeFalse();
        var persisted = reloaded.Lines.Single(line => line.Quantity == 7);
        persisted.Id.ShouldNotBe(0);
        persisted.OrderId.ShouldBe(entity.Id);
        reloaded
            .Lines.OrderBy(static line => line.SortOrder)
            .Select(static line => line.Quantity)
            .ShouldBe([1, 5, 7]);
    }

    [Test]
    public async Task CompositeComputedKeyMapsToCompositePrimaryKey()
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
                        new CompositeLineEntity
                        {
                            TenantId = "t1",
                            LineNo = 2,
                            Qty = 2,
                        },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        var first = entity.Lines.Single(line => line.LineNo == 1);
        counter.Reset();

        CompositeOrderDto Read(CompositeOrderEntity order) =>
            new()
            {
                Title = order.Title,
                Lines = order
                    .Lines.OrderBy(static line => line.LineNo)
                    .Select(static line => new CompositeLineDto
                    {
                        TenantId = line.TenantId,
                        LineNo = line.LineNo,
                        Qty = line.Qty,
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Single(line => line.LineNo == 1).Qty = 11;
        incoming.Lines.RemoveAll(static line => line.LineNo == 2);
        incoming.Lines.Add(
            new CompositeLineDto
            {
                TenantId = "t2",
                LineNo = 3,
                Qty = 30,
            }
        );

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<CompositeOrderEntity, CompositeOrderDto>
            .Create(new CompositeOrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(2);
        ReferenceEquals(entity.Lines.Single(line => line.LineNo == 1), first).ShouldBeTrue();
        first.Qty.ShouldBe(11);
        entity.Lines.Any(static line => line.LineNo == 2).ShouldBeFalse();
        entity.Lines.Single(static line => line.LineNo == 3).TenantId.ShouldBe("t2");
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        using var verify = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var reloaded = verify
            .CompositeOrders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id);
        reloaded.Lines.Count.ShouldBe(2);
        reloaded.Lines.Single(line => line.LineNo == 1).Qty.ShouldBe(11);
    }

    [Test]
    public async Task TemporaryKeyWritebackUsesKeyProjectionAndPreservesBaselineIdentity()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .TemporaryCompositeOrders.Add(
                new TemporaryCompositeOrderEntity
                {
                    Lines =
                    [
                        new TemporaryCompositeLineEntity
                        {
                            TenantId = "tenant",
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

        static TemporaryCompositeOrderDto Read(TemporaryCompositeOrderEntity order) =>
            new()
            {
                Lines = order
                    .Lines.Select(static line => new TemporaryCompositeLineDto
                    {
                        TenantId = line.TenantId,
                        LineNo = line.LineNo,
                        Qty = line.Qty,
                        Key = line.TenantId + ":" + line.LineNo,
                    })
                    .ToList(),
            };

        var baseline = Read(entity);
        var baselineTemporary = Guid.NewGuid();
        baseline.Lines[0].TemporaryId = baselineTemporary;
        var incoming = Read(entity);
        incoming.Lines[0].TemporaryId = baselineTemporary;
        var createdTemporary = Guid.NewGuid();
        incoming.Lines.Add(
            new TemporaryCompositeLineDto
            {
                TenantId = "tenant",
                LineNo = 2,
                Qty = 2,
                TemporaryId = createdTemporary,
            }
        );
        var accepted = baseline.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<
            TemporaryCompositeOrderEntity,
            TemporaryCompositeOrderDto
        >.Create(new TemporaryCompositeOrderMapper(), context.Model);

        var result = mapper.ApplyWithResult(context, entity, accepted);
        await context.SaveChangesAsync();
        var authoritative = Read(entity);
        counter.Reset();
        mapper.AssignTemporaryKeys(context, result, baseline, authoritative);

        authoritative
            .Lines.Single(static line => line.LineNo == 1)
            .TemporaryId.ShouldBe(baselineTemporary);
        authoritative
            .Lines.Single(static line => line.LineNo == 2)
            .TemporaryId.ShouldBe(createdTemporary);
    }

    [Test]
    public async Task NaturalDtoKeyMapsToAlternateEntityKey()
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
        var lineA = entity.Lines.Single(static line => line.Sku == "a");
        var surrogateA = lineA.Id;
        counter.Reset();

        SkuOrderDto Read(SkuOrderEntity order) =>
            new()
            {
                Lines = order
                    .Lines.OrderBy(static line => line.Sku)
                    .Select(static line => new SkuLineDto { Sku = line.Sku, Qty = line.Qty })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Lines.Single(static line => line.Sku == "a").Qty = 10;
        incoming.Lines.RemoveAll(static line => line.Sku == "b");
        incoming.Lines.Add(new SkuLineDto { Sku = "c", Qty = 3 });

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<SkuOrderEntity, SkuOrderDto>
            .Create(new SkuOrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        ReferenceEquals(entity.Lines.Single(static line => line.Sku == "a"), lineA).ShouldBeTrue();
        lineA.Id.ShouldBe(surrogateA);
        lineA.Qty.ShouldBe(10);
        entity.Lines.Any(static line => line.Sku == "b").ShouldBeFalse();
        entity.Lines.Single(static line => line.Sku == "c").Qty.ShouldBe(3);
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        using var verify = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var reloaded = verify
            .SkuOrders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id);
        reloaded.Lines.Count.ShouldBe(2);
        reloaded.Lines.Single(static line => line.Sku == "a").Qty.ShouldBe(10);
        reloaded.Lines.Single(static line => line.Sku == "a").Id.ShouldBe(surrogateA);
    }

    [Test]
    public async Task SameChildTypePairDivergesAcrossTwoPaths()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .Duos.Add(
                new DuoEntity
                {
                    Actives =
                    [
                        new DuoLineEntity { Qty = 1, Note = "keep" },
                        new DuoLineEntity { Qty = 2, Note = "drop" },
                    ],
                    Archived = [new DuoLineEntity { Qty = 9, Note = "old" }],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Actives).LoadAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        var active = entity.Actives.Single(static line => line.Qty == 1);
        var archived = entity.Archived.Single();
        counter.Reset();

        DuoDto Read(DuoEntity owner) =>
            new()
            {
                Actives = owner
                    .Actives.OrderBy(static line => line.Id)
                    .Select(static line => new DuoLineDto
                    {
                        Id = line.Id,
                        Qty = line.Qty,
                        Note = line.Note,
                    })
                    .ToList(),
                Archived = owner
                    .Archived.OrderBy(static line => line.Id)
                    .Select(static line => new DuoLineDto
                    {
                        Id = line.Id,
                        Qty = line.Qty,
                        Note = line.Note,
                    })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Actives.Single(static line => line.Qty == 1).Note = "ignored-edit";
        incoming.Actives.Single(static line => line.Qty == 1).Qty = 10;
        incoming.Actives.RemoveAll(static line => line.Qty == 2);
        var activeTemp = Guid.NewGuid();
        incoming.Actives.Add(
            new DuoLineDto
            {
                Qty = 3,
                Note = "new-active",
                TemporaryId = activeTemp,
            }
        );
        incoming.Archived.Single().Note = "new-note";
        var archivedTemp = Guid.NewGuid();
        incoming.Archived.Add(
            new DuoLineDto
            {
                Qty = 8,
                Note = "new-archived",
                TemporaryId = archivedTemp,
            }
        );

        var accepted = current.CreateChangeSet(incoming);
        var result = SparseEntityMapper<DuoEntity, DuoDto>
            .Create(new DuoDivergentMapper(), context.Model)
            .ApplyWithResult(context, entity, accepted);

        ReferenceEquals(entity.Actives.Single(static line => line.Qty == 10), active)
            .ShouldBeTrue();
        active.Note.ShouldBe("keep");
        entity.Actives.Any(static line => line.Qty == 2).ShouldBeFalse();
        ReferenceEquals(entity.Archived.Single(static line => line.Qty == 9), archived)
            .ShouldBeTrue();
        archived.Note.ShouldBe("new-note");
        result.CreatedByPath["Actives"].Count.ShouldBe(1);
        result.CreatedByPath["Archived"].Count.ShouldBe(1);
        result.TryGetCreated<DuoLineEntity>(activeTemp, out _).ShouldBeTrue();
        result.TryGetCreated<DuoLineEntity>(archivedTemp, out _).ShouldBeTrue();
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        using var verify = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var reloaded = verify
            .Duos.Include(static owner => owner.Actives)
            .Include(static owner => owner.Archived)
            .Single(owner => owner.Id == entity.Id);
        reloaded.Actives.Count.ShouldBe(2);
        reloaded.Actives.Single(static line => line.Qty == 10).Note.ShouldBe("keep");
        reloaded.Archived.Count.ShouldBe(2);
        reloaded.Archived.Single(static line => line.Qty == 9).Note.ShouldBe("new-note");
    }

    /// <summary>Same element pair, distinct path-scoped child mappings and removal policies.</summary>
    [SparseFragmentMapper<DuoEntity, DuoDto>]
    public sealed partial class DuoDivergentMapper
    {
        private void Configure()
        {
            Map.Collection<DuoLineDto, DuoLineEntity>(
                dto => dto.Actives,
                entity => entity.Actives,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                        child.Ignore(line => line.TemporaryId);
                        child.Ignore(line => line.Note);
                    });
                    lines.Delete();
                }
            );
            Map.Collection<DuoLineDto, DuoLineEntity>(
                dto => dto.Archived,
                entity => entity.Archived,
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

    [Test]
    public async Task PureReorderPersistsSortOrder()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = CreateSample(connection, counter);
        var entity = SeedOrder(context);
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        var reordered = new List<OrderLineDto>
        {
            incoming.Lines[2],
            incoming.Lines[0],
            incoming.Lines[1],
        };
        incoming.Lines = reordered;

        var accepted = current.CreateChangeSet(incoming);
        accepted.IsChanged.ShouldBeTrue();
        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Select(static line => line.Quantity).ShouldBe([3, 1, 2]);
        entity.Lines.Select(static line => line.SortOrder).ShouldBe([0, 1, 2]);
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using var verify = new SampleDbContext(verifyOptions);
        verify
            .Orders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id)
            .Lines.OrderBy(static line => line.SortOrder)
            .Select(static line => line.Quantity)
            .ShouldBe([3, 1, 2]);
    }

    [Test]
    public async Task ReorderWithMembershipPersistsBoth()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = CreateSample(connection, counter);
        var entity = SeedOrder(context);
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Lines.RemoveAll(static line => line.Quantity == 1);
        var moved = incoming.Lines.Single(static line => line.Quantity == 3);
        incoming.Lines.Remove(moved);
        incoming.Lines.Insert(0, moved);
        incoming.Lines.Add(
            new OrderLineDto
            {
                Quantity = 9,
                UnitPrice = 90m,
                TemporaryId = Guid.NewGuid(),
            }
        );

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<OrderEntity, OrderDto>
            .Create(new OrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Select(static line => line.Quantity).ShouldBe([3, 2, 9]);
        entity.Lines.Select(static line => line.SortOrder).ShouldBe([0, 1, 2]);

        await context.SaveChangesAsync();
        var verifyOptions = new DbContextOptionsBuilder<SampleDbContext>()
            .UseSqlite(connection)
            .Options;
        using var verify = new SampleDbContext(verifyOptions);
        verify
            .Orders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id)
            .Lines.OrderBy(static line => line.SortOrder)
            .Select(static line => line.Quantity)
            .ShouldBe([3, 2, 9]);
    }

    /// <summary>Order mapping without an order setter: membership syncs, pure reorder fails.</summary>
    [SparseFragmentMapper<OrderEntity, OrderDto>]
    public sealed partial class OrderNoOrderMapper
    {
        private void Configure()
        {
            Map.Member(dto => dto.Name, entity => entity.CustomerName);
            Map.Member(
                dto => dto.Code,
                entity => entity.Code,
                code => code == null ? null : code.ToUpperInvariant()
            );
            Map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
            Map.Ignore(dto => dto.DisplayTotal);
            Map.Object(
                dto => dto.Address,
                entity => entity.Address,
                child =>
                {
                    child.Ignore(address => address.City);
                }
            );
            Map.Collection<OrderLineDto, OrderLineEntity>(
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
                }
            );
        }
    }

    [Test]
    public async Task PureReorderWithoutOrderSetterFails()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = CreateSample(connection, counter);
        var entity = SeedOrder(context);
        counter.Reset();

        var current = ReadOrderDto(entity);
        var incoming = ReadOrderDto(entity);
        incoming.Lines = [incoming.Lines[2], incoming.Lines[0], incoming.Lines[1]];

        var accepted = current.CreateChangeSet(incoming);
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<OrderEntity, OrderDto>
                .Create(new OrderNoOrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionNotSupported);
        error.DtoPath.ShouldBe("Lines");
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task EndToEndRebaseApplySaveChangesReconcile()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = CreateSample(connection, counter);
        var entity = SeedOrder(context);
        var baseline = ReadOrderDto(entity);

        // Client edit session over the baseline: one edit plus one temporary addition.
        var draft = CloneOrder(baseline);
        var session = draft.CreateEditSession();
        draft.Lines.Single(static line => line.Quantity == 2).Quantity = 20;
        var temporary = Guid.NewGuid();
        draft.Lines.Add(
            new OrderLineDto
            {
                Quantity = 9,
                UnitPrice = 90m,
                Subtotal = 810m,
                TemporaryId = temporary,
            }
        );
        var submitted = session.CreateChangeSet();

        // Server: rebase onto the authoritative read, then map onto the tracked entity.
        var rebased = submitted.RebaseOnto(baseline);
        rebased.HasConflicts.ShouldBeFalse();
        var accepted = rebased.Rebased;
        counter.Reset();

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var applied = mapper.ApplyWithResult(context, entity, accepted);
        counter.Commands.ShouldBe(0);
        await context.SaveChangesAsync();

        var authoritative = ReadOrderDto(entity);
        mapper.AssignTemporaryKeys(context, applied, baseline, authoritative);

        authoritative.Lines.Count.ShouldBe(4);
        authoritative.Lines.Single(static line => line.Quantity == 20).Id.ShouldNotBe(0);
        var persistedAddition = authoritative.Lines.Single(static line => line.Quantity == 9);
        persistedAddition.Id.ShouldNotBe(0);
        persistedAddition.TemporaryId.ShouldBe(temporary);

        var reconciled = session.TryReconcile(submitted, authoritative, out _);
        reconciled.ShouldBeTrue();
        session.HasChanges.ShouldBeFalse();
        draft.Lines.Single(line => line.TemporaryId == temporary).Id.ShouldBe(persistedAddition.Id);
    }

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
}
