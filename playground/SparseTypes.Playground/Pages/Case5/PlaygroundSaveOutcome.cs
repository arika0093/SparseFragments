using Microsoft.EntityFrameworkCore;
using SparseTypes;

namespace SparseTypes.Playground.Pages.Case5;

internal enum PlaygroundSaveOutcomeKind
{
    Saved,
    FailedBeforeSave,
    Conflict,
    SaveOutcomeUnknown,
    PersistedButClientSyncFailed,
}

internal sealed class PlaygroundSaveOutcome
{
    private PlaygroundSaveOutcome(
        PlaygroundSaveOutcomeKind kind,
        Exception? error = null,
        IReadOnlyList<SparseConflict>? conflicts = null
    )
    {
        Kind = kind;
        Error = error;
        Conflicts = conflicts;
    }

    internal PlaygroundSaveOutcomeKind Kind { get; }

    internal Exception? Error { get; }

    internal IReadOnlyList<SparseConflict>? Conflicts { get; }

    internal static PlaygroundSaveOutcome Saved() => new(PlaygroundSaveOutcomeKind.Saved);

    internal static PlaygroundSaveOutcome FailedBeforeSave(Exception error) =>
        new(PlaygroundSaveOutcomeKind.FailedBeforeSave, error);

    internal static PlaygroundSaveOutcome Conflict(DbUpdateConcurrencyException error) =>
        new(PlaygroundSaveOutcomeKind.Conflict, error);

    internal static PlaygroundSaveOutcome SaveOutcomeUnknown(Exception error) =>
        new(PlaygroundSaveOutcomeKind.SaveOutcomeUnknown, error);

    internal static PlaygroundSaveOutcome PersistedButClientSyncFailed(
        Exception error,
        IReadOnlyList<SparseConflict>? conflicts = null
    ) => new(PlaygroundSaveOutcomeKind.PersistedButClientSyncFailed, error, conflicts);

    internal static async Task<PlaygroundSaveOutcome> ExecuteAsync(
        Action prepare,
        Func<Task> save,
        Func<Task<PlaygroundSaveOutcome>> synchronize
    )
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(synchronize);

        try
        {
            prepare();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (Exception exception)
        {
            return FailedBeforeSave(exception);
        }

        try
        {
            await save();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return Conflict(exception);
        }
        catch (Exception exception)
        {
            return SaveOutcomeUnknown(exception);
        }

        try
        {
            return await synchronize();
        }
        catch (Exception exception)
        {
            return PersistedButClientSyncFailed(exception);
        }
    }
}
