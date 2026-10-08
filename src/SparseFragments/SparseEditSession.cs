using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SparseFragments;

/// <summary>
/// Tracks edits to a live model against a retained fragment baseline without depending on a UI
/// framework.
/// </summary>
/// <typeparam name="TModel">The editable model type. Must be a reference type.</typeparam>
/// <typeparam name="TFragment">The generated fragment type for <typeparamref name="TModel"/>.</typeparam>
/// <typeparam name="TPatch">The generated baseline-free patch type for <typeparamref name="TModel"/>.</typeparam>
/// <typeparam name="TChangeSet">The generated baseline-aware change set type for <typeparamref name="TModel"/>.</typeparam>
/// <typeparam name="TObservable">The generated observable proxy type for <typeparamref name="TModel"/>.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
{
    private readonly Func<TModel, TFragment> _fromModel;
    private readonly Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> _between;
    private readonly Func<TChangeSet, TPatch> _toPatch;
    private readonly Func<TChangeSet, bool> _isEmpty;
    private readonly Func<TChangeSet, Optional<TFragment?>, RebaseResult<TChangeSet>>? _rebase;
    private readonly Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>>? _apply;
    private readonly Action<TModel, TFragment>? _write;
    private readonly TObservable _observable;
    private Optional<TFragment?> _baseline;
    private SparsePendingSubmit<TChangeSet, TFragment>? _pending;
    private long _generation;

    private SparseEditSession(
        TModel model,
        TFragment baseline,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable,
        Func<TChangeSet, Optional<TFragment?>, RebaseResult<TChangeSet>>? rebase = null,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>>? apply = null,
        Action<TModel, TFragment>? write = null,
        Action? onChanged = null
    )
    {
        Model = model;
        _fromModel = fromModel;
        _between = between;
        _toPatch = toPatch;
        _isEmpty = isEmpty;
        _rebase = rebase;
        _apply = apply;
        _write = write;
        _baseline = Optional<TFragment?>.Present(baseline);
        _observable = toObservable(
            model,
            () =>
            {
                onChanged?.Invoke();
                OnPropertyChanged(nameof(HasChanges));
            }
        );
    }

    /// <summary>Creates a session capturing the current model state as its baseline.</summary>
    public static SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, TObservable> toObservable
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateDelegates(fromModel, between, toPatch, isEmpty, toObservable);

        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            (value, _) => toObservable(value)
        );
    }

    /// <summary>Creates a session whose observable proxy reports model changes to the session.</summary>
    public static SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateNotificationDelegates(fromModel, between, toPatch, isEmpty, toObservable);
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            toObservable,
            onChanged: onChanged
        );
    }

    /// <summary>Creates a session editing <paramref name="current"/> against a separate baseline snapshot.</summary>
    public static SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel baseline,
        TModel current,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, TObservable> toObservable
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateDelegates(fromModel, between, toPatch, isEmpty, toObservable);

        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            (value, _) => toObservable(value)
        );
    }

    /// <summary>Creates a session with a separate baseline and an observable change callback.</summary>
    public static SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable> Create(
        TModel baseline,
        TModel current,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateNotificationDelegates(fromModel, between, toPatch, isEmpty, toObservable);
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            toObservable,
            onChanged: onChanged
        );
    }

    /// <summary>Creates a submit-capable edit session.</summary>
    public static SparseEditSession<
        TModel,
        TFragment,
        TPatch,
        TChangeSet,
        TObservable
    > CreateWithSubmit(
        TModel model,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable,
        Func<TChangeSet, Optional<TFragment?>, RebaseResult<TChangeSet>> rebase,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> apply,
        Action<TModel, TFragment> write,
        Action? onChanged = null
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateSubmitDelegates(
            fromModel,
            between,
            toPatch,
            isEmpty,
            toObservable,
            rebase,
            apply,
            write
        );
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            toObservable,
            rebase,
            apply,
            write,
            onChanged
        );
    }

    /// <summary>Creates a submit-capable session against a separate baseline model.</summary>
    public static SparseEditSession<
        TModel,
        TFragment,
        TPatch,
        TChangeSet,
        TObservable
    > CreateWithSubmit(
        TModel baseline,
        TModel current,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable,
        Func<TChangeSet, Optional<TFragment?>, RebaseResult<TChangeSet>> rebase,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> apply,
        Action<TModel, TFragment> write,
        Action? onChanged = null
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateSubmitDelegates(
            fromModel,
            between,
            toPatch,
            isEmpty,
            toObservable,
            rebase,
            apply,
            write
        );
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            toObservable,
            rebase,
            apply,
            write,
            onChanged
        );
    }

    private static void ValidateSubmitDelegates(
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable,
        Func<TChangeSet, Optional<TFragment?>, RebaseResult<TChangeSet>> rebase,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> apply,
        Action<TModel, TFragment> write
    )
    {
        if (fromModel is null)
            throw new ArgumentNullException(nameof(fromModel));
        if (between is null)
            throw new ArgumentNullException(nameof(between));
        if (toPatch is null)
            throw new ArgumentNullException(nameof(toPatch));
        if (isEmpty is null)
            throw new ArgumentNullException(nameof(isEmpty));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
        if (rebase is null)
            throw new ArgumentNullException(nameof(rebase));
        if (apply is null)
            throw new ArgumentNullException(nameof(apply));
        if (write is null)
            throw new ArgumentNullException(nameof(write));
    }

    private static void ValidateNotificationDelegates(
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, Action?, TObservable> toObservable
    )
    {
        if (fromModel is null)
            throw new ArgumentNullException(nameof(fromModel));
        if (between is null)
            throw new ArgumentNullException(nameof(between));
        if (toPatch is null)
            throw new ArgumentNullException(nameof(toPatch));
        if (isEmpty is null)
            throw new ArgumentNullException(nameof(isEmpty));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
    }

    private static void ValidateDelegates(
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, TObservable> toObservable
    )
    {
        if (fromModel is null)
            throw new ArgumentNullException(nameof(fromModel));
        if (between is null)
            throw new ArgumentNullException(nameof(between));
        if (toPatch is null)
            throw new ArgumentNullException(nameof(toPatch));
        if (isEmpty is null)
            throw new ArgumentNullException(nameof(isEmpty));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
    }

    /// <summary>The live editable model. The UI or application mutates this instance directly.</summary>
    public TModel Model { get; }

    /// <summary>A stable typed observable proxy over <see cref="Model"/>.</summary>
    public TObservable Observable => _observable;

    /// <summary>Whether the current model differs semantically from the retained baseline.</summary>
    public bool HasChanges => !_isEmpty(CreateChangeSet());

    /// <summary>Whether a submit operation is currently in flight.</summary>
    public bool IsSubmitting => _pending is not null;

    /// <summary>Raised when session state or its observable model may have changed.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Derives the baseline-aware change set between the retained baseline and current model.</summary>
    public TChangeSet CreateChangeSet() =>
        _between(_baseline, Optional<TFragment?>.Present(_fromModel(Model)));

    /// <summary>Derives the baseline-free patch from the current baseline-aware change set.</summary>
    public TPatch CreatePatch() => _toPatch(CreateChangeSet());

    /// <summary>Accepts the current model state as the new baseline.</summary>
    public void AcceptChanges()
    {
        _baseline = Optional<TFragment?>.Present(_fromModel(Model));
        _generation++;
        _pending = null;
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsSubmitting));
    }

    /// <summary>Captures the change set and model snapshot to send to a server.</summary>
    public SparsePendingSubmit<TChangeSet, TFragment> BeginSubmit()
    {
        EnsureSubmitSupported();
        if (_pending is not null)
            throw new InvalidOperationException(
                "A submit is already in progress for this session."
            );
        var snapshot = _fromModel(Model);
        var pending = new SparsePendingSubmit<TChangeSet, TFragment>(
            _between(_baseline, Optional<TFragment?>.Present(snapshot)),
            snapshot,
            _generation
        );
        _pending = pending;
        OnPropertyChanged(nameof(IsSubmitting));
        return pending;
    }

    /// <summary>Completes a pending submit and rebases remaining local edits if server state is supplied.</summary>
    public SparseSubmitResult Complete(
        SparsePendingSubmit<TChangeSet, TFragment> pending,
        SparseSubmitResponse<TModel> response
    )
    {
        EnsureSubmitSupported();
        if (pending is null)
            throw new ArgumentNullException(nameof(pending));
        if (response is null)
            throw new ArgumentNullException(nameof(response));
        if (!ReferenceEquals(_pending, pending) || pending.Generation != _generation)
            throw new InvalidOperationException(
                "The submit handle is stale or does not belong to this session."
            );

        _pending = null;
        OnPropertyChanged(nameof(IsSubmitting));
        SparseSubmitResult result;
        switch (response.Status)
        {
            case SparseSubmitStatus.Failed:
                result = new SparseSubmitResult(SparseSubmitStatus.Failed);
                break;
            case SparseSubmitStatus.Accepted:
            {
                if (response.ServerState is null)
                {
                    _baseline = Optional<TFragment?>.Present(pending.Snapshot);
                    var later = _between(
                        Optional<TFragment?>.Present(pending.Snapshot),
                        Optional<TFragment?>.Present(_fromModel(Model))
                    );
                    result = new SparseSubmitResult(
                        _isEmpty(later) ? SparseSubmitStatus.Accepted : SparseSubmitStatus.Rebased
                    );
                    break;
                }

                var server = _fromModel(response.ServerState);
                var local = _between(
                    Optional<TFragment?>.Present(pending.Snapshot),
                    Optional<TFragment?>.Present(_fromModel(Model))
                );
                var hasLaterEdits = !_isEmpty(local);
                var rebased = _rebase!(local, Optional<TFragment?>.Present(server));
                _baseline = Optional<TFragment?>.Present(server);
                WriteApplied(rebased.Patch, rebased.HasConflicts ? _fromModel(Model) : server);
                var status = rebased.HasConflicts
                    ? SparseSubmitStatus.Conflicted
                    : SparseSubmitStatus.Accepted;
                if (!rebased.HasConflicts && hasLaterEdits)
                    status = SparseSubmitStatus.Rebased;
                result = new SparseSubmitResult(status, rebased.Conflicts);
                break;
            }
            case SparseSubmitStatus.Rejected:
                if (response.ServerState is null)
                {
                    result = new SparseSubmitResult(SparseSubmitStatus.Rejected);
                    break;
                }
                else
                {
                    var server = _fromModel(response.ServerState);
                    var local = _between(
                        _baseline,
                        Optional<TFragment?>.Present(_fromModel(Model))
                    );
                    var rebased = _rebase!(local, Optional<TFragment?>.Present(server));
                    _baseline = Optional<TFragment?>.Present(server);
                    WriteApplied(rebased.Patch, rebased.HasConflicts ? _fromModel(Model) : server);
                    result = new SparseSubmitResult(
                        rebased.HasConflicts
                            ? SparseSubmitStatus.Conflicted
                            : SparseSubmitStatus.Rebased,
                        rebased.Conflicts
                    );
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(response));
        }
        _generation++;
        OnPropertyChanged(nameof(HasChanges));
        return result;
    }

    /// <summary>Sends the current change set and completes the session submit lifecycle.</summary>
    /// <remarks>Send exceptions and cancellation leave the baseline unchanged and are rethrown.</remarks>
    public async Task<SparseSubmitResult> SubmitAsync(
        Func<TChangeSet, CancellationToken, Task<SparseSubmitResponse<TModel>>> send,
        CancellationToken cancellationToken = default
    )
    {
        if (send is null)
            throw new ArgumentNullException(nameof(send));
        var pending = BeginSubmit();
        try
        {
            var response = await send(pending.ChangeSet, cancellationToken).ConfigureAwait(false);
            return Complete(pending, response);
        }
        catch
        {
            if (ReferenceEquals(_pending, pending))
                Complete(pending, SparseSubmitResponse<TModel>.Failed());
            throw;
        }
    }

    private void EnsureSubmitSupported()
    {
        if (_rebase is null || _apply is null || _write is null)
            throw new NotSupportedException(
                "Submit and in-place updates are unavailable because this model has members that cannot be written in place."
            );
    }

    private void WriteApplied(TChangeSet changes, TFragment baseline)
    {
        var updated = _apply!(changes, Optional<TFragment?>.Present(baseline));
        if (!updated.IsPresent || updated.Value is null)
            throw new InvalidOperationException(
                "The rebased change set did not produce a model state."
            );
        _write!(Model, updated.Value);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
