# ChangeSet Rebase

A ChangeSet is authored against the state it was derived from. If the underlying state changes before the ChangeSet is applied, applying it directly can overwrite a concurrent change to the same member. `RebaseOnto` compares the ChangeSet's own before-state, the local transition, and the current state, then produces a ChangeSet for the current state.

The old baseline is never supplied at rebase time because the ChangeSet already contains the baseline information required by its changes.

<!-- sample: rebase-first-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class RebaseSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}
```
<!-- /sample -->

<!-- sample: rebase-first -->
```csharp
var baseModel = new RebaseSettings { RetryCount = 1, Label = "a" };
var editedModel = new RebaseSettings { RetryCount = 2, Label = "a" };
var currentModel = new RebaseSettings { RetryCount = 1, Label = "b" };

var changes = baseModel.CreateChangeSet(editedModel);
if (!changes.TryApplyTo(currentModel, out var reconciled))
{
    throw new InvalidOperationException("The change conflicts with the current model.");
}

// reconciled.RetryCount == 2
// reconciled.Label == "b"
```
<!-- /sample -->

## Disconnected Editing

This section is a how-to. It shows the receive-side flow that needs only the current state.

The canonical flow needs only the current state on the receiving side:

```text
client receives state A
client edits A -> B and creates ChangeSet
another client updates server A -> C
server receives the stale ChangeSet
server loads only current state C
ChangeSet.RebaseOnto(C)
```

For ordinary, present non-null DTOs, `before.CreateChangeSet(edited)` and `TryApplyTo(current, out updated)` provide this flow without manual Fragment or Optional conversions. The extensions snapshot the models into Fragments and delegate to the same rebase semantics.

In-place application is a separate local concern. When the destination object is already bound to a UI, apply through the baseline-free Patch API (details in [UI frameworks](ui-frameworks.md)):

```csharp
var changes = baseline.CreateChangeSet(edited);
changes.ToPatch().ApplyInPlace(boundModel);
```

`Fragment.WriteTo(model)` and `Patch.ApplyInPlace(model)` mutate the existing model instead of returning
a replacement. ChangeSet has no `ApplyInPlace`; a blind overwrite must spell
`changes.ToPatch().ApplyInPlace(model)` so conflicting edits cannot slip through
an unguarded call. `ToPatch()` discards the before-state, so the result is a
baseline-free operation that can no longer rebase or report conflicts.
`List<T>` and `Dictionary<TKey,TValue>` properties keep their
existing collection object and replace its contents; nested model properties
may be replaced. Get-only or init-only members prevent these in-place APIs
from being generated, while ordinary immutable patch and rebase APIs
remain available ([SPF026](analyzer.md#spf026-in-place-submit-is-unavailable)).

The presence-aware APIs remain necessary when the root itself may be missing, present null, or present value. `Missing` never equals a present value, not even a present `null` or `default`. Therefore missing to present null, present null to missing, and missing to present default remain observable transitions only through the Fragment and Optional surface.

`RebaseOnto` returns a `RebaseResult<ChangeSet>`: a new ChangeSet for the current state in `Rebased` plus structured conflicts for edits that cannot be reconciled automatically. Conflicting members are excluded from the rebased ChangeSet. The application owns the decision, persistence, and transport: keep persistence atomic and decline to commit when conflicts remain, or resolve per field and retry. Use this lower-level result when continuing to work with ChangeSet algebra; use `TryApplyTo` when the desired outcome is an updated model or conflicts.

<!-- sample: rebase-presence -->
```csharp
var missing = Optional<RebaseSettings.Fragment?>.Missing;
var presentNull = Optional<RebaseSettings.Fragment?>.Present(null);
var rootChange = RebaseSettings.ChangeSet.Between(missing, presentNull);
RebaseResult<RebaseSettings.ChangeSet> result = rootChange.RebaseOnto(missing);

if (result.HasConflicts)
{
    throw new InvalidOperationException("The root transition conflicts.");
}

