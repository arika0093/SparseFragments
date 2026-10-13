# Edit-session groups

`EditSessionGroup` coordinates several typed edit sessions as sections of one
submission. It captures change sets and changed paths, then lets callers
acknowledge or reconcile each section independently. It does not provide
transport, persistence, cross-session transactions, or a global edit lock.

## Registration

Register each section with a stable, non-empty identifier. Identifiers use
ordinal, case-sensitive comparison. `Register` returns a typed
`EditSessionSectionHandle<TModel, TChangeSet>`; use that handle to retrieve the
captured change set and acknowledge it without casting heterogeneous sections.

An `EditSessionGroupRegistration<TModel, TChangeSet>` supplies the session,
delegates to create and inspect its generated change set, and a delegate to
enumerate paths from that change set. It can also supply `Validate` to report
section-specific readiness errors and `TryReconcile` to connect the generated
session's reconciliation operation. `ValidateAsync` receives the captured
change set and a cancellation token.

`Prepare` checks every registered section, including unchanged sections. It
rejects open drafts and readiness failures before returning a submission. A
generated session reports root and nested scopes through
`ISparseEditSessionOpenDraftState`. Custom sessions with open drafts must
provide `HasOpenDrafts` or implement that interface. Registration fails when
neither source can report draft state.

## Preparing and acknowledging changes

`Prepare` captures each non-empty generated change set and its changed paths,
then runs synchronous readiness checks. It rejects registrations with an
asynchronous validator; call `PrepareAsync(cancellationToken)` for those
sections. `PrepareAsync` captures every section synchronously before awaiting
validation. Each asynchronous validator receives that captured change set, so
edits made while validation is in progress do not change the submission.

The resulting `EditSessionGroupSubmission` exposes ordered section metadata,
typed entries through `Get(handle)`, and aggregate paths that retain both the
stable section identifier and the original `SparsePath`. Generated change sets
are immutable, so subsequent edits do not alter the captured snapshot.
Validation failures produce `EditSessionGroupValidationException`, and
`PrepareAsync` propagates cancellation as `OperationCanceledException`.

After a successful save, call `AcceptChanges(submission, handle)` for each
saved section. Each call advances only that section's baseline by the submitted
transition. Edits made after preparation remain pending. Acknowledgements are
independent, so a failed section can remain dirty while successful sections
advance.

For server-assigned values or normalized state, call
`TryReconcile(submission, handle, persisted, out error)` with the authoritative
model. The registered callback should invoke the generated session's
`TryReconcile` operation. It adopts persisted state as the baseline and
preserves local edits made after the submitted snapshot when reconciliation
succeeds.

Every section has a baseline revision. Acknowledging a submission prepared
against an older revision returns a stale-submission error or throws
`EditSessionStaleSubmissionException` for `AcceptChanges`. Unregistered or
replaced section handles are also invalidated, and submissions from another
group cannot be acknowledged. Keep one outstanding submission per section
when reconciling; independent acknowledgements may proceed for other sections.

## Unregistering and replacement

`Unregister` and `Replace` require an explicit
`EditSessionGroupRemovalPolicy`. `RequireClean` rejects a section with pending
changes or open drafts. `DetachWithPendingChanges` allows removal or
replacement while leaving the old session and any drafts untouched. Replacement
returns a new handle and invalidates the previous handle and its submissions.

The group serializes its own registration and acknowledgement bookkeeping. It
cannot make concurrently edited sessions into one atomic snapshot; callers
should coordinate model edits while preparing a multi-section submission.
