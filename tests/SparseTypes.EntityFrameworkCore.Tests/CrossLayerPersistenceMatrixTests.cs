using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Concurrency;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

// Cross-layer persistence matrix: generated Core sessions drive the real
// SQLite mapping through SparseEntitySavePipeline. Every scenario asserts the
// database contents (fresh context), the session baseline/current, the
// pending ChangeSet, child states, history availability, the reported error,
// and whether persistence occurred.
public sealed class CrossLayerPersistenceMatrixTests
{
    private static readonly Guid TempA = new("11111111-1111-1111-1111-111111111111");
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
    public async Task PipelineSaveReconcilesPostSubmitEditsAndAssignsTemporaryKeys()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Lines.Single(static line => line.Quantity == 1).Quantity = 10;
        session.Observable.Lines.AddModel(PendingLine(9, TempA));
        history.CanUndo.ShouldBeTrue();

        // The exact immutable submission captured before the awaited save.
        var submitted = session.CreateChangeSet();
        submitted.IsEmpty.ShouldBeFalse();

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var applied = mapper.ApplyWithResult(context, entity, submitted);

        // The user keeps typing while the save is in flight: these edits are
        // not part of the submission and must survive reconciliation.
        session.Observable.Lines.Single(line => line.TemporaryId == TempA).Quantity = 99;
        session.Observable.Lines.AddModel(PendingLine(7, TempLate));

        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, _) => context.SaveChangesAsync(),
            readBack: (_, _) =>
            {
                var authoritative = ReadOrderDto(entity);
                mapper.AssignTemporaryKeys(context, applied, before, authoritative);
                return Task.FromResult(authoritative);
            },
            synchronize: (changes, persisted, _) =>
            {
                session.TryReconcile(changes, persisted, out var reconcileError).ShouldBeTrue();
                reconcileError.ShouldBeNull();
                return Task.CompletedTask;
            }
        );

        // The user-visible outcome: one save, success, no error.
        result.Stage.ShouldBe(SparseEntitySaveStage.Saved);
        result.Error.ShouldBeNull();

        // Persistence occurred exactly once with the submitted values.
        using (var fresh = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            var persisted = await fresh
                .Orders.Include(static order => order.Lines)
                .SingleAsync(order => order.Id == entity.Id);
            persisted.Lines.Count.ShouldBe(3);
            persisted.Lines.Single(line => line.Quantity == 10).ShouldNotBeNull();
            persisted.Lines.Single(line => line.Quantity == 9).ShouldNotBeNull();
        }

        // The assigned key landed on the submitted row without disturbing the
        // post-submit quantity, and the late row stays pending.
        var liveA = draft.Lines.Single(line => line.TemporaryId == TempA);
        liveA.Id.ShouldNotBe(0);
        liveA.Quantity.ShouldBe(99);
        var late = draft.Lines.Single(line => line.TemporaryId == TempLate);
        late.Id.ShouldBe(0);
        var pending = session.CreateChangeSet();
        pending.IsChanged.ShouldBeTrue();
        pending.Lines.GetTemporaryChange(TempLate).IsAdded.ShouldBeTrue();
        pending
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldContain(static text => text.Contains("Quantity", StringComparison.Ordinal));

        // Acknowledgement clears the undo retention.
        history.CanUndo.ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task ChildSessionAddressEditPlusLineReorderSavesAndReconciles()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        var address = session.OpenChildSession(OrderDto.SparsePath.Address);
        address.Session.Observable.Street = "New Street";
        session.Observable.Lines.Single(static line => line.Quantity == 2).Quantity = 20;
        session.Observable.Lines.Move(1, 0);

        session.TryAcceptChildSessions(out var acceptConflicts).ShouldBeTrue();
        acceptConflicts.ShouldBeNull();
        address.HasChanges.ShouldBeFalse();
        var submitted = session.CreateChangeSet();

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (changes, _) =>
            {
                mapper.ApplyWithResult(context, entity, changes);
                return context.SaveChangesAsync();
            },
            readBack: (_, _) => Task.FromResult(ReadOrderDto(entity)),
            synchronize: (changes, persisted, _) =>
            {
                session.TryReconcile(changes, persisted, out var reconcileError).ShouldBeTrue();
                reconcileError.ShouldBeNull();
                return Task.CompletedTask;
            }
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.Saved);
        result.Error.ShouldBeNull();

        using (var fresh = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            var persisted = await fresh
                .Orders.Include(static order => order.Address)
                .Include(static order => order.Lines)
                .SingleAsync(order => order.Id == entity.Id);
            persisted.Address!.Street.ShouldBe("New Street");
            persisted
                .Lines.OrderBy(static line => line.SortOrder)
                .Select(static line => line.Quantity)
                .ShouldBe([20, 1]);
        }

        draft.Address!.Street.ShouldBe("New Street");
        draft.Lines.Select(static line => line.Quantity).ShouldBe([20, 1]);
        session.HasChanges.ShouldBeFalse();
        address.HasChanges.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public async Task RemoveAndRecreateLinePersistsDeleteAndInsert()
    {
        using var connection = TestDatabases.OpenMemory();
        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var entity = SeedOrder(context);
        var before = ReadOrderDto(entity);

        var draft = CloneOrder(before);
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        var removedId = draft.Lines[0].Id;
        session.Observable.Lines.RemoveAt(0);
        session.Observable.Lines.AddModel(PendingLine(5, TempA));
        var submitted = session.CreateChangeSet();

        // A post-submit note stays pending through the save.
        session.Observable.Notes = "post-submit";

        var mapper = SparseEntityMapper<OrderEntity, OrderDto>.Create(
            new OrderMapper(),
            context.Model
        );
        var applied = mapper.ApplyWithResult(context, entity, submitted);
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, _) => context.SaveChangesAsync(),
            readBack: (_, _) =>
            {
                var authoritative = ReadOrderDto(entity);
                mapper.AssignTemporaryKeys(context, applied, before, authoritative);
                return Task.FromResult(authoritative);
            },
            synchronize: (changes, persisted, _) =>
            {
                session.TryReconcile(changes, persisted, out var reconcileError).ShouldBeTrue();
                reconcileError.ShouldBeNull();
                return Task.CompletedTask;
            }
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.Saved);
        result.Error.ShouldBeNull();

        using (var fresh = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            var persisted = await fresh
                .Orders.Include(static order => order.Lines)
                .SingleAsync(order => order.Id == entity.Id);
            persisted.Lines.Select(static line => line.Id).ShouldNotContain(removedId);
            persisted.Lines.Count.ShouldBe(2);
            persisted.Lines.Single(line => line.Quantity == 5).ShouldNotBeNull();
        }

        var liveA = draft.Lines.Single(line => line.TemporaryId == TempA);
        liveA.Id.ShouldNotBe(0);
        draft.Notes.ShouldBe("post-submit");
        var pending = session.CreateChangeSet();
        pending.Notes.After.Value.ShouldBe("post-submit");
        history.CanUndo.ShouldBeFalse();
    }

    private static VersionedOrderEntity SeedVersionedOrder(VersionedOrdersContext context)
    {
        var entity = context
            .Orders.Add(
                new VersionedOrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Lines =
                    [
                        new VersionedOrderLineEntity
                        {
                            Quantity = 1,
                            UnitPrice = 10m,
                            SortOrder = 0,
                        },
                    ],
                }
            )
            .Entity;
        context.SaveChanges();
        return entity;
    }

    private static VersionedOrderDto CloneVersioned(VersionedOrderDto source) =>
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
    public async Task StaleTokenConflictPreservesDraftForRefreshAndRebase()
    {
        using var connection = TestDatabases.OpenMemory();
        int orderId;
        VersionedOrderDto baseline;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var seeded = SeedVersionedOrder(seed);
            orderId = seeded.Id;
            var seedMapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
                new VersionedOrderMapper(),
                seed.Model
            );
            var tracked = await seed
                .Orders.Include(static order => order.Lines)
                .SingleAsync(order => order.Id == orderId);
            baseline = seedMapper.ToDto(tracked);
        }

        var draft = CloneVersioned(baseline);
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Notes = "local notes";
        var submitted = session.CreateChangeSet();
        history.CanUndo.ShouldBeTrue();

        // Another writer commits first and advances the aggregate token.
        using (var remote = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var remoteEntity = await remote.Orders.SingleAsync(order => order.Id == orderId);
            remoteEntity.Notes = "remote notes";
            await remote.SaveChangesAsync();
        }

        var saveCalls = 0;
        using var stale = TestDatabases.CreateVersioned(connection, new QueryCounter());
        var staleMapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
            new VersionedOrderMapper(),
            stale.Model
        );
        var staleEntity = await stale
            .Orders.Include(static order => order.Lines)
            .SingleAsync(order => order.Id == orderId);
        var result = await SparseEntitySavePipeline.ExecuteAsync<
            VersionedOrderDto.ChangeSet,
            VersionedOrderDto
        >(
            prepare: _ => Task.FromResult(submitted),
            save: (changes, _) =>
            {
                saveCalls++;
                staleMapper.Apply(stale, staleEntity, changes);
                stale.Entry(staleEntity).Property(static order => order.Version).OriginalValue =
                    baseline.Version;
                return stale.SaveChangesAsync();
            },
            readBack: (_, _) => Task.FromResult(baseline),
            synchronize: (_, _, _) => Task.CompletedTask
        );

        // A stale write is a concurrency conflict, never a silent success:
        // the save ran once, nothing was acknowledged, the draft is intact.
        result.Stage.ShouldBe(SparseEntitySaveStage.ConcurrencyConflict);
        result.Error.ShouldBeOfType<DbUpdateConcurrencyException>();
        saveCalls.ShouldBe(1);
        session.HasChanges.ShouldBeTrue();
        draft.Notes.ShouldBe("local notes");
        session
            .CreateChangeSet()
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .ShouldBe(submitted.EnumerateChangedPaths().Select(static path => path.ToString()));
        history.CanUndo.ShouldBeTrue();
        using (var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var persisted = await fresh.Orders.SingleAsync(order => order.Id == orderId);
            persisted.Notes.ShouldBe("remote notes");
        }

        // Recovery re-reads instead of resubmitting: the overlap surfaces as
        // a typed rebase conflict, and only the resolved change set is
        // applied under the fresh token.
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
        var authoritative = recoveryMapper.ToDto(currentEntity);

        // The session acknowledges its own baseline-rooted submission: the
        // resolution only served the server write, while the client baseline
        // never moved. The AndApply variant adopts the server-advanced token
        // into the live model; plain TryReconcile would leave the token
        // advance as a pending live-vs-baseline diff.
        session
            .TryReconcileAndApply(submitted, authoritative, out var applyConflicts)
            .ShouldBeTrue();
        applyConflicts.ShouldBeNull();

        draft.Notes.ShouldBe("local notes");
        draft.Version.ShouldBe(authoritative.Version);
        session.HasChanges.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();
        using (var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var persisted = await fresh.Orders.SingleAsync(order => order.Id == orderId);
            persisted.Notes.ShouldBe("local notes");
            persisted.Version.ShouldNotBe(current.Version);
        }
    }

    [Test]
    public async Task SemanticConflictRejectsBeforeAnyWriteWhileStaleTokenFailsTheWrite()
    {
        using var connection = TestDatabases.OpenMemory();
        int semanticId;
        int staleId;
        using (var seed = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            semanticId = SeedVersionedOrder(seed).Id;
            staleId = SeedVersionedOrder(seed).Id;
        }

        // The 409 semantic-conflict path: overlapping member edits surface as
        // typed rebase conflicts without touching the database.
        var semanticCounter = new QueryCounter();
        using (var context = TestDatabases.CreateVersioned(connection, semanticCounter))
        {
            var mapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
                new VersionedOrderMapper(),
                context.Model
            );
            var tracked = await context
                .Orders.Include(static order => order.Lines)
                .SingleAsync(order => order.Id == semanticId);
            var baseline = mapper.ToDto(tracked);
            var draft = CloneVersioned(baseline);
            var session = draft.CreateEditSession();
            session.Observable.Notes = "local notes";
            var submitted = session.CreateChangeSet();

            semanticCounter.Reset();
            var remote = CloneVersioned(baseline);
            remote.Notes = "remote notes";
            var rebase = submitted.RebaseOnto(remote);
            rebase.HasConflicts.ShouldBeTrue();
            rebase.Conflicts.Select(static conflict => conflict.PathText).ShouldContain("Notes");

            // No query, no write, no acknowledgement: the draft is reusable
            // once the user resolves the typed conflict.
            semanticCounter.Commands.ShouldBe(0);
            session.HasChanges.ShouldBeTrue();
            draft.Notes.ShouldBe("local notes");
        }

        // The 412 stale-write path: disjoint member edits rebase cleanly, but
        // the token still guards the read-to-save race underneath.
        using (var context = TestDatabases.CreateVersioned(connection, new QueryCounter()))
        {
            var mapper = SparseEntityMapper<VersionedOrderEntity, VersionedOrderDto>.Create(
                new VersionedOrderMapper(),
                context.Model
            );
            var tracked = await context
                .Orders.Include(static order => order.Lines)
                .SingleAsync(order => order.Id == staleId);
            var baseline = mapper.ToDto(tracked);
            var draft = CloneVersioned(baseline);
            var session = draft.CreateEditSession();
            session.Observable.Notes = "local notes";
            var submitted = session.CreateChangeSet();

            var remote = CloneVersioned(baseline);
            remote.Name = "remote name";
            var rebase = submitted.RebaseOnto(remote);
            rebase.HasConflicts.ShouldBeFalse();

            using (var winner = TestDatabases.CreateVersioned(connection, new QueryCounter()))
            {
                var winnerEntity = await winner.Orders.SingleAsync(order => order.Id == staleId);
                winnerEntity.CustomerName = "remote name";
                await winner.SaveChangesAsync();
            }

            mapper.Apply(context, tracked, rebase.Rebased);
            context.Entry(tracked).Property(static order => order.Version).OriginalValue =
                baseline.Version;
            await Should.ThrowAsync<DbUpdateConcurrencyException>(context.SaveChangesAsync());

            // The failed write leaves the draft pending: the client must
            // refresh and resubmit under the fresh version, never blindly
            // retry the same conditional write.
            session.HasChanges.ShouldBeTrue();
            draft.Notes.ShouldBe("local notes");
            using (var fresh = TestDatabases.CreateVersioned(connection, new QueryCounter()))
            {
                var persisted = await fresh.Orders.SingleAsync(order => order.Id == staleId);
                persisted.CustomerName.ShouldBe("remote name");
                persisted.Notes.ShouldBe("keep");
            }
        }
    }
}
