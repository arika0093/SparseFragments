# Fragments and Patches

A `Fragment` is sparse state: for every model member it records missing, present, or explicitly null. A `Patch` is a set of operations over that sparse state — set a value, set an explicit null, remove a contribution, or leave it unchanged. Both exist because they answer different questions: a fragment says *what is specified*, a patch says *what to change*.

Samples below assert with a small local `Require` helper; use your own test framework in real code.

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

Capture state sparsely with `Fragment.From`, or build it member by member. `From` snapshots an isolated copy; sparse construction shares assigned references (see [Clone & ownership](cloning-and-ownership.md)).

<!-- sample: core-create -->
```csharp
var current = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 });

var sparse = new CounterSettings.Fragment
{
    RetryCount = 3, // present; Label stays missing
};

DocsCheck.Require(!sparse.Label.IsPresent, "Label stays missing");
DocsCheck.Require(sparse.RetryCount.Value == 3, "RetryCount is present");
```
<!-- /sample -->

Prefer `Fragment.From(model)` when the source model may keep mutating. Prefer `new X.Fragment { ... }` when constructing the contribution directly. Missing never equals a present value — not even a present `null` or `default` — so `missing → present null`, `present null → missing`, and `missing → present default` are all observable transitions.

## Layer Overrides with Merge

`lower.Merge(higher)` overlays two fragments: present members of the higher layer win, missing members fall through. Direction matters — the higher-priority layer is the argument.

<!-- sample: core-layering -->
```csharp
var defaults = new CounterSettings.Fragment { RetryCount = 3 };
var environment = new CounterSettings.Fragment { RetryCount = 5 };
var user = new CounterSettings.Fragment { Label = "dark" };

var effective = defaults.Merge(environment).Merge(user);
DocsCheck.Require(effective.Label.Value == "dark", "user Label wins");
DocsCheck.Require(effective.RetryCount.Value == 5, "environment RetryCount wins");
```
<!-- /sample -->

## Diff Ordinary Models

`Fragment.Diff(beforeModel, afterModel)` compares two ordinary models and returns a sparse `Fragment` holding the changed after-values. Apply it with `ApplyChanges`. Use it to persist only what differs from defaults.

<!-- sample: core-diff -->
```csharp
var beforeModel = new CounterSettings { Label = "a", RetryCount = 1 };
var afterModel = new CounterSettings { Label = "a", RetryCount = 2 };

var diff = CounterSettings.Fragment.Diff(beforeModel, afterModel);
DocsCheck.Require(!diff.Label.IsPresent, "unchanged Label is missing");
DocsCheck.Require(diff.RetryCount.Value == 2, "changed RetryCount is present");

var restored = CounterSettings.Fragment.From(beforeModel).ApplyChanges(diff);
DocsCheck.Require(restored.RetryCount.Value == 2, "ApplyChanges replays the diff");
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
DocsCheck.Require(updated.Label.IsPresent, "explicit null stays present");
DocsCheck.Require(updated.Label.Value is null, "value is null");
DocsCheck.Require(updated.RetryCount.Value == 1, "untouched member kept");

var remove = new CounterSettings.Patch();
remove.RetryCount.Unset();
DocsCheck.Require(
    !remove.Apply(basis).Value!.RetryCount.IsPresent, "Unset drops the contribution");
```
<!-- /sample -->

## Fragment.Diff vs Patch.Between

Both derive change, but over different inputs for different jobs:

- `Fragment.Diff(beforeModel, afterModel)` takes **ordinary models** and returns a **Fragment** of changed after-values. Replay it with `ApplyChanges`. Reach for it when comparing model snapshots (persistence, defaults comparison).
- `Patch.Between(beforeSparse, afterSparse)` takes **sparse contribution states** (`Optional<Fragment?>`) and returns a **Patch** of operations that preserves presence transitions such as `present → missing`. Replay it with `Apply`. Reach for it when reconciling layered or partial contributions (rebase input, edit sessions, stream processing).

<!-- sample: core-between -->
```csharp
var a = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a" }));
var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

var removal = CounterSettings.Patch.Between(a, b); // Label: present → missing
DocsCheck.Require(
    !removal.Apply(a).Value!.Label.IsPresent, "Between preserves the removal");
```
<!-- /sample -->

## Which API for Which Task

- Layering defaults, environment, and user overrides → `Merge` (higher layer wins).
- Persisting or transmitting only differences → `Fragment.Diff` plus `ApplyChanges`.
- Expressing an explicit edit (set / null / unset) → `Patch` plus `Apply`.
- Deriving operations between two sparse contributions → `Patch.Between`.
- Merging whole object graphs member by member → nested fragments with [Merge strategies](merge-strategies.md).

`Merge` composes contributions; `ApplyChanges` replays a `Diff` Fragment; `Apply` executes `Patch` operations. They are not interchangeable: `ApplyChanges` never unsets a member that the diff did not carry, while a `Patch` explicitly can.
