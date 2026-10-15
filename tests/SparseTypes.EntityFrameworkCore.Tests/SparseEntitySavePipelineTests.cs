using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore;

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
}
