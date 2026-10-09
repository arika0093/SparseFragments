# Fragments and Patches

A `Fragment` is sparse state: for every model member it records missing, present, or explicitly null. A `Patch` is a set of operations over that sparse state (set a value, set an explicit null, remove a contribution, or leave it unchanged). A `ChangeSet` is the immutable before to after transition between two sparse states. A `ChangePayload` is the versioned JSON envelope that carries a `ChangeSet`, a `Patch`, or both member by member across a process boundary.

The four answer different questions: a fragment says *what is specified*, a patch says *what to change* ("set these values"), a change set says *what changed* ("these values changed from X to Y"), and a change payload says *what travels* ("apply these transitions and commands"). The task table at the end maps each need to its API.

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

`new T.Fragment { ... }` specifies only the members you assign. Any unassigned members remain missing. In contrast, `T.Fragment.From(model)` snapshots an ordinary model into a complete fragment contribution, isolating the fragment from subsequent mutations of the source model.

<!-- sample: core-create -->
```csharp
var current = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 }
);

var sparse = new CounterSettings.Fragment
{
    RetryCount = 3, // present; Label stays missing
};

// sparse.Label.IsPresent == false
// sparse.RetryCount.Value == 3
```
<!-- /sample -->

Direct sparse construction keeps assigned reference values unless explicitly cloned (see [Clone & ownership](cloning-and-ownership.md)).

SparseFragments distinguishes missing members from present values, including present `null` or type defaults. Because missing never equals a present value, transitions between missing, present null, and present defaults are all distinct, observable states.

## Layer Overrides with Merge

`lower.Merge(higher)` overlays two fragments: present members of the higher layer win, missing members fall through. Direction matters because the higher-priority layer is the argument.

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

`Fragment.Diff(beforeModel, afterModel)` compares two ordinary models and returns a sparse `Fragment` holding the changed after-values. Apply it with `ApplyChanges` to persist only what differs from defaults.

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

A `Patch` is mutable and baseline-free. It describes desired operations directly without recording which baseline state they were derived from.

`new X.Patch { ... }` specifies these operations: assigning a value sets it (including an explicit `null`), `Remove()` drops the contribution, and untouched members stay unchanged. Apply a patch to a fragment with `Apply`.

<!-- sample: core-patch -->
```csharp
var basis = CounterSettings.Fragment.From(
    new CounterSettings { Label = "a", RetryCount = 1 }
);

var update = new CounterSettings.Patch { Label = (string?)null };
var updated = basis.Apply(update);
// updated.Label.IsPresent == true
// updated.Label.Value is null
// updated.RetryCount.Value == 1

var remove = new CounterSettings.Patch();
remove.RetryCount.Remove();
// !remove.Apply(basis).Value!.RetryCount.IsPresent
```
<!-- /sample -->

## Derive Transitions with ChangeSet

`T.ChangeSet.Between(beforeSparse, afterSparse)` takes sparse contribution states (`Optional<Fragment?>`) and returns the immutable before to after transition. It preserves presence transitions such as present to missing exactly, including the root missing, present-null, and present-value states.

Replay it with `ToPatch()` followed by `Apply`, or reconcile it against newer state with `RebaseOnto` (see [ChangeSet rebase](rebase.md)). A ChangeSet carries the before-state required for the transitions it represents. It does not store a mandatory full baseline snapshot beyond that semantic information.

For ordinary, present non-null model roots, prefer the generated extensions: `beforeModel.CreateChangeSet(afterModel)` and `model.CreateEditSession()`. `CreateChangeSet` delegates to `T.ChangeSet.Between`, while the session retains a baseline as an edit continues. `ChangeSet.FromPatch(baselineModel, patch)` and model-targeted apply and rebase methods are also available. These APIs snapshot through `Fragment.From` and delegate to the presence-aware behavior. Use the `Optional<Fragment?>` overloads when root missing, present-null, or present-value semantics matter.

