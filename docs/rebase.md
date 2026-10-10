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

The lower-level `RebaseOnto` method returns a `RebaseResult<ChangeSet>`: a new ChangeSet for the current state in `Rebased` plus structured conflicts for edits that cannot be reconciled automatically. Conflicting members are excluded from the rebased ChangeSet. The application owns the decision, persistence, and transport: keep persistence atomic and decline to commit when conflicts remain, or resolve per field and retry. Use this lower-level result when continuing to work with ChangeSet algebra; use `TryApplyTo` when the desired outcome is an updated model or conflicts.

The presence-aware APIs remain necessary when the root itself may be missing, present null, or present value. `Missing` never equals a present value, not even a present `null` or `default`. Therefore missing to present null, present null to missing, and missing to present default remain observable transitions only through the Fragment and Optional surface:

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

### In-place application for bound models

When the destination object is already bound to a UI, prefer the conflict-checked `ChangeSet.TryApplyInPlace`. It rebases the change onto the bound model's current state first, so an unrelated concurrent edit is preserved while a conflicting edit surfaces as a structured conflict:

<!-- sample: rebase-in-place -->
```csharp
var inPlaceBase = new RebaseDocsSettings { RetryCount = 1, Label = "a" };
var inPlaceEdited = new RebaseDocsSettings { RetryCount = 2, Label = "a" };
var boundModel = new RebaseDocsSettings { RetryCount = 1, Label = "b" };

var pending = inPlaceBase.CreateChangeSet(inPlaceEdited);
if (!pending.TryApplyInPlace(boundModel, out var inPlaceConflicts))
{
    throw new InvalidOperationException("The change conflicts with the bound model.");
}

// boundModel.RetryCount == 2
// boundModel.Label == "b"
```
<!-- /sample -->

`ApplyInPlace(model, options)` is the throwing form of the same check. When the change includes an init-only or constructor-only member, the in-place write cannot complete: `TryApplyInPlace` returns `false` with an in-place write conflict, and `ApplyInPlace` throws `InvalidOperationException`.

The explicit blind form `changes.ToPatch().ApplyInPlace(model)` skips the before-state check. `ToPatch()` discards the before-state, so the result can no longer rebase or report conflicts. Use it only when the caller already owns conflict handling.

