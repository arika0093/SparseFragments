using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

// Cross-layer save-failure matrix: every pipeline failure stage is driven
// against a live edit session and a real SQLite database. Each scenario
// asserts the reported stage and error, the database contents (fresh
// context), the session baseline/current, the pending ChangeSet, history
// availability, and that the pipeline never retried or acknowledged on the
// caller's behalf.
public sealed class CrossLayerSaveFailureMatrixTests
{
    private static int SeedNotes(SampleDbContext context, string notes)
    {
        var entity = context
            .Orders.Add(
                new OrderEntity
                {
                    CustomerName = "Ann",
                    Notes = notes,
                    Code = "ABC",
                }
            )
            .Entity;
        context.SaveChanges();
        return entity.Id;
    }

    private static string ReadNotes(SqliteConnection connection, int orderId)
    {
        using var fresh = TestDatabases.CreateSample(connection, new QueryCounter());
        return fresh.Orders.Single(order => order.Id == orderId).Notes!;
    }

    private static List<string> ChangedPaths(OrderDto.ChangeSet changes) =>
        changes
            .EnumerateChangedPaths()
            .Select(static path => path.ToString())
            .OrderBy(static text => text, StringComparer.Ordinal)
            .ToList();

    [Test]
    public async Task CanceledPrepareLeavesDatabaseUntouchedAndSubmissionReusable()
    {
        using var connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            orderId = SeedNotes(seed, "keep");
        }