<!-- sample: core-between -->
```csharp
var a = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a" })
);
var b = Optional<CounterSettings.Fragment?>.Present(new CounterSettings.Fragment());

var removal = CounterSettings.ChangeSet.Between(a, b); // Label: present → missing
// !removal.ToPatch().Apply(a).Value!.Label.IsPresent
```
<!-- /sample -->

Use `ChangeSet.Between` when comparing sparse states whose transition may later be observed through typed member transitions, serialized, inverted, composed, or rebased. Use `Fragment.Diff` when comparing ordinary models for persistence or defaults comparison.

All baseline-dependent operations belong to ChangeSet. Use a mutable `Patch` for baseline-free local operations only.

## Observe Typed Member Transitions

A `ChangeSet` is the immutable observed before to after transition. Generated member names mirror the source model, so reading a transition needs no reflection, property descriptors, or `object?` casts:

<!-- sample: core-typed -->
```csharp
var before = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var after = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);

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

Unchanged members remain typed and report `IsChanged == false` without retaining anything: their `Before` and `After` are missing. Changed members preserve the missing, present-null, and present-value states, so missing to present, present null to missing, and value changes are all observable without losing presence information.

For consumers that need rows or logs instead of typed traversal, `EnumerateChanges()` returns flattened `ChangeInfo` entries with a path, presence-aware `Before` and `After` values, and a `ChangeKind`. Nested values use dotted paths; keyed entries use their key in brackets. `Added`, `Removed`, and `Changed` classify value transitions, while `Order` uses the collection path and carries the before and after key sequences. The method supplies change data, not presentation or formatting.

When a baseline-free operation is needed instead, cross the explicit boundary:

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
        new DocsOrder
        {
            Name = "a",
            Customer = new DocsCustomer { Name = "Ann" },
        }
    )
);
var after = Optional<DocsOrder.Fragment?>.Present(
    DocsOrder.Fragment.From(
        new DocsOrder
        {
            Name = "a",
            Customer = new DocsCustomer { Name = "Bob" },
        }
    )
);

var changes = DocsOrder.ChangeSet.Between(before, after);
// changes.Name.IsChanged == false
// changes.Customer.Name.IsChanged == true
// changes.Customer.Name.Before.Value == "Ann"
// changes.Customer.Name.After.Value == "Bob"
```
<!-- /sample -->

`changes.Customer` is the nested before to after transition for that member: it reports `IsEmpty` for the subtree while its own members expose `IsChanged`, `Before`, and `After`.

The root `ChangeSet` itself is not a generic enumerable. Model members are heterogeneous, so there is no single element type to enumerate. Keyed collection transitions are the exception because their items share one `TKey` and `TElement` type (see [Keyed collections](keyed-collections.md)).

## Patch vs ChangeSet vs ChangePayload

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

ChangePayload
    "apply these transitions and commands"
    versioned JSON envelope ("version": "0.1")
    carries ChangeSet transitions and Patch commands member by member
    converts back with ToChangeSet or ToPatch
```

<!-- sample: core-changeset -->
```csharp
var start = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var finish = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);

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

`ChangeSet.ToPatch()` is the explicit information-loss boundary. It discards the before-state and returns the equivalent desired-operation patch. There is no silent mixed composition back into a ChangeSet. A baseline-free Patch can introduce a changed path whose before-state is unknown, so the composition stays explicit.

The client and server models may differ. A read projection can expose `HasPassword` while the write side accepts `NewPassword`, and the envelope does not require one shared CLR type on both sides: it names members and carries typed values, and each side converts the envelope into its own `Patch` or `ChangeSet`. The library performs no domain-specific model mapping.

## Compose and Invert ChangeSets

ChangeSets compose sequentially and invert without an external baseline, while Patches compose as baseline-free operations:

<!-- sample: core-algebra -->
```csharp
var s0 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var s1 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 1 })
);
var s2 = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 })
);

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

Generated `Fragment` types carry `System.Text.Json` support through
`FragmentJsonConverter`. A `ChangeSet` travels only through its generated
transport type. Serialize a ChangeSet through its generated payload:

<!-- sample: core-serialization -->
```csharp
using System.Text.Json;

