using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore;

/// <summary>Identifies the stage reached by a sparse entity save operation.</summary>
public enum SparseEntitySaveStage
{
    /// <summary>The save and client synchronization completed.</summary>
    Saved,

    /// <summary>Preparation failed before the save callback was invoked.</summary>
    PreparationFailed,

    /// <summary>A concurrency conflict prevented persistence.</summary>
    ConcurrencyConflict,

    /// <summary>The save callback failed without confirming whether persistence completed.</summary>
    SaveOutcomeUnknown,

    /// <summary>Persistence completed, but reading back the authoritative state failed.</summary>
    ReadBackFailed,

    /// <summary>Persistence and readback completed, but client synchronization failed.</summary>
    ClientSynchronizationFailed,
}

/// <summary>The result of one sparse entity save pipeline execution.</summary>
/// <typeparam name="TPreparation">The prepared submission state.</typeparam>
/// <typeparam name="TReadBack">The authoritative state read after persistence.</typeparam>
public sealed class SparseEntitySaveResult<TPreparation, TReadBack>
{
    internal SparseEntitySaveResult(
        SparseEntitySaveStage stage,
        TPreparation? preparation,
        TReadBack? readBack,
        Exception? error
    )
    {
        Stage = stage;
        Preparation = preparation;
        ReadBack = readBack;
        Error = error;
    }

    /// <summary>Gets the stage reached by the operation.</summary>
    public SparseEntitySaveStage Stage { get; }

    /// <summary>Gets the prepared submission state when preparation succeeded.</summary>
    public TPreparation? Preparation { get; }

    /// <summary>Gets the authoritative readback when it completed.</summary>
    public TReadBack? ReadBack { get; }

    /// <summary>Gets the exception from the failed stage, or null on success.</summary>
    public Exception? Error { get; }
}

/// <summary>Runs consumer-supplied preparation, persistence, readback, and synchronization steps.</summary>
/// <remarks>
/// The pipeline does not query, persist, retry, or roll back on its own. Consumers provide every step;
/// a save-stage exception is classified as an unknown outcome unless it is a concurrency conflict.
/// </remarks>
public static class SparseEntitySavePipeline
{
    /// <summary>Executes one save attempt without retrying any stage.</summary>
    /// <typeparam name="TPreparation">The prepared submission state.</typeparam>
    /// <typeparam name="TReadBack">The authoritative state read after persistence.</typeparam>
    /// <param name="prepare">Reads and rebases current state, then applies the accepted submission.</param>
    /// <param name="save">Persists the prepared submission exactly once.</param>
    /// <param name="readBack">Reads persisted state and performs any key correlation required by the consumer.</param>
    /// <param name="synchronize">Reconciles the live client state with the readback.</param>
    /// <param name="cancellationToken">Cancels the current callback when supported by the consumer.</param>
    /// <returns>The stage reached and any prepared or authoritative values.</returns>
    public static async Task<SparseEntitySaveResult<TPreparation, TReadBack>> ExecuteAsync<
        TPreparation,
        TReadBack
    >(
        Func<CancellationToken, Task<TPreparation>> prepare,
        Func<TPreparation, CancellationToken, Task> save,
        Func<TPreparation, CancellationToken, Task<TReadBack>> readBack,
        Func<TPreparation, TReadBack, CancellationToken, Task> synchronize,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(readBack);
        ArgumentNullException.ThrowIfNull(synchronize);

        TPreparation prepared;
        try
        {
            prepared = await prepare(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return new(SparseEntitySaveStage.ConcurrencyConflict, default, default, exception);
        }
        catch (Exception exception)
        {
            return new(SparseEntitySaveStage.PreparationFailed, default, default, exception);
        }

        try
        {
            await save(prepared, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return new(SparseEntitySaveStage.ConcurrencyConflict, prepared, default, exception);
        }
        catch (Exception exception)
        {
            return new(SparseEntitySaveStage.SaveOutcomeUnknown, prepared, default, exception);
        }

        TReadBack persisted;
        try
        {
            persisted = await readBack(prepared, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new(SparseEntitySaveStage.ReadBackFailed, prepared, default, exception);
        }

        try
        {
            await synchronize(prepared, persisted, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new(
                SparseEntitySaveStage.ClientSynchronizationFailed,
                prepared,
                persisted,
                exception
            );
        }

        return new(SparseEntitySaveStage.Saved, prepared, persisted, null);
    }
}
