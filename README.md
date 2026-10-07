# SparseFragments

*Typed partial state for C#.*

Annotate a partial POCO with `[SparseFragmentModel]` and the generator emits a presence-aware **Fragment** — each member tracks whether it was specified — plus `Merge`, `Diff`, and typed `Patch` operations over that partial state.

Try it live in the browser: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/)

## The Problem: Missing Is Not Null

Plain C# properties cannot distinguish "the caller did not specify this member" from "the caller explicitly set it to `null`". That distinction matters as soon as data is layered: higher-priority sources must override only what they actually set, while an explicit `null` must win over a lower layer's value and a missing member must fall through.

Hand-writing this per model is boilerplate-heavy and error-prone. SparseFragments generates it from your POCOs at compile time with no runtime reflection, keeping startup cost flat and the output trim/AOT-friendly.

## When to Use It

Each scenario below keeps an edit, override, or delta as a `Fragment`/`Patch` that remembers what was specified, then combines it with `Merge`, `Diff`, or `Apply`:

* **Layered overlays.** Combine defaults with per-environment, per-user, or per-tenant overrides. Each layer carries only what it changes; a priority-ordered `Merge` produces the effective state.
* **Partial-update APIs.** HTTP PATCH-style endpoints where "absent", "null", and "value" are three distinct intents.
* **Minimal persisted settings.** `Diff` the current settings against the defaults and persist only the resulting fragment.
* **Edit sessions and dirty tracking.** Accumulate user edits in a `Patch`, check `IsEmpty`, apply for a preview, or drop to cancel. The original model is never mutated.

## Install

```shell
dotnet add package SparseFragments
```

The generator ships inside the package as an analyzer, so this is the only setup step. The runtime targets `netstandard2.0`, so it can be consumed from `netstandard2.0`-compatible projects as well as modern .NET (`net8.0` / `net10.0`).

## Quick Start

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

Reachable partial nested types (like `Child` here) automatically receive generated Fragment/Patch APIs. See [Model shapes](docs/model-shapes.md) for the full rules.

### 2. Missing, null, and values

`Optional<T>` carries the three states — *missing*, *present null*, and *present value*:

```csharp
Optional<string?> a = Optional<string?>.Missing;       // not specified (IsPresent == false)
Optional<string?> b = "hello";                         // present (implicit conversion)
Optional<string?> c = Optional<string?>.Present(null); // explicitly null
```

An explicitly set `null` overrides a lower layer; an unspecified member falls through:

```csharp
// Given the Settings model defined in §1 above:
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

`Merge` overlays a higher-priority fragment onto a lower-priority one. Only *present* members override; *missing* members keep the lower layer's values.

```csharp
var lower = Settings.Fragment.From(new Settings
{
    Label = "base",
    Child = new Child { Host = "db.local" },
    Plugins = ["base-plugin"],
});
var higher = new Settings.Fragment
{
    Child = new Child.Fragment { Count = 9 },          // Host falls through to the lower layer
    Plugins = new[] { "extra-plugin" },                // plain values convert implicitly
};

var merged = lower.Merge(higher).ToModel();
// merged.Label       == "base"      (unset above → lower value survives)
// merged.Child.Host  == "db.local"  (nested fragments merge member by member)
// merged.Child.Count == 9           (higher priority wins)
// merged.Plugins     == ["base-plugin", "extra-plugin"]  (Append concatenates)
```

Per-member rules (`Replace` / `Deep` / `Append` / `SetUnion`, or your own strategy) are covered in [Merge strategies](docs/merge-strategies.md).

### 4. Diff and patch

`Diff` captures the minimal delta between two states; a `Patch` represents "changes to apply to one layer".

```csharp
var before = new Settings { Label = "before" };
var after = new Settings { Label = "after" };

var diff = Settings.Fragment.Diff(before, after);              // minimal semantic delta
var result = Settings.Fragment.From(before).ApplyChanges(diff);
// result.Label == "after"

var original = Settings.Fragment.From(new Settings
{
    Label = "original",
    Child = new Child { Count = 7, Host = "keep" },
});

