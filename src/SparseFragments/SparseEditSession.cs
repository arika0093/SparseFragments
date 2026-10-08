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
    private readonly TObservable _observable;
    private Optional<TFragment?> _baseline;

    private SparseEditSession(
        TModel model,
        TFragment baseline,
        Func<TModel, TFragment> fromModel,
        Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> between,
        Func<TChangeSet, TPatch> toPatch,
        Func<TChangeSet, bool> isEmpty,
        Func<TModel, TObservable> toObservable
    )
    {
        Model = model;
        _observable = toObservable(model);
        _fromModel = fromModel;
        _between = between;
        _toPatch = toPatch;
        _isEmpty = isEmpty;
        _baseline = Optional<TFragment?>.Present(baseline);
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
            toObservable
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
            toObservable
        );
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

    /// <summary>Derives the baseline-aware change set between the retained baseline and current model.</summary>
    public TChangeSet CreateChangeSet() =>
        _between(_baseline, Optional<TFragment?>.Present(_fromModel(Model)));

    /// <summary>Derives the baseline-free patch from the current baseline-aware change set.</summary>
    public TPatch CreatePatch() => _toPatch(CreateChangeSet());

    /// <summary>Accepts the current model state as the new baseline.</summary>
    public void AcceptChanges() => _baseline = Optional<TFragment?>.Present(_fromModel(Model));
}
