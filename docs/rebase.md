# ChangeSet Rebase

A ChangeSet is authored against the state it was derived from. If the underlying state changes before the ChangeSet is applied, applying it directly can overwrite a concurrent change to the same member. `RebaseOnto` compares the ChangeSet's own before-state, the local transition, and the current state, then produces a ChangeSet for the current state. The old baseline is never supplied at rebase time because the ChangeSet already contains the baseline information required by its changes.

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
var baseState = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "a" }));
var editedState = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2, Label = "a" }));
var currentState = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "b" }));

// The ChangeSet carries its own before-state: only the current state is needed.
var changes = RebaseSettings.ChangeSet.Between(baseState, editedState);
var rebased = changes.RebaseOnto(currentState);

var reconciled = rebased.Patch.ToPatch().Apply(currentState);
// !rebased.HasConflicts
// reconciled.Value!.RetryCount.Value == 2
// reconciled.Value!.Label.Value == "b"
```
<!-- /sample -->

## Disconnected Editing

The canonical flow needs only the current state on the receiving side:

```text
client receives state A
client edits A -> B and creates ChangeSet
another client updates server A -> C
server receives the stale ChangeSet
server loads only current state C
ChangeSet.RebaseOnto(C)
```

All states are presence-aware `Optional<Fragment?>` values, so *missing*, *present null*, and *present value* participate in reconciliation exactly as they do in merge: `Missing` never equals a present value — not even a present `null` or `default` — so `missing → present null`, `present null → missing`, and `missing → present default` are all observable transitions.

Rebase returns a `RebaseResult<ChangeSet>`: a **new ChangeSet for the current state** (in `result.Patch`) plus **structured conflicts** (in `result.Conflicts`, with `result.HasConflicts` summarizing) for edits that cannot be reconciled automatically. Conflicting members are excluded from the rebased ChangeSet.

## The Three Outcomes

### 1. Current matches Before: replay

Local and concurrent edits touch different members: the local transition is replayed onto the current state, and the concurrent edit is preserved.

```csharp
// before:  { RetryCount = 1, Label = "a" }
// edited:  { RetryCount = 2, Label = "a" }
// current: { RetryCount = 1, Label = "b" }
// result:  RetryCount = 2, Label untouched → applied gives { RetryCount = 2, Label = "b" }
```

### 2. Current matches After: already applied

The local transition is already present in `current` (someone else made the same change): rebase succeeds with a semantic no-op — the rebased ChangeSet is empty.

<!-- sample: rebase-applied -->
```csharp
var appliedBase = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1 }));
var appliedEdited = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2 }));
var alreadyThere = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2 }));

// Current == After: the change is already present, so rebase is a no-op.
var noOp = RebaseSettings.ChangeSet.Between(appliedBase, appliedEdited)
    .RebaseOnto(alreadyThere);
// !noOp.HasConflicts
// noOp.Patch.IsEmpty == true
```
<!-- /sample -->

### 3. Otherwise: structured conflict

Local and current changed the same member differently: the member is excluded from the rebased ChangeSet and reported as a conflict.

<!-- sample: rebase-conflict -->
```csharp
var conflictBase = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1 }));
var conflictEdited = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 2 }));
var conflictCurrent = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 3 }));

var conflicted = RebaseSettings.ChangeSet.Between(conflictBase, conflictEdited)
    .RebaseOnto(conflictCurrent);

