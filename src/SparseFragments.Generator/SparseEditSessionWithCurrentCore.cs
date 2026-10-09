#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using SparseFragments;

namespace SparseFragments.__GeneratedSessionCore
{
    /// <summary>Generated operations used to create a typed edit session.</summary>
    /// <typeparam name="TModel">The live model type.</typeparam>
    /// <typeparam name="TFragment">The generated fragment type.</typeparam>
    /// <typeparam name="TPatch">The generated patch type.</typeparam>
    /// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
    /// <typeparam name="TObservable">The generated observable proxy type.</typeparam>
    /// <typeparam name="TCurrent">The generated read-only model view type.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal sealed class EditSessionCoreConfiguration<
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
            if (FromModel is null)
                throw new ArgumentNullException(nameof(FromModel));
            if (Between is null)
                throw new ArgumentNullException(nameof(Between));
            if (ToPatch is null)
                throw new ArgumentNullException(nameof(ToPatch));
            if (IsEmpty is null)
                throw new ArgumentNullException(nameof(IsEmpty));
            if (AdvanceBaseline is null)
                throw new ArgumentNullException(nameof(AdvanceBaseline));
            if (ToObservable is null)
                throw new ArgumentNullException(nameof(ToObservable));
            if (ToCurrent is null)
                throw new ArgumentNullException(nameof(ToCurrent));
        }
    }

    /// <summary>A typed edit session with a recursive read-only current model view.</summary>
    /// <typeparam name="TModel">The editable model type.</typeparam>
    /// <typeparam name="TFragment">The generated fragment type.</typeparam>
    /// <typeparam name="TPatch">The generated patch type.</typeparam>
    /// <typeparam name="TChangeSet">The generated change-set type.</typeparam>
    /// <typeparam name="TObservable">The generated mutable observable proxy type.</typeparam>
    /// <typeparam name="TCurrent">The generated recursive read-only model view type.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable, TCurrent>
        : EditSessionCore<TModel, TFragment, TPatch, TChangeSet, TObservable>
        where TModel : class
        where TFragment : class
        where TPatch : class
        where TChangeSet : class
        where TObservable : class
        where TCurrent : class
    {
        private readonly TCurrent _current;

        /// <summary>Initializes a generated edit-session specialization.</summary>
        /// <param name="baseline">The model state to retain as the baseline.</param>
        /// <param name="current">The live editable model.</param>
        /// <param name="configuration">Generated operations for the model.</param>
        /// <param name="onChanged">An optional callback raised after observable edits.</param>
        protected EditSessionCore(
            TModel baseline,
            TModel current,
            EditSessionCoreConfiguration<
                TModel,
                TFragment,
                TPatch,
                TChangeSet,
                TObservable,
                TCurrent
            > configuration,
            Action? onChanged
        )
            : this(
                current,
                CaptureBaseline(baseline, current, configuration),
                configuration,
                onChanged
            ) { }

        private EditSessionCore(
            TModel model,
            TFragment baseline,
            EditSessionCoreConfiguration<
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
        public static EditSessionCore<
            TModel,
            TFragment,
            TPatch,
            TChangeSet,
            TObservable,
            TCurrent
        > Create(
            TModel model,
            EditSessionCoreConfiguration<
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
            if (model is null)
                throw new ArgumentNullException(nameof(model));
            ValidateConfiguration(configuration);
            return new EditSessionCore<
                TModel,
                TFragment,
                TPatch,
                TChangeSet,
                TObservable,
                TCurrent
            >(model, model, configuration, onChanged);
        }

        /// <summary>Creates a session editing a current model against a separate baseline.</summary>
        public static EditSessionCore<
            TModel,
            TFragment,
            TPatch,
            TChangeSet,
            TObservable,
            TCurrent
        > Create(
            TModel baseline,
            TModel current,
            EditSessionCoreConfiguration<
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
            if (baseline is null)
                throw new ArgumentNullException(nameof(baseline));
            if (current is null)
                throw new ArgumentNullException(nameof(current));
            ValidateConfiguration(configuration);
            return new EditSessionCore<
                TModel,
                TFragment,
                TPatch,
                TChangeSet,
                TObservable,
                TCurrent
            >(baseline, current, configuration, onChanged);
        }

        /// <summary>The live model through a recursive read-only view.</summary>
        public TCurrent Current => _current;

        private static TFragment CaptureBaseline(
            TModel baseline,
            TModel current,
            EditSessionCoreConfiguration<
                TModel,
                TFragment,
                TPatch,
                TChangeSet,
                TObservable,
                TCurrent
            > configuration
        )
        {
            if (baseline is null)
                throw new ArgumentNullException(nameof(baseline));
            if (current is null)
                throw new ArgumentNullException(nameof(current));
            ValidateConfiguration(configuration);
            return configuration.FromModel(baseline);
        }

        private static void ValidateConfiguration(
            EditSessionCoreConfiguration<
                TModel,
                TFragment,
                TPatch,
                TChangeSet,
                TObservable,
                TCurrent
            > configuration
        )
        {
            if (configuration is null)
                throw new ArgumentNullException(nameof(configuration));
            configuration.Validate();
        }
    }
}