var applied = result.Rebased.ToPatch().Apply(missing);
// applied.IsPresent && applied.Value is null
```
<!-- /sample -->

## The Three Outcomes

This section is an explanation. It defines how rebase classifies each member.

### 1. Current matches Before: replay

Local and concurrent edits touch different members: the local transition is replayed onto the current state, and the concurrent edit is preserved.

```csharp
// before:  { RetryCount = 1, Label = "a" }
// edited:  { RetryCount = 2, Label = "a" }
// current: { RetryCount = 1, Label = "b" }
// result:  RetryCount = 2, Label untouched → applied gives { RetryCount = 2, Label = "b" }
```

### 2. Current matches After: already applied

The local transition is already present in `current` (someone else made the same change): rebase succeeds with a semantic no-op, so the rebased ChangeSet is empty.

<!-- sample: rebase-applied -->
```csharp
var appliedBase = new RebaseSettings { RetryCount = 1 };
var appliedEdited = new RebaseSettings { RetryCount = 2 };
var alreadyThere = new RebaseSettings { RetryCount = 2 };

// Current == After: the change is already present, so rebase is a no-op.
var noOp = appliedBase.CreateChangeSet(appliedEdited);
if (!noOp.TryApplyTo(alreadyThere, out var unchanged))
{
    throw new InvalidOperationException("The change conflicts with the current model.");
}

// unchanged.RetryCount == 2
```
<!-- /sample -->

### 3. Otherwise: structured conflict

Local and current changed the same member differently: the member is excluded from the rebased ChangeSet and reported as a conflict.

<!-- sample: rebase-conflict -->
```csharp
var conflictBase = new RebaseSettings { RetryCount = 1 };
var conflictEdited = new RebaseSettings { RetryCount = 2 };
var conflictCurrent = new RebaseSettings { RetryCount = 3 };

if (
    conflictBase
        .CreateChangeSet(conflictEdited)
        .TryApplyTo(conflictCurrent, out _, out var conflicts)
)
{
    throw new InvalidOperationException("Expected a conflict.");
}

var conflict = conflicts.Single();
// conflict.Kind == SparseConflictKind.Scalar
// conflict.Path == ["RetryCount"]
```
<!-- /sample -->

## Structured Conflicts

This section is a reference. It defines the conflict shape.

Each `SparseConflict` reports where the conflict occurred and the base/local/current values involved.

| Member | Meaning |
| --- | --- |
| `Path` | Member path from the root contribution (e.g. `["Nested", "Host"]`) |
| `PathText` | Dotted form of `Path` (`"Nested.Host"`; empty for the root contribution) |
| `Kind` | What kind of member collided (see below) |
| `BaseValue` / `LocalValue` / `CurrentValue` | The three presence-aware values as `Optional<object?>` |
| `Reason` | Optional human-readable explanation |

Conflict kinds:

| Kind | Meaning |
| --- | --- |
| `WholeContribution` | The whole contribution was changed concurrently |
| `Nested` | A nested contribution was changed concurrently |
| `Scalar` | A scalar member was changed concurrently |
| `CollectionAppend` | An append-merged collection was changed concurrently |
| `CollectionSetUnion` | A set-union member was changed concurrently |
| `CustomStrategy` | A custom merge strategy reported a conflict |

Nested conflicts expose the full member path: a local `Nested.Host = "b"` against a concurrent `Nested.Host = "c"` (from base `"a"`) reports `Path == ["Nested", "Host"]` with the three values attached, so UI code can offer per-field resolution.

Clean paths may remain in the rebased ChangeSet while conflicts are reported separately. Applications commonly keep persistence atomic and decline to commit when any conflict remains. The detailed `TryApplyTo` overload returns `false` and exposes structured conflicts without returning a partially applied model.

## Collection and Structural Behavior

This section is a reference. It lists per-shape rebase rules.

* `Nested structural members` rebase member by member; only the colliding leaf conflicts while disjoint nested edits replay.
* `Append-merged collections` treat an already-applied addition as a no-op (replaying `["a", "b"]` onto a current state that already contains `["a", "b"]` stays put) and report concurrent divergent growth as `CollectionAppend`.
* `Set-union members` rebase against comparer-aware equality: same entries under the same comparer replay cleanly; entries that differ under the member's comparer conflict as `CollectionSetUnion`.
* `Keyed structural collections` rebase element-wise where keys line up; per-key divergent edits conflict while disjoint key ranges (added, removed, or edited on different keys) replay.
* `The whole contribution` participates too: `Set`-style root transitions and root presence changes (missing versus present-null versus present) rebase through the same machinery, with unresolvable root divergence reported as `WholeContribution`.
* `An empty ChangeSet` rebases across root presence changes without conflicts because there is nothing to reconcile.

## Custom Strategies

This section is a reference. It defines the `TryRebase` contract.

A custom `FragmentMergeStrategy<T>` can override `TryRebase` to define its own three-way reconciliation for the member:

* it receives `Optional<T>` for the edit base, the desired state, and the current state, with the missing and present distinction preserved end to end;
* a present result maps to a `Set` patch operation, a missing result maps to `Remove`, and a result equal to the current state stays `Keep` (a semantic no-op);
* the default implementation succeeds when the desired state still matches the edit base (unchanged local edit, so the current state wins) or when the current state matches the edit base or the desired state (clean replay or already applied), and reports a conflict otherwise;
* returning `false` surfaces a `CustomStrategy` conflict carrying the member path and the three values.

## No Revision History Required

This section is an explanation. It separates ChangeSet state from persistence concerns.

Semantic rebase does not require SparseFragments to retain a Git-like revision history. Three things stay distinct:

```text
ChangeSet before-state
    semantic information needed to reconcile a change

