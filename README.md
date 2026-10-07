# SparseFragments

*Source-generated partial state and typed changes for C#.*

Annotate an ordinary C# model with `[SparseFragmentModel]` and SparseFragments generates typed `Fragment`, `Patch`, and `ChangeSet` representations from it. All three share the model's structural semantics — missing vs null vs value presence, nested structure, and collection identity and rules — and together they cover layering, local editing, state transitions, reconciliation, and serialization.

Try it live in the browser: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/)

## Fragment, Patch, and ChangeSet at a glance

| Type | Represents | Typical use |
| --- | --- | --- |
| `Fragment` | Presence-aware partial state ("this part of the state is specified") | Layering and sparse values |
| `Patch` | Mutable desired operation ("apply these desired operations"), baseline-free | Local mutation and command construction |
| `ChangeSet` | Immutable before → after transition ("these values changed from before to after"), baseline-aware | Diff, rebase, and exchange |

The three are siblings generated from the same model semantics, not layers around each other: a `Patch` is not an observed diff, a `ChangeSet` is not a serialized `Patch`, and a `Fragment` is not an ordinary nullable DTO. Baseline-aware algebra (`Between`, `ToPatch`, `FromPatch`, `Invert`, `Compose`, `RebaseOnto`) belongs to `ChangeSet`.

## The Problem: Missing Is Not Null

Plain C# properties cannot distinguish "the caller did not specify this member" from "the caller explicitly set it to `null`". That distinction matters as soon as data is layered: higher-priority sources must override only what they actually set, while an explicit `null` must win over a lower layer's value and a missing member must fall through.

Hand-writing this per model is boilerplate-heavy and error-prone. SparseFragments generates it from your POCOs at compile time with no runtime reflection, keeping startup cost flat and the output trim/AOT-friendly.

`Optional<T>` preserves the three states — *missing*, *present null*, and *present value* — across all three representations: a `Fragment` preserves sparse state, a `Patch` preserves desired mutation intent, and a `ChangeSet` preserves the known before/after transition.

## When to Use It

Each scenario below keeps an edit, override, or delta that remembers what was specified, then combines it with the representation that fits:

* **Layered overlays.** Combine defaults with per-environment, per-user, or per-tenant overrides. Each layer carries only what it changes; a priority-ordered `Merge` produces the effective state. (→ `Fragment` / `Merge`)
* **Partial-update APIs.** HTTP PATCH-style endpoints where "absent", "null", and "value" are three distinct intents. (→ `Fragment` / `Patch`)
* **Minimal persisted settings.** Persist only what differs from the defaults and replay it later. (→ `Fragment`)
* **Local edit sessions and dirty tracking.** Accumulate user edits in a mutable patch, preview with `Apply`, or drop to cancel. The original state is never mutated. (→ `Patch`)
* **Disconnected editing and optimistic reconciliation.** Carry a known before → after transition across a process boundary and rebase it onto concurrent state, with structured conflicts where both sides changed the same member. (→ `ChangeSet`)

## Install

```shell
dotnet add package SparseFragments
```

