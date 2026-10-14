using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SparseTypes;

/// <summary>Coordinates typed edit sessions as independent sections of a submission.</summary>
/// <remarks>
/// The group owns section identity and acknowledgement revisions. Session edits may
/// continue after preparation; acknowledging the captured change set advances only
/// that transition, leaving later edits pending. The group does not provide a
/// transaction across sessions or an implicit transport or persistence layer.
/// A session instance backs at most one live section: registering the same instance
/// twice is rejected because the two sections would share one session baseline.
/// Acknowledgement should flow through the group. External acknowledgement of a
/// registered session (direct <c>AcceptChanges</c>, <c>TryReconcile</c>, or reload)
/// moves the session baseline without moving group revisions; the next group
/// acknowledgement detects the outcome, preserves the original session error, and
/// resynchronizes revisions, but callers must re-prepare afterwards. Prefer
/// <c>Unregister</c> or <c>Replace</c> before acknowledging a session outside the group.
/// </remarks>
public sealed class EditSessionGroup
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IEditSessionGroupSection> _sections = new(
        StringComparer.Ordinal
    );
    private readonly Dictionary<string, long> _baselineRevisions = new(StringComparer.Ordinal);
    private readonly HashSet<object> _sessions = new(ReferenceIdentityComparer.Instance);

    /// <summary>Registers a unique stable section identifier and its typed session.</summary>
    /// <param name="sectionId">A non-empty identifier, compared with ordinal case-sensitive rules.</param>
    /// <param name="registration">Typed operations and readiness checks for the session.</param>
    /// <returns>A typed handle used to read and acknowledge submitted changes.</returns>
    /// <exception cref="ArgumentException">The identifier is invalid, already registered, or the session instance already backs another live section.</exception>
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

            if (!_sessions.Add(registration.Session))
                throw new ArgumentException(
                    $"The session is already registered as section '{FindSectionId(registration.Session)}'. A session instance cannot back multiple live sections.",
                    nameof(registration)
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

    /// <summary>Removes a section using an explicit policy for dirty sessions.</summary>
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
            _sessions.Remove(section.Session);
        }
    }

    /// <summary>Replaces a registration using an explicit policy for its pending state.</summary>
    /// <param name="handle">The current typed section handle.</param>
    /// <param name="registration">The replacement session and its typed operations.</param>
    /// <param name="policy">Whether replacement must reject dirty state or discard the registration.</param>
    /// <returns>A new typed handle; the previous handle and its submissions become invalid.</returns>
    /// <exception cref="ArgumentException">The replacement session instance already backs another live section.</exception>
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
            // Release first so replacing a section with its own session stays valid.
            _sessions.Remove(previous.Session);
            if (!_sessions.Add(registration.Session))
            {
                _sessions.Add(previous.Session);
                throw new ArgumentException(
                    $"The session is already registered as section '{FindSectionId(registration.Session)}'. A session instance cannot back multiple live sections.",
                    nameof(registration)
                );
            }

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
    /// <remarks>
    /// Preparation runs in order: capture immutable change sets, then evaluate the live-session
    /// readiness hook, then validate the captured change sets. Submission validators observe only
    /// the captured <c>TChangeSet</c> placed in the eventual submission; later session edits remain
    /// pending and do not alter what validators observe. The group never holds its internal gate
    /// across user callbacks.
    /// </remarks>
    /// <exception cref="EditSessionGroupValidationException">A section is unready or cannot be captured.</exception>
    public EditSessionGroupSubmission Prepare()
    {
        var captures = CaptureSections();
        var issues = new List<EditSessionGroupValidationIssue>();
        foreach (var capture in captures)
        {
            var section = capture.Section;
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

            if (!section.TryValidate(out var readinessError))
                issues.Add(new EditSessionGroupValidationIssue(section.SectionId, readinessError!));
            if (!section.TryValidateChanges(capture.Changes, out var changesError))
                issues.Add(new EditSessionGroupValidationIssue(section.SectionId, changesError!));
        }

        ThrowIfInvalid(issues);
        return CreateSubmission(captures);
    }

    /// <summary>
    /// Captures all registered change sets synchronously, then validates the immutable capture asynchronously.
    /// </summary>
    /// <remarks>
    /// Preparation runs in order: synchronously capture immutable change sets, evaluate the live-session
    /// readiness hook and synchronous captured validators, then await asynchronous validators that each
    /// receive the captured change set. Post-capture edits remain pending and do not retroactively alter
    /// what validators observe. After awaits the group re-evaluates whether a section was unregistered or
    /// replaced and whether its baseline revision advanced; such changes fail
    /// validation. Ordinary local edits do not invalidate the captured submission, and the live readiness
    /// hook is not re-evaluated after awaits. The group never holds its internal gate across user callbacks
    /// or awaits, so validators may call back into the group without deadlocking; structural changes made
    /// during validation fail the in-flight preparation. Synchronous validator exceptions (other than
    /// cancellation) become validation issues; asynchronous validator exceptions become validation issues
    /// while cancellation propagates as <see cref="OperationCanceledException"/>.
    /// </remarks>
    /// <param name="cancellationToken">The token used to cancel asynchronous validation.</param>
    /// <returns>The immutable submission after all section validation succeeds.</returns>
    /// <exception cref="EditSessionGroupValidationException">A section is unready or fails validation.</exception>
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
                if (section.IsTransitionAcknowledged(entry))
                {
                    // The session committed before failing (a post-commit
                    // notification handler threw, or the baseline moved
                    // outside the group): the submitted transition no longer
                    // pends, so the group revision must follow the session
                    // instead of reporting a stale submission. The original
                    // error is preserved untouched.
                    AdvanceBaselineRevision(section);
                    throw;
                }

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
        IEditSessionGroupSection[] registeredSections;
        lock (_gate)
        {
            // Snapshot identities only; user capture delegates run outside the gate
            // so re-entrant group calls from those delegates cannot deadlock.
            registeredSections = _sections
                .Values.OrderBy(value => value.SectionId, StringComparer.Ordinal)
                .ToArray();
        }

        var captures = new List<EditSessionGroupCapture>(registeredSections.Length);
        var issues = new List<EditSessionGroupValidationIssue>();
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

    private async Task<EditSessionGroupSubmission> ValidateAsynchronously(
        IReadOnlyList<EditSessionGroupCapture> captures,
        EditSessionGroupSubmission submission,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<EditSessionGroupValidationIssue>();
        // Phase 1: readiness (live session) and synchronous captured validation, no gate held.
        foreach (var capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!capture.Section.TryValidate(out var readinessError))
                issues.Add(
                    new EditSessionGroupValidationIssue(capture.Section.SectionId, readinessError!)
                );
            if (!capture.Section.TryValidateChanges(capture.Changes, out var changesError))
                issues.Add(
                    new EditSessionGroupValidationIssue(capture.Section.SectionId, changesError!)
                );
        }

        // Phase 2: asynchronous captured validation, no gate held across awaits.
        foreach (var capture in captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        // Phase 3: re-evaluate registration identity and baseline revisions after awaits.
        lock (_gate)
        {
            foreach (var capture in captures)
            {
                if (
                    !_sections.TryGetValue(capture.Section.SectionId, out var current)
                    || !ReferenceEquals(current, capture.Section)
                )
                {
                    issues.Add(
                        new EditSessionGroupValidationIssue(
                            capture.Section.SectionId,
                            $"Section '{capture.Section.SectionId}' was unregistered or replaced during validation."
                        )
                    );
                    continue;
                }

                if (current.BaselineRevision != capture.BaselineRevision)
                {
                    issues.Add(
                        new EditSessionGroupValidationIssue(
                            capture.Section.SectionId,
                            $"Section '{capture.Section.SectionId}' baseline changed during validation."
                        )
                    );
                    continue;
                }
            }
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
        if (section.HasChanges())
            throw new InvalidOperationException(
                $"Section '{section.SectionId}' has pending changes. Use DetachWithPendingChanges to remove it explicitly."
            );
    }

    private void AdvanceBaselineRevision(IEditSessionGroupSection section)
    {
        var revision = checked(section.BaselineRevision + 1);
        section.BaselineRevision = revision;
        _baselineRevisions[section.SectionId] = revision;
    }

    private string FindSectionId(object session) =>
        _sections
            .Values.Where(section => ReferenceEquals(section.Session, session))
            .Select(section => section.SectionId)
            .FirstOrDefault()
        ?? "?";

    private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

        bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
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
                    capture = new EditSessionGroupCapture(this, changes, BaselineRevision, null);
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
                capture = new EditSessionGroupCapture(this, changes, BaselineRevision, submission);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>Evaluates the live-session readiness hook.</summary>
        /// <remarks>
        /// May inspect the current mutable session. It does not observe the captured submission.
        /// </remarks>
        public bool TryValidate(out string? error)
        {
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

        /// <summary>Validates the captured change set without inspecting live session state.</summary>
        public bool TryValidateChanges(object changes, out string? error)
        {
            error = null;
            try
            {
                var validationError = Registration.ValidateChanges?.Invoke((TChangeSet)changes);
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

        public bool IsTransitionAcknowledged(EditSessionSubmittedSection entry)
        {
            try
            {
                var fresh = Registration.CreateChangeSet();
                if (fresh is null)
                    return false;
                if (Registration.IsEmpty(fresh))
                    return true;
                var freshPaths = Registration.EnumerateChangedPaths(fresh);
                if (freshPaths is null)
                    return false;
                var pending = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in freshPaths)
                    pending.Add(path.ToString());
                return entry.ChangedPaths.All(path => !pending.Contains(path.ToString()));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return false;
            }
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

    bool HasAsyncValidation { get; }

    bool HasChanges();

    bool TryCapture(out EditSessionGroupCapture? capture, out string? error);

    /// <summary>Evaluates the live-session readiness hook.</summary>
    bool TryValidate(out string? error);

    /// <summary>Validates the captured change set without inspecting live session state.</summary>
    bool TryValidateChanges(object changes, out string? error);

    Task<string?> ValidateAsync(object changes, CancellationToken cancellationToken);

    void AcceptChanges(EditSessionSubmittedSection submission);

    /// <summary>Tests whether a submitted transition no longer pends on the live session.</summary>
    /// <remarks>
    /// Used to tell a rejected acknowledgement (the transition still pends)
    /// from a post-commit failure (the session baseline advanced and only a
    /// later step, such as a notification handler, threw). Probes through the
    /// registration delegates and reports unknown as not acknowledged.
    /// </remarks>
    bool IsTransitionAcknowledged(EditSessionSubmittedSection entry);

    bool TryReconcile(EditSessionSubmittedSection submission, object persisted, out string? error);
}

internal sealed class EditSessionGroupCapture
{
    public EditSessionGroupCapture(
        IEditSessionGroupSection section,
        object changes,
        long baselineRevision,
        EditSessionSubmittedSection? submissionEntry
    )
    {
        Section = section;
        Changes = changes;
        BaselineRevision = baselineRevision;
        SubmissionEntry = submissionEntry;
    }

    public IEditSessionGroupSection Section { get; }

    public object Changes { get; }

    public long BaselineRevision { get; }

    public EditSessionSubmittedSection? SubmissionEntry { get; }
}