current concurrency token / rowversion / version
    persistence race protection

historical snapshots
    not required for ordinary ChangeSet rebase
```

The server in the disconnected-editing flow loads only the current state and still rebases correctly, because the incoming ChangeSet already carries the before-state its own transitions need. Persistence still needs its normal race protection (a concurrency token such as an EF `rowversion`, a `Version` column, an `UpdatedAt` marker, an ETag, or an operation id). Those tokens are application and envelope metadata, not members of the ChangeSet itself. SparseFragments prescribes neither the token type nor the persistence technology.

## Mixed Requests With Redacted Members

This section is a how-to. It shows the receive-side flow when a request mixes ordinary transitions with write-only operations.

A member whose before-state arrives redacted is an explicit write-only operation: the sender could not disclose the previous value, often because the value is secret. The request still carries the requested after-state. Ordinary members in the same request keep ordinary baseline-aware validation and rebase.

<!-- sample: mixed-apply -->
```csharp
var currentModel = new RebaseDocsSettings { RetryCount = 1, Label = "current" };
var payload = JsonSerializer.Deserialize<RebaseDocsSettings.ChangePayload>(
    """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted"},"after":{"state":"value","value":"rotated"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
)!;
if (!payload.TryApplyMixedTo(currentModel, out var updated, out var outcome))
{
    throw new InvalidOperationException("The change conflicts with the current model.");
}

// updated.Label == "rotated"
// updated.RetryCount == 2
```
<!-- /sample -->

`TryApplyMixedTo` rebases the ordinary members onto the current model and passes the write-only members through without historical comparison. When any ordinary member conflicts, the call returns `false` and applies nothing: the write-only subset is never committed on its own, and the current model is left untouched. The outcome names the write-only paths in `WriteOnlyPaths` and carries the structured conflicts in `Conflicts`.

Use `ChangePayload.ToPatch()` when the destination only needs the desired operations without validation, and `InvertReversibleChanges(out var skipped)` when rolling back: write-only paths are excluded from the rollback and reported in `skipped`. `ChangeSet.FromPayload` accepts only fully baseline-aware envelopes and throws a typed error naming the redacted paths.

## Mixed-Operation Rules

This section is a reference. It defines how mixed operations compose, roll back, and project.

| First operation | Second operation | Merged operation | Value-level check still required |
| --- | --- | --- | --- |
| Transition | Transition | Transition from the first before-state to the second after-state | Yes, first after-state must equal second before-state |
| Transition | Blind set | Transition from the first before-state to the second after-state | No |
| Blind set | Transition | Blind set to the second after-state | Yes, first after-state must equal second before-state |
| Blind set | Blind set | Blind set to the second after-state | No |

Disjoint paths merge without checks. A merged operation regains complete history only when one side supplied a real baseline; history is never inferred from a redacted endpoint.

Rollback inverts transitions and skips write-only operations, which have no prior value to restore. A non-empty skipped list means the result is not a complete inverse. `ChangeSet.Invert()` on a complete change set stays a true inversion.

`ToPatch()` always succeeds and discards baseline information. `FromPayload` and `ToChangeSet` fail on any redacted before-state. After-states must stay concrete (a value, an explicit null where valid, or a missing endpoint for removal); a redacted after-state is malformed. `Redacted` does not mean `Missing`: a redacted before-state paired with a missing after-state is a blind remove that validates against nothing.

Diagnostics carry member paths and reasons only, never secret before, current, or after values. Per-item redacted endpoints in keyed and dictionary members are rejected with a typed error; send a whole-member blind set for those members. Blind whole-collection removal has no patch projection. Whole-root redacted operations are reported under the `$root` path.

Strict rejection of redacted members is separate opt-in work and does not change the pass-through default.

## Why Write-Only Operations Pass Through

This section is an explanation. It gives the reason for the pass-through default.

Three-way rebase compares the recorded before-state with the current state. A redacted before-state supplies nothing to compare, so the default applies the requested after-state directly, the same way an explicit patch set does. Passing a value through is not the same as reconciling concurrent edits to that member: revision checks, ETags, and authorization stay with the application, as described in No Revision History Required above. Atomicity holds at the request boundary, so a conflict in any ordinary member fails the whole request instead of persisting the write-only subset.

An application request therefore wraps the ChangeSet in its own envelope:

```csharp
// Application envelope: IDs and concurrency tokens live outside ChangeSet.
sealed record UpdateOrderRequest(
    Guid OrderId,
    byte[] RowVersion,
    Order.ChangePayload Changes);
```

The handler converts the payload with `ToChangeSet()`, loads only the current database state, calls `TryApplyTo(current, out updated, out conflicts)`, and, when there are no conflicts, saves under the normal concurrency token. When conflicts remain, it returns them instead of saving.

## End-to-End Example

This section is a how-to. It follows one pass through client edit, serialization, rebase, and save-or-conflict.

One complete pass through client edit, serialization, current-state rebase, and save-or-conflict:

<!-- sample: rebase-e2e -->
```csharp
using System.Text.Json;

// Server sends DTO (state A); the client edits A -> B and creates a ChangeSet.
var stateA = new RebaseSettings { RetryCount = 1, Label = "a" };
var stateB = new RebaseSettings { RetryCount = 2, Label = "a" };
var outgoing = stateA.CreateChangeSet(stateB);

// The typed payload travels as JSON through the application's own transport.
var json = JsonSerializer.Serialize(outgoing.ToPayload());
var incoming = JsonSerializer
    .Deserialize<RebaseSettings.ChangePayload>(json)!
    .ToChangeSet();

// Meanwhile the server moved A -> C. The server loads only the current state:
// no historical snapshots are required because the ChangeSet carries its own before-state.
var stateC = new RebaseSettings { RetryCount = 1, Label = "b" };

if (incoming.TryApplyTo(stateC, out var saved, out var conflicts))
{
    // Save under the normal DB concurrency token.
    // saved.RetryCount == 2
    // saved.Label == "b"
}
else
{
    // Surface conflicts without saving a partially applied model.
    // conflicts contains paths, kinds, and base/local/current values.
}
```
<!-- /sample -->

The transport in the middle can be HTTP, SignalR, or any message bus the application already uses. SparseFragments only requires that the serialized payload arrives intact. The two terminal branches stay the same everywhere: no conflicts means save the updated model under the application's concurrency token; conflicts mean surface their paths, kinds, and base, local, and current values without saving.
