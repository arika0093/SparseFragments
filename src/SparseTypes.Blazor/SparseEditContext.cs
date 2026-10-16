using System.ComponentModel;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor;

/// <summary>Non-generic access to an edit scope's shared state-query snapshot.</summary>
/// <remarks>Lets field components share one snapshot without naming the generated path builder type.</remarks>
internal interface ISparseStateSnapshotScope
{
    /// <summary>Gets the shared snapshot for the current refresh generation.</summary>
    bool TryGetStateSnapshot(out SparseChangeSetQueryView? snapshot, out string? error);
}

/// <summary>The render-scoped template context provided by <see cref="SparseEdit{TModel, TPaths}"/>.</summary>
/// <remarks>Exposes the concrete generated path builder (<see cref="Path"/>).</remarks>
/// <typeparam name="TModel">The live editable model type.</typeparam>
/// <typeparam name="TPaths">The concrete generated root path builder type.</typeparam>
public sealed class SparseEditContext<TModel, TPaths>
    : ISparseEditScope<TModel>,
        ISparseStateSnapshotScope,
        IDisposable
    where TModel : class
    where TPaths : ISparseTypedPath<TModel, TModel>
{
    private readonly object _sync = new();
    private readonly INotifyPropertyChanged? _notifier;
    private readonly PropertyChangedEventHandler? _notifierHandler;
    private SparseChangeSetQueryView? _snapshot;
    private long _snapshotVersion;
    private long _sessionVersion;
    private object? _snapshotModel;
    private bool _disposed;

    internal SparseEditContext(
        ISparseEditSessionWithPaths<TModel, TPaths> session,
        TPaths path,
        EditContext editContext
    )
    {
        Session = session;
        Path = path;
        EditContext = editContext;
        if (session is INotifyPropertyChanged notifier)
        {
            _notifier = notifier;
            _notifierHandler = (_, _) => Interlocked.Increment(ref _sessionVersion);
            notifier.PropertyChanged += _notifierHandler;
        }
    }

    /// <summary>Gets the active edit session.</summary>
    public ISparseEditSessionWithPaths<TModel, TPaths> Session { get; }

    /// <inheritdoc />
    ISparseEditSession<TModel> ISparseEditScope<TModel>.Session => Session;

    /// <summary>Gets the concrete generated path navigation rooted at the session model.</summary>
    /// <remarks>Path-only access: reading this property never reads the mutable model or invalidates the session change cache.</remarks>
    public TPaths Path { get; }

    /// <summary>Gets the original-model edit context bound to the session model.</summary>
    public EditContext EditContext { get; }

    /// <summary>Gets whether the session model differs from the retained baseline.</summary>
    public bool HasChanges => Session.HasChanges;

    /// <summary>Gets the session-owned history, or null when the session has none enabled.</summary>
    public ISparseEditHistory? History => SparseSessionInterop.TryGetHistory(Session);

    /// <summary>Gets whether an undo entry is available.</summary>
    public bool CanUndo => History?.CanUndo == true;

    /// <summary>Gets whether a redo entry is available.</summary>
    public bool CanRedo => History?.CanRedo == true;

    /// <summary>Undoes the newest history entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">
    /// History is disabled, empty, or conflicts with the before-state.
    /// </exception>
    public void Undo() => RequireHistory().Undo();

    /// <summary>Attempts to undo the newest history entry.</summary>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryUndo(out IReadOnlyList<SparseConflict>? conflicts)
    {
        var history = History;
        if (history is null)
        {
            conflicts = null;
            return false;
        }

        return history.TryUndo(out conflicts);
    }

    /// <summary>Redoes the newest undone history entry, throwing on conflict.</summary>
    /// <exception cref="InvalidOperationException">
    /// History is disabled, empty, or conflicts with the before-state.
    /// </exception>
    public void Redo() => RequireHistory().Redo();

    /// <summary>Attempts to redo the newest undone history entry.</summary>
    /// <param name="conflicts">Conflicts when the result is false.</param>
    /// <returns>True on success; otherwise false.</returns>
    public bool TryRedo(out IReadOnlyList<SparseConflict>? conflicts)
    {
        var history = History;
        if (history is null)
        {
            conflicts = null;
            return false;
        }

        return history.TryRedo(out conflicts);
    }

    /// <summary>Gets the shared state-query snapshot for the current refresh generation.</summary>
    /// <param name="snapshot">The shared view on success.</param>
    /// <param name="error">The failure reason on failure.</param>
    /// <returns>True when one snapshot serves this generation; otherwise false.</returns>
    /// <remarks>
    /// The first field in a refresh generation captures one change set; later
    /// fields reuse its indexed view instead of deriving a root diff each. The
    /// generation ends on session notifications (edits, batches, baseline
    /// changes, reload, reconcile, undo/redo), model replacement, session
    /// replacement, or <see cref="InvalidateStateSnapshot"/>. Raw model edits
    /// that raise no session notification need an explicit
    /// <see cref="InvalidateStateSnapshot"/>; a plain parameter refresh reuses
    /// the generation only while the session reports no intervening change.
    /// Sessions without the snapshot capability report failure so callers keep
    /// the live per-field query path.
    /// </remarks>
    public bool TryGetStateSnapshot(out SparseChangeSetQueryView? snapshot, out string? error)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                snapshot = null;
                error = "The edit context has been disposed.";
                return false;
            }

            object? model;
            try
            {
                model = Session is ISparseEditSessionModelAccessor<TModel> accessor
                    ? accessor.GetModelForFrameworkAccess()
                    : Session.Model;
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or ObjectDisposedException)
            {
                snapshot = null;
                error = exception.Message;
                return false;
            }

            if (
                _snapshot is not null
                && _snapshotVersion == Interlocked.Read(ref _sessionVersion)
                && ReferenceEquals(_snapshotModel, model)
            )
            {
                snapshot = _snapshot;
                error = null;
                return true;
            }

            // A notification mid-capture would stamp a stale view as current,
            // so retry while the version moves; the final attempt stays
            // use-once instead of caching a possibly stale snapshot.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var version = Interlocked.Read(ref _sessionVersion);
                if (
                    !SparseSessionInterop.TryCaptureStateSnapshot(
                        Session,
                        out var captured,
                        out error
                    ) || captured is null
                )
                {
                    snapshot = null;
                    return false;
                }

                if (version == Interlocked.Read(ref _sessionVersion))
                {
                    _snapshot = captured;
                    _snapshotModel = model;
                    _snapshotVersion = version;
                    snapshot = captured;
                    error = null;
                    return true;
                }

                if (attempt == 2)
                {
                    snapshot = captured;
                    error = null;
                    return true;
                }
            }

            snapshot = null;
            error = "The state snapshot could not be captured.";
            return false;
        }
    }

    /// <summary>Ends the current snapshot generation so the next query captures fresh.</summary>
    /// <remarks>Call this after raw model edits that raise no session notification.</remarks>
    public void InvalidateStateSnapshot()
    {
        Interlocked.Increment(ref _sessionVersion);
    }

    internal void RefreshStateSnapshotForParameterUpdate()
    {
        lock (_sync)
        {
            var version = Interlocked.Read(ref _sessionVersion);
            if (_disposed || _snapshot is null || _snapshotVersion != version)
            {
                return;
            }

            object? model;
            try
            {
                model = Session is ISparseEditSessionModelAccessor<TModel> accessor
                    ? accessor.GetModelForFrameworkAccess()
                    : Session.Model;
            }
            catch (Exception exception)
                when (exception is InvalidOperationException or ObjectDisposedException)
            {
                InvalidateStateSnapshot();
                return;
            }

            if (
                !SparseSessionInterop.TryCaptureStateSnapshot(Session, out var captured, out _)
                || captured is null
            )
            {
                InvalidateStateSnapshot();
                return;
            }

            if (version != Interlocked.Read(ref _sessionVersion))
            {
                return;
            }

            if (
                ReferenceEquals(_snapshotModel, model)
                && HaveSameChanges(_snapshot.Changes, captured.Changes)
            )
            {
                return;
            }

            var nextVersion = version + 1;
            if (Interlocked.CompareExchange(ref _sessionVersion, nextVersion, version) != version)
            {
                return;
            }

            _snapshot = captured;
            _snapshotModel = model;
            _snapshotVersion = nextVersion;
        }
    }

    private static bool HaveSameChanges(ISparseChangeSet left, ISparseChangeSet right)
    {
        if (left.IsEmpty != right.IsEmpty)
        {
            return false;
        }

        var leftRecords = left.EnumerateChangeRecords();
        var rightRecords = right.EnumerateChangeRecords();
        if (leftRecords.Count != rightRecords.Count)
        {
            return false;
        }

        for (var index = 0; index < leftRecords.Count; index++)
        {
            var leftRecord = leftRecords[index];
            var rightRecord = rightRecords[index];
            if (
                !leftRecord.Path.Equals(rightRecord.Path)
                || leftRecord.Kind != rightRecord.Kind
                || !leftRecord.Before.Equals(rightRecord.Before)
                || !leftRecord.After.Equals(rightRecord.After)
            )
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_notifier is not null && _notifierHandler is not null)
            {
                _notifier.PropertyChanged -= _notifierHandler;
            }

            _snapshot = null;
            _snapshotModel = null;
        }
    }

    private ISparseEditHistory RequireHistory() =>
        History
        ?? throw new InvalidOperationException(
            "The session has no enabled history. Call EnableHistory on the session; "
                + "this component never enables history itself."
        );
}