var patch = new Settings.Patch { Label = (string?)null };      // explicit null (stays present)
patch.Child.Count = 9;                                         // typed nested set
var updated = original.Apply(patch);
// original is untouched; updated.Child.Host keeps "keep".

var remove = new Settings.Patch();
remove.Child.Unset();                                          // drop this layer's contribution
var toNull = new Settings.Patch();
toNull.Child.SetNull();                                        // explicit null, beats lower layers
```

`Patch.IsEmpty` tells you at a glance whether the patch changes anything at all.

### 5. Beyond the basics

Builders, cloning, and the JSON Patch bridge follow the same partial state; the linked guides carry the full behavior:

```csharp
var edited = original.ToBuilder().Build();                     // edited copy via the builder
var clone = original.ToModel().DeepClone();                    // isolated graph (see Clone & ownership)

var baseline = new Settings.Fragment { Label = "base" };
var baselineOpt = Optional<Settings.Fragment?>.Present(baseline);
var jsonPatch = Settings.Patch.FromJsonPatch(                  // RFC 6902 import (see JSON Patch)
    baselineOpt,
    System.Text.Encoding.UTF8.GetBytes("""[{"op":"replace","path":"/Label","value":"patched"}]"""));
var exported = jsonPatch.ToJsonPatch(baselineOpt);             // ReadOnlyMemory<byte>, UTF-8 JSON
```

## End-to-end example

The [Collaborative editing example](examples/CollaborativeEditing/README.md) runs the pieces above as one database-backed flow: PostgreSQL + EF Core, Blazor and WPF clients, JSON Patch over HTTP, and revision-based rebase. It demonstrates:

* mapping persistence entities to a SparseFragments model at an explicit boundary;
* deriving a typed patch from a retained baseline and sending it as JSON Patch;
* rebasing a stale save onto the latest revision, including structured conflicts;
* keyed quest edits (add/remove/edit/reorder) and layered settings overrides across two UI frameworks.

See the example README for the run path and file map; API semantics stay in the guides below.

## Documentation

| Capability | Documentation |
| --- | --- |
| Distinguish missing, null, and explicit values | [Fragments and patches](docs/fragments-and-patches.md) |
| Layer defaults and overrides | [Fragments and patches](docs/fragments-and-patches.md) § layering |
| Store only values that changed | [Fragments and patches](docs/fragments-and-patches.md) § diff |
| Apply partial updates | [Fragments and patches](docs/fragments-and-patches.md) § patches |
| Customize how members merge | [Merge strategies](docs/merge-strategies.md) |
| Add, remove, edit, and reorder collection items | [Keyed collections](docs/keyed-collections.md) |
| Inspect model semantics and Patch changes | [Inspection](docs/inspection.md) |
| Reconcile concurrent edits | [Patch rebase](docs/rebase.md) |
| Track edits in Blazor, WPF, MAUI, WinUI, or Avalonia | [UI frameworks](docs/ui-frameworks.md) |
| Exchange changes as RFC 6902 JSON Patch | [JSON Patch](docs/json-patch.md) |
| Control copying and reference sharing | [Clone & ownership](docs/cloning-and-ownership.md) |
| Check supported model shapes and constructors | [Model shapes](docs/model-shapes.md) |
| Resolve generator errors | [Diagnostics](docs/analyzer.md) |
| Follow a multi-client database-backed editing flow | [Collaborative editing example](examples/CollaborativeEditing/README.md) |

## Packages and Compatibility

* `SparseFragments` — core package (runtime `netstandard2.0`). Packed-package consumers are verified on `net48` (Windows-only execution), `net8.0`, and `net10.0`; the lowest compile-time surface is additionally covered by the `netstandard2.0` consumer. Framework support implied by the TFM is distinct from these executed environments.
* `SparseFragments.Blazor` — Blazor edit sessions (`net8.0` / `net10.0`).

## License

Licensed under the Apache-2.0 License — see [LICENSE](LICENSE).
