# Edit-session groups

`EditSessionGroup` coordinates several typed edit sessions as sections of one
submission. It captures change sets and changed paths, then lets callers
acknowledge or reconcile each section independently. It does not provide
transport, persistence, cross-session transactions, or a global edit lock.

For a runnable workflow covering registration, submission, and independent acknowledgement, see [Coordinate multi-session submissions](../how-to/edit-session-groups.md).

## Registration

Register each section with a stable, non-empty identifier. Identifiers use
ordinal, case-sensitive comparison. `Register` returns a typed
`EditSessionSectionHandle<TModel, TChangeSet>`; use that handle to retrieve the
captured change set and acknowledge it without casting heterogeneous sections.

An `EditSessionGroupRegistration<TModel, TChangeSet>` supplies the session,
delegates to create and inspect its generated change set, and a delegate to
enumerate paths from that change set. It can also supply readiness and
validation callbacks and `TryReconcile` to connect the generated
session's reconciliation operation.

`Validate` is the live-session readiness hook. It takes no arguments and may
inspect the current mutable session, UI state, or permission to submit. It
does not receive the captured submission, so it cannot validate an earlier
immutable change set. `ValidateChanges` is the synchronous submission
validator. It receives the exact immutable change set placed in the eventual
submission and must not inspect the live session. `ValidateAsync` is the
asynchronous submission validator. It receives the captured change set and a
cancellation token, paired with `ValidateChanges` for synchronous coverage.

`Prepare` checks every registered section, including unchanged sections, and
evaluates readiness before returning a submission. A generated session's
`CreateChangeSet` accepts pending child sessions and descendants before
capturing the root change set. A child-session conflict makes capture fail;
the group reports the exception message as a validation issue.

## Preparing and acknowledging changes

`Prepare` captures each non-empty generated change set and its changed paths,
then runs synchronous readiness checks. It rejects registrations with an
asynchronous validator; call `PrepareAsync(cancellationToken)` for those
sections. `PrepareAsync` captures every section synchronously before awaiting
validation. Each synchronous and asynchronous submission validator receives
that captured change set, so edits made while validation is in progress do
not change the submission. Post-capture edits remain pending on the live
session.

Preparation follows capture, readiness, and submission validation in that
order. Capture produces immutable snapshots and accepts pending child
sessions. Readiness evaluates the live `Validate` hook. Submission validation
runs `ValidateChanges` and `ValidateAsync` against the captured change sets. A synchronous validator
that throws (other than cancellation) produces a validation issue with the
exception message. An asynchronous validator that throws produces a
validation issue the same way, while cancellation propagates as
`OperationCanceledException`.

`PrepareAsync` re-evaluates a limited set after its awaits. Child edits made
after capture remain pending on the live session and do not change the
submission. It checks whether a section was unregistered or replaced during
validation. It checks whether a section baseline revision advanced during
validation, for example through `AcceptChanges` of an earlier submission.
Any of these changes fail the in-flight preparation with
`EditSessionGroupValidationException`. Ordinary local edits do not invalidate
the captured submission. The live readiness hook runs before awaits and is
not evaluated again after awaits.

The group never holds its internal lock across user callbacks or awaits, so
a validator may call back into the group without deadlocking. Structural
changes made during validation fail the in-flight preparation as described
above. Callers should still coordinate model edits while preparing a
multi-section submission, since concurrently edited sessions cannot become
one atomic snapshot.

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

## Migration notes

Existing `Validate` callbacks keep their meaning as live-session readiness
checks. No signature change is required for checks that inspect UI state,
permissions, or other live state.

When a `Validate` callback reads the change set through the live session,
move that logic to `ValidateChanges`, which receives the captured change set
directly. Keep a `Validate` callback only for the part of the check that
needs live state. `ValidateAsync` already receives the captured change set,
so its signature is unchanged; add `ValidateChanges` alongside it when the
same rule needs synchronous coverage in `Prepare`.

## Unregistering and replacement

`Unregister` and `Replace` require an explicit
`EditSessionGroupRemovalPolicy`. `RequireClean` rejects a section with pending
changes, including changes in child sessions. `DetachWithPendingChanges`
allows removal or replacement while leaving the old session and its child
sessions untouched. Replacement returns a new handle and invalidates the
previous handle and its submissions.

The group serializes its own registration and acknowledgement bookkeeping. It
cannot make concurrently edited sessions into one atomic snapshot; callers
should coordinate model edits while preparing a multi-section submission.
