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

## Install

```shell
dotnet add package SparseFragments
```

The generator ships inside the package as an analyzer, so this is the only setup step. See [Compatibility](#compatibility) for runtime targets and AOT notes.

## Quick start

Define an ordinary partial model:

```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public Child? Child { get; set; }
}

public partial class Child
{
    public int Count { get; set; }
    public string Host { get; set; } = "localhost";
}
```

Reachable partial nested types (like `Child` here) automatically receive the generated APIs. See [Model shapes](docs/model-shapes.md) for the full rules.

A `Fragment` carries only what is specified; `Merge` overlays a higher-priority layer onto a lower one:

```csharp
var defaults = Settings.Fragment.From(new Settings
{
    Label = "fallback",
    Child = new Child { Host = "db.local" },
});

var overlay = new Settings.Fragment
{
    Child = new Child.Fragment { Count = 9 }, // Host stays missing, so it falls through
};

var effective = defaults.Merge(overlay).ToModel();
// effective.Label == "fallback", effective.Child.Host == "db.local",
// effective.Child.Count == 9
```

A `Patch` holds mutable desired operations applied with `Apply`; the source fragment is never mutated:

```csharp
var patch = new Settings.Patch { Label = (string?)null }; // explicitly null, stays present
patch.Child.Count = 9;                                    // typed nested set

var updated = defaults.Apply(patch);
// updated.Label is present null; updated.Child.Host keeps "db.local".
```

A `ChangeSet` captures the immutable before → after transition and replays it through `ToPatch`:

```csharp
var changes = Settings.ChangeSet.Between(
    Optional<Settings.Fragment?>.Present(defaults),
    Optional<Settings.Fragment?>.Present(updated));

var replayed = defaults.Apply(changes.ToPatch());
// replayed matches updated
```

## Why missing is not null

`Optional<T>` carries three states — *missing*, *present null*, and *present value* — that plain C# properties cannot distinguish:

```csharp
Optional<string?> missing = Optional<string?>.Missing;       // not specified
Optional<string?> presentNull = Optional<string?>.Present(null); // explicitly null
Optional<string?> presentValue = "hello";                     // present value
```

An explicitly set `null` overrides a lower layer while a missing member falls through, which is what makes layering and partial updates sound. The quick-start merge above relies on exactly this: `Count = 9` wins where present, `Host` survives where missing.

## Common workflows

**Layered state** is `Fragment` plus `Merge`. Combine defaults with per-environment, per-user, or per-tenant overrides; only present members win. Per-member rules (`Replace` / `Deep` / `Append` / `SetUnion`, or custom strategies) are covered in [Merge strategies](docs/merge-strategies.md).

**Minimal persisted settings** stay fragments too: `Settings.Fragment.Diff(current, defaults)` compares two ordinary models and returns only what differs, replayed later with `ApplyChanges`:

```csharp
var delta = Settings.Fragment.Diff(new Settings(), new Settings { Label = "custom" });
var restored = Settings.Fragment.From(new Settings()).ApplyChanges(delta);
// restored.Label == "custom"
```

**Local desired edits** are `Patch` plus `Apply` and `Compose`. Assigning a value sets it (including an explicit `null`), `Unset()` drops the contribution, and untouched members stay unchanged:

```csharp
var first = new Settings.Patch { Label = "a" };
var second = new Settings.Patch();
second.Child.Count = 2;
var combined = first.Compose(second);

var clear = new Settings.Patch();
clear.Child.SetNull(); // explicit null, beats lower layers
var drop = new Settings.Patch();
drop.Child.Unset();    // remove this layer's contribution
```

**Observed before/after changes** are `ChangeSet.Between`. Project a transition back to operations with `ToPatch()`, attach a known baseline to an existing patch with `ChangeSet.FromPatch`, chain contiguous transitions with `Compose`, and reverse one without an external baseline using the parameterless `Invert()`:

```csharp
var transition = Settings.ChangeSet.Between(
    Optional<Settings.Fragment?>.Present(defaults),
    Optional<Settings.Fragment?>.Present(updated));
var undone = transition.Invert();
```

See [Fragments and patches](docs/fragments-and-patches.md) for the full contracts.

**Disconnected and concurrent reconciliation** is `ChangeSet.RebaseOnto`: it compares the ChangeSet's own before-state against the current state and returns a new ChangeSet plus structured conflicts, with no revision history required:

```csharp
var current = Optional<Settings.Fragment?>.Present(
    Settings.Fragment.From(new Settings { Label = "concurrent" }));

var rebased = transition.RebaseOnto(current);
if (!rebased.HasConflicts)
{
    var saved = rebased.Patch.ToPatch().Apply(current);
}
```

See [ChangeSet rebase](docs/rebase.md) for the disconnected-editing flow.

**UI edit sessions** derive the same representations from a live model: `CreateChangeSet()` for changes that leave the local process, `CreatePatch()` for purely local application. See [UI frameworks](docs/ui-frameworks.md) for Blazor sessions and `Observable` binding on other frameworks.

## End to end: from local state to a shared change

One model flows through all three representations; the transition crosses process boundaries as ordinary JSON:

```csharp
using System.Text.Json;

var state = Settings.Fragment.From(new Settings { Label = "v1" });
var edit = new Settings.Patch { Label = "v2" };
var edited = state.Apply(edit);

var outgoing = Settings.ChangeSet.Between(
    Optional<Settings.Fragment?>.Present(state),
    Optional<Settings.Fragment?>.Present(edited));

var json = JsonSerializer.Serialize(outgoing); // no SparseFragments wire protocol
var incoming = JsonSerializer.Deserialize<Settings.ChangeSet>(json)!;

var arrival = incoming.RebaseOnto(Optional<Settings.Fragment?>.Present(state));
var saved = arrival.Patch.ToPatch().Apply(Optional<Settings.Fragment?>.Present(state));
```

SparseFragments defines no transport protocol: generated `Patch` and `ChangeSet` types serialize through the ordinary `System.Text.Json` APIs, including source-generated contexts on NativeAOT. See [ChangeSet rebase](docs/rebase.md) for the full client/server pass.

## Documentation

State, presence, and merge:

* [Fragments and patches](docs/fragments-and-patches.md) — sparse state, presence, diff, and patch operations
* [Merge strategies](docs/merge-strategies.md) — per-member merge rules and custom strategies

Changes, Patch, and ChangeSet:

* [Fragments and patches](docs/fragments-and-patches.md) — Patch vs ChangeSet, `Between`/`ToPatch`/`FromPatch`, compose/invert, serialization
* [ChangeSet rebase](docs/rebase.md) — disconnected editing, `RebaseOnto`, and structured conflicts

Collections and UI:

* [Keyed collections](docs/keyed-collections.md) — element-wise identity, ordering, and per-key edits
* [UI frameworks](docs/ui-frameworks.md) — edit sessions, validation, and `Observable` binding

Model rules, ownership, and tooling:

* [Model shapes](docs/model-shapes.md) — supported shapes and constructors
* [Clone & ownership](docs/cloning-and-ownership.md) — copying and reference sharing
* [Diagnostics](docs/analyzer.md) — generator errors

## Compatibility

* `SparseFragments` — core package (runtime `netstandard2.0`). Packed-package consumers are verified on `net48` (Windows-only execution), `net8.0`, and `net10.0`; the lowest compile-time surface is additionally covered by the `netstandard2.0` consumer.
* `SparseFragments.Blazor` — Blazor edit sessions (`net8.0` / `net10.0`).
* The generator uses no runtime reflection, keeping startup cost flat and the output trim/AOT-friendly.

## License

Licensed under the Apache-2.0 License — see [LICENSE](LICENSE).