var conflict = conflicted.Conflicts.Single();
// conflicted.HasConflicts == true
// conflict.Kind == SparsePatchConflictKind.Scalar
// conflict.Path == ["RetryCount"]
```
<!-- /sample -->

## Structured Conflicts

Each `SparsePatchConflict` reports where the conflict occurred and the base/local/current values involved.

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

Clean paths may remain in the rebased ChangeSet while conflicts are reported separately. Applications commonly keep persistence atomic and decline to commit when any conflict remains: check `HasConflicts` first, apply and save only when it is false, and otherwise return the structured conflicts to the caller.

## Collection and Structural Behavior

* **Nested structural members** rebase member-by-member; only the colliding leaf conflicts while disjoint nested edits replay.
* **Append-merged collections** treat an already-applied addition as a no-op (replaying `["a", "b"]` onto a current state that already contains `["a", "b"]` stays put) and report concurrent divergent growth as `CollectionAppend`.
* **Set-union members** rebase against comparer-aware equality: same entries under the same comparer replay cleanly; entries that differ under the member's comparer conflict as `CollectionSetUnion`.
* **Keyed structural collections** rebase element-wise where keys line up; per-key divergent edits conflict while disjoint key ranges (added/removed/edited on different keys) replay.
* **The whole contribution** participates too: `Set`-style root transitions and root presence changes (`Missing` vs present-null vs present) rebase through the same machinery, with unresolvable root divergence reported as `WholeContribution`.
* **An empty ChangeSet** rebases across root presence changes without conflicts — there is nothing to reconcile.

## Custom Strategies

A custom `FragmentMergeStrategy<T>` can override `TryRebase` to define its own three-way reconciliation for the member:

* it receives `Optional<T>` for the edit base, the desired state, and the current state, with the missing/present distinction preserved end to end;
* a present result maps to a `Set` patch operation, a missing result maps to `Unset`, and a result equal to the current state stays `Unchanged` (a semantic no-op);
* the default implementation succeeds when the desired state still matches the edit base (unchanged local edit — the current state wins) or when the current state matches the edit base or the desired state (clean replay or already applied), and reports a conflict otherwise;
* returning `false` surfaces a `CustomStrategy` conflict carrying the member path and the three values.

## No Revision History Required

Semantic rebase does not require SparseFragments to retain a Git-like revision history. Three things stay distinct:

```text
ChangeSet before-state
    semantic information needed to reconcile a change

current concurrency token / rowversion / version
    persistence race protection

historical snapshots
    not required for ordinary ChangeSet rebase
```

The server in the disconnected-editing flow loads only the current state and still rebases correctly, because the incoming ChangeSet already carries the before-state its own transitions need. Persistence still needs its normal race protection — a concurrency token such as an EF `rowversion`, a `Version` column, an `UpdatedAt` marker, an ETag, or an operation id — but those are application/envelope metadata, not members of the ChangeSet itself. SparseFragments prescribes neither the token type nor the persistence technology.

An application request therefore wraps the ChangeSet in its own envelope:

```csharp
// Application envelope: IDs and concurrency tokens live outside ChangeSet.
sealed record UpdateOrderRequest(
    Guid OrderId,
    byte[] RowVersion,
    Order.ChangeSet Changes);
```

The handler deserializes the ChangeSet, loads only the current database state, calls `RebaseOnto(current)`, and — when there are no conflicts — saves under the normal concurrency token. When conflicts remain, it returns the structured conflicts instead of saving.

## End-to-End Example

One complete pass through client edit, serialization, current-state rebase, and save-or-conflict:

<!-- sample: rebase-e2e -->
```csharp
using System.Text.Json;

// Server sends DTO (state A); the client edits A -> B and creates a ChangeSet.
var stateA = new RebaseSettings { RetryCount = 1, Label = "a" };
var stateB = new RebaseSettings { RetryCount = 2, Label = "a" };
var outgoing = RebaseSettings.ChangeSet.Between(
    Optional<RebaseSettings.Fragment?>.Present(RebaseSettings.Fragment.From(stateA)),
    Optional<RebaseSettings.Fragment?>.Present(RebaseSettings.Fragment.From(stateB)));

// The ChangeSet travels as JSON through the application's own transport.
var json = JsonSerializer.Serialize(outgoing);
var incoming = JsonSerializer.Deserialize<RebaseSettings.ChangeSet>(json)!;

// Meanwhile the server moved A -> C. The server loads only the current state:
// no historical snapshots are required because the ChangeSet carries its own before-state.
var stateC = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(new RebaseSettings { RetryCount = 1, Label = "b" }));
var arrival = incoming.RebaseOnto(stateC);

// !arrival.HasConflicts, so apply and save under the normal DB concurrency token.
// Conflicts would instead return structured conflict information without saving.
var saved = arrival.Patch.ToPatch().Apply(stateC);
// saved.Value!.RetryCount.Value == 2
// saved.Value!.Label.Value == "b"
```
<!-- /sample -->

The transport in the middle can be HTTP, SignalR, or any message bus the application already uses — SparseFragments only requires that the serialized ChangeSet arrives intact. The two terminal branches stay the same everywhere: no conflicts means apply and save under the application's concurrency token; conflicts mean surface `arrival.Conflicts` (paths, kinds, and base/local/current values) without saving.
