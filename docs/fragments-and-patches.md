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

For ordinary, present non-null model roots, the same algebra has convenience overloads: `ChangeSet.Between(beforeModel, afterModel)`, `ChangeSet.FromPatch(baselineModel, patch)`, and model-targeted apply/rebase methods. These snapshot through `Fragment.From` and delegate to the presence-aware behavior; use the `Optional<Fragment?>` overloads when root Missing / Present(null) semantics matter.

<!-- sample: core-between -->
```csharp
var a = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a" }));
var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

var removal = CounterSettings.ChangeSet.Between(a, b); // Label: present → missing
// !removal.ToPatch().Apply(a).Value!.Label.IsPresent
```
<!-- /sample -->

Reach for `ChangeSet.Between` when comparing sparse states whose transition may later be observed through typed member transitions, serialized, inverted, composed, or rebased. Reach for `Fragment.Diff` when comparing ordinary models for persistence or defaults comparison. All baseline-dependent operations belong to ChangeSet; use a mutable `Patch` for baseline-free local operations only.

## Observe Typed Member Transitions

A `ChangeSet` is the immutable observed `before -> after` transition. Generated member names mirror the source model, so reading a transition needs no reflection, property descriptors, or `object?` casts:

<!-- sample: core-typed -->
```csharp
var before = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 }));
var after = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 }));

var changes = CounterSettings.ChangeSet.Between(before, after);

if (changes.Label.IsChanged)
{
    Console.WriteLine($"{changes.Label.Before} -> {changes.Label.After}");
}
// changes.Label.Before.Value == "a"
// changes.Label.After.Value == "b"
// changes.RetryCount.IsChanged == false
```
<!-- /sample -->

Unchanged members remain typed and report `IsChanged == false` without retaining anything: their `Before` / `After` are missing. Changed members preserve the missing / present-null / present-value states, so `missing -> present`, `present null -> missing`, and value changes are all observable without losing presence information. When a baseline-free operation is needed instead, cross the explicit boundary:

```csharp
var patch = changes.ToPatch();
```

Nested members recurse through the same typed shape:

<!-- sample: core-nested-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class DocsOrder
{
    public string? Name { get; set; }

    public DocsCustomer? Customer { get; set; }
}

public partial class DocsCustomer
{
    public string Name { get; set; } = string.Empty;
}
```
<!-- /sample -->

<!-- sample: core-nested -->
```csharp
var before = Optional<DocsOrder.Fragment?>.Present(
    DocsOrder.Fragment.From(
        new DocsOrder { Name = "a", Customer = new DocsCustomer { Name = "Ann" } }));
var after = Optional<DocsOrder.Fragment?>.Present(
    DocsOrder.Fragment.From(
        new DocsOrder { Name = "a", Customer = new DocsCustomer { Name = "Bob" } }));

var changes = DocsOrder.ChangeSet.Between(before, after);
// changes.Name.IsChanged == false
// changes.Customer.Name.IsChanged == true
// changes.Customer.Name.Before.Value == "Ann"
// changes.Customer.Name.After.Value == "Bob"
```
<!-- /sample -->

`changes.Customer` is the nested `before -> after` transition for that member: it reports `IsEmpty` for the subtree while its own members expose `IsChanged` / `Before` / `After`. The root `ChangeSet` itself is not a generic enumerable — model members are heterogeneous, so there is no single element type to enumerate. Keyed collection transitions are the exception because their items share one `TKey` / `TElement` type (see [Keyed collections](keyed-collections.md)).

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

Typed convenience projections such as `IsChanged`, keyed `Added` / `Removed` / `Edited`, item enumeration, and `BeforeOrder` / `AfterOrder` / `OrderChanged` are API projections over the transition, not duplicate wire fields. The canonical JSON contract carries only the changed-path transition state (`$whole` for whole-root transitions, per-member before/after otherwise — never full fragments); deserialization recomputes the projections, so a round-tripped ChangeSet observes the same typed transitions and `ToPatch().Apply(start)` still replays the after-state.

### ChangeSet JSON v1

ChangeSet JSON is a versioned SparseFragments format beginning with version 1. The serialized document is a root envelope with a required integer `version` and a `changes` object carrying the v1 body grammar:

```json
{
  "version": 1,
  "changes": {
    "Label": {
      "before": { "state": "value", "value": "a" },
      "after": { "state": "value", "value": "b" }
    }
  }
}
```

An empty ChangeSet serializes as `{"version": 1, "changes": {}}`. The `version` and `changes` names are wire-format metadata and always use those exact names, independent of `JsonSerializerOptions.PropertyNamingPolicy`; model-derived member names inside `changes` keep the existing `JsonPropertyName` / naming-policy behavior. Nested ChangeSets reuse the body grammar directly and never emit nested envelopes. Readers require exactly version 1, reject missing/duplicate/non-integer/unsupported versions, missing/duplicate `changes`, and unknown envelope properties, independent of root property order. The pre-v1 unversioned shape is not accepted.

For keyed collection edits, each item's `before` and `after` objects contain only the changed members; the key is already carried by the item's `key` field. Adds and removes still carry the full item value, and reorder-only entries retain their endpoints.

Callers still use ordinary `System.Text.Json`; SparseFragments adds no separate public JSON codec API and generates no JSON Schema. `Patch` JSON is not versioned by this contract, and the format carries no transport metadata, timestamps, revisions, ETags, model type names, or persistence policy. Future incompatible ChangeSet format changes require a new version.

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
| Compare ordinary present models | `T.ChangeSet.Between(beforeModel, afterModel)` | ChangeSet |
| Observe a member transition | `changes.Label.IsChanged` / `Before` / `After` | Member transition |
| Observe a nested transition | `changes.Customer.Name.IsChanged` | Member transition |
| Project a transition to operations | `changes.ToPatch()` | Patch |
| Attach a baseline to a patch | `T.ChangeSet.FromPatch(baseline, patch)` | ChangeSet |
| Apply a Patch to an ordinary model | `patch.ApplyTo(model)` | New model |
| Rebase onto an ordinary model | `changes.RebaseOnto(model)` | Rebase result |
| Apply a ChangeSet if conflict-free | `changes.TryApplyTo(model, out updated)` | `true` + model, or `false` |
| Get ChangeSet conflict details | `changes.TryApplyTo(model, out updated, out conflicts)` | Conflicts on `false` |
| Reverse a transition | `changes.Invert()` | ChangeSet |
| Chain contiguous transitions | `first.Compose(second)` | ChangeSet |
| Reconcile against newer state | `changes.RebaseOnto(current)` | ChangeSet + conflicts |
| Apply Patch operations | `fragment.Apply(patch)` | Fragment |

`Merge` composes contributions; `ApplyChanges` replays a `Diff` Fragment; `Apply` executes `Patch` operations. They are not interchangeable: `ApplyChanges` never unsets a member that the diff did not carry, while a `Patch` explicitly can.
