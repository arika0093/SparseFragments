using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SparseFragments;

/// <summary>Coordinates typed edit sessions as independent sections of a submission.</summary>
/// <remarks>
/// The group owns section identity and acknowledgement revisions. Session edits may
/// continue after preparation; acknowledging the captured change set advances only
/// that transition, leaving later edits pending. The group does not provide a
/// transaction across sessions or an implicit transport or persistence layer.
/// </remarks>
public sealed class EditSessionGroup
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IEditSessionGroupSection> _sections = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, long> _baselineRevisions = new(StringComparer.Ordinal);

    /// <summary>Registers a unique stable section identifier and its typed session.</summary>
    /// <param name="sectionId">A non-empty identifier, compared with ordinal case-sensitive rules.</param>
    /// <param name="registration">Typed operations and readiness checks for the session.</param>
    /// <returns>A typed handle used to read and acknowledge submitted changes.</returns>
    /// <exception cref="ArgumentException">The identifier is invalid or already registered.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is null.</exception>
    public EditSessionSectionHandle<TModel, TChangeSet> Register<TModel, TChangeSet>(
        string sectionId,
        EditSessionGroupRegistration<TModel, TChangeSet> registration
    )
        where TModel : class
        where TChangeSet : class
    {
        ValidateSectionId(sectionId);
        ValidateRegistration(registration);
        lock (_gate)
        {
            if (_sections.ContainsKey(sectionId))
                throw new ArgumentException(
                    $"Section '{sectionId}' is already registered. Use Replace to replace it explicitly.",
                    nameof(sectionId)
                );

            var revision = _baselineRevisions.TryGetValue(sectionId, out var previousRevision)
                ? checked(previousRevision + 1)
                : 0;
            var section = new EditSessionGroupSection<TModel, TChangeSet>(
                sectionId,
                revision,
                registration
            );
            _sections.Add(sectionId, section);
            _baselineRevisions[sectionId] = revision;
            return new EditSessionSectionHandle<TModel, TChangeSet>(this, section);
        }
    }

    /// <summary>Removes a section using an explicit policy for dirty sessions and open drafts.</summary>
    /// <param name="handle">The current typed section handle.</param>
    /// <param name="policy">Whether removal must reject dirty state or detach it from the group.</param>
    public void Unregister<TModel, TChangeSet>(
        EditSessionSectionHandle<TModel, TChangeSet> handle,
        EditSessionGroupRemovalPolicy policy
    )
        where TModel : class
        where TChangeSet : class
    {
        lock (_gate)
        {
            var section = GetCurrentSection(handle);
            EnsureRemovalAllowed(section, policy);
            _sections.Remove(section.SectionId);
        }
    }

    /// <summary>Replaces a registration using an explicit policy for its pending state.</summary>
    /// <param name="handle">The current typed section handle.</param>
    /// <param name="registration">The replacement session and its typed operations.</param>
    /// <param name="policy">Whether replacement must reject dirty state or discard the registration.</param>
    /// <returns>A new typed handle; the previous handle and its submissions become invalid.</returns>
    public EditSessionSectionHandle<TModel, TChangeSet> Replace<TModel, TChangeSet>(
        EditSessionSectionHandle<TModel, TChangeSet> handle,
        EditSessionGroupRegistration<TModel, TChangeSet> registration,
        EditSessionGroupRemovalPolicy policy
    )
        where TModel : class
        where TChangeSet : class
    {
        ValidateRegistration(registration);
        lock (_gate)
        {
            var previous = GetCurrentSection(handle);
            EnsureRemovalAllowed(previous, policy);
            var revision = checked(previous.BaselineRevision + 1);
            var replacement = new EditSessionGroupSection<TModel, TChangeSet>(
                previous.SectionId,
                revision,
                registration
            );
            _sections[previous.SectionId] = replacement;
            _baselineRevisions[previous.SectionId] = revision;
            return new EditSessionSectionHandle<TModel, TChangeSet>(this, replacement);
        }
    }

    /// <summary>Captures an immutable submission and validates registered sections synchronously.</summary>
    /// <exception cref="EditSessionGroupValidationException">A section is unready, has an open draft, or cannot be captured.</exception>
    public EditSessionGroupSubmission Prepare()
    {
        var captures = CaptureSections();
        var issues = new List<EditSessionGroupValidationIssue>();
        foreach (var section in captures.Select(static capture => capture.Section))
        {
            if (section.HasAsyncValidation)
            {
                issues.Add(
                    new EditSessionGroupValidationIssue(
                        section.SectionId,
                        "Asynchronous validation is configured; use PrepareAsync."
                    )
                );
                continue;
            }

            if (!section.TryValidate(out var error))
                issues.Add(new EditSessionGroupValidationIssue(section.SectionId, error!));
        }

        ThrowIfInvalid(issues);
        return CreateSubmission(captures);
    }

    /// <summary>
    /// Captures all registered change sets synchronously, then validates the immutable capture asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel asynchronous validation.</param>
    /// <returns>The immutable submission after all section validation succeeds.</returns>
    /// <exception cref="EditSessionGroupValidationException">A section is unready, has an open draft, or fails validation.</exception>
    /// <exception cref="OperationCanceledException">Validation is canceled.</exception>
    public Task<EditSessionGroupSubmission> PrepareAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captures = CaptureSections();
        cancellationToken.ThrowIfCancellationRequested();
        var submission = CreateSubmission(captures);
        return ValidateAsynchronously(captures, submission, cancellationToken);
    }

    /// <summary>Acknowledges one submitted transition while preserving later session edits.</summary>
    /// <param name="submission">The submission that contains the transition.</param>
    /// <param name="handle">The current typed section handle.</param>
    /// <exception cref="EditSessionStaleSubmissionException">The submission is unrelated, stale, or its registration was replaced.</exception>
    public void AcceptChanges<TModel, TChangeSet>(
        EditSessionGroupSubmission submission,
        EditSessionSectionHandle<TModel, TChangeSet> handle
    )
        where TModel : class
        where TChangeSet : class
    {
        lock (_gate)
        {
            var section = GetCurrentSection(handle);
            var entry = GetCurrentSubmissionEntry(submission, section);
            try
            {
                section.AcceptChanges(entry);
            }
            catch (InvalidOperationException exception)
            {
                throw new EditSessionStaleSubmissionException(
                    $"The submitted transition for section '{section.SectionId}' no longer matches its session baseline.",
                    exception
                );
            }

            AdvanceBaselineRevision(section);
        }
    }

    /// <summary>Attempts to reconcile one submitted transition with its authoritative persisted model.</summary>
    /// <param name="submission">The submission that contains the transition.</param>
    /// <param name="handle">The current typed section handle.</param>
    /// <param name="persisted">The authoritative persisted model.</param>
    /// <param name="error">The reconciliation failure, or an unrelated or stale submission reason.</param>
    /// <returns><see langword="true"/> when the section was reconciled; otherwise <see langword="false"/>.</returns>
    public bool TryReconcile<TModel, TChangeSet>(
        EditSessionGroupSubmission submission,
        EditSessionSectionHandle<TModel, TChangeSet> handle,
        TModel persisted,
        out string? error
    )
        where TModel : class
        where TChangeSet : class
    {
        if (persisted is null)
            throw new ArgumentNullException(nameof(persisted));
        lock (_gate)
        {
            if (
                !TryGetCurrentSubmissionSection(
                    submission,
                    handle,
                    out var section,
                    out var entry,
                    out error
                )
            )
                return false;
            if (!section.TryReconcile(entry!, persisted, out error))
                return false;
            AdvanceBaselineRevision(section);
            return true;
        }
    }

    private IEditSessionGroupSection GetCurrentSection<TModel, TChangeSet>(
        EditSessionSectionHandle<TModel, TChangeSet> handle
    )
        where TModel : class
        where TChangeSet : class
    {
        if (handle is null)
            throw new ArgumentNullException(nameof(handle));
        if (!ReferenceEquals(handle.Owner, this))
            throw new EditSessionStaleSubmissionException(
                "The section handle belongs to another group."
            );
        if (
            !_sections.TryGetValue(handle.SectionId, out var current)
            || !ReferenceEquals(current, handle.Section)
        )
            throw new EditSessionStaleSubmissionException(
                $"Section '{handle.SectionId}' was unregistered or replaced."
            );
        return current;
    }

    private IReadOnlyList<EditSessionGroupCapture> CaptureSections()
    {
        lock (_gate)
        {
            var captures = new List<EditSessionGroupCapture>(_sections.Count);
            var issues = new List<EditSessionGroupValidationIssue>();
            var registeredSections = _sections
                .Values.OrderBy(value => value.SectionId, StringComparer.Ordinal)
                .ToArray();
            foreach (var section in registeredSections)
            {
                if (!section.TryCapture(out var capture, out var error))
                {
                    issues.Add(new EditSessionGroupValidationIssue(section.SectionId, error!));
                    continue;
                }

                captures.Add(capture!);
            }

            ThrowIfInvalid(issues);
            return Array.AsReadOnly(captures.ToArray());
        }
    }

    private EditSessionGroupSubmission CreateSubmission(
        IReadOnlyList<EditSessionGroupCapture> captures
    )
    {
        var entries = captures
            .Where(static capture => capture.SubmissionEntry is not null)
            .Select(static capture => capture.SubmissionEntry!)
            .ToArray();
        return new EditSessionGroupSubmission(this, Array.AsReadOnly(entries));
    }

    private static void ThrowIfInvalid(List<EditSessionGroupValidationIssue> issues)
    {
        if (issues.Count != 0)
            throw new EditSessionGroupValidationException(Array.AsReadOnly(issues.ToArray()));
    }

    private static async Task<EditSessionGroupSubmission> ValidateAsynchronously(
        IReadOnlyList<EditSessionGroupCapture> captures,
        EditSessionGroupSubmission submission,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<EditSessionGroupValidationIssue>();
        foreach (var capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!capture.Section.TryValidate(out var syncError))
                issues.Add(
                    new EditSessionGroupValidationIssue(capture.Section.SectionId, syncError!)
                );

            if (!capture.Section.HasAsyncValidation)
                continue;
            try
            {
                var asyncError = await capture.Section.ValidateAsync(
                    capture.Changes,
                    cancellationToken
                );
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(asyncError))
                    issues.Add(
                        new EditSessionGroupValidationIssue(capture.Section.SectionId, asyncError!)
                    );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                issues.Add(
                    new EditSessionGroupValidationIssue(
                        capture.Section.SectionId,
                        exception.Message
                    )
                );
            }
        }

        foreach (var section in captures.Select(static capture => capture.Section))
        {
            if (
                !section.TryValidateOpenDrafts(out var draftError)
                && !issues.Any(issue =>
                    issue.SectionId == section.SectionId && issue.Message == draftError
                )
            )
                issues.Add(new EditSessionGroupValidationIssue(section.SectionId, draftError!));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfInvalid(issues);
        return submission;
    }

    private EditSessionSubmittedSection GetCurrentSubmissionEntry(
        EditSessionGroupSubmission submission,
        IEditSessionGroupSection section
    )
    {
        if (submission is null)
            throw new ArgumentNullException(nameof(submission));
        if (!submission.BelongsTo(this) || !submission.TryGet(section, out var entry))
            throw new EditSessionStaleSubmissionException(
                $"The submission does not contain section '{section.SectionId}'."
            );
        if (entry!.BaselineRevision != section.BaselineRevision)
            throw new EditSessionStaleSubmissionException(
                $"The submission for section '{section.SectionId}' was prepared against an older baseline revision."
            );
        return entry;
    }

    private bool TryGetCurrentSubmissionSection<TModel, TChangeSet>(
        EditSessionGroupSubmission? submission,
        EditSessionSectionHandle<TModel, TChangeSet>? handle,
        out IEditSessionGroupSection section,
        out EditSessionSubmittedSection? entry,
        out string? error
    )
        where TModel : class
        where TChangeSet : class
    {
        section = null!;
        entry = null;
        error = null;
        if (submission is null)
        {
            error = "The submission is null.";
            return false;
        }

        if (!submission.BelongsTo(this))
        {
            error = "The submission belongs to another group.";
            return false;
        }

        if (handle is null || !ReferenceEquals(handle.Owner, this))
        {
            error = "The section handle is null or belongs to another group.";
            return false;
        }

        if (
            !_sections.TryGetValue(handle.SectionId, out section)
            || !ReferenceEquals(section, handle.Section)
        )
        {
            error = $"Section '{handle.SectionId}' was unregistered or replaced.";
            return false;
        }

        if (!submission.TryGet(section, out entry))
        {
            error = $"The submission does not contain section '{section.SectionId}'.";
            return false;
        }

        if (entry!.BaselineRevision != section.BaselineRevision)
        {
            error =
                $"The submission for section '{section.SectionId}' was prepared against an older baseline revision.";
            return false;
        }

        return true;
    }

    private static void EnsureRemovalAllowed(
        IEditSessionGroupSection section,
        EditSessionGroupRemovalPolicy policy
    )
    {
        if (policy == EditSessionGroupRemovalPolicy.DetachWithPendingChanges)
            return;
        if (policy != EditSessionGroupRemovalPolicy.RequireClean)
            throw new ArgumentOutOfRangeException(nameof(policy));
        if (section.HasOpenDrafts || section.HasChanges())
            throw new InvalidOperationException(
                $"Section '{section.SectionId}' has pending changes or open drafts. Use DetachWithPendingChanges to remove it explicitly."
            );
    }

    private void AdvanceBaselineRevision(IEditSessionGroupSection section)
    {
        var revision = checked(section.BaselineRevision + 1);
        section.BaselineRevision = revision;
        _baselineRevisions[section.SectionId] = revision;
    }

    private static void ValidateSectionId(string sectionId)
    {
        if (
            string.IsNullOrWhiteSpace(sectionId)
            || !string.Equals(sectionId, sectionId.Trim(), StringComparison.Ordinal)
        )
            throw new ArgumentException(
                "A section identifier must be non-empty and have no surrounding whitespace.",
                nameof(sectionId)
            );
    }

    private static void ValidateRegistration<TModel, TChangeSet>(
        EditSessionGroupRegistration<TModel, TChangeSet> registration
    )
        where TModel : class
        where TChangeSet : class
    {
        if (registration is null)
            throw new ArgumentNullException(nameof(registration));
        if (registration.Session is null)
            throw new ArgumentException("A session is required.", nameof(registration));
        if (registration.CreateChangeSet is null)
            throw new ArgumentException(
                "A change-set capture delegate is required.",
                nameof(registration)
            );
        if (registration.IsEmpty is null)
            throw new ArgumentException(
                "An empty-check delegate is required.",
                nameof(registration)
            );
        if (registration.EnumerateChangedPaths is null)
            throw new ArgumentException(
                "A changed-path delegate is required.",
                nameof(registration)
            );
        if (
            registration.HasOpenDrafts is null
            && registration.Session is not ISparseEditSessionOpenDraftState
        )
            throw new ArgumentException(
                "Provide HasOpenDrafts or implement ISparseEditSessionOpenDraftState so open drafts cannot be submitted.",
                nameof(registration)
            );
    }

    private sealed class EditSessionGroupSection<TModel, TChangeSet> : IEditSessionGroupSection
        where TModel : class
        where TChangeSet : class
    {
        public EditSessionGroupSection(
            string sectionId,
            long baselineRevision,
            EditSessionGroupRegistration<TModel, TChangeSet> registration
        )
        {
            SectionId = sectionId;
            BaselineRevision = baselineRevision;
            Registration = registration;
        }

        public string SectionId { get; }

        public long BaselineRevision { get; set; }

        public EditSessionGroupRegistration<TModel, TChangeSet> Registration { get; }

        public object Session => Registration.Session;

        public bool HasAsyncValidation => Registration.ValidateAsync is not null;

        public bool HasOpenDrafts =>
            Registration.HasOpenDrafts?.Invoke()
            ?? ((ISparseEditSessionOpenDraftState)Registration.Session).HasOpenDrafts;

        public bool HasChanges()
        {
            var changes = Registration.CreateChangeSet();
            return !Registration.IsEmpty(changes);
        }

        public bool TryCapture(out EditSessionGroupCapture? capture, out string? error)
        {
            capture = null;
            error = null;
            try
            {
                var changes = Registration.CreateChangeSet();
                if (changes is null)
                {
                    error = "The change-set delegate returned null.";
                    return false;
                }

                if (Registration.IsEmpty(changes))
                {
                    capture = new EditSessionGroupCapture(this, changes, null);
                    return true;
                }

                var changedPaths = Registration.EnumerateChangedPaths(changes);
                if (changedPaths is null)
                {
                    error = "The changed-path delegate returned null.";
                    return false;
                }

                var submission = new EditSessionSubmissionEntry<TChangeSet>(
                    this,
                    SectionId,
                    BaselineRevision,
                    changes,
                    Array.AsReadOnly(changedPaths.ToArray())
                );
                capture = new EditSessionGroupCapture(this, changes, submission);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryValidate(out string? error)
        {
            if (!TryValidateOpenDrafts(out error))
                return false;
            error = null;
            try
            {
                var validationError = Registration.Validate?.Invoke();
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    error = validationError;
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryValidateOpenDrafts(out string? error)
        {
            error = null;
            try
            {
                if (!HasOpenDrafts)
                    return true;
                error = "The section has an open edit draft.";
                return false;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
                return false;
            }
        }

        public async Task<string?> ValidateAsync(
            object changes,
            CancellationToken cancellationToken
        )
        {
            if (Registration.ValidateAsync is null)
                return null;
            return await Registration
                .ValidateAsync((TChangeSet)changes, cancellationToken)
                .ConfigureAwait(false);
        }

        public void AcceptChanges(EditSessionSubmittedSection submission)
        {
            var typed = (EditSessionSubmissionEntry<TChangeSet>)submission;
            Registration.Session.AcceptChanges(typed.Changes);
        }

        public bool TryReconcile(
            EditSessionSubmittedSection submission,
            object persisted,
            out string? error
        )
        {
            if (Registration.TryReconcile is null)
            {
                error = "The section has no reconciliation delegate.";
                return false;
            }

            var typed = (EditSessionSubmissionEntry<TChangeSet>)submission;
            var result = Registration.TryReconcile(typed.Changes, (TModel)persisted);
            error = result.Error;
            return result.Success;
        }
    }
}

internal interface IEditSessionGroupSection
{
    string SectionId { get; }

    object Session { get; }

    long BaselineRevision { get; set; }

    bool HasOpenDrafts { get; }

    bool HasAsyncValidation { get; }

    bool HasChanges();

    bool TryCapture(out EditSessionGroupCapture? capture, out string? error);

    bool TryValidate(out string? error);

    bool TryValidateOpenDrafts(out string? error);

    Task<string?> ValidateAsync(object changes, CancellationToken cancellationToken);

    void AcceptChanges(EditSessionSubmittedSection submission);

    bool TryReconcile(EditSessionSubmittedSection submission, object persisted, out string? error);
}

internal sealed class EditSessionGroupCapture
{
    public EditSessionGroupCapture(
        IEditSessionGroupSection section,
        object changes,
        EditSessionSubmittedSection? submissionEntry
    )
    {
        Section = section;
        Changes = changes;
        SubmissionEntry = submissionEntry;
    }

    public IEditSessionGroupSection Section { get; }

    public object Changes { get; }

    public EditSessionSubmittedSection? SubmissionEntry { get; }
}
