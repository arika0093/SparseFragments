# Patch Rebase

Rebase replays a patch authored against an older state onto a newer state. Two writers start from the same baseline, one commits first, and the other's patch is reconciled against the committed state.

## The Base / Local / Current Model

Rebase takes three states:

* **base** — the state the local patch was authored against;
* **local** — the desired patch (the user's edits);
* **current** — the newer state onto which the patch is replayed.

All three are presence-aware `Optional<Fragment?>` values, so *missing*, *present null*, and *present value* participate in reconciliation exactly as they do in merge: `Missing` never equals a present value — not even a present `null` or `default` — so `missing → present null`, `present null → missing`, and `missing → present default` are all observable transitions.

```csharp
var baseState = Optional<Settings.Fragment?>.Present(
    Settings.Fragment.From(new Settings { RetryCount = 1, Label = "a" }));
var local = new Settings.Patch { RetryCount = 2 };
var currentState = Optional<Settings.Fragment?>.Present(
    Settings.Fragment.From(new Settings { RetryCount = 1, Label = "b" }));

RebaseResult<Settings.Patch> result =
    Settings.Patch.Rebase(baseState, local, currentState);

if (!result.HasConflicts)
{
    var applied = result.Patch.Apply(currentState);
    // applied: RetryCount == 2 (local edit), Label == "b" (concurrent edit kept)
}
```

Rebase returns a **new patch for the current state** plus **structured conflicts** for edits that cannot be reconciled automatically. Conflicting members are excluded from the rebased patch.

## The Three Outcomes

### 1. Disjoint edits replay cleanly

Local and concurrent edits touch different members: the local edit is replayed onto the current state, and the concurrent edit is preserved.

```csharp
// base:   { RetryCount = 1, Label = "a" }
// local:  RetryCount = 2
// current:{ RetryCount = 1, Label = "b" }
// result: RetryCount = 2, Label untouched → applied gives { RetryCount = 2, Label = "b" }
```

### 2. Already-applied edits become no-ops

The local edit is already present in `current` (someone else made the same change): rebase succeeds with a semantic no-op — the rebased patch is empty for that member.

```csharp
// base:   { RetryCount = 1 }
// local:  RetryCount = 2
// current:{ RetryCount = 2 }
// result: no conflicts, result.Patch.IsEmpty == true
```

### 3. Conflicting edits produce structured conflicts

Local and current changed the same member differently: the member is excluded from the rebased patch and reported as a conflict.

```csharp
// base:   { RetryCount = 1 }
// local:  RetryCount = 2
// current:{ RetryCount = 3 }

var result = Settings.Patch.Rebase(baseState, local, currentState);

result.HasConflicts.ShouldBeTrue();
var conflict = result.Conflicts.Single();
conflict.Kind.ShouldBe(SparsePatchConflictKind.Scalar);
conflict.Path.ShouldBe(["RetryCount"]);
conflict.BaseValue.Value.ShouldBe(1);
conflict.LocalValue.Value.ShouldBe(2);
conflict.CurrentValue.Value.ShouldBe(3);
```

## Structured Conflicts

Each `SparsePatchConflict` carries domain-neutral, presence-aware information:

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

## Collection and Structural Behavior

* **Nested structural members** rebase member-by-member; only the colliding leaf conflicts while disjoint nested edits replay.
* **Append-merged collections** treat an already-applied addition as a no-op (replaying `["a", "b"]` onto a current state that already contains `["a", "b"]` stays put) and report concurrent divergent growth as `CollectionAppend`.
* **Set-union members** rebase against comparer-aware equality: same entries under the same comparer replay cleanly; entries that differ under the member's comparer conflict as `CollectionSetUnion`.
* **Keyed structural collections** rebase element-wise where keys line up; per-key divergent edits conflict while disjoint key ranges (added/removed/edited on different keys) replay.
* **The whole contribution** participates too: `Set`-style root transitions and root presence changes (`Missing` vs present-null vs present) rebase through the same machinery, with unresolvable root divergence reported as `WholeContribution`.
* **An empty local patch** rebases across root presence changes without conflicts — there is nothing to reconcile.

## Custom Strategies

A custom `FragmentMergeStrategy<T>` can override `TryRebase` to define its own three-way reconciliation for the member:

* it receives `Optional<T>` for the edit base, the desired state, and the current state, with the missing/present distinction preserved end to end;
* a present result maps to a `Set` patch operation, a missing result maps to `Unset`, and a result equal to the current state stays `Unchanged` (a semantic no-op);
* the default implementation succeeds when the desired state still matches the edit base (unchanged local edit — the current state wins) or when the current state matches the edit base or the desired state (clean replay or already applied), and reports a conflict otherwise;
* returning `false` surfaces a `CustomStrategy` conflict carrying the member path and the three values.
