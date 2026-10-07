# Fragments and Patches

A `Fragment` is sparse state: for every model member it records missing, present, or explicitly null. A `Patch` is a set of operations over that sparse state — set a value, set an explicit null, remove a contribution, or leave it unchanged. A `ChangeSet` is the immutable before → after transition between two sparse states. The three exist because they answer different questions: a fragment says *what is specified*, a patch says *what to change* ("set these values"), and a change set says *what changed* ("these values changed from X to Y").

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

`new X.Patch { ... }` expresses edits directly: assigning a value sets it (including an explicit `null`), `Unset()` drops the contribution, and untouched members stay unchanged. Apply a patch with `Apply`. A Patch is mutable and baseline-free: it carries desired operations without saying which state they were derived from.

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

## Derive Transitions with ChangeSet

`T.ChangeSet.Between(beforeSparse, afterSparse)` takes sparse contribution states (`Optional<Fragment?>`) and returns the immutable before → after transition. It preserves presence transitions such as `present → missing` exactly, including the root `Missing` / present-null / present-value states. Replay it with `ToPatch()` followed by `Apply`, or reconcile it against newer state with `RebaseOnto` (see [ChangeSet rebase](rebase.md)). A ChangeSet carries the before-state required for the transitions it represents — it does not store a mandatory full baseline snapshot beyond that semantic information.

<!-- sample: core-between -->
```csharp
var a = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a" }));
var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

var removal = CounterSettings.ChangeSet.Between(a, b); // Label: present → missing
// !removal.ToPatch().Apply(a).Value!.Label.IsPresent
```
<!-- /sample -->

Reach for `ChangeSet.Between` when comparing sparse states whose transition may later be inspected, serialized, inverted, composed, or rebased. Reach for `Fragment.Diff` when comparing ordinary models for persistence or defaults comparison. All baseline-dependent operations belong to ChangeSet; use a mutable `Patch` for baseline-free local operations only.

## Patch vs ChangeSet

```text
Patch
    "set these values"
    mutable desired operation
    baseline-free

ChangeSet
    "these values changed from X to Y"
    immutable before -> after transition
    baseline-aware
    supports invert / compose / rebase
```

<!-- sample: core-changeset -->
```csharp
var start = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
var finish = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 }));

// Patch says "set these values": mutable and baseline-free.
var desired = new CounterSettings.Patch { Label = "b" };

// ChangeSet says "these values changed from X to Y": immutable and baseline-aware.
var transition = CounterSettings.ChangeSet.Between(start, finish);
// transition.IsEmpty == false
// transition.ToPatch().Apply(start) replays finish

// ChangeSet.FromPatch attaches a known baseline to an existing patch.
var fromPatch = CounterSettings.ChangeSet.FromPatch(start, desired);
// fromPatch.ToPatch().Apply(start) replays finish
```
<!-- /sample -->

`ChangeSet.ToPatch()` is the explicit information-loss boundary: it discards the before-state and returns the equivalent desired-operation patch. There is no silent mixed composition back into a ChangeSet — a baseline-free Patch can introduce a changed path whose before-state is unknown.

## Compose and Invert ChangeSets

ChangeSets compose sequentially and invert without an external baseline, while Patches compose as baseline-free operations:

<!-- sample: core-algebra -->
```csharp
var s0 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
var s1 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 }));
var s2 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 }));

// ChangeSet + ChangeSet -> ChangeSet for contiguous transitions.
var first = CounterSettings.ChangeSet.Between(s0, s1);
var second = CounterSettings.ChangeSet.Between(s1, s2);
var combined = first.Compose(second);
// combined.ToPatch().Apply(s0) reaches s2

// ChangeSet inverts without an external baseline.
var undone = combined.Invert();
// undone.ToPatch().Apply(s2) walks back to s0

// Patch + Patch -> Patch stays baseline-free.
var local = new CounterSettings.Patch { Label = "x" };
var more = new CounterSettings.Patch { RetryCount = 5 };
var both = local.Compose(more);
// both.Label == "x", both.RetryCount == 5
```
<!-- /sample -->

The closed algebra is:

```text
Patch + Patch
    -> Patch

ChangeSet + ChangeSet
    -> ChangeSet

ChangeSet -> Patch
    -> explicit ToPatch()
```

`Compose` requires contiguous transitions: the first ChangeSet's after-state must equal the second's before-state, otherwise it throws `InvalidOperationException`. Static `Compose(first, second)` overloads mirror the instance methods on both types.

## Serialize Patches and ChangeSets

SparseFragments defines no JSON transport protocol. Generated `Patch` and `ChangeSet` types serialize through the ordinary `System.Text.Json` APIs:

<!-- sample: core-serialization -->
```csharp
using System.Text.Json;

var start = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
var finish = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 }));

var changes = CounterSettings.ChangeSet.Between(start, finish);

// SparseFragments defines no transport protocol: use ordinary System.Text.Json.
var json = JsonSerializer.Serialize(changes);
var restored = JsonSerializer.Deserialize<CounterSettings.ChangeSet>(json)!;
// restored.ToPatch().Apply(start) replays finish

var patchJson = JsonSerializer.Serialize(new CounterSettings.Patch { Label = "b" });
var patchBack = JsonSerializer.Deserialize<CounterSettings.Patch>(patchJson)!;
// patchBack.Label == "b"
```
<!-- /sample -->

For NativeAOT, register the generated top-level types on the normal application-owned source-generated context:

```csharp
[JsonSerializable(typeof(Order.Patch))]
[JsonSerializable(typeof(Order.ChangeSet))]
internal partial class AppJsonContext : JsonSerializerContext;
```

```csharp
var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonContext.Default };
var json = JsonSerializer.Serialize(changes, options);
var restored = JsonSerializer.Deserialize<Order.ChangeSet>(json, options);
```

Registering the generated top-level Patch/ChangeSet types is sufficient for their statically reachable generated object graphs, subject to the ordinary System.Text.Json rules for dynamic/`object`/polymorphic member values: member scalar/collection types resolve through `options.TypeInfoResolver` like any other application type, so add them to the application context when the trimmer requires it.

## Which API for Which Task

| Need | API | Result |
| --- | --- | --- |
| Build a sparse override | `new T.Fragment { ... }` | Fragment |
| Snapshot an ordinary model | `T.Fragment.From(model)` | Fragment |
| Combine lower/higher layers | `lower.Merge(higher)` | Fragment |
| Compare two ordinary models | `T.Fragment.Diff(before, after)` | Fragment diff |
| Replay a Fragment diff | `fragment.ApplyChanges(changes)` | Fragment |
| Express explicit set/null/unset edits | `new T.Patch { ... }` | Patch |
| Compose local operations | `patch.Compose(next)` | Patch |
| Compare sparse states exactly | `T.ChangeSet.Between(before, after)` | ChangeSet |
| Project a transition to operations | `changes.ToPatch()` | Patch |
| Attach a baseline to a patch | `T.ChangeSet.FromPatch(baseline, patch)` | ChangeSet |
| Reverse a transition | `changes.Invert()` | ChangeSet |
| Chain contiguous transitions | `first.Compose(second)` | ChangeSet |
| Reconcile against newer state | `changes.RebaseOnto(current)` | ChangeSet + conflicts |
| Apply Patch operations | `fragment.Apply(patch)` | Fragment |

`Merge` composes contributions; `ApplyChanges` replays a `Diff` Fragment; `Apply` executes `Patch` operations. They are not interchangeable: `ApplyChanges` never unsets a member that the diff did not carry, while a `Patch` explicitly can.
