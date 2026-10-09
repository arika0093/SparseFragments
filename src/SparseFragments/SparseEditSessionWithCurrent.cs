using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseFragments;

/// <summary>Generated operations used to create a typed edit session.</summary>
/// <typeparam name="TModel">The live model type.</typeparam>
/// <typeparam name="TFragment">The generated fragment type.</typeparam>
/// <typeparam name="TPatch">The generated patch type.</typeparam>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
/// <typeparam name="TObservable">The generated observable proxy type.</typeparam>
/// <typeparam name="TCurrent">The generated read-only model view type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditSessionConfiguration<
    TModel,
    TFragment,
    TPatch,
    TChangeSet,
    TObservable,
    TCurrent
>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
    where TCurrent : class
{
    /// <summary>Captures a model as a fragment.</summary>
    public Func<TModel, TFragment> FromModel { get; init; } = null!;

    /// <summary>Derives a change set between two sparse fragment states.</summary>
    public Func<Optional<TFragment?>, Optional<TFragment?>, TChangeSet> Between { get; init; } =
        null!;

    /// <summary>Projects a change set to a baseline-free patch.</summary>
    public Func<TChangeSet, TPatch> ToPatch { get; init; } = null!;

    /// <summary>Tests whether a change set is semantically empty.</summary>
    public Func<TChangeSet, bool> IsEmpty { get; init; } = null!;

    /// <summary>Advances a baseline by a change set.</summary>
    public Func<
        TChangeSet,
        Optional<TFragment?>,
        Optional<TFragment?>
    > AdvanceBaseline { get; init; } = null!;

    /// <summary>Creates an observable proxy with edit and raw-model access callbacks.</summary>
    public Func<TModel, Action?, Action?, TObservable> ToObservable { get; init; } = null!;

    /// <summary>Creates the recursive read-only view for a model.</summary>
    public Func<TModel, TCurrent> ToCurrent { get; init; } = null!;

    /// <summary>Attempts to apply a change set and returns its updated model or conflicts.</summary>
    public Func<
        TChangeSet,
        TModel,
        (TModel? Updated, IReadOnlyList<SparseConflict>? Conflicts)
    >? TryApplyTo { get; init; }

    /// <summary>Writes an updated model into the existing live model instance.</summary>
    public Action<TModel, TModel>? WriteModel { get; init; }

    /// <summary>Inverts a change set.</summary>
    public Func<TChangeSet, TChangeSet>? Invert { get; init; }

    /// <summary>Rebases a change set onto a newer model state.</summary>
    public Func<TChangeSet, TModel, RebaseResult<TChangeSet>>? Rebase { get; init; }

    /// <summary>Enumerates changed paths from a generated change set.</summary>
    public Func<TChangeSet, IReadOnlyList<string>>? EnumerateChangedPaths { get; init; }

    /// <summary>Refreshes notifications on an observable proxy after an in-place update.</summary>
    public Action<TObservable>? RefreshObservable { get; init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(FromModel);
        ArgumentNullException.ThrowIfNull(Between);
        ArgumentNullException.ThrowIfNull(ToPatch);
        ArgumentNullException.ThrowIfNull(IsEmpty);
        ArgumentNullException.ThrowIfNull(AdvanceBaseline);
        ArgumentNullException.ThrowIfNull(ToObservable);
        ArgumentNullException.ThrowIfNull(ToCurrent);
    }
}

/// <summary>A typed edit session with a recursive read-only current model view.</summary>
/// <typeparam name="TModel">The editable model type.</typeparam>
/// <typeparam name="TFragment">The generated fragment type.</typeparam>
/// <typeparam name="TPatch">The generated patch type.</typeparam>
/// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
/// <typeparam name="TObservable">The generated mutable observable proxy type.</typeparam>
/// <typeparam name="TCurrent">The generated recursive read-only model view type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable, TCurrent>
    : SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable>
    where TModel : class
    where TFragment : class
    where TPatch : class
    where TChangeSet : class
    where TObservable : class
    where TCurrent : class
{
    private readonly TCurrent _current;

    private SparseEditSession(
        TModel model,
        TFragment baseline,
        SparseEditSessionConfiguration<
            TModel,
            TFragment,
            TPatch,
            TChangeSet,
            TObservable,
            TCurrent
        > configuration,
        Action? onChanged
    )
        : base(
            model,
            baseline,
            configuration.FromModel,
            configuration.Between,
            configuration.ToPatch,
            configuration.IsEmpty,
            configuration.AdvanceBaseline,
            configuration.ToObservable,
            onChanged,
            configuration.TryApplyTo,
            configuration.WriteModel,
            configuration.Invert,
            configuration.Rebase,
            configuration.EnumerateChangedPaths,
            configuration.RefreshObservable,
            cacheObservableChanges: true
        )
    {
        _current = configuration.ToCurrent(model);
    }

    /// <summary>Creates a session using one model as the baseline and live editable value.</summary>
    public static SparseEditSession<
        TModel,
        TFragment,
        TPatch,
        TChangeSet,
        TObservable,
        TCurrent
    > Create(
        TModel model,
        SparseEditSessionConfiguration<
            TModel,
            TFragment,
            TPatch,
            TChangeSet,
            TObservable,
            TCurrent
        > configuration,
        Action? onChanged = null
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateConfiguration(configuration);
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable, TCurrent>(
            model,
            configuration.FromModel(model),
            configuration,
            onChanged
        );
    }

    /// <summary>Creates a session editing a current model against a separate baseline.</summary>
    public static SparseEditSession<
        TModel,
        TFragment,
        TPatch,
        TChangeSet,
        TObservable,
        TCurrent
    > Create(
        TModel baseline,
        TModel current,
        SparseEditSessionConfiguration<
            TModel,
            TFragment,
            TPatch,
            TChangeSet,
            TObservable,
            TCurrent
        > configuration,
        Action? onChanged = null
    )
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ValidateConfiguration(configuration);
        return new SparseEditSession<TModel, TFragment, TPatch, TChangeSet, TObservable, TCurrent>(
            current,
            configuration.FromModel(baseline),
            configuration,
            onChanged
        );
    }

    /// <summary>The live model through a recursive read-only view.</summary>
    public TCurrent Current => _current;

    private static void ValidateConfiguration(
        SparseEditSessionConfiguration<
            TModel,
            TFragment,
            TPatch,
            TChangeSet,
            TObservable,
            TCurrent
        > configuration
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
    }
}
