using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Concurrency;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>
/// Proves the application-managed SQLite concurrency token advances on every
/// aggregate write and turns stale second-context saves into deterministic
/// <see cref="DbUpdateConcurrencyException"/> failures.
/// </summary>
public sealed class EfCoreConcurrencyTokenTests
{
    private static VersionedOrderEntity SeedOrder(VersionedOrdersContext context)
    {
        var entity = context
            .Orders.Add(
                new VersionedOrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Address = new VersionedOrderAddressEntity
                    {
                        Street = "Old Street",
                        City = "Osaka",
                    },
                    Lines =
                    [
                        new VersionedOrderLineEntity
                        {
                            Quantity = 1,
                            UnitPrice = 10m,
                            SortOrder = 0,
                        },
                        new VersionedOrderLineEntity
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
        return entity;
    }

    private static VersionedOrderDto CloneDto(VersionedOrderDto source) =>
        new()
        {
            Name = source.Name,
            Notes = source.Notes,
            Version = source.Version,
            Lines = source
                .Lines.Select(static line => new VersionedOrderLineDto
                {
                    Id = line.Id,
                    TemporaryId = line.TemporaryId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Subtotal = line.Subtotal,
                })
                .ToList(),
        };

    [Test]
    public async Task SuccessfulWriteAdvancesPersistedToken()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        Guid before;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var entity = SeedOrder(seed);
            orderId = entity.Id;
            before = entity.Version;
        }

        before.ShouldNotBe(Guid.Empty);

        using (var context = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var loaded = await context.Orders.SingleAsync(order => order.Id == orderId);
            loaded.Notes = "edited";
            await context.SaveChangesAsync();
        }

        using var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var persisted = await fresh.Orders.SingleAsync(order => order.Id == orderId);
        persisted.Notes.ShouldBe("edited");
        persisted.Version.ShouldNotBe(before);
    }