        var draft = new OrderDto();
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "Ann";
        session.Observable.Notes = "edited";
        var submitted = session.CreateChangeSet();
        var pendingBefore = ChangedPaths(submitted);
        pendingBefore.Count.ShouldBeGreaterThan(0);

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var saveCalls = 0;
        var readBackCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: async token =>
            {
                _ = await context.Orders.SingleAsync(order => order.Id == orderId, token);
                return submitted;
            },
            save: (_, _) =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            readBack: (_, _) =>
            {
                readBackCalls++;
                return Task.FromResult(draft);
            },
            synchronize: (_, _, _) => Task.CompletedTask,
            cancellationToken: canceled.Token
        );

        // Preparation cancellation means the save never ran: retry-safe.
        result.Stage.ShouldBe(SparseEntitySaveStage.PreparationFailed);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        saveCalls.ShouldBe(0);
        readBackCalls.ShouldBe(0);

        // Nothing persisted and nothing was acknowledged: the database, the
        // pending change set, and the history are all untouched.
        ReadNotes(connection, orderId).ShouldBe("keep");
        session.HasChanges.ShouldBeTrue();
        ChangedPaths(session.CreateChangeSet()).ShouldBe(pendingBefore);
        history.CanUndo.ShouldBeTrue();

        // The same capture stays usable: acknowledging it now advances the
        // baseline exactly once.
        session.AcceptChanges(submitted);
        session.HasChanges.ShouldBeFalse();
        draft.Notes.ShouldBe("edited");
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public async Task UnknownSaveOutcomeMustBeQueriedNeverResubmitted()
    {
        using var connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            orderId = SeedNotes(seed, "keep");
        }

        var draft = new OrderDto();
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "Ann";
        session.Observable.Code = "ABC";
        session.Observable.Notes = "saved";
        var submitted = session.CreateChangeSet();
        var pendingBefore = ChangedPaths(submitted);

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var tracked = await context.Orders.SingleAsync(order => order.Id == orderId);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var saveCalls = 0;
        var readBackCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, token) =>
            {
                saveCalls++;
                tracked.Notes = "saved";
                return SaveCommittedOrderAsync(context, token);
            },
            readBack: (_, _) =>
            {
                readBackCalls++;
                return Task.FromResult(draft);
            },
            synchronize: (_, _, _) => Task.CompletedTask,
            cancellationToken: canceled.Token
        );

        // Cancellation after the commit leaves the outcome unknown: the
        // write may have landed, so resubmitting blindly would be wrong.
        result.Stage.ShouldBe(SparseEntitySaveStage.SaveOutcomeUnknown);
        result.Error.ShouldBeOfType<OperationCanceledException>();
        result.Preparation.ShouldNotBeNull();
        saveCalls.ShouldBe(1);
        readBackCalls.ShouldBe(0);

        // The session was not acknowledged: the draft, its pending change
        // set, and its history are intact for the must-query recovery.
        session.HasChanges.ShouldBeTrue();
        draft.Notes.ShouldBe("saved");
        ChangedPaths(session.CreateChangeSet()).ShouldBe(pendingBefore);
        history.CanUndo.ShouldBeTrue();

        // The mandatory query shows the write landed, so recovery reconciles
        // the queried state instead of resubmitting the write.
        ReadNotes(connection, orderId).ShouldBe("saved");
        var authoritative = new OrderDto
        {
            Name = "Ann",
            Notes = "saved",
            Code = "ABC",
        };
        session.TryReconcile(submitted, authoritative, out var reconcileError).ShouldBeTrue();
        reconcileError.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();
    }

    private static async Task SaveCommittedOrderAsync(
        SampleDbContext context,
        CancellationToken token
    )
    {
        await context.SaveChangesAsync(CancellationToken.None);
        token.ThrowIfCancellationRequested();
    }

    [Test]
    public async Task ReadBackFailureKeepsConfirmedPersistenceWithoutClientSync()
    {
        using var connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            orderId = SeedNotes(seed, "keep");
        }

        var draft = new OrderDto();
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "Ann";
        session.Observable.Code = "ABC";
        session.Observable.Notes = "saved";
        var submitted = session.CreateChangeSet();
        var pendingBefore = ChangedPaths(submitted);

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var tracked = await context.Orders.SingleAsync(order => order.Id == orderId);
        var synchronizeCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, _) =>
            {
                tracked.Notes = "saved";
                return context.SaveChangesAsync();
            },
            readBack: (_, _) =>
                Task.FromException<OrderDto>(new InvalidOperationException("readback")),
            synchronize: (_, _, _) =>
            {
                synchronizeCalls++;
                return Task.CompletedTask;
            }
        );

        // Persistence completed but the authoritative read is missing: the
        // client must re-read instead of assuming the draft is current.
        result.Stage.ShouldBe(SparseEntitySaveStage.ReadBackFailed);
        result.Error.ShouldBeOfType<InvalidOperationException>();
        result.Error!.Message.ShouldBe("readback");
        result.Preparation.ShouldNotBeNull();
        result.ReadBack.ShouldBeNull();
        synchronizeCalls.ShouldBe(0);

        // The write is confirmed in the database while the session stays
        // pending with its history: no success was claimed.
        ReadNotes(connection, orderId).ShouldBe("saved");
        session.HasChanges.ShouldBeTrue();
        ChangedPaths(session.CreateChangeSet()).ShouldBe(pendingBefore);
        history.CanUndo.ShouldBeTrue();
    }

    [Test]
    public async Task ClientSyncFailureRetainsAuthoritativeReadbackForManualRecovery()
    {
        using var connection = TestDatabases.OpenMemory();
        int orderId;
        using (var seed = TestDatabases.CreateSample(connection, new QueryCounter()))
        {
            orderId = SeedNotes(seed, "keep");
        }

        var draft = new OrderDto();
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "Ann";
        session.Observable.Code = "ABC";
        session.Observable.Notes = "saved";
        var submitted = session.CreateChangeSet();
        var pendingBefore = ChangedPaths(submitted);

        using var context = TestDatabases.CreateSample(connection, new QueryCounter());
        var tracked = await context.Orders.SingleAsync(order => order.Id == orderId);
        var saveCalls = 0;
        var result = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, _) =>
            {
                saveCalls++;
                tracked.Notes = "saved";
                return context.SaveChangesAsync();
            },
            readBack: (_, _) =>
                Task.FromResult(
                    new OrderDto
                    {
                        Name = "Ann",
                        Notes = "saved",
                        Code = "ABC",
                    }
                ),
            synchronize: (_, _, _) =>
                Task.FromException(new InvalidOperationException("client sync"))
        );

        // Persistence and readback completed; only client state is
        // unsynchronized, and the authoritative readback is retained for the
        // manual recovery.
        result.Stage.ShouldBe(SparseEntitySaveStage.ClientSynchronizationFailed);
        result.Error.ShouldBeOfType<InvalidOperationException>();
        result.Error!.Message.ShouldBe("client sync");
        result.Preparation.ShouldNotBeNull();
        var retained = result.ReadBack.ShouldNotBeNull();
        retained.Notes.ShouldBe("saved");
        saveCalls.ShouldBe(1);

        // The session stays pending with its history; recovery completes the
        // synchronization without resubmitting the write.
        ReadNotes(connection, orderId).ShouldBe("saved");
        session.HasChanges.ShouldBeTrue();
        ChangedPaths(session.CreateChangeSet()).ShouldBe(pendingBefore);
        history.CanUndo.ShouldBeTrue();

        session.TryReconcile(submitted, retained, out var reconcileError).ShouldBeTrue();
        reconcileError.ShouldBeNull();
        session.HasChanges.ShouldBeFalse();
        saveCalls.ShouldBe(1);
        history.CanUndo.ShouldBeFalse();
    }

    [Test]
    public async Task FailingSaveNeverRetriesWithinOneExecution()
    {
        var draft = new OrderDto();
        var session = draft.CreateEditSession();
        var history = session.EnableHistory();
        session.Observable.Name = "Ann";
        var submitted = session.CreateChangeSet();
        var pendingBefore = ChangedPaths(submitted);

        // The first attempt fails without confirming persistence. The
        // pipeline reports the unknown outcome instead of retrying: the save
        // callback ran exactly once.
        var landed = false;
        var saveCalls = 0;
        var unknown = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.FromException(new IOException("connection lost"));
            },
            readBack: (_, _) => Task.FromResult(draft),
            synchronize: (_, _, _) => Task.CompletedTask
        );

        unknown.Stage.ShouldBe(SparseEntitySaveStage.SaveOutcomeUnknown);
        unknown.Error.ShouldBeOfType<IOException>();
        unknown.Error!.Message.ShouldBe("connection lost");
        saveCalls.ShouldBe(1);

        // The session is untouched: the draft, its pending change set, and
        // its history survive for the explicit, consumer-driven retry.
        session.HasChanges.ShouldBeTrue();
        ChangedPaths(session.CreateChangeSet()).ShouldBe(pendingBefore);
        history.CanUndo.ShouldBeTrue();

        // The mandatory query shows the write is absent, so the consumer
        // retries explicitly: the second attempt is a new execution, not a
        // hidden retry, and it acknowledges the submission exactly once.
        landed.ShouldBeFalse();
        landed = true;
        var retry = await SparseEntitySavePipeline.ExecuteAsync<OrderDto.ChangeSet, OrderDto>(
            prepare: _ => Task.FromResult(submitted),
            save: (_, _) =>
            {
                saveCalls++;
                return Task.CompletedTask;
            },
            readBack: (_, _) => Task.FromResult(draft),
            synchronize: (changes, _, _) =>
            {
                session.AcceptChanges(changes);
                return Task.CompletedTask;
            }
        );

        retry.Stage.ShouldBe(SparseEntitySaveStage.Saved);
        retry.Error.ShouldBeNull();
        saveCalls.ShouldBe(2);
        landed.ShouldBeTrue();
        session.HasChanges.ShouldBeFalse();
        history.CanUndo.ShouldBeFalse();
    }
}
