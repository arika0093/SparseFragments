using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Whole-tracker change-detection scheduling around mapper apply.</summary>
/// <remarks>
/// Counts <c>DetectingAllChanges</c> (whole-tracker scans, explicit or
/// automatic) to prove read-only preflight scans once no matter how many
/// collections it visits. Execution keeps the caller policy.
/// </remarks>
public sealed partial class ChangeTrackerDetectionTests
{
    /// <summary>Counts whole-tracker scans until disposed.</summary>
    private sealed class ScanCounter : IDisposable
    {
        private readonly ChangeTracker _tracker;
        private readonly EventHandler<DetectChangesEventArgs> _handler;

        public ScanCounter(DbContext context)
        {
            _tracker = context.ChangeTracker;
            _handler = static (_, _) => { };
            _tracker.DetectingAllChanges += OnDetecting;
        }

        public int Count { get; private set; }

        public void Dispose() => _tracker.DetectingAllChanges -= OnDetecting;

        private void OnDetecting(object? sender, DetectChangesEventArgs args) => Count++;
    }

    /// <summary>Removal mapping with an instance removal hook for nested and throwing cases.</summary>
    [SparseFragmentMapper<SoftOrderEntity, SoftOrderDto>]
    public sealed partial class HookedRemoveMapper
    {
        /// <summary>Gets or sets the hook invoked for each removed child during execution.</summary>
        public Action<DbContext, SoftLineEntity>? OnRemove { get; set; }