var start = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "a", RetryCount = 1 })
);
var finish = Optional<CounterSettings.Fragment?>.Present(
    CounterSettings.Fragment.From(new CounterSettings { Label = "b", RetryCount = 2 })
);

var changes = CounterSettings.ChangeSet.Between(start, finish);

var json = JsonSerializer.Serialize(changes.ToPayload());
var restored = JsonSerializer
    .Deserialize<CounterSettings.ChangePayload>(json)!
    .ToChangeSet();
// restored.ToPatch().Apply(start) replays finish
```
<!-- /sample -->

NativeAOT source-generated metadata for generated ChangePayload types is exercised by the `SparseFragments.NativeAotSmoke` tests.

Typed projections such as `IsChanged`, keyed `Added`, `Removed`, and `Edited`, and `BeforeOrder` and `AfterOrder` are not duplicated in the payload; `ToChangeSet()` reconstructs them.

### ChangePayload JSON

Serialize and deserialize the generated `T.ChangePayload`, not `T.ChangeSet` or `T.Patch`. A `ChangeSet` enters the envelope with `ToPayload()`; a `Patch` enters it with `ChangePayload.FromPatch(patch)`, which redacts every before-state by construction. The `changes` array mixes both kinds member by member: one member can carry a regular before/after transition while another carries a baseline-free command. The typed member variants are suitable for OpenAPI endpoint schemas.

Each endpoint carries a `state`: `missing` (known absent), `null` (explicit null), `value` (with a `value`), or `redacted` (deliberately undisclosed). Redacted is never read as missing. A redacted before-state must arrive with an explicit desired after-state; envelopes that omit it, or that redact an after-state, are rejected.

`ToChangeSet()` rebuilds a complete `ChangeSet` and rejects redacted or otherwise incomplete histories instead of fabricating the missing baseline. `ToPatch()` is the explicit baseline-discarding projection and accepts them. Mark members whose previous values must not travel with `[SparseRedactBefore]`; `ToPayload()` then emits a redacted before-state while the after-state still travels, including nested models, keyed items, dictionaries, and whole-root before snapshots. An after-state can itself be sensitive: logging, diagnostics, UI, and history must not echo it.

The top-level payload carries the required string `"version": "0.1"`; nested changes omit it. Payload DTO property names use camel case, `kind` values are lowercase, and property order is explicit; embedded model values follow the application's JSON metadata. Keyed and dictionary model edits carry only their nested `edit` payload; additions and removals carry only the endpoint needed to apply that operation. Unused nullable fields are omitted from JSON.

<!-- sample: core-change-payload-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class LoginSettings
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}
```
<!-- /sample -->

<!-- sample: core-change-payload -->
```csharp
var current = LoginSettings.Fragment.From(
    new LoginSettings { DisplayName = "a", Password = "before-password" }
);

var rotation = new LoginSettings.Patch { Password = "after-password" };

// A command needs no baseline: the envelope redacts what it never observed.
var command = LoginSettings.ChangePayload.FromPatch(rotation);
var applied = current.Apply(command.ToPatch());
// applied.DisplayName.Value == "a"
// applied.Password.Value == "after-password"

var transition = LoginSettings.ChangeSet.Between(
    Optional<LoginSettings.Fragment?>.Present(current),
    Optional<LoginSettings.Fragment?>.Present(applied)
);
var wire = JsonSerializer.Serialize(transition.ToPayload());
// wire carries "after-password" but never "before-password"
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
| Express explicit set/null/remove edits | `new T.Patch { ... }` | Patch |
| Compose local operations | `patch.Compose(next)` | Patch |
| Compare sparse states exactly | `T.ChangeSet.Between(before, after)` | ChangeSet |
| Compare ordinary present models | `beforeModel.CreateChangeSet(afterModel)` | ChangeSet |
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

`Merge` composes contributions; `ApplyChanges` replays a `Diff` Fragment; `Apply` executes `Patch` operations. They are not interchangeable: `ApplyChanges` never removes a member that the diff did not carry, while a `Patch` explicitly can.
