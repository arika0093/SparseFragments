<img src="./assets/hero2.png" />

# SparseFragments

[![NuGet Version](https://img.shields.io/nuget/v/SparseFragments?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseFragments/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseFragments/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-SparseFragments-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/SparseFragments)

*Source-generated partial state and typed changes for C#.*

Annotate an ordinary C# model with `[SparseFragmentModel]` and SparseFragments generates typed `Fragment`, `Patch`, and `ChangeSet` types from it. Each one tells missing members apart from explicit `null`, and each one follows the model's nesting and collection rules.

Try it live in the browser: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/)

## When to Use It

Use a separate object for the edit, then combine it with the base state:

* **Layered overlays.** Combine defaults with per-environment, per-user, or per-tenant overrides. Each layer carries only what it changes; a priority-ordered merge produces the effective state.
* **Partial-update APIs.** HTTP PATCH-style endpoints where "absent", "null", and "value" are three distinct intents.
* **Minimal persisted settings.** Persist only what differs from the defaults and replay it later.
* **Local edit sessions and dirty tracking.** Accumulate user edits in a mutable patch, preview, or drop to cancel. The original state is never mutated.
* **Disconnected editing and optimistic reconciliation.** Send a before → after transition across a process boundary and rebase it onto changed state. Members changed on both sides come back as structured conflicts.

## Install

```shell
dotnet add package SparseFragments
```

The package contains the source generator, so no additional setup is needed.

## Quick Start

The samples below share one `Settings` model. They build on each other, so read them in order.

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

In a `Fragment`, an explicitly set `null` overrides a lower layer; an unset member falls through:

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

`Merge` overlays a higher-priority fragment onto a lower-priority one. Only *present* members override; *missing* members keep the lower layer's values, including inside nested fragments:

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

A `Patch` is a mutable list of operations applied with `Apply`. It holds only the desired changes, not the before-state:

```csharp
var patch = new Settings.Patch { Label = (string?)null }; // explicitly null, stays present
patch.Child.Count = 9;                                    // typed nested set

var updated = defaults.Apply(patch);
// updated.Label is present null; updated.Child.Host keeps "db.local".
// defaults is untouched: Apply never mutates its source.
```

Assignment sets a value (including explicit `null`); `Unset()` removes this layer's contribution; untouched members stay as they are:

```csharp
var clear = new Settings.Patch();
clear.Child.SetNull(); // explicit null, beats lower layers
var drop = new Settings.Patch();
drop.Child.Unset();    // remove this layer's contribution
```

`Patch.IsEmpty` is true when the patch changes nothing.

### 5. Capture before → after with ChangeSet

Take the `defaults` and `updated` values from the previous step. A `Patch` holds desired operations; a `ChangeSet` holds one specific before → after transition:

```csharp
var before = Optional<Settings.Fragment?>.Present(defaults);
var after = Optional<Settings.Fragment?>.Present(updated);

var changes = Settings.ChangeSet.Between(before, after);
// !changes.IsEmpty: the transition is real
```

Members mirror the source model, so reading a transition needs no casts:

```csharp
if (changes.Label.IsChanged)
{
    var beforeLabel = changes.Label.Before;
    var afterLabel = changes.Label.After;
}
// beforeLabel.Value == "fallback"; afterLabel.Value is null (explicitly cleared)
```

Call `ToPatch()` to convert the transition back to operations and apply it to the same baseline:

```csharp
var replayed = defaults.Apply(changes.ToPatch());
// replayed matches updated
```

`ChangeSet` also provides `FromPatch`, parameterless `Invert`, `Compose`, and `RebaseOnto`. See [Fragments and patches](docs/fragments-and-patches.md) and [ChangeSet rebase](docs/rebase.md).

### 6. Beyond the basics

The same types work for JSON, UI sessions, and collections. `Patch` and `ChangeSet` use the standard `System.Text.Json` APIs directly:

```csharp
using System.Text.Json;

var json = JsonSerializer.Serialize(changes); // no SparseFragments wire protocol
var restored = JsonSerializer.Deserialize<Settings.ChangeSet>(json)!;
// restored.ToPatch().Apply(before).Value matches updated
```

Details are in Documentation below: keyed collections, rebase, UI binding, cloning, model shapes, and diagnostics.

## Architecture
### The Problem: Missing Is Not Null

Plain C# properties cannot distinguish "the caller did not specify this member" from "the caller explicitly set it to `null`". When data is layered, higher-priority sources must override only what they set. An explicit `null` must win over a lower layer's value, and a missing member must fall through.

Writing this by hand means a presence flag per member plus merge logic, repeated for every model.

### Optional

`Optional<T>` holds three states that plain C# properties cannot tell apart: *missing*, *present null*, and *present value*:

```csharp
Optional<string?> missing = Optional<string?>.Missing; // not specified
Optional<string?> value = "hello";                     // present value (implicit conversion)
Optional<string?> explicitNull = Optional<string?>.Present(null); // explicitly null
```

### Fragment, Patch, and ChangeSet

`Fragment`, `Patch`, and `ChangeSet` are built on `Optional`:

| Type | Represents | Typical use |
| --- | --- | --- |
| `Fragment` | Presence-aware partial state | Hold sparse state; merge layers |
| `Patch` | Mutable desired operations, without before-state | Collect local edits; build commands |
| `ChangeSet` | Immutable before → after transition | Diff, rebase, and exchange |

### Source-Generated

`[SparseFragmentModel]` generates code like the following:

```csharp
partial class Settings
{
    // Deep copy without retaining references
    public Settings DeepClone();
    // Implementation of the concepts above
    public sealed class Fragment;
    public sealed class Patch;
    public sealed class ChangeSet;
    // A mutable builder for a generated fragment.
    public sealed class FragmentBuilder;
    // Bindable proxy over the live model instance
    public sealed class Observable : INotifyPropertyChanged;
}

// Child types receive the same generated code as well
```

The code is generated at compile time, so it uses no reflection and works with NativeAOT.

## Documentation

| Capability | Documentation |
| --- | --- |
| Fragments, Patch operations, and JSON serialization | [Fragments and patches](docs/fragments-and-patches.md) |
| Layer defaults and overrides | [Merge strategies](docs/merge-strategies.md) |
| Observe and rebase ChangeSet transitions | [ChangeSet rebase](docs/rebase.md) |
| Add, remove, edit, and reorder collection items | [Keyed collections](docs/keyed-collections.md) |
| Track edits in Blazor, WPF, MAUI, WinUI, or Avalonia | [UI frameworks](docs/ui-frameworks.md) |
| Control copying and reference sharing | [Clone & ownership](docs/cloning-and-ownership.md) |
| Check supported model shapes and constructors | [Model shapes](docs/model-shapes.md) |
| Resolve generator errors | [Diagnostics](docs/analyzer.md) |

## Packages and Compatibility

### SparseFragments

* Released as `netstandard2.0`.
  * .NET Framework 4.6.1 or later
  * .NET (all versions)
  * MAUI
  * Works in most other [frameworks](https://learn.microsoft.com/en-us/dotnet/standard/net-standard?tabs=net-standard-2-0#select-net-standard-version) as well.
* Source generation works only in *IDE* environments using Roslyn 4.3.1 or later.
  * VisualStudio 2022: 17.3 or later
  * JetBrains Rider: 2023.1 or later
  * Unity: 6 or later

### SparseFragments.Blazor

Extension for `EditContext`. Requires `net8.0` or later.

## License

Licensed under the Apache-2.0 License.