`Fragment.WriteTo(model)` and `Patch.ApplyInPlace(model)` mutate the existing model instead of returning
a replacement.
`List<T>` and `Dictionary<TKey,TValue>` properties keep their
existing collection object and replace its contents; nested model properties
may be replaced. Get-only or init-only members prevent these in-place APIs
from being generated, while ordinary immutable patch and rebase APIs
remain available ([SPF026](analyzer.md#spf026-in-place-submit-is-unavailable)).

## The Three Outcomes

Rebase classifies each member into one of three outcomes:

### 1. Current matches Before: replay

Local and concurrent edits touch different members: the local transition is replayed onto the current state, and the concurrent edit is preserved.

<!-- illustrative: state sketch; comments only, not code -->
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
// conflict.PathText == "RetryCount"
```
<!-- /sample -->

## Structured Conflicts

Each `SparseConflict` reports where the conflict occurred and the base/local/current values involved.

| Member | Meaning |
| --- | --- |
| `Path` | Typed path from the root contribution as `SparsePath` (e.g. `Nested.Host`) |
| `PathText` | Wire-compatible rendering of `Path` (`"Nested.Host"`; `"$root"` for the root contribution) |
| `Kind` | What kind of member collided (see below) |
| `BaseValue` / `LocalValue` / `CurrentValue` | The three presence-aware values as `Optional<object?>` |
| `Reason` | Optional human-readable explanation |

`Path` carries member, typed-key, and index segments with stable identity: distinct roots, key types, and escaping-sensitive keys never share a path. `conflicts.Find(path)` locates the first conflict at exactly the given path (typed paths such as `Order.SparsePath.Items.Key(id).Price` are checked at compile time); ancestors and descendants never match. Keyed entries report key identity rather than positions, and order clashes report the collection path.

Conflict kinds:

| Kind | Meaning |
| --- | --- |
| `WholeContribution` | The whole contribution was changed concurrently |
| `Nested` | A nested contribution was changed concurrently |
| `Scalar` | A scalar member was changed concurrently |
| `CollectionAppend` | An append-merged collection was changed concurrently |
| `CollectionSetUnion` | A set-union member was changed concurrently |
| `CustomStrategy` | A custom merge strategy reported a conflict |

Nested conflicts expose the full member path: a local `Nested.Host = "b"` against a concurrent `Nested.Host = "c"` (from base `"a"`) reports `PathText == "Nested.Host"` with the three values attached, so UI code can offer per-field resolution.

Clean paths may remain in the rebased ChangeSet while conflicts are reported separately. Applications commonly keep persistence atomic and decline to commit when any conflict remains. The detailed `TryApplyTo` overload returns `false` and exposes structured conflicts without returning a partially applied model.

## Resolving conflicts by path

`ChangeSet.BeginResolution` opens mutable, framework-neutral resolution state over one `RebaseResult`. The state owns the original result (including the clean `Rebased` contributions), the authoritative current-state snapshot, and one decision per canonical path. `TryBuild` then produces a baseline-aware `ChangeSet` holding the clean edits plus the chosen resolutions. The call never mutates the original `ChangeSet`, the input models, or the session, and it fails instead of dropping a conflict silently.

<!-- illustrative: resolution flow; compile-checked coverage lives in RebaseResolutionTests -->
```csharp
var currentState = Optional<RebaseSettings.Fragment?>.Present(
    RebaseSettings.Fragment.From(currentModel)
);
var rebase = before.CreateChangeSet(edited).RebaseOnto(currentState);
var resolution = RebaseSettings.ChangeSet.BeginResolution(rebase, currentModel);

var retry = RebaseSettings.SparsePath.RetryCount;

var conflict = resolution.FindConflict(retry);
bool exact = resolution.HasConflict(retry);
bool below = resolution.HasConflictsUnder(SparsePath.Root<RebaseSettings>());
bool around = resolution.HasConflictsAffecting(retry);

resolution.UseIncoming(retry); // take the local value
resolution.UseCurrent(retry); // keep the current value
resolution.SetValue(retry, 4); // take a custom typed value
resolution.SetValue(retry, Optional<int>.Missing); // removal stays distinct from null

if (resolution.TryBuild(out var resolved, out var failure))
{
    // resolved is based on currentState and reapplies onto newer states.
}
else
{
    // failure.UnresolvedConflicts names what still needs a decision.
}
```

`EnumerateConflicts` returns every conflict in reported order; `EnumerateUnresolvedConflicts` returns the live-filtered subset. `FindConflict` matches exact paths only, while `HasConflictsUnder` reports descendants and `HasConflictsAffecting` covers the path itself plus its ancestors and descendants. `GetState` exposes the same status per path for generic consumers, so no per-model conflict members are generated.

Decisions fan out deterministically. `UseIncoming` or `UseCurrent` at an ancestor path covers every conflict below it, and an exact decision always wins over an ancestor. Custom values require an exact conflict path, and paths with no conflict at or under them are rejected when decided. Keyed entries resolve at the member path (whole collection or order) or below a typed key (element replacement, removal, or element-leaf recursion), dictionary entries resolve at the member path or below a typed key, nested leaves resolve below their member, and set or append members resolve as a whole. A `RedactedBefore` conflict carries no plaintext, so only `UseCurrent` (keep) is accepted for it.

Pass the same current snapshot used for `RebaseOnto`. When the state moved under the resolution, `TryBuild` fails instead of replaying clean edits against stale values; rebase onto the latest state and resolve again. The built `ChangeSet` stays baseline-aware, so it reapplies and rebases like any other change.

## Collection and Structural Behavior

* `Nested structural members` rebase member by member; only the colliding leaf conflicts while disjoint nested edits replay.
* `Append-merged collections` treat an already-applied addition as a no-op (replaying `["a", "b"]` onto a current state that already contains `["a", "b"]` stays put) and report concurrent divergent growth as `CollectionAppend`.
* `Set-union members` rebase against comparer-aware equality: same entries under the same comparer replay cleanly; entries that differ under the member's comparer conflict as `CollectionSetUnion`.
* `Keyed structural collections` rebase element-wise where keys line up; per-key divergent edits conflict while disjoint key ranges (added, removed, or edited on different keys) replay.
* `The whole contribution` participates too: `Set`-style root transitions and root presence changes (missing versus present-null versus present) rebase through the same machinery, with unresolvable root divergence reported as `WholeContribution`.
* `An empty ChangeSet` rebases across root presence changes without conflicts because there is nothing to reconcile.

## Custom Strategies

A custom `FragmentMergeStrategy<T>` can override `TryRebase` to define its own three-way reconciliation for the member:

* it receives `Optional<T>` for the edit base, the desired state, and the current state, with the missing and present distinction preserved end to end;
* a present result maps to a `Set` patch operation, a missing result maps to `Remove`, and a result equal to the current state stays `Keep` (a semantic no-op);
* the default implementation succeeds when the desired state still matches the edit base (unchanged local edit, so the current state wins) or when the current state matches the edit base or the desired state (clean replay or already applied), and reports a conflict otherwise;
* returning `false` surfaces a `CustomStrategy` conflict carrying the member path and the three values.

## Rebase Policies

A member-level policy selects rebase behavior without requiring a custom merge strategy. The policy below merges divergent labels instead of conflicting:

<!-- sample: rebase-policy-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class RebasePolicySettings
{
    public string? Label { get; set; }

    [SparseRebasePolicy(typeof(ConcatLabelPolicy))]
    public string? Tag { get; set; }
}

public sealed class ConcatLabelPolicy : FragmentRebasePolicy<string?>
{
    public override bool AreEqual(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    public override bool TryRebase(
        Optional<string?> editBase,
        Optional<string?> desired,
        Optional<string?> current,
        out Optional<string?> rebased,
        out string? reason
    )
    {
        if (!editBase.IsPresent || !desired.IsPresent || !current.IsPresent)
        {
            return FragmentRebasePolicy<string?>
                .FailOnConflict()
                .TryRebase(editBase, desired, current, out rebased, out reason);
        }

        if (AreEqual(desired.Value, editBase.Value))
        {
            rebased = current;
            reason = null;
            return true;
        }

        if (AreEqual(current.Value, editBase.Value) || AreEqual(current.Value, desired.Value))
        {
            rebased = desired;
            reason = null;
            return true;
        }

        rebased = Optional<string?>.Present(desired.Value + "|" + current.Value);
        reason = null;
        return true;
    }
}
```
<!-- /sample -->

<!-- sample: rebase-policy -->
```csharp
var policyBase = new RebasePolicySettings { Label = "a", Tag = "a" };
var policyEdited = new RebasePolicySettings { Label = "b", Tag = "b" };
var policyCurrent = new RebasePolicySettings { Label = "a", Tag = "c" };

if (!policyBase.CreateChangeSet(policyEdited).TryApplyTo(policyCurrent, out var merged))
{
    throw new InvalidOperationException("The policy reconciles divergent labels.");
}

// merged.Label == "b"
// merged.Tag == "b|c"
```
<!-- /sample -->

Selection order for scalar and whole-replace members: an explicit `FragmentRebasePolicy<T>` first, then `FragmentMergeStrategy<T>.TryRebase`, then `ChangePayloadRebaseOptions.DefaultRebaseMode` when it is not `Default`, then the built-in three-way reconciliation. Merge still uses the merge strategy when a policy is present.

Policies apply to scalar and whole-replace members only. Nested models, keyed sequences, dictionaries, and `Append`/`SetUnion` members reconcile member by member; a whole-member policy on those shapes fails with [SPF027](analyzer.md#spf027-invalid-custom-rebase-policy).

`FragmentRebasePolicy<T>` ships `FailOnConflict`, `PreferIncoming`, and `PreferCurrent` factories. The prefer modes overwrite on divergence and never report a conflict; they are last-write-wins shortcuts rather than reconciliations that found no conflict.

## Redacted Before-States

A redacted-before member carries its desired value but withholds its before-state, as with a write-only secret. By default it passes through as its explicit patch operation: no historical comparison, no invented baseline, and no automatic undo. Pass `RejectChangesWithRedactedBeforeValuesDuringRebase = true` to fail instead:

<!-- sample: rebase-redacted -->
```csharp
var secretBase = new RebaseSettings { RetryCount = 1, Label = "a" };
var secretEdited = new RebaseSettings { RetryCount = 1, Label = "new-secret" };
var secretCurrent = new RebaseSettings { RetryCount = 1, Label = "other" };
var redacted = new ChangePayloadRebaseOptions
{
    RejectChangesWithRedactedBeforeValuesDuringRebase = true,
    RedactedBeforePaths = ["Label"],
};

if (
    secretBase
        .CreateChangeSet(secretEdited)
        .TryApplyTo(secretCurrent, out _, out var redactedConflicts, redacted)
)
{
    throw new InvalidOperationException("Expected a redacted-before failure.");
}

var redactedConflict = redactedConflicts.Single();
// redactedConflict.Kind == SparseConflictKind.RedactedBefore
// redactedConflict.PathText == "Label"
```
<!-- /sample -->

Strict failure is atomic for a mixed request: the redacted member is excluded from the rebased change and `TryApplyTo` returns `false` without a partially applied model. Reports carry path and kind but no secret plaintext: the `RedactedBefore` conflict attaches missing base, local, and current values. Redacted is not `Missing`: the desired value is present and only its history is withheld. Unconditional `Patch` application is a baseline-free overwrite rather than a historical rebase, so it never consults these options.

Downstream generators formalize the same redaction as a transport policy: the in-memory `ChangeSet` stays complete and baseline-aware, while the payload omits the before-state and keeps the required after-state. A redacted payload cannot convert to a complete `ChangeSet`; project it with the payload `ToPatch()` instead, which applies the requested after-state without historical comparison, as for an explicit patch set. A strict rebase policy is available to downstream generators to refuse such projections. The wire version token stays `"0.1"`.

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

The server in the disconnected-editing flow loads only the current state and still rebases correctly, because the incoming ChangeSet already carries the before-state its own transitions need. Persistence still needs its normal race protection (a concurrency token such as an EF `rowversion`, a `Version` column, an `UpdatedAt` marker, an ETag, or an operation id). Those tokens are application and envelope metadata, not members of the ChangeSet itself. SparseFragments prescribes neither the token type nor the persistence technology.

## Mixed Requests With Redacted Members

A member whose before-state arrives redacted is an explicit write-only operation: the sender could not disclose the previous value, often because the value is secret. The request still carries the requested after-state. Ordinary members in the same request keep ordinary baseline-aware validation and rebase.

<!-- sample: mixed-apply -->
```csharp
var currentModel = new RebaseDocsSettings { RetryCount = 1, Label = "current" };
var payload = JsonSerializer.Deserialize<RebaseDocsSettings.ChangePayload>(
    """{"version":"0.1","changes":[{"member":"Label","before":{"state":"redacted","value":null},"after":{"state":"value","value":"rotated"}},{"member":"RetryCount","before":{"state":"value","value":1},"after":{"state":"value","value":2}}]}"""
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

Three-way rebase compares the recorded before-state with the current state. A redacted before-state supplies nothing to compare, so the default applies the requested after-state directly, the same way an explicit patch set does. Passing a value through is not the same as reconciling concurrent edits to that member: revision checks, ETags, and authorization stay with the application, as described in No Revision History Required above. Atomicity holds at the request boundary, so a conflict in any ordinary member fails the whole request instead of persisting the write-only subset.

An application request therefore wraps the ChangeSet in its own envelope:

<!-- illustrative: schematic envelope; uses undefined member names and does not compile as written -->
```csharp
// Application envelope: IDs and concurrency tokens live outside ChangeSet.
sealed record UpdateOrderRequest(
    Guid OrderId,
    byte[] RowVersion,
    Order.ChangePayload Changes);
```

The handler converts the payload with `ToChangeSet()`, loads only the current database state, calls `TryApplyTo(current, out updated, out conflicts)`, and, when there are no conflicts, saves under the normal concurrency token. When conflicts remain, it returns them instead of saving.

## End-to-End Example

Here is a complete pass through client edit, serialization, current-state rebase, and save-or-conflict. The payload shapes in this flow are specified in the [ChangePayload wire reference](change-payload.md).

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

### Save under a concurrency token

Rebase reconciles member edits. It does not replace persistence race protection. The application carries its own concurrency token, such as a row version, ETag, or `UpdatedAt` marker, outside the `ChangeSet`. Two tokens stay distinct: the client token records the version the client read (contextual metadata, never a pre-rebase rejection), and the loaded server token guards the conditional write inside the transaction.

Member-level rebase never makes the read, modify, and write sequence atomic. The datastore owns the token increment and the atomic conditional write. A successful `TryApplyTo` alone is never a committed save.

The store below models that contract in memory. `LoadCurrent` returns the row with its token; `TrySave` writes only when the row still carries the expected token, then increments it. A `false` return means another writer moved the row first, the same way a zero affected-row count does for `UPDATE ... WHERE Id = ... AND Version = ...` or an EF Core concurrency-token conflict.

<!-- sample: rebase-server-store -->
```csharp
sealed class RebaseVersionedStore
{
    private RebaseDocsSettings _row = new() { RetryCount = 1, Label = "a" };
    private int _version = 7;
    private bool _seeded;

    public int WriteCount { get; private set; }

    public void Seed(RebaseDocsSettings row, int version)
    {
        _row = row;
        _version = version;
        _seeded = true;
    }

    public RebaseDocsSettings LoadCurrent(out int version)
    {
        version = _version;
        return new RebaseDocsSettings { RetryCount = _row.RetryCount, Label = _row.Label };
    }

    // Compare-and-swap: the write lands only when the row still carries
    // expectedVersion, and the store owns the increment. A false return
    // means another writer moved the row first.
    public bool TrySave(int expectedVersion, RebaseDocsSettings merged)
    {
        if (!_seeded || expectedVersion != _version)
        {
            return false;
        }

        _row = new RebaseDocsSettings { RetryCount = merged.RetryCount, Label = merged.Label };
        _version++;
        WriteCount++;
        return true;
    }
}
```
<!-- /sample -->

The flow below reuses one set of states throughout. The client reads A at v7 and submits A to B (RetryCount 1 to 2, Label untouched). Another writer persists C at v8 (Label "a" to "b"). The server loads only C at v8, rebases onto it, and conditionally writes against v8.

<!-- sample: rebase-server-save -->
```csharp
var store = new RebaseVersionedStore();
store.Seed(new RebaseDocsSettings { RetryCount = 1, Label = "a" }, version: 7);

// The client read A at v7 and edited A -> B. The v7 token travels as request
// metadata; the server never rejects a disjoint change on the stale client
// token before reconciliation. Only the loaded server token guards the write.
var clientBaseline = new RebaseDocsSettings { RetryCount = 1, Label = "a" };
const int clientVersion = 7;
var clientEdited = new RebaseDocsSettings { RetryCount = 2, Label = "a" };
var incoming = JsonSerializer.Deserialize<RebaseDocsSettings.ChangePayload>(
    JsonSerializer.Serialize(clientBaseline.CreateChangeSet(clientEdited).ToPayload())
)!.ToChangeSet();

// Another writer persisted C at v8 (Label "a" -> "b") before the save.
var writerRow = store.LoadCurrent(out var writerVersion);
if (!store.TrySave(writerVersion, new RebaseDocsSettings { RetryCount = 1, Label = "b" }))
{
    throw new InvalidOperationException("The seed write conflicts.");
}

// Disjoint success: the server loads only C at v8, rebases B onto it, and
// conditionally writes against v8. Rebase alone never commits the row.
var loaded = store.LoadCurrent(out var loadedVersion);
// loadedVersion == 8
if (!incoming.TryApplyTo(loaded, out var merged, out var saveConflicts))
{
    throw new InvalidOperationException("The change conflicts with the current row.");
}

if (!store.TrySave(loadedVersion, merged))
{
    throw new InvalidOperationException("The row moved under the save; reload and retry.");
}

// The row now holds RetryCount 2 with Label "b" at v9. The client token is
// still 7: it described the read, never the write guard.

// An overlapping edit reports structured conflicts and writes nothing: with
// C also moving RetryCount, the same incoming change cannot rebase.
var overlapCurrent = new RebaseDocsSettings { RetryCount = 3, Label = "b" };
if (incoming.TryApplyTo(overlapCurrent, out _, out var overlapConflicts))
{
    throw new InvalidOperationException("Expected a conflict.");
}

var overlapConflict = overlapConflicts.Single();
// overlapConflict.Kind == SparseConflictKind.Scalar
// overlapConflict.PathText == "RetryCount"

// A race after the read but before the conditional write fails the
// affected-row check instead of overwriting. The save reloads the newer row,
// rebases the same incoming change onto it, and retries with a bound instead
// of reusing the stale merged model.
var raced = store.LoadCurrent(out var racedVersion);
// racedVersion == 9
if (!incoming.TryApplyTo(raced, out var racedMerge, out _))
{
    throw new InvalidOperationException("The change conflicts with the current row.");
}

store.TrySave(racedVersion, new RebaseDocsSettings { RetryCount = 2, Label = "c" });
var raceHit = store.TrySave(racedVersion, racedMerge);
// raceHit == false: the row moved under the save, so nothing overwrote it.

var saved = raceHit;
var attempts = 0;
while (!saved && attempts < 2)
{
    attempts++;
    var retryLoaded = store.LoadCurrent(out var retryVersion);
    if (!incoming.TryApplyTo(retryLoaded, out var retryMerged, out _))
    {
        throw new InvalidOperationException("The change conflicts with the current row.");
    }

    saved = store.TrySave(retryVersion, retryMerged);
}

if (!saved)
{
    throw new InvalidOperationException("The save did not converge; report exhaustion.");
}

// The row holds RetryCount 2 with Label "c" at v11. A stale-token rejection, a
// semantic conflict, an affected-row race with exhaustion, and a validation
// failure stay distinct outcomes with distinct handling.
```
<!-- /sample -->

A stale client token never rejects a disjoint change before reconciliation; the conditional write against the loaded token is the only race guard. A rebase conflict means the edits overlap: surface the structured conflicts instead of saving, and the failed attempt writes nothing. An affected-row failure means the row moved between the load and the write: reload the current state, rebase again, and retry with a bound. Retry exhaustion is its own outcome, reported rather than overwritten. Validation errors stay separate from all three. The conflict-checked `ChangeSet.TryApplyInPlace` fits bound models; the blind `ToPatch().ApplyInPlace` form skips the before-state check and suits callers that already own conflict handling, as described in [In-place application for bound models](#in-place-application-for-bound-models).
