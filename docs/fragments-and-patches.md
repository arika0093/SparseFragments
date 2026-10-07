# Fragments and Patches

A `Fragment` is sparse state: for every model member it records missing, present, or explicitly null. A `Patch` is a set of operations over that sparse state — set a value, set an explicit null, remove a contribution, or leave it unchanged. Both exist because they answer different questions: a fragment says *what is specified*, a patch says *what to change*.

<!-- sample: core-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class CounterSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}
```
<!-- /sample -->

## Create a Sparse Fragment

`new T.Fragment { ... }` represents only the members you specify. `T.Fragment.From(model)` snapshots an ordinary model as a full contribution.

<!-- sample: core-create -->
```csharp
var current = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 });

var sparse = new CounterSettings.Fragment
{
    RetryCount = 3, // present; Label stays missing
};

// sparse.Label.IsPresent == false
// sparse.RetryCount.Value == 3
```
<!-- /sample -->

`From` also isolates the Fragment from later mutation of the source model. Direct sparse construction keeps assigned reference values unless explicitly cloned (see [Clone & ownership](cloning-and-ownership.md)).

Use `new T.Fragment { ... }` for sparse contributions and overrides. Use `Fragment.From(model)` when an existing ordinary model should become a full Fragment state. Snapshotting is a consequence of that choice: a snapshot stays stable while the source model keeps changing. Missing never equals a present value — not even a present `null` or `default` — so `missing → present null`, `present null → missing`, and `missing → present default` are all observable transitions.

## Layer Overrides with Merge

`lower.Merge(higher)` overlays two fragments: present members of the higher layer win, missing members fall through. Direction matters — the higher-priority layer is the argument.

<!-- sample: core-layering -->
```csharp
var defaults = new CounterSettings.Fragment { RetryCount = 3 };
var environment = new CounterSettings.Fragment { RetryCount = 5 };
var user = new CounterSettings.Fragment { Label = "dark" };

var effective = defaults.Merge(environment).Merge(user);
// effective.Label == "dark"
// effective.RetryCount == 5
```
<!-- /sample -->

## Diff Ordinary Models

`Fragment.Diff(beforeModel, afterModel)` compares two ordinary models and returns a sparse `Fragment` holding the changed after-values. Apply it with `ApplyChanges`. Use it to persist only what differs from defaults.

<!-- sample: core-diff -->
```csharp
var beforeModel = new CounterSettings { Label = "a", RetryCount = 1 };
var afterModel = new CounterSettings { Label = "a", RetryCount = 2 };

var diff = CounterSettings.Fragment.Diff(beforeModel, afterModel);
// diff.Label.IsPresent == false
// diff.RetryCount.Value == 2

var restored = CounterSettings.Fragment.From(beforeModel).ApplyChanges(diff);
// restored.RetryCount.Value == 2
```
<!-- /sample -->

## Apply Explicit Edits with Patch

`new X.Patch { ... }` expresses edits directly: assigning a value sets it (including an explicit `null`), `Unset()` drops the contribution, and untouched members stay unchanged. Apply a patch with `Apply`.

<!-- sample: core-patch -->
```csharp
var basis = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 });

var update = new CounterSettings.Patch { Label = (string?)null };
var updated = basis.Apply(update);
// updated.Label.IsPresent == true
// updated.Label.Value is null
// updated.RetryCount.Value == 1

var remove = new CounterSettings.Patch();
remove.RetryCount.Unset();
// !remove.Apply(basis).Value!.RetryCount.IsPresent
```
<!-- /sample -->

## Fragment.Diff vs Patch.Between

Both derive change, but over different inputs for different jobs:

- `Fragment.Diff(beforeModel, afterModel)` takes **ordinary models** and returns a **Fragment** of changed after-values. Replay it with `ApplyChanges`. Reach for it when comparing model snapshots (persistence, defaults comparison).
- `Patch.Between(beforeSparse, afterSparse)` takes **sparse contribution states** (`Optional<Fragment?>`) and returns a **Patch** of operations that preserves presence transitions such as `present → missing`. Replay it with `Apply`. Reach for it when reconciling layered or partial contributions (rebase input, edit sessions).

<!-- sample: core-between -->
```csharp
var a = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a" }));
var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

var removal = CounterSettings.Patch.Between(a, b); // Label: present → missing
// !removal.Apply(a).Value!.Label.IsPresent
```
<!-- /sample -->

## Which API for Which Task

| Need | API | Result |
| --- | --- | --- |
| Build a sparse override | `new T.Fragment { ... }` | Fragment |
| Snapshot an ordinary model | `T.Fragment.From(model)` | Fragment |
| Combine lower/higher layers | `lower.Merge(higher)` | Fragment |
| Compare two ordinary models | `T.Fragment.Diff(before, after)` | Fragment diff |
| Replay a Fragment diff | `fragment.ApplyChanges(changes)` | Fragment |
| Express explicit set/null/unset edits | `new T.Patch { ... }` | Patch |
| Compare sparse states exactly | `T.Patch.Between(before, after)` | Patch |
| Apply Patch operations | `fragment.Apply(patch)` | Fragment |

`Merge` composes contributions; `ApplyChanges` replays a `Diff` Fragment; `Apply` executes `Patch` operations. They are not interchangeable: `ApplyChanges` never unsets a member that the diff did not carry, while a `Patch` explicitly can.

The [Collaborative editing example](../examples/CollaborativeEditing/README.md) exercises these APIs together: system defaults layered under per-workspace overrides via `Merge`, with each save derived by `Patch.Between` from a retained baseline.
