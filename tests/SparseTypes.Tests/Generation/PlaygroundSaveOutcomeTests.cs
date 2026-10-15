using Microsoft.EntityFrameworkCore;
using SparseTypes.Playground.Pages.Case5;

namespace SparseTypes.Tests.Generation;

public sealed class PlaygroundSaveOutcomeTests
{
    [Test]
    public async Task PreparationFailureDoesNotAttemptSave()
    {
        var saveCalls = 0;
        var syncCalls = 0;

        var outcome = await PlaygroundSaveOutcome.ExecuteAsync(
            prepare: () => throw new InvalidOperationException("invalid submission"),
            save: () =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            synchronize: () =>
            {
                syncCalls++;
                return Task.FromResult(PlaygroundSaveOutcome.Saved());
            }
        );

        outcome.Kind.ShouldBe(PlaygroundSaveOutcomeKind.FailedBeforeSave);
        saveCalls.ShouldBe(0);
        syncCalls.ShouldBe(0);
    }

    [Test]
    public async Task SaveConflictIsReportedWithoutSynchronizationOrRetry()
    {
        var saveCalls = 0;
        var syncCalls = 0;

        var outcome = await PlaygroundSaveOutcome.ExecuteAsync(
            prepare: () => { },
            save: () =>
            {
                saveCalls++;
                throw new DbUpdateConcurrencyException("stale revision");
            },
            synchronize: () =>
            {
                syncCalls++;
                return Task.FromResult(PlaygroundSaveOutcome.Saved());
            }
        );

        outcome.Kind.ShouldBe(PlaygroundSaveOutcomeKind.Conflict);
        saveCalls.ShouldBe(1);
        syncCalls.ShouldBe(0);
    }

    [Test]
    public async Task UnknownSaveOutcomeDoesNotRetryOrSynchronize()
    {
        var saveCalls = 0;
        var syncCalls = 0;

        var outcome = await PlaygroundSaveOutcome.ExecuteAsync(
            prepare: () => { },
            save: () =>
            {
                saveCalls++;
                throw new IOException("connection lost");
            },
            synchronize: () =>
            {
                syncCalls++;
                return Task.FromResult(PlaygroundSaveOutcome.Saved());
            }
        );

        outcome.Kind.ShouldBe(PlaygroundSaveOutcomeKind.SaveOutcomeUnknown);
        saveCalls.ShouldBe(1);
        syncCalls.ShouldBe(0);
    }

    [Test]
    public async Task SynchronizationFailureIsReportedAfterOneSuccessfulSave()
    {
        var saveCalls = 0;
        var syncCalls = 0;

        var outcome = await PlaygroundSaveOutcome.ExecuteAsync(
            prepare: () => { },
            save: () =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            synchronize: () =>
            {
                syncCalls++;
                throw new InvalidOperationException("draft reconciliation failed");
            }
        );

        outcome.Kind.ShouldBe(PlaygroundSaveOutcomeKind.PersistedButClientSyncFailed);
        saveCalls.ShouldBe(1);
        syncCalls.ShouldBe(1);
    }
}