        private void Configure()
        {
            Map.Collection(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                    });
                    lines.Remove((db, line) => OnRemove?.Invoke(db, line));
                }
            );
        }
    }

    private static OrderEntity SeedSampleOrder(SampleDbContext context)
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

    private static OrderDto ReadSampleOrder(OrderEntity entity) =>
        new()
        {
            Name = entity.CustomerName,
            Notes = entity.Notes,
            Code = entity.Code,
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
                })
                .ToList(),
        };

    private static DuoEntity SeedDuo(KeyedMappingContext context)
    {
        var entity = context
            .Duos.Add(
                new DuoEntity
                {
                    Actives = [new DuoLineEntity { Qty = 1 }, new DuoLineEntity { Qty = 2 }],
                    Archived = [new DuoLineEntity { Qty = 9 }],
                }
            )
            .Entity;
        context.SaveChanges();
        context.Entry(entity).Collection(static owner => owner.Actives).Load();
        context.Entry(entity).Collection(static owner => owner.Archived).Load();
        return entity;
    }

    private static DuoDto ReadDuo(DuoEntity owner) =>
        new()
        {
            Actives = owner
                .Actives.OrderBy(static line => line.Id)
                .Select(static line => new DuoLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
            Archived = owner
                .Archived.OrderBy(static line => line.Id)
                .Select(static line => new DuoLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
        };

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void EmptyChangeSetKeepsCallerPolicy(bool detectChanges)
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedSampleOrder(context);
        var current = ReadSampleOrder(entity);
        var changes = current.CreateChangeSet(current);
        changes.IsEmpty.ShouldBeTrue();
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        // Preflight reads nothing and execution attaches without an
        // explicit scan, so an empty change set detects nothing at all.
        scans.Count.ShouldBe(0);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void ScalarEditRestoresCallerPolicy(bool detectChanges)
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedSampleOrder(context);
        var current = ReadSampleOrder(entity);
        var incoming = ReadSampleOrder(entity);
        incoming.Name = "Bob";
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        entity.CustomerName.ShouldBe("Bob");
        entity.Lines.Count.ShouldBe(2);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
    }

    [Test]
    public void ScalarEditScansWholeTrackerOnceWhenEnabled()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedSampleOrder(context);
        var current = ReadSampleOrder(entity);
        var incoming = ReadSampleOrder(entity);
        incoming.Name = "Bob";
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        // The single upfront preflight scan; the unvisited collection and
        // the scalar execution writes add no further whole-tracker scan.
        scans.Count.ShouldBe(1);
    }

    [Test]
    public void EditsAcrossTwoCollectionsScanWholeTrackerOnceInPreflight()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = SeedDuo(context);
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives[0].Qty = 10;
        incoming.Archived[0].Qty = 90;
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        entity.Actives.Single(static line => line.Qty == 10).ShouldNotBeNull();
        entity.Archived.Single(static line => line.Qty == 90).ShouldNotBeNull();
        // Edits-only plans execute without collection entry access, so the
        // single upfront preflight scan covers both collections.
        scans.Count.ShouldBe(1);
    }

    [Test]
    public void MembershipChangesDetectOnceBeforeExecutionReads()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = SeedDuo(context);
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.RemoveAll(static line => line.Qty == 2);
        incoming.Actives.Add(new DuoLineDto { Qty = 3, TemporaryId = Guid.NewGuid() });
        incoming.Archived.RemoveAll(static line => line.Qty == 9);
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        entity.Actives.Count.ShouldBe(2);
        entity.Archived.Count.ShouldBe(0);
        // One preflight scan plus one execution entry read per changed
        // collection; preflight no longer scans per collection.
        scans.Count.ShouldBe(3);
    }

    [Test]
    public void DisabledDetectionPerformsNoScans()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = SeedDuo(context);
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives.RemoveAll(static line => line.Qty == 2);
        incoming.Actives.Add(new DuoLineDto { Qty = 3, TemporaryId = Guid.NewGuid() });
        incoming.Archived[0].Qty = 90;
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        entity.Actives.Count.ShouldBe(2);
        entity.Archived.Single(static line => line.Qty == 90).ShouldNotBeNull();
        scans.Count.ShouldBe(0);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBeFalse();
    }

    [Test]
    public void UnrelatedTrackedEntitiesAddNoScans()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = SeedDuo(context);
        context.Duos.Add(
            new DuoEntity
            {
                Actives = [new DuoLineEntity { Qty = 100 }],
                Archived = [new DuoLineEntity { Qty = 200 }],
            }
        );
        context.SaveChanges();
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives[0].Qty = 10;
        incoming.Archived[0].Qty = 90;
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        scans.Count.ShouldBe(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void DetachedRootAppliesAndRestoresPolicy(bool detectChanges)
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedSampleOrder(context);
        context.Entry(entity).State = EntityState.Detached;
        var current = ReadSampleOrder(entity);
        var incoming = ReadSampleOrder(entity);
        incoming.Name = "Detached Bob";
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        using var scans = new ScanCounter(context);

        mapper.Apply(context, entity, changes);

        entity.CustomerName.ShouldBe("Detached Bob");
        context.Entry(entity).State.ShouldNotBe(EntityState.Detached);
        scans.Count.ShouldBe(detectChanges ? 1 : 0);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void ThrowingPreflightRestoresPolicy(bool detectChanges)
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = SeedDuo(context);
        // Unloaded navigation: preflight must fail before attaching anything.
        context.Entry(entity).Collection(static owner => owner.Actives).IsLoaded = false;
        var current = ReadDuo(entity);
        var incoming = ReadDuo(entity);
        incoming.Actives[0].Qty = 10;
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = detectChanges;
        using var scans = new ScanCounter(context);

        Should.Throw<SparseEntityMappingException>(() => mapper.Apply(context, entity, changes));

        // Only the upfront detect ran; the failing read added no scan.
        scans.Count.ShouldBe(detectChanges ? 1 : 0);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBe(detectChanges);
    }

    [Test]
    public void ThrowingExecutionRestoresPolicy()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var entity = context
            .SoftOrders.Add(new SoftOrderEntity { Lines = [new SoftLineEntity { Qty = 1 }] })
            .Entity;
        context.SaveChanges();
        context.Entry(entity).Collection(static order => order.Lines).Load();
        var current = new SoftOrderDto
        {
            Lines = entity
                .Lines.Select(static line => new SoftLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
        };
        var incoming = new SoftOrderDto { Lines = [] };
        var changes = current.CreateChangeSet(incoming);
        var mapping = new HookedRemoveMapper
        {
            OnRemove = static (_, _) => throw new InvalidOperationException("Callback failure."),
        };
        var mapper = SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
            mapping,
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = true;
        using var scans = new ScanCounter(context);

        Should.Throw<InvalidOperationException>(() => mapper.Apply(context, entity, changes));

        scans.Count.ShouldBeGreaterThanOrEqualTo(1);
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBeTrue();
    }

    [Test]
    public void NestedMapperCallJoinsOuterPolicy()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var soft = context
            .SoftOrders.Add(new SoftOrderEntity { Lines = [new SoftLineEntity { Qty = 1 }] })
            .Entity;
        var duo = context
            .Duos.Add(new DuoEntity { Actives = [new DuoLineEntity { Qty = 5 }], Archived = [] })
            .Entity;
        context.SaveChanges();
        context.Entry(soft).Collection(static order => order.Lines).Load();
        context.Entry(duo).Collection(static owner => owner.Actives).Load();
        context.Entry(duo).Collection(static owner => owner.Archived).Load();

        var nestedCurrent = ReadDuo(duo);
        var nestedIncoming = ReadDuo(duo);
        nestedIncoming.Actives[0].Qty = 50;
        var nestedChanges = nestedCurrent.CreateChangeSet(nestedIncoming);
        var nestedMapper = SparseEntityMapper<DuoEntity, DuoDto>.Create(
            new KeyedCollectionMappingTests.DuoDivergentMapper(),
            context.Model
        );
        var removedIds = new List<int>();
        var mapping = new HookedRemoveMapper
        {
            OnRemove = (db, line) =>
            {
                removedIds.Add(line.Id);
                nestedMapper.Apply(db, duo, nestedChanges);
            },
        };
        var current = new SoftOrderDto
        {
            Lines = soft
                .Lines.Select(static line => new SoftLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
        };
        var incoming = new SoftOrderDto { Lines = [] };
        var changes = current.CreateChangeSet(incoming);
        var mapper = SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
            mapping,
            context.Model
        );
        context.ChangeTracker.AutoDetectChangesEnabled = true;

        mapper.Apply(context, soft, changes);

        removedIds.Count.ShouldBe(1);
        duo.Actives.Single(static line => line.Qty == 50).ShouldNotBeNull();
        context.ChangeTracker.AutoDetectChangesEnabled.ShouldBeTrue();
    }
}