    [Test]
    public async Task StaleSecondContextFailsDeterministically()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            orderId = SeedOrder(seed).Id;
        }

        using var first = TestDatabases.CreateVersioned(connection, new QueryCounter());
        using var second = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var firstOrder = await first.Orders.SingleAsync(order => order.Id == orderId);
        var secondOrder = await second.Orders.SingleAsync(order => order.Id == orderId);

        firstOrder.Notes = "first";
        await first.SaveChangesAsync();

        secondOrder.Notes = "second";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(second.SaveChangesAsync());

        using var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var persisted = await fresh.Orders.SingleAsync(order => order.Id == orderId);
        persisted.Notes.ShouldBe("first");
    }

    [Test]
    public async Task ChildOnlyWriteAdvancesAggregateToken()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            orderId = SeedOrder(seed).Id;
        }

        using var first = TestDatabases.CreateVersioned(connection, new QueryCounter());
        using var second = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var firstOrder = await first
            .Orders.Include(static order => order.Lines)
            .SingleAsync(order => order.Id == orderId);
        var secondOrder = await second
            .Orders.Include(static order => order.Lines)
            .SingleAsync(order => order.Id == orderId);
        var before = firstOrder.Version;

        // No parent scalar changes: only a line quantity moves.
        firstOrder.Lines.Single(line => line.Quantity == 1).Quantity = 10;
        await first.SaveChangesAsync();

        using (var check = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var advanced = await check.Orders.SingleAsync(order => order.Id == orderId);
            advanced.Version.ShouldNotBe(before);
        }

        secondOrder.Lines.Single(line => line.Quantity == 2).Quantity = 20;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(second.SaveChangesAsync());
    }

    [Test]
    public async Task OwnedAddressOnlyWritesAdvanceTokenAcrossSyncAndAsyncSaves()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var entity = seed
                .Orders.Add(new VersionedOrderEntity { CustomerName = "Ann", Address = null })
                .Entity;
            seed.SaveChanges();
            orderId = entity.Id;
        }

        Guid version;
        using (var add = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var order = await add.Orders.SingleAsync(order => order.Id == orderId);
            version = order.Version;
            order.Address = new VersionedOrderAddressEntity
            {
                Street = "First Street",
                City = "Osaka",
            };

            add.SaveChanges();
            order.Version.ShouldNotBe(version);
            version = order.Version;
        }

        using (var replace = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var order = await replace
                .Orders.Include(static order => order.Address)
                .SingleAsync(order => order.Id == orderId);
            order.Address!.City.ShouldBe("Osaka");
            order.Address = new VersionedOrderAddressEntity
            {
                Street = "Second Street",
                City = "Kyoto",
            };

            await replace.SaveChangesAsync();
            order.Version.ShouldNotBe(version);
            version = order.Version;
        }

        using (var modify = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var order = await modify
                .Orders.Include(static order => order.Address)
                .SingleAsync(order => order.Id == orderId);
            order.Address!.City.ShouldBe("Kyoto");
            order.Address!.City = "Nara";

            modify.SaveChanges();
            order.Version.ShouldNotBe(version);
            version = order.Version;
        }

        using (var delete = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var order = await delete
                .Orders.Include(static order => order.Address)
                .SingleAsync(order => order.Id == orderId);
            order.Address!.City.ShouldBe("Nara");
            order.Address = null;

            await delete.SaveChangesAsync();
            order.Version.ShouldNotBe(version);
            order.Address.ShouldBeNull();
        }

        using var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var persisted = await fresh
            .Orders.Include(static order => order.Address)
            .SingleAsync(order => order.Id == orderId);
        persisted.Address.ShouldBeNull();
    }

    [Test]
    public async Task StaleAddressOnlyWriteFailsWithoutOverwritingTheWinner()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        Guid originalVersion;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var seeded = SeedOrder(seed);
            orderId = seeded.Id;
            originalVersion = seeded.Version;
        }

        using var first = TestDatabases.CreateVersioned(connection, new QueryCounter());
        using var second = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var firstOrder = await first
            .Orders.Include(static order => order.Address)
            .SingleAsync(order => order.Id == orderId);
        var secondOrder = await second
            .Orders.Include(static order => order.Address)
            .SingleAsync(order => order.Id == orderId);

        firstOrder.Address!.City = "First writer";
        await first.SaveChangesAsync();

        using (var check = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var persisted = await check.Orders.SingleAsync(order => order.Id == orderId);
            persisted.Version.ShouldNotBe(originalVersion);
        }

        secondOrder.Address!.City = "Stale writer";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(second.SaveChangesAsync());

        using var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var winner = await fresh
            .Orders.Include(static order => order.Address)
            .SingleAsync(order => order.Id == orderId);
        winner.Address!.City.ShouldBe("First writer");
    }

    [Test]
    public async Task DisjointUpdatesStillGuardTheWriteRace()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        VersionedOrderDto baseline;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            orderId = SeedOrder(seed).Id;
            var mapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
                new VersionedOrderMapper(),
                seed.Model
            );
            var tracked = await seed
                .Orders.Include(static order => order.Lines)
                .SingleAsync(order => order.Id == orderId);
            baseline = mapper.ToDto(tracked);
        }

        // Disjoint member edits merge cleanly at the ChangeSet level.
        var localDraft = CloneDto(baseline);
        var localSession = localDraft.CreateEditSession();
        localDraft.Notes = "local notes";
        var localChanges = localSession.CreateChangeSet();
        var remoteCurrent = CloneDto(baseline);
        remoteCurrent.Name = "remote name";
        localChanges.RebaseOnto(remoteCurrent).HasConflicts.ShouldBeFalse();

        // The aggregate token still guards the read-to-save race underneath.
        using var first = TestDatabases.CreateVersioned(connection, new QueryCounter());
        using var second = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var firstOrder = await first.Orders.SingleAsync(order => order.Id == orderId);
        var secondOrder = await second.Orders.SingleAsync(order => order.Id == orderId);

        firstOrder.CustomerName = "remote name";
        await first.SaveChangesAsync();

        secondOrder.Notes = "local notes";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(second.SaveChangesAsync());
    }

    [Test]
    public async Task StaleSaveRecoversThroughRefreshAndRebase()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            orderId = SeedOrder(seed).Id;
        }

        using var stale = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var mapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
            new VersionedOrderMapper(),
            stale.Model
        );
        var staleEntity = await stale
            .Orders.Include(static order => order.Lines)
            .SingleAsync(order => order.Id == orderId);
        var baseline = mapper.ToDto(staleEntity);

        var draft = CloneDto(baseline);
        var session = draft.CreateEditSession();
        draft.Notes = "local notes";
        var submitted = session.CreateChangeSet();

        using (var remote = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var remoteEntity = await remote.Orders.SingleAsync(order => order.Id == orderId);
            remoteEntity.Notes = "remote notes";
            await remote.SaveChangesAsync();
        }

        // The stale submission applies locally, but the conditional write
        // fails against the advanced token, exactly as the sample handler
        // guards it with the client-supplied version.
        mapper.Apply(stale, staleEntity, submitted);
        stale.Entry(staleEntity).Property(static order => order.Version).OriginalValue =
            baseline.Version;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(stale.SaveChangesAsync());

        // Recovery re-reads instead of resubmitting: the overlapping edit
        // surfaces as a rebase conflict, and only the resolved change set
        // is applied under the fresh token.
        using var recovery = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var recoveryMapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
            new VersionedOrderMapper(),
            recovery.Model
        );
        var currentEntity = await recovery
            .Orders.Include(static order => order.Lines)
            .SingleAsync(order => order.Id == orderId);
        var current = recoveryMapper.ToDto(currentEntity);

        var rebase = submitted.RebaseOnto(current);
        rebase.HasConflicts.ShouldBeTrue();
        rebase.Conflicts.Select(static conflict => conflict.PathText).ShouldContain("Notes");

        var resolution = VersionedOrderDto.ChangeSet.BeginResolution(rebase, current);
        foreach (var conflict in resolution.EnumerateUnresolvedConflicts())
        {
            resolution.UseIncoming(conflict.Path);
        }

        resolution.TryBuild(out var resolved, out var failure).ShouldBeTrue();
        failure.ShouldBeNull();
        resolved.ShouldNotBeNull();

        recoveryMapper.Apply(recovery, currentEntity, resolved!);
        await recovery.SaveChangesAsync();

        using var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var persisted = await fresh
            .Orders.Include(static order => order.Lines)
            .SingleAsync(order => order.Id == orderId);
        persisted.Notes.ShouldBe("local notes");
        persisted.Version.ShouldNotBe(current.Version);
        persisted.Lines.Count.ShouldBe(2);
    }
}
