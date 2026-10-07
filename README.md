<img src="./assets/hero2.png" />

# SparseFragments

[![NuGet Version](https://img.shields.io/nuget/v/SparseFragments?style=flat-square&logo=NuGet&color=0080CC)](https://www.nuget.org/packages/SparseFragments/) ![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/arika0093/SparseFragments/test.yaml?branch=main&label=Test&style=flat-square) [![DeepWiki](https://img.shields.io/badge/DeepWiki-SparseFragments-blue.svg?logo=data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACwAAAAyCAYAAAAnWDnqAAAAAXNSR0IArs4c6QAAA05JREFUaEPtmUtyEzEQhtWTQyQLHNak2AB7ZnyXZMEjXMGeK/AIi+QuHrMnbChYY7MIh8g01fJoopFb0uhhEqqcbWTp06/uv1saEDv4O3n3dV60RfP947Mm9/SQc0ICFQgzfc4CYZoTPAswgSJCCUJUnAAoRHOAUOcATwbmVLWdGoH//PB8mnKqScAhsD0kYP3j/Yt5LPQe2KvcXmGvRHcDnpxfL2zOYJ1mFwrryWTz0advv1Ut4CJgf5uhDuDj5eUcAUoahrdY/56ebRWeraTjMt/00Sh3UDtjgHtQNHwcRGOC98BJEAEymycmYcWwOprTgcB6VZ5JK5TAJ+fXGLBm3FDAmn6oPPjR4rKCAoJCal2eAiQp2x0vxTPB3ALO2CRkwmDy5WohzBDwSEFKRwPbknEggCPB/imwrycgxX2NzoMCHhPkDwqYMr9tRcP5qNrMZHkVnOjRMWwLCcr8ohBVb1OMjxLwGCvjTikrsBOiA6fNyCrm8V1rP93iVPpwaE+gO0SsWmPiXB+jikdf6SizrT5qKasx5j8ABbHpFTx+vFXp9EnYQmLx02h1QTTrl6eDqxLnGjporxl3NL3agEvXdT0WmEost648sQOYAeJS9Q7bfUVoMGnjo4AZdUMQku50McDcMWcBPvr0SzbTAFDfvJqwLzgxwATnCgnp4wDl6Aa+Ax283gghmj+vj7feE2KBBRMW3FzOpLOADl0Isb5587h/U4gGvkt5v60Z1VLG8BhYjbzRwyQZemwAd6cCR5/XFWLYZRIMpX39AR0tjaGGiGzLVyhse5C9RKC6ai42ppWPKiBagOvaYk8lO7DajerabOZP46Lby5wKjw1HCRx7p9sVMOWGzb/vA1hwiWc6jm3MvQDTogQkiqIhJV0nBQBTU+3okKCFDy9WwferkHjtxib7t3xIUQtHxnIwtx4mpg26/HfwVNVDb4oI9RHmx5WGelRVlrtiw43zboCLaxv46AZeB3IlTkwouebTr1y2NjSpHz68WNFjHvupy3q8TFn3Hos2IAk4Ju5dCo8B3wP7VPr/FGaKiG+T+v+TQqIrOqMTL1VdWV1DdmcbO8KXBz6esmYWYKPwDL5b5FA1a0hwapHiom0r/cKaoqr+27/XcrS5UwSMbQAAAABJRU5ErkJggg==)](https://deepwiki.com/arika0093/SparseFragments)

*Source-generated partial state and typed changes for C#.*

SparseFragments generates typed APIs around an ordinary C# model when the application needs to represent only part of a value or only part of an edit.

Add `[SparseFragmentModel]` to the model you already use:

```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class Settings
{
    public string? Label { get; set; }
    public DatabaseSettings? Database { get; set; }

    [SparseMerge(MergeMode.Append)]
    public IReadOnlyList<string> Plugins { get; set; } = [];
}

public partial class DatabaseSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
}
```

`Settings` remains the model used by the application. The generated types represent partial values, edits, and before → after changes without replacing the model.

They help when:

* a settings layer must omit a property instead of assigning its default value;
* an editor must preserve which properties the user actually changed;
* a client must send an edit without overwriting unrelated server changes;
* a UI needs notifications and must distinguish a touched field from a value that is still changed;
* a collection needs to track edits by item identity rather than by array position.

Try these cases live in the browser: [*SparseFragments Playground*](https://arika0093.github.io/SparseFragments/).

## Install

```shell
dotnet add package SparseFragments
```

The package contains the source generator, so no additional generation step is required.

## What Problem Does It Solve?

### Partial Values

Consider these two settings documents:

```json
{}
```

```json
{ "label": null }
```

They mean different things. The first does not specify `Label`, so a lower-priority value should remain.

The second explicitly sets `Label` to `null`, so it should replace the lower-priority value. A `string?` property cannot represent both cases by itself.

The usual workaround is another DTO, a presence flag for each property, or custom JSON logic. Nested models repeat the same problem for every property.

SparseFragments generates a `Fragment` that keeps the distinction between omitted values, explicit `null`, and supplied values:

```csharp
var defaults = Settings.Fragment.From(new Settings
{
    Label = "default",
    Database = new() { Host = "db.local", Port = 5432 },
    Plugins = ["core"],
});

var user = new Settings.Fragment
{
    Label = (string?)null,
    Database = new DatabaseSettings.Fragment
    {
        Port = 6432,
    },
};

var effective = defaults.Merge(user).ToModel();

// effective.Label         == null
// effective.Database.Host == "db.local"
// effective.Database.Port == 6432
```

Only values supplied by the higher layer participate in the merge. This makes the same representation useful for application defaults, environment settings, tenant settings, user overrides, and other partially supplied values.

Per-member merge rules can replace, recursively merge, append, form a set union, or use a custom strategy. See [Merge strategies](docs/merge-strategies.md).

`Fragment.Diff(beforeModel, afterModel)` performs the complementary operation when a full model should be reduced to only the values that differ, such as user settings persisted relative to defaults.

### Partial Edits

A full edited object does not say which values the user intended to change. Suppose a form loads this state:

```text
Label         = "production"
Database.Host = "db.example.com"
Database.Port = 5432
```

If the user changes only the port, the resulting model still contains all three values. The edit itself contains one change:

```text
Database.Port: 5432 -> 6432
```

Preserving that distinction prevents untouched values from becoming accidental updates. It also lets a UI decide whether there are real unsaved changes rather than whether a field was merely touched.

SparseFragments generates a `Patch` when the application already knows what it wants to change:

```csharp
var current = Settings.Fragment.From(new Settings
{
    Label = "production",
    Database = new() { Host = "db.example.com", Port = 5432 },
});

var patch = new Settings.Patch();
patch.Database.Port = 6432;

var updated = current.Apply(patch);
```

`Label` and `Database.Host` are not part of this patch, so applying it leaves them alone. Assigning a value, assigning explicit `null`, and calling `Unset()` are separate operations.

The same distinction matters when an edit is saved in a different process from the one that created it. EF Core already tracks property changes when the same `DbContext` loads and edits an entity; SparseFragments does not replace that tracking.

When an edit was made in a browser or another process, however, the receiving application needs to know which values were intended to change. A patch or change set provides that information so application code can update only those values on the current entity.

### Before → After Changes

Some operations need the previous value as well as the requested update. Undo, audit output, conflict detection, and synchronization all depend on the specific before → after transition.

A `Patch` can say:

```text
set Database.Port to 6432
```

A generated `ChangeSet` can retain:

```text
Database.Port: 5432 -> 6432
```

Create one by comparing two versions:

```csharp
var before = Settings.Fragment.From(original);
var after = Settings.Fragment.From(edited);

var changes = Settings.ChangeSet.Between(before, after);
```

The generated members follow the source model:

```csharp
if (changes.Database.Port.IsChanged)
{
    Console.WriteLine(
        $"{changes.Database.Port.Before} -> {changes.Database.Port.After}");
}
```

Application code does not need property-name strings, reflection, or `object?` casts. Nested changes keep the same structure.

`ChangeSet` also supports inversion, composition, conversion back to a patch, and rebasing. See [Fragments and patches](docs/fragments-and-patches.md) and [ChangeSet rebase](docs/rebase.md).

### Client/Server Edits

A `ChangeSet` is serializable with the standard `System.Text.Json` APIs:

```csharp
using System.Text.Json;

var json = JsonSerializer.Serialize(changes);
var incoming = JsonSerializer.Deserialize<Settings.ChangeSet>(json)!;
```

SparseFragments does not define a transport protocol. The serialized value can travel through HTTP, SignalR, a message bus, or the transport the application already uses.

Serialization matters when state can change while an edit is in transit. Consider a client that loads state A and produces A → B while the server independently changes A → C.

Replacing C with B would discard the server-side change. The incoming change set instead retains the before-state needed to compare the client's edit with the current state.

`RebaseOnto` preserves non-conflicting changes and reports incompatible edits to the same member as structured conflicts. The server therefore needs the incoming `ChangeSet` and its current state, not a retained historical snapshot of A.

See [ChangeSet rebase](docs/rebase.md) for the complete client → server flow and conflict handling.

### UI Editing

UI binding creates a different problem: change notification and meaningful data changes are not the same thing.

WPF, WinForms, .NET MAUI, WinUI, and Avalonia commonly bind through `INotifyPropertyChanged`. SparseFragments generates an `Observable` wrapper so the ordinary model does not need a second hand-written property hierarchy just for binding:

```csharp
var model = new Settings();

var observable = new Settings.Observable(
    model,
    onChanged: () => HasUnsavedChanges = true);

observable.Label = "edited";

// model.Label == "edited"
```

The wrapper writes through to the original model and raises change notifications. A retained baseline can then be compared with the current model to derive the actual before → after change.

Blazor uses an edit session over the ordinary model:

```csharp
var settings = new Settings { Label = "original" };
var session = settings.CreateEditSession();

session.Model.Label = "edited";

session.HasChanges; // true
var changes = session.CreateChangeSet();
```

A touched field is not necessarily a remaining change. If the user restores the original value, the session reports no semantic change:

```csharp
session.Model.Label = "edited";
session.Model.Label = "original";

session.HasChanges; // false
```

The resulting `ChangeSet` is the same serializable type that can be sent to a server. See [UI frameworks](docs/ui-frameworks.md).

### Keyed Collections

Collection changes need stable identity. An array index only describes a position, so inserting an item at the front moves every later index even when the existing items themselves did not change.

Mark one member of a structural element with `[SparseKey]`:

```csharp
public partial class Quest
{
    [SparseKey]
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";
    public int Points { get; set; }
}

[SparseFragmentModel]
public partial class Roster
{
    public List<Quest> Quests { get; set; } = [];
}
```

SparseFragments then derives additions, removals, edits, and order changes by key:

```csharp
var changes = Roster.ChangeSet.Between(before, after);

changes.Quests.Added;
changes.Quests.Removed;
changes.Quests.Edited;
changes.Quests.OrderChanged;
```

An edit to one quest remains an edit to that quest even if another item is inserted before it. The keyed collection example in the [Playground](https://arika0093.github.io/SparseFragments/) shows the generated transition while items are added, removed, edited, and reordered.

See [Keyed collections](docs/keyed-collections.md) for key rules, collection semantics, and typed per-item transitions.

## Generated API

The generated types answer different questions:

| Type | Question | Typical use |
| --- | --- | --- |
| `Fragment` | Which values are provided? | Layers, overrides, partially supplied values |
| `Patch` | What should change? | Commands and local edits |
| `ChangeSet` | What changed from before to after? | Diff, serialization, undo, compose, rebase |

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
* Source generation works only in *IDE* environments using Roslyn 4.3.1 or later.
  * VisualStudio 2022: 17.3 or later
  * JetBrains Rider: 2023.1 or later
  * Unity: 6 or later

### SparseFragments.Blazor

Extension for `EditContext`. Requires `net8.0` or later.

## License

Licensed under the Apache-2.0 License.
