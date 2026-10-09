using System;
using System.ComponentModel;

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
    private readonly Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> _advanceBaseline;
    private readonly TObservable _observable;
    private Optional<TFragment?> _baseline;

    private SparseEditSession(
        TModel model,
        TFragment baseline,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        Model = model;
        _fromModel = fromModel;
        _between = between;
        _toPatch = toPatch;
        _isEmpty = isEmpty;
        _advanceBaseline = advanceBaseline;
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
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
        Func<TModel, TObservable> toObservable
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateDelegates(fromModel, between, toPatch, isEmpty, advanceBaseline, toObservable);

        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
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
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        if (model is null)
            throw new ArgumentNullException(nameof(model));
        ValidateNotificationDelegates(
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            toObservable
        );
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            model,
            fromModel(model),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            toObservable,
            onChanged
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
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
        Func<TModel, TObservable> toObservable
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateDelegates(fromModel, between, toPatch, isEmpty, advanceBaseline, toObservable);

        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
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
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
        Func<TModel, Action?, TObservable> toObservable,
        Action? onChanged = null
    )
    {
        if (baseline is null)
            throw new ArgumentNullException(nameof(baseline));
        if (current is null)
            throw new ArgumentNullException(nameof(current));
        ValidateNotificationDelegates(
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            toObservable
        );
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>(
            current,
            fromModel(baseline),
            fromModel,
            between,
            toPatch,
            isEmpty,
            advanceBaseline,
            toObservable,
            onChanged
        );
    }

    private static void ValidateNotificationDelegates(
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
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
        if (advanceBaseline is null)
            throw new ArgumentNullException(nameof(advanceBaseline));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
    }

    private static void ValidateDelegates(
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TChangeSet, Optional<TFragment?>, Optional<TFragment?>> advanceBaseline,
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
        if (advanceBaseline is null)
            throw new ArgumentNullException(nameof(advanceBaseline));
        if (toObservable is null)
            throw new ArgumentNullException(nameof(toObservable));
    }

    /// <summary>The live editable model. The UI or application mutates this instance directly.</summary>
    public TModel Model { get; }

    /// <summary>A stable typed observable proxy over <see cref="Model"/>.</summary>
    public TObservable Observable => _observable;

    /// <summary>Whether the current model differs semantically from the retained baseline.</summary>
    public bool HasChanges
    {
        get
        {
            try
            {
                return !_isEmpty(CreateChangeSet());
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    /// <summary>Raised when session state or its observable model may have changed.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Derives the baseline-aware change set between the retained baseline and current model.</summary>
    public TChangeSet CreateChangeSet() =>
        _between(_baseline, Optional<TFragment?>.Present(_fromModel(Model)));

    /// <summary>Derives the baseline-free patch from the current baseline-aware change set.</summary>
    public TPatch CreatePatch() => _toPatch(CreateChangeSet());

    /// <summary>Accepts the current model state as the new baseline.</summary>
    /// <remarks>
    /// The candidate baseline is validated before it is retained: a live model
    /// containing unassigned keyed sentinels or duplicate stable keys fails fast
    /// instead of leaving future diffs unusable.
    /// </remarks>
    public void AcceptChanges()
    {
        var candidate = Optional<TFragment?>.Present(_fromModel(Model));
        // Fail-fast when the live model cannot serve as a keyed baseline.
        // Probed against the retained baseline (distinct instances) because a
        // self-diff short-circuits on reference equality without validating keys.
        _between(candidate, _baseline);
        _baseline = candidate;
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>
    /// Advances the retained baseline by the supplied transition without touching the live model.
    /// </summary>
    /// <remarks>
    /// The transition's before-state must match the retained baseline on every changed path;
    /// edits made after the change set was captured stay pending against the new baseline.
    /// The candidate baseline is fully computed before commit, so a rejected transition
    /// leaves the retained baseline unchanged. A transition that would promote unassigned
    /// keyed sentinels into the baseline is rejected atomically; for server-assigned IDs,
    /// reload the authoritative model and create a fresh session instead of acknowledging
    /// the unassigned Add.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The transition is stale or incompatible with the retained baseline, or advancing
    /// would not produce a valid model state.
    /// </exception>
    public void AcceptChanges(TChangeSet changes)
    {
        if (changes is null)
            throw new ArgumentNullException(nameof(changes));
        // Validate and project first; only a valid candidate replaces the baseline.
        // The live model and observable proxy are never touched here.
        var advanced = _advanceBaseline(changes, _baseline);
        if (!advanced.IsPresent || advanced.Value is null)
            throw new InvalidOperationException(
                "Advancing the baseline did not produce a valid model state."
            );
        try
        {
            // Reject candidates that would break future keyed diffs, such as
            // unassigned keyed sentinels. Probed against the retained baseline
            // (distinct instances) because a self-diff short-circuits on
            // reference equality without validating keys.
            _between(advanced, _baseline);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Accepting this change set would retain an invalid baseline, such as unassigned keyed sentinels.",
                ex
            );
        }
        _baseline = advanced;
        OnPropertyChanged(nameof(HasChanges));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
