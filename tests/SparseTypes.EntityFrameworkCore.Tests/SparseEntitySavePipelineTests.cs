using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

public sealed class SparseEntitySavePipelineTests
{
    [Test]
    public async Task PreparationFailureSkipsPersistenceAndLaterStages()
    {
        var saveCalls = 0;
        var readBackCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ =>
                Task.FromException<string>(new InvalidOperationException("prepare")),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            readBack: (_, _) =>
            {
                readBackCalls++;
                return Task.FromResult("persisted");
            },
            synchronize: static (_, _, _) => Task.CompletedTask
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.PreparationFailed);
        result.Error.ShouldBeOfType<InvalidOperationException>();
        saveCalls.ShouldBe(0);
        readBackCalls.ShouldBe(0);
    }

    [Test]
    public async Task ConcurrencyFailureIsReportedWithoutRetry()
    {
        var saveCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.FromException(new DbUpdateConcurrencyException("stale"));
            },
            readBack: static (_, _) => Task.FromResult("persisted"),
            synchronize: static (_, _, _) => Task.CompletedTask
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.ConcurrencyConflict);
        result.Error.ShouldBeOfType<DbUpdateConcurrencyException>();
        saveCalls.ShouldBe(1);
    }

    [Test]
    public async Task SaveFailureIsUnknownAndDoesNotRunReadback()
    {
        var saveCalls = 0;
        var readBackCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.FromException(new IOException("connection lost"));
            },
            readBack: (_, _) =>
            {
                readBackCalls++;
                return Task.FromResult("persisted");
            },
            synchronize: static (_, _, _) => Task.CompletedTask
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.SaveOutcomeUnknown);
        result.Error.ShouldBeOfType<IOException>();
        saveCalls.ShouldBe(1);
        readBackCalls.ShouldBe(0);
    }

    [Test]
    public async Task ReadBackFailureIsDistinctFromSynchronizationFailure()
    {
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: static (_, _) => Task.CompletedTask,
            readBack: static (_, _) =>
                Task.FromException<string>(new InvalidOperationException("readback")),
            synchronize: static (_, _, _) => Task.CompletedTask
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.ReadBackFailed);
        result.Error.ShouldBeOfType<InvalidOperationException>();
        result.Preparation.ShouldBe("prepared");
        result.ReadBack.ShouldBeNull();
    }

    [Test]
    public async Task SynchronizationFailureRetainsThePersistedReadBack()
    {
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: static (_, _) => Task.CompletedTask,
            readBack: static (_, _) => Task.FromResult("persisted"),
            synchronize: static (_, _, _) =>
                Task.FromException(new InvalidOperationException("sync"))
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.ClientSynchronizationFailed);
        result.Error.ShouldBeOfType<InvalidOperationException>();
        result.Preparation.ShouldBe("prepared");
        result.ReadBack.ShouldBe("persisted");
    }

    [Test]
    public async Task PreCanceledTokenSurfacesAtPreparationWithoutPersistence()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var saveCalls = 0;
        var readBackCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult("prepared");
            },
            save: (_, _) =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            readBack: (_, _) =>
            {
                readBackCalls++;
                return Task.FromResult("persisted");
            },
            synchronize: static (_, _, _) => Task.CompletedTask,
            cancellationToken: canceled.Token
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.PreparationFailed);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        saveCalls.ShouldBe(0);
        readBackCalls.ShouldBe(0);
    }

    [Test]
    public async Task CancellationAtPrepareIsPreparationFailure()
    {
        var saveCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromException<string>(new OperationCanceledException()),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            readBack: static (_, _) => Task.FromResult("persisted"),
            synchronize: static (_, _, _) => Task.CompletedTask
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.PreparationFailed);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        result.Preparation.ShouldBeNull();
        saveCalls.ShouldBe(0);
    }

    [Test]
    public async Task CancellationAtSaveIsUnknownAndDoesNotRetry()
    {
        var saveCalls = 0;
        var readBackCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.FromException(new TaskCanceledException());
            },
            readBack: (_, _) =>
            {
                readBackCalls++;
                return Task.FromResult("persisted");
            },
            synchronize: static (_, _, _) => Task.CompletedTask
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.SaveOutcomeUnknown);
        result.Error.ShouldBeOfType<TaskCanceledException>();
        result.Preparation.ShouldBe("prepared");
        saveCalls.ShouldBe(1);
        readBackCalls.ShouldBe(0);
    }

    [Test]
    public async Task CancellationAtReadBackKeepsConfirmedPersistence()
    {
        var synchronizeCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: static (_, _) => Task.CompletedTask,
            readBack: static (_, _) => Task.FromException<string>(new OperationCanceledException()),
            synchronize: (_, _, _) =>
            {
                synchronizeCalls++;
                return Task.CompletedTask;
            }
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.ReadBackFailed);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        result.Preparation.ShouldBe("prepared");
        result.ReadBack.ShouldBeNull();
        synchronizeCalls.ShouldBe(0);
    }

    [Test]
    public async Task CancellationAtSynchronizeRetainsTheReadBack()
    {
        var result = await SparseEntitySavePipeline.ExecuteAsync(
            prepare: static _ => Task.FromResult("prepared"),
            save: static (_, _) => Task.CompletedTask,
            readBack: static (_, _) => Task.FromResult("persisted"),
            synchronize: static (_, _, _) => Task.FromException(new OperationCanceledException())
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.ClientSynchronizationFailed);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        result.Preparation.ShouldBe("prepared");
        result.ReadBack.ShouldBe("persisted");
    }

    [Test]
    public async Task RealisticEfPrepareCancellationLeavesTheDatabaseUntouched()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            seed.Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                }
            );
            seed.SaveChanges();
        }

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var orderId = context.Orders.Single().Id;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var saveCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync<string, string>(
            prepare: async token =>
            {
                var loaded = await context.Orders.SingleAsync(order => order.Id == orderId, token);
                return loaded.Notes ?? string.Empty;
            },
            save: (_, _) =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            readBack: static (preparation, _) => Task.FromResult(preparation),
            synchronize: static (_, _, _) => Task.CompletedTask,
            cancellationToken: canceled.Token
        );

        result.Stage.ShouldBe(SparseEntitySaveStage.PreparationFailed);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        saveCalls.ShouldBe(0);

        // The canceled attempt persisted nothing, so the seed is intact and a
        // retry with a live token is safe.
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        fresh.Orders.Single(order => order.Id == orderId).Notes.ShouldBe("keep");
    }

    [Test]
    public async Task RealisticEfSaveCancellationAfterCommitIsStillUnknown()
    {
        using SqliteConnection connection = TestDatabases.OpenMemory();
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            seed.Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = "keep",
                    Code = "ABC",
                }
            );
            seed.SaveChanges();
        }

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var orderId = context.Orders.Single().Id;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var saveCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderEntity, string>(
            prepare: async _ =>
            {
                var loaded = await context.Orders.SingleAsync(order => order.Id == orderId);
                loaded.Notes = "saved";
                return loaded;
            },
            save: (prepared, token) =>
            {
                saveCalls++;
                return SaveCommittedOrderAsync(prepared, token);
            },
            readBack: static (preparation, _) => Task.FromResult(preparation.Notes ?? string.Empty),
            synchronize: static (_, _, _) => Task.CompletedTask,
            cancellationToken: canceled.Token
        );

        async Task SaveCommittedOrderAsync(OrderEntity prepared, CancellationToken token)
        {
            await context.SaveChangesAsync(CancellationToken.None);
            prepared.Notes.ShouldBe("saved");
            token.ThrowIfCancellationRequested();
        }

        result.Stage.ShouldBe(SparseEntitySaveStage.SaveOutcomeUnknown);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        result.Preparation.ShouldNotBeNull();
        saveCalls.ShouldBe(1);

        // The write committed before cancellation surfaced: a fresh read shows
        // it, so resubmitting the same change set would be wrong.
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        fresh.Orders.Single(order => order.Id == orderId).Notes.ShouldBe("saved");
    }
}