The generator ships inside the package as an analyzer, so this is the only setup step. See [Packages and Compatibility](#packages-and-compatibility) for runtime targets and AOT notes.

## Quick Start

One `Settings` model runs through every step below: each sample builds on values introduced by the previous ones, so read top to bottom and copy each block in order.

### 1. Define your model

All you need is `[SparseFragmentModel]` on a `partial` class:

```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public Child? Child { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

public partial class Child
{
    public int Count { get; set; }
    public string Host { get; set; } = "localhost";
}
```

Reachable partial nested types (like `Child` here) automatically receive the generated APIs. See [Model shapes](docs/model-shapes.md) for the full rules.

### 2. Missing, null, and values

`Optional<T>` carries the three states — *missing*, *present null*, and *present value* — that plain C# properties cannot distinguish:

```csharp
Optional<string?> missing = Optional<string?>.Missing;       // not specified
Optional<string?> value = "hello";                            // present value (implicit conversion)
Optional<string?> explicitNull = Optional<string?>.Present(null); // explicitly null
```

A generated `Fragment` makes that distinction concrete. An explicitly set `null` overrides a lower layer; an unspecified member falls through:

```csharp
var defaults = Settings.Fragment.From(new Settings
{
    Label = "fallback",
    Child = new Child { Host = "db.local" },
});

// Explicit null is present: it overrides the lower layer.
var clearsLabel = new Settings.Fragment { Label = (string?)null };
// Nothing set: everything is missing, so the lower layer survives.
var saysNothing = new Settings.Fragment();

defaults.Merge(clearsLabel).ToModel().Label; // null (explicit null wins)
defaults.Merge(saysNothing).ToModel().Label; // "fallback" (missing falls through)
```

### 3. Merge layered contributions

`Merge` overlays a higher-priority fragment onto a lower-priority one. Only *present* members override; *missing* members keep the lower layer's values — including inside nested fragments:

```csharp
var lower = Settings.Fragment.From(new Settings
{
    Label = "base",
    Child = new Child { Host = "db.local" },
    Plugins = ["base-plugin"],
});
var higher = new Settings.Fragment
{
    Child = new Child.Fragment { Count = 9 }, // Host stays missing, so it falls through
    Plugins = new[] { "extra-plugin" },       // plain values convert implicitly
};

var merged = lower.Merge(higher).ToModel();
// merged.Label       == "base"      (unset above, so the lower value survives)
// merged.Child.Host  == "db.local" (nested fragments merge member by member)
// merged.Child.Count == 9           (higher priority wins where present)
// merged.Plugins     == ["base-plugin", "extra-plugin"]  (Append concatenates)
```

Per-member rules (`Replace` / `Deep` / `Append` / `SetUnion`, or your own strategy) are covered in [Merge strategies](docs/merge-strategies.md).

### 4. Apply desired changes with Patch

A `Patch` is a mutable list of desired operations applied with `Apply`. It says what should be done to the current contribution; it does not carry the before-state:

```csharp
var patch = new Settings.Patch { Label = (string?)null }; // explicitly null, stays present
patch.Child.Count = 9;                                    // typed nested set

var updated = defaults.Apply(patch);
// updated.Label is present null; updated.Child.Host keeps "db.local".
// defaults is untouched: Apply never mutates its source.
```

Assigning a value sets it (including an explicit `null`), `Unset()` drops the contribution, and untouched members stay unchanged:

```csharp
var clear = new Settings.Patch();
clear.Child.SetNull(); // explicit null, beats lower layers
var drop = new Settings.Patch();
drop.Child.Unset();    // remove this layer's contribution
```

`Patch.IsEmpty` tells you at a glance whether the patch changes anything at all.

### 5. Capture before → after with ChangeSet

Starting from the `defaults` and `updated` values produced in the previous step, derive the immutable before → after transition. A `Patch` describes desired operations; a `ChangeSet` records the known before → after transition:

```csharp
var before = Optional<Settings.Fragment?>.Present(defaults);
var after = Optional<Settings.Fragment?>.Present(updated);

var changes = Settings.ChangeSet.Between(before, after);
// !changes.IsEmpty: the transition is real
```

Reading a transition is typed — member names mirror the source model, with no reflection or `object?` casts:

```csharp
if (changes.Label.IsChanged)
{
    var beforeLabel = changes.Label.Before;
    var afterLabel = changes.Label.After;
}
// beforeLabel.Value == "fallback"; afterLabel.Value is null (explicitly cleared)
```

Project the transition back to operations with `ToPatch()` and replay it anywhere the baseline applies:

```csharp
var replayed = defaults.Apply(changes.ToPatch());
// replayed matches updated
```

Baseline-aware follow-ups — `FromPatch`, parameterless `Invert`, `Compose`, and `RebaseOnto` for disconnected reconciliation — stay on `ChangeSet`. See [Fragments and patches](docs/fragments-and-patches.md) for the full contracts and [ChangeSet rebase](docs/rebase.md) for the disconnected-editing flow.

### 6. Beyond the basics

The same partial state flows through JSON, UI sessions, and collections. Generated `Patch` and `ChangeSet` types serialize through the ordinary `System.Text.Json` APIs — SparseFragments defines no transport protocol:

```csharp
using System.Text.Json;

var json = JsonSerializer.Serialize(changes); // no SparseFragments wire protocol
var restored = JsonSerializer.Deserialize<Settings.ChangeSet>(json)!;
// restored.ToPatch().Apply(before).Value matches updated
```

From here, the guides pick up where the tutorial leaves off: [keyed collections](docs/keyed-collections.md) for element-wise identity and ordering, [ChangeSet rebase](docs/rebase.md) for the full disconnected client/server pass, [Fragments and patches](docs/fragments-and-patches.md) for typed member observation and serialization, [UI frameworks](docs/ui-frameworks.md) for edit sessions and `Observable` binding, [Clone & ownership](docs/cloning-and-ownership.md) for copying and reference sharing, [Model shapes](docs/model-shapes.md) for supported shapes and constructors, and [Diagnostics](docs/analyzer.md) for generator errors.

## Documentation

| Capability | Documentation |
| --- | --- |
| Distinguish missing, null, and explicit values | [Fragments and patches](docs/fragments-and-patches.md) |
| Layer defaults and overrides | [Merge strategies](docs/merge-strategies.md) |
| Build and apply Patch operations | [Fragments and patches](docs/fragments-and-patches.md) |
| Observe and rebase ChangeSet transitions | [ChangeSet rebase](docs/rebase.md) |
| Add, remove, edit, and reorder collection items | [Keyed collections](docs/keyed-collections.md) |
| Track edits in Blazor, WPF, MAUI, WinUI, or Avalonia | [UI frameworks](docs/ui-frameworks.md) |
| Serialize patches and transitions as ordinary JSON | [Fragments and patches](docs/fragments-and-patches.md) |
| Control copying and reference sharing | [Clone & ownership](docs/cloning-and-ownership.md) |
| Check supported model shapes and constructors | [Model shapes](docs/model-shapes.md) |
| Resolve generator errors | [Diagnostics](docs/analyzer.md) |

## Packages and Compatibility

* `SparseFragments` — core package (runtime `netstandard2.0`). Packed-package consumers are verified on `net48` (Windows-only execution), `net8.0`, and `net10.0`; the lowest compile-time surface is additionally covered by the `netstandard2.0` consumer.
* `SparseFragments.Blazor` — Blazor edit sessions (`net8.0` / `net10.0`).
* The generator uses no runtime reflection, keeping startup cost flat and the output trim/AOT-friendly.

## License

Licensed under the Apache-2.0 License — see [LICENSE](LICENSE).
