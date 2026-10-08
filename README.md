<img src="./assets/hero2.png" />

# SparseFragments

[![NuGet Version](https://img.shields.io/nuget/v/SparseFragments?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseFragments/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseFragments/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-SparseFragments-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/SparseFragments)

*Source-generated partial state and typed changes for C#.*

SparseFragments generates typed APIs around ordinary C# models for partial values, edits, and before → after changes.

Add `[SparseFragmentModel]` to a `partial` model. The application keeps using the same model; the source generator adds the supporting types around it.

It helps when:

* a settings layer must omit a property instead of assigning its default value;
* an editor must preserve which properties the user actually changed;
* a client must send an edit without overwriting unrelated server changes;
* a UI needs notifications and must distinguish a touched field from a value that is still changed;
* a collection needs to track edits by item identity rather than by array position.

Try these cases live in the browser: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/).

## What Problem Does It Solve?

### Partial Values

A settings layer often needs to distinguish “not specified” from “explicitly set to `null`”. A normal nullable property cannot represent both meanings at once.

A generated `Fragment` keeps that distinction:

```csharp
var user = new Settings.Fragment { Label = (string?)null };
var effective = defaults.Merge(user);
```

This is useful for defaults, environment settings, tenant settings, user overrides, and other partially supplied values.

See [Fragments and patches](docs/fragments-and-patches.md) and [Merge strategies](docs/merge-strategies.md) for construction, diffing, and merge rules.

### Partial Edits

A full edited object does not say which values the user intended to change. Treating every property as an update can overwrite values the editor never touched.

A generated `Patch` contains only the requested operations:

```csharp
var patch = new Settings.Patch();
patch.Database.Port = 6432;

var updated = current.Apply(patch);
```

This is useful for local commands, partial-update APIs, and edits created in another process. EF Core already tracks edits made directly to tracked entities; SparseFragments is useful when the edit arrives from elsewhere.

See [Fragments and patches](docs/fragments-and-patches.md) for patch operations and application.

### Before → After Changes

Undo, audit output, conflict detection, and synchronization need more than the final value. They need to know what changed.

A generated `ChangeSet` records that transition:

```csharp
var changes = Settings.ChangeSet.Between(before, after);

changes.Database.Port.IsChanged;
changes.Database.Port.Before;
changes.Database.Port.After;
```

See [Fragments and patches](docs/fragments-and-patches.md) for typed transitions, inversion, composition, and conversion back to a patch.

### Client/Server Edits

A `ChangeSet` can be sent through the transport the application already uses by converting it to its generated payload:

```csharp
var json = JsonSerializer.Serialize(changes.ToPayload());
var incoming = JsonSerializer.Deserialize<Settings.ChangePayload>(json)!.ToChangeSet();

var rebased = incoming.RebaseOnto(current);
```

Rebasing lets the receiver preserve unrelated newer changes instead of replacing current state with a stale object. Conflicting edits are reported separately.

See [ChangeSet rebase](docs/rebase.md) for serialization, client/server flows, and conflict handling.

### UI Editing

Change notification and “there is still something to save” are different questions. A field can be touched and then restored to its original value.

`SparseEditSession` compares a retained baseline with the live model. It is synchronous and provides no transport or conflict framework:

```csharp
var session = order.CreateEditSession();
session.Model.Name = "Updated";

var submitted = session.CreateChangeSet();
var response = await SendChangesAsync(submitted.ToPayload());
if (response.IsSuccess)
{
    // Advances the baseline only. The live model is untouched,
    // so edits made after CreateChangeSet stay pending.
    session.AcceptChanges(submitted);
}
```

`AcceptChanges(submitted)` advances only the baseline, so edits made after `CreateChangeSet` stay pending. When the server assigns keys or normalizes data, replace the model with the persisted state and start a fresh session instead of acknowledging the submitted transition. `ChangeSet` has no `ApplyInPlace`; a blind overwrite must spell `changes.ToPatch().ApplyInPlace(model)`, which discards the before-state.

See [UI frameworks](docs/ui-frameworks.md) for sessions, `EditContext` handling, validation, and `Observable` wrappers for Blazor, WPF, WinForms, .NET MAUI, WinUI, and Avalonia integration.

### Keyed Collections

Collection edits need stable identity. Array positions are not enough when items can be inserted, removed, or reordered.

With a `[SparseKey]` on the element model, changes are exposed by key:

```csharp
var changes = Roster.ChangeSet.Between(before, after);

changes.Quests.Added;
changes.Quests.Removed;
changes.Quests.Edited;
changes.Quests.OrderChanged;
```

See [Keyed collections](docs/keyed-collections.md) for key rules and per-item transitions. The [Playground](https://arika0093.github.io/SparseFragments/) shows the behavior interactively.

## Install

```shell
dotnet add package SparseFragments
```

The package contains the source generator, so no additional generation step is required.

## Quick Start

With .NET 10 or later, the whole example fits in a single file:

```csharp
#:package SparseFragments@*

using SparseFragments;

var defaults = Settings.Fragment.From(new Settings
{
    Label = "default",
    Database = new() { Host = "db.local", Port = 5432 },
});

var environment = new Settings.Fragment
{
    Database = new DatabaseSettings.Fragment { Port = 6432 },
};

var effective = defaults.Merge(environment);

var patch = new Settings.Patch { Label = "production" };
var updated = effective.Apply(patch);

var changes = Settings.ChangeSet.Between(effective, updated);

Console.WriteLine(updated.ToModel().Label); // production
Console.WriteLine(changes.Label.IsChanged); // True

[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public DatabaseSettings? Database { get; set; }
}

public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
```

Save it as `quickstart.cs` and run:

```shell
dotnet run --file quickstart.cs
```

The example uses the three main generated types. A `Fragment` says which values are provided, a `Patch` says what to change, and a `ChangeSet` records what changed from before to after.

The [documentation](#documentation) covers the full APIs and detailed behavior.

## Generated API

The generated types answer different questions:

| Type | Question | Typical use |
| --- | --- | --- |
| `Fragment` | Which values are provided? | Layers, overrides, partially supplied values |
| `Patch` | What should change? | Baseline-free commands and local edits |
| `ChangeSet` | What changed from before to after? | Baseline-aware diff, undo, compose, conflict-aware rebase |
| `ChangePayload` | How does the change travel? | Transport-only typed versioned JSON (`"version": "0.1"`) |
| `SparseEditSession` | What is still unsaved? | Synchronous editing against a retained baseline |

`Fragment` is presence-aware state and merge. `Patch` is baseline-free operations. `ChangeSet` is baseline-aware transitions plus conflict-aware rebase. `ChangePayload` is transport only: one envelope carries `ChangeSet` transitions and `Patch` commands member by member, with `missing`, `null`, `value`, and `redacted` endpoint states. Members marked `[SparseRedactBefore]` travel without their before-state; such envelopes convert with `ToPatch()`, while `ToChangeSet()` accepts only complete histories. `SparseEditSession` compares the retained baseline with the live model; it provides no transport or conflict framework.

The distinction is visible in a small example:

```text
Fragment
    Label is not provided
    Database.Port is 6432

Patch
    set Label to null
    set Database.Port to 6432

ChangeSet
    Label: "default" -> null
    Database.Port: 5432 -> 6432
```

All three follow the source model's nesting and configured collection behavior.

### Presence Tracking

`Optional<T>` represents the three states that a normal property cannot distinguish: missing, present `null`, and present value.

```csharp
Optional<string?> missing = Optional<string?>.Missing;
Optional<string?> value = "hello";
Optional<string?> explicitNull = Optional<string?>.Present(null);
```

Generated fragments use this distinction while exposing model-shaped members, so application code normally works through the generated types instead of maintaining presence flags by hand.

### Source Generation

`[SparseFragmentModel]` generates code like the following:

```csharp
partial class Settings
{
    public Settings DeepClone();

    public sealed class Fragment;
    public sealed class Patch;
    public sealed class ChangeSet;
    public sealed class ChangePayload;
    public sealed class FragmentBuilder;
    public sealed class Observable : INotifyPropertyChanged;
}
```

Reachable eligible `partial` nested types receive the corresponding generated APIs as well. See [Model shapes](docs/model-shapes.md) for the supported shapes and constructor rules.

The code is generated at compile time, uses no reflection for these generated operations, and works with NativeAOT.

## Documentation

| Capability | Documentation |
| --- | --- |
| Fragments, patch operations, and JSON serialization | [Fragments and patches](docs/fragments-and-patches.md) |
| Layer defaults and overrides | [Merge strategies](docs/merge-strategies.md) |
| Observe and rebase before → after changes | [ChangeSet rebase](docs/rebase.md) |
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
  * Works in most other [frameworks](https://learn.microsoft.com/en-us/dotnet/standard/net-standard?tabs=net-standard-2.0#select-net-standard-version) as well.
* Generated code requires C# 9.0 or later. Set `<LangVersion>9.0</LangVersion>` (or later) in the consuming project. The `netstandard2.0` and .NET Framework targets default to C# 7.3, so those consumers must opt in explicitly. Using a language version newer than a target framework's default is not an officially supported combination per [C# language versioning](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning); a successful build does not change that policy. `record struct` models need C# 10 or later. The contract covers generated consumer code only; the generator itself may use newer C#.
* Source generation works only in *IDE* environments using Roslyn 4.3.1 or later.
  * VisualStudio 2022: 17.3 or later
  * JetBrains Rider: 2023.1 or later
  * Unity: 6 or later

### SparseFragments.Blazor

Extension for `EditContext`. Requires `net8.0` or later.

## License

Licensed under the Apache-2.0 License.
