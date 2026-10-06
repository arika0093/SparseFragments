# SparseFragments

*Typed partial state for C#.*

Distinguish missing, null, and values. Generate fragments, merge, diff, and typed patches from ordinary POCOs at compile time.

Annotate a partial class with `[SparseFragmentModel]`, and the generator emits a typed **Fragment** — a presence-aware view where each member tracks whether it was specified — plus merge, semantic diff, and typed patch operations over that partial state.

Try it live in the browser: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/)

## The Problem: Missing Is Not Null

Plain C# properties cannot distinguish "the caller did not specify this member" from "the caller explicitly set it to `null`". That distinction matters as soon as data is layered: higher-priority sources must override only what they actually set, while an explicit `null` must win over a lower layer's value and a missing member must fall through.

Hand-writing this per model is boilerplate-heavy and error-prone, and reflection-based solutions sacrifice startup performance and AOT/trim compatibility. SparseFragments generates it from your POCOs at compile time.

## Is This For You?

All of these are uses of the same typed partial state: keep an edit, override, or delta as a `Fragment`/`Patch` that remembers what was specified, then combine it with `Merge`, `Diff`, or `Apply`.

* **Layered overlays.** Combine defaults with per-environment, per-user, or per-tenant overrides. Each layer carries only what it changes; a priority-ordered `Merge` produces the effective state.
* **Partial-update APIs.** HTTP PATCH-style endpoints where "absent", "null", and "value" are three distinct intents — no reflection involved.
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

### 2. The three states of `Optional<T>`

At the heart of SparseFragments is `Optional<T>` — *missing*, *present null*, and *present value*:

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

### 3. Create fragments

There are two ways to create a fragment:

```csharp
// (a) From a whole model — every member becomes present
var full = Settings.Fragment.From(new Settings { Label = "base" });

// (b) Sparse construction — only the members you set become present
var sparse = new Settings.Fragment { Label = "base" };
sparse.IsEmpty; // false
```

Option (b) is the core of SparseFragments: a minimal *contribution* whose unspecified members fall through to lower layers.

### 4. Merge layered contributions

`Merge` overlays a higher-priority fragment onto a lower-priority one. Only *present* members override; *missing* members keep the lower layer's values.

```csharp
// A full model as the base layer.
var lower = Settings.Fragment.From(new Settings
{
    Label = "base",
    Child = new Child { Host = "db.local" },
    Plugins = ["base-plugin"],
});
// A sparse fragment carrying only what this layer overrides.
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

### 5. Diff and patch

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

### 6. Build and clone

Builders and `DeepClone` work around the same partial state when you need an edited copy or an isolated graph:

```csharp
var builder = original.ToBuilder();
builder.Label = Optional<string?>.Missing;                     // copy without this member
var edited = builder.Build();

var clone = original.ToModel().DeepClone();                    // fully independent graph
clone.Child!.Count = 42;                                       // original.Child.Count is still 7
```

Patch assignment shares references by default; `Fragment.From`, `DeepClone`, whole-contribution `Set(model)`, and JSON Patch import snapshot instead. Details: [Clone & ownership](docs/cloning-and-ownership.md).

### 7. Exchange patches as JSON Patch

Typed `Patch` values stay in-process. When a patch has to cross a process boundary — an HTTP PATCH endpoint, another service, or stored JSON — convert it to a standard [RFC 6902](https://datatracker.ietf.org/doc/html/rfc6902) document with the built-in `FromJsonPatch` / `ToJsonPatch` bridge:

```csharp
using System.Text;
using SparseFragments;

var baseline = new Settings.Fragment { Label = "base" };
var document = Encoding.UTF8.GetBytes(
    """[{"op":"replace","path":"/Label","value":"patched"}]""");

var patch = Settings.Patch.FromJsonPatch(
    Optional<Settings.Fragment?>.Present(baseline),
    document);

var updated = baseline.Apply(patch);
// updated.Label == "patched"

var baselineOpt = Optional<Settings.Fragment?>.Present(baseline);
var exported = patch.ToJsonPatch(baselineOpt); // ReadOnlyMemory<byte>, UTF-8 JSON
```

Round-tripping holds semantically: re-importing the export onto the same baseline produces the same fragment. Pointers, options, NativeAOT setup, and typed failures are covered in [JSON Patch](docs/json-patch.md).

## Documentation

| Topic | Purpose |
| --- | --- |
| [Merge strategies](docs/merge-strategies.md) | Replace / Deep / Append / SetUnion / custom strategy behavior |
| [Keyed collections](docs/keyed-collections.md) | Stable-key structural collection patch semantics |
| [Patch rebase](docs/rebase.md) | Concurrent edit reconciliation and structured conflicts |
| [JSON Patch](docs/json-patch.md) | RFC 6902 bridge, serialization, failures, NativeAOT |
| [Clone & ownership](docs/cloning-and-ownership.md) | Reference sharing, snapshots, cycles, DeepClone |
| [Model shapes](docs/model-shapes.md) | Supported model forms, nested models, constructors, promotion |
| [UI frameworks](docs/ui-frameworks.md) | Blazor edit sessions, Observable proxies, WPF, MAUI, WinUI, Avalonia patterns |
| [Diagnostics](docs/analyzer.md) | Generator diagnostics reference |

## Packages and Compatibility

* `SparseFragments` — core package (runtime `netstandard2.0`). Packed-package consumers are verified on `net48` (Windows-only execution), `net8.0`, and `net10.0`; the lowest compile-time surface is additionally covered by the `netstandard2.0` consumer. Framework support implied by the TFM is distinct from these executed environments.
* `SparseFragments.Blazor` — Blazor edit sessions (`net8.0` / `net10.0`).

Try it live: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/)

## License

This project is licensed under the Apache-2.0 License.

```
Copyright 2026- arika0093

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0
```
