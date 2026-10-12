using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>Removal policies, safeguards, dispatch contracts, and build-time rejection.</summary>
public sealed class KeyedCollectionPolicyTests
{
    /// <summary>Soft-delete order mapping.</summary>
    public sealed class SoftOrderMapper : IEntityModelMapping<SoftOrderEntity, SoftOrderDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<SoftOrderEntity, SoftOrderDto> map)
        {
            map.Collection<SoftLineDto, SoftLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                    });
                    lines.SoftDelete(
                        line => line.IsDeleted = true,
                        line => line.IsDeleted
                    );
                }
            );
        }
    }

    /// <summary>Detach mapping over the archived duo collection.</summary>
    public sealed class DuoDetachMapper : IEntityModelMapping<DuoEntity, DuoDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<DuoEntity, DuoDto> map)
        {
            map.Ignore(dto => dto.Actives);
            map.Collection<DuoLineDto, DuoLineEntity>(
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

    /// <summary>Custom removal mapping recording the callback.</summary>
    public sealed class SoftCustomMapper : IEntityModelMapping<SoftOrderEntity, SoftOrderDto>
    {
        /// <summary>Gets the removed identities observed by the callback.</summary>
        public List<int> RemovedIds { get; } = [];

        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<SoftOrderEntity, SoftOrderDto> map)
        {
            map.Collection<SoftLineDto, SoftLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                    });
                    lines.Remove((_, line) => RemovedIds.Add(line.Id));
                }
            );
        }
    }

    private static async Task<SoftOrderEntity> SeedSoftOrder(KeyedMappingContext context)
    {
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
        return entity;
    }

    private static SoftOrderDto ReadSoftOrder(SoftOrderEntity entity) =>
        new()
        {
            Lines = entity
                .Lines.Where(static line => !line.IsDeleted)
                .OrderBy(static line => line.Id)
                .Select(static line => new SoftLineDto { Id = line.Id, Qty = line.Qty })
                .ToList(),
        };

    [Test]
    public async Task SoftDeleteKeepsRowFlagged()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = await SeedSoftOrder(context);
        counter.Reset();

        var current = ReadSoftOrder(entity);
        var incoming = ReadSoftOrder(entity);
        incoming.Lines.RemoveAll(static line => line.Qty == 2);

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
            .Create(new SoftOrderMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Lines.Count.ShouldBe(2);
        var removed = entity.Lines.Single(static line => line.Qty == 2);
        removed.IsDeleted.ShouldBeTrue();
        context.Entry(removed).State.ShouldBe(EntityState.Modified);
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        using var verify = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var reloaded = verify
            .SoftOrders.Include(static order => order.Lines)
            .Single(order => order.Id == entity.Id);
        reloaded.Lines.Count.ShouldBe(2);
        reloaded.Lines.Single(static line => line.Qty == 2).IsDeleted.ShouldBeTrue();
    }

    [Test]
    public async Task DetachUnlinksWithoutDeleting()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .Duos.Add(
                new DuoEntity
                {
                    Archived = [new DuoLineEntity { Qty = 1 }, new DuoLineEntity { Qty = 2 }],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static owner => owner.Archived).LoadAsync();
        var detachedId = entity.Archived.Single(static line => line.Qty == 2).Id;
        counter.Reset();

        DuoDto Read(DuoEntity owner) =>
            new()
            {
                Archived = owner
                    .Archived.OrderBy(static line => line.Id)
                    .Select(static line => new DuoLineDto { Id = line.Id, Qty = line.Qty })
                    .ToList(),
            };
        var current = Read(entity);
        var incoming = Read(entity);
        incoming.Archived.RemoveAll(static line => line.Qty == 2);

        var accepted = current.CreateChangeSet(incoming);
        SparseEntityMapper<DuoEntity, DuoDto>
            .Create(new DuoDetachMapper(), context.Model)
            .Apply(context, entity, accepted);

        entity.Archived.Count.ShouldBe(1);
        counter.Commands.ShouldBe(0);

        await context.SaveChangesAsync();
        using var verify = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var survivors = verify
            .Set<DuoLineEntity>()
            .Where(line => line.ArchivedOwnerId == entity.Id || line.Id == detachedId)
            .ToList();
        survivors.Count.ShouldBe(2);
        var unlinked = survivors.Single(line => line.Id == detachedId);
        unlinked.ArchivedOwnerId.ShouldBeNull();
    }

    [Test]
    public async Task CustomRemoveCallbackRuns()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = await SeedSoftOrder(context);
        var removedId = entity.Lines.Single(static line => line.Qty == 1).Id;
        counter.Reset();

        var current = ReadSoftOrder(entity);
        var incoming = ReadSoftOrder(entity);
        incoming.Lines.RemoveAll(static line => line.Qty == 1);

        var accepted = current.CreateChangeSet(incoming);
        var mapping = new SoftCustomMapper();
        SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
            .Create(mapping, context.Model)
            .Apply(context, entity, accepted);

        mapping.RemovedIds.ShouldBe([removedId]);
        entity.Lines.Count.ShouldBe(1);
    }

    /// <summary>Collection mapping without any removal policy.</summary>
    public sealed class PolicyLessMapper : IEntityModelMapping<SoftOrderEntity, SoftOrderDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<SoftOrderEntity, SoftOrderDto> map)
        {
            map.Collection<SoftLineDto, SoftLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                    });
                }
            );
        }
    }

    /// <summary>Detach mapping over a required relationship.</summary>
    public sealed class RequiredDetachMapper : IEntityModelMapping<SoftOrderEntity, SoftOrderDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<SoftOrderEntity, SoftOrderDto> map)
        {
            map.Collection<SoftLineDto, SoftLineEntity>(
                dto => dto.Lines,
                entity => entity.Lines,
                lines =>
                {
                    lines.Element(child =>
                    {
                        child.Ignore(line => line.Id);
                    });
                    lines.Detach();
                }
            );
        }
    }

    [Test]
    public void MissingRemovalPolicyFailsAtBuild()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
                new PolicyLessMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MetadataMismatch);
        error.DtoPath.ShouldBe("Lines");
    }

    [Test]
    public void DetachOnRequiredRelationshipFailsAtBuild()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SoftOrderEntity, SoftOrderDto>.Create(
                new RequiredDetachMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MetadataMismatch);
        error.DtoPath.ShouldBe("Lines");
    }

    [Test]
    public async Task UnloadedCollectionFailsWithoutQuery()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        _ = await SeedSoftOrder(context);

        // A fresh scope loads the parent without its lines: the navigation stays unloaded.
        using var fresh = new KeyedMappingContext(
            new DbContextOptionsBuilder<KeyedMappingContext>().UseSqlite(connection).Options
        );
        var tracked = fresh.SoftOrders.Single();
        fresh.Entry(tracked).Collection(static order => order.Lines).IsLoaded.ShouldBeFalse();

        var current = new SoftOrderDto
        {
            Lines = [new SoftLineDto { Id = 1, Qty = 1 }, new SoftLineDto { Id = 2, Qty = 2 }],
        };
        var incoming = new SoftOrderDto
        {
            Lines = [new SoftLineDto { Id = 1, Qty = 10 }, new SoftLineDto { Id = 2, Qty = 2 }],
        };
        var accepted = current.CreateChangeSet(incoming);
        counter.Reset();

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
                .Create(new SoftOrderMapper(), fresh.Model)
                .Apply(fresh, tracked, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionNotLoaded);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task EditedUnknownKeyFails()
    {
        using var connection = TestDatabases.OpenMemory();
        var counter = new QueryCounter();
        using var context = TestDatabases.CreateKeyed(connection, counter);
        var entity = context
            .SoftOrders.Add(new SoftOrderEntity { Lines = [new SoftLineEntity { Qty = 1 }] })
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        counter.Reset();

        // Stale caller state claims a second row the tracked collection never loaded.
        var current = new SoftOrderDto
        {
            Lines =
            [
                new SoftLineDto { Id = entity.Lines[0].Id, Qty = 1 },
                new SoftLineDto { Id = 999, Qty = 2 },
            ],
        };
        var incoming = new SoftOrderDto
        {
            Lines =
            [
                new SoftLineDto { Id = entity.Lines[0].Id, Qty = 1 },
                new SoftLineDto { Id = 999, Qty = 20 },
            ],
        };
        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SoftOrderEntity, SoftOrderDto>
                .Create(new SoftOrderMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyNotFound);
        counter.Commands.ShouldBe(0);
    }

    [Test]
    public async Task NaturalKeyAddCollisionFails()
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
                        new SkuLineEntity { Sku = "x", Qty = 5 },
                    ],
                }
            )
            .Entity;
        await context.SaveChangesAsync();
        await context.Entry(entity).Collection(static order => order.Lines).LoadAsync();
        counter.Reset();

        var current = new SkuOrderDto { Lines = [new SkuLineDto { Sku = "a", Qty = 1 }] };
        // Stale caller state hides the tracked "x" row, so Between reports it as an addition.
        var incoming = new SkuOrderDto
        {
            Lines = [new SkuLineDto { Sku = "a", Qty = 1 }, new SkuLineDto { Sku = "x", Qty = 5 }],
        };
        var accepted = current.CreateChangeSet(incoming);

        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SkuOrderEntity, SkuOrderDto>
                .Create(new SkuCollisionMapper(), context.Model)
                .Apply(context, entity, accepted)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.CollectionKeyConflict);
        counter.Commands.ShouldBe(0);
    }

    /// <summary>Natural-key mapping used for the collision probe.</summary>
    public sealed class SkuCollisionMapper : IEntityModelMapping<SkuOrderEntity, SkuOrderDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<SkuOrderEntity, SkuOrderDto> map)
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
    public void TypedDispatchUsesOrdinaryGeneratorContracts()
    {
        var current = new SkuOrderDto { Lines = [new SkuLineDto { Sku = "a", Qty = 1 }] };
        var incoming = new SkuOrderDto
        {
            Lines = [new SkuLineDto { Sku = "a", Qty = 2 }, new SkuLineDto { Sku = "b", Qty = 1 }],
        };
        var changes = current.CreateChangeSet(incoming);

        var found = ((ISparseChangeSet)changes).TryGetKeyedChange<
            string,
            SkuLineDto,
            SkuLineDto.ChangeSet
        >("Lines", out var keyed);
        found.ShouldBeTrue();
        keyed.ShouldNotBeNull();
        keyed!.Added.Count.ShouldBe(1);
        keyed.Removed.Count.ShouldBe(0);
        keyed.Edited.Count.ShouldBe(1);
        keyed.Edited["a"].ShouldBeOfType<SkuLineDto.ChangeSet>();
        ((ISparseChangeSet)keyed.Edited["a"]).IsChanged.ShouldBeTrue();

        var mismatch = ((ISparseChangeSet)changes).TryGetKeyedChange<
            int,
            SkuLineDto,
            SkuLineDto.ChangeSet
        >("Lines", out _);
        mismatch.ShouldBeFalse();

        // No separate EF generator: the mapping package links only the runtime contracts.
        typeof(SparseEntityMapper<SoftOrderEntity, SoftOrderDto>)
            .Assembly.GetReferencedAssemblies()
            .Where(static name => name.Name == "SparseFragments.Generator")
            .ShouldBeEmpty();
    }

    /// <summary>Collection over a scalar sequence with element configuration.</summary>
    public sealed class TagsElementMapper : IEntityModelMapping<TagsEntity, TagsDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<TagsEntity, TagsDto> map)
        {
            map.Collection<string, string>(
                dto => dto.Tags,
                entity => entity.Tags,
                lines =>
                {
                    lines.Element(child => { });
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public void ScalarSequenceWithElementConfigFailsAtBuild()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateMapping(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<TagsEntity, TagsDto>.Create(new TagsElementMapper(), context.Model)
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MetadataMismatch);
        error.DtoPath.ShouldBe("Tags");
    }

    /// <summary>Key mapping whose type disagrees with the generated transition.</summary>
    public sealed class KeyMismatchMapper : IEntityModelMapping<SkuOrderEntity, SkuOrderDto>
    {
        /// <inheritdoc />
        public void Configure(SparseEntityMapBuilder<SkuOrderEntity, SkuOrderDto> map)
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
                    lines.Key(dto => dto.Sku.Length, entity => entity.Id);
                    lines.Delete();
                }
            );
        }
    }

    [Test]
    public void KeyTypeMismatchFailsAtBuild()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateKeyed(connection, new QueryCounter());
        var error = Should.Throw<SparseEntityMappingException>(() =>
            SparseEntityMapper<SkuOrderEntity, SkuOrderDto>.Create(
                new KeyMismatchMapper(),
                context.Model
            )
        );
        error.Failure.ShouldBe(SparseEntityMappingFailure.MetadataMismatch);
        error.DtoPath.ShouldBe("Lines");
    }
}
