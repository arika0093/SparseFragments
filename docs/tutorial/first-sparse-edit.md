# First sparse edit

This tutorial builds one working edit from install to output. It uses a
single settings model and follows one continuous sequence: snapshot a
baseline, add a sparse environment override, merge the layers, send a patch
command, then observe the change as a typed transition. Each step shows the
values it produces, so a mismatch means something went wrong on that step.

Run it with a .NET 8 SDK or later. Create a console project that targets
`net8.0`, add the `SparseTypes` package, and replace the generated `Program.cs`.
In C#, top-level executable statements must precede type declarations: place
the executable steps at the top of `Program.cs`, and place the model class
declaration at the bottom.

```shell
dotnet new console -o SparseTutorial --framework net8.0
dotnet add SparseTutorial package SparseTypes
```

## Define one settings model

The model is an ordinary C# class. Place this declaration at the bottom of
`Program.cs`. The attribute asks the generator for the sparse companions
(`Fragment`, `Patch`, `ChangeSet`) around it. The full generation rules stay in
the [model shapes reference](../reference/model-shapes.md).

<!-- sample: tutorial-models -->
```csharp
using SparseTypes;

[SparseModel]
public partial class TutorialSiteSettings
{
    public string? Theme { get; set; }

    public int RefreshSeconds { get; set; }
}
```
<!-- /sample -->

## Snapshot the baseline

Add this at the top of `Program.cs` (above the class declaration).
`Fragment.From` copies the current values into a complete contribution. The
fragment is isolated from later changes to the model, so it keeps working as
the lower layer while the application moves on.

<!-- sample: tutorial-baseline -->
```csharp
var baselineSettings = new TutorialSiteSettings { Theme = "light", RefreshSeconds = 30 };
var baseline = TutorialSiteSettings.Fragment.From(baselineSettings);
// baseline.Theme.Value == "light"
// baseline.RefreshSeconds.Value == 30
```
<!-- /sample -->

## Add a sparse environment override

A constructed fragment carries only the members it assigns. `RefreshSeconds`
is present here; `Theme` stays missing, which is different from assigning it
`null` or zero. Missing members fall through on the next merge.

<!-- sample: tutorial-override -->
```csharp
var environmentOverride = new TutorialSiteSettings.Fragment { RefreshSeconds = 60 };
// environmentOverride.Theme.IsPresent == false
// environmentOverride.RefreshSeconds.Value == 60
```
<!-- /sample -->

## Merge the layers

`Merge` overlays the higher-priority argument over the receiver. Present
members of the override win; missing members keep the lower-layer value. The
direction matters: `baseline.Merge(environmentOverride)` is not the same
call as the reverse. Layering rules and collection modes live in the
[merge strategies reference](../explanation/merge-strategies.md).

<!-- sample: tutorial-merge -->
```csharp
var effective = baseline.Merge(environmentOverride);
// effective.Theme.Value == "light"
// effective.RefreshSeconds.Value == 60
```
<!-- /sample -->

## Send a patch command

A `Patch` holds desired operations without recording a baseline. Assigning a
value sets it; untouched members stay unchanged. `Apply` runs the operations
against the effective fragment and returns the new state.

<!-- sample: tutorial-patch -->
```csharp
var command = new TutorialSiteSettings.Patch { Theme = "dark" };
var updated = effective.Apply(command);
// updated.Theme.Value == "dark"
// updated.RefreshSeconds.Value == 60
```
<!-- /sample -->

## Observe the typed transition

`ChangeSet.Between` turns the two sparse states into a before-to-after
transition. Members read like the model: `IsChanged` tells whether the
member moved, and `Before` and `After` carry the presence-aware endpoints.
Unchanged members stay typed and report `IsChanged == false`.

<!-- sample: tutorial-changeset -->
```csharp
var tutorialBefore = Optional<TutorialSiteSettings.Fragment?>.Present(effective);
var tutorialAfter = Optional<TutorialSiteSettings.Fragment?>.Present(updated);
var changes = TutorialSiteSettings.ChangeSet.Between(tutorialBefore, tutorialAfter);

if (changes.Theme.IsChanged)
{
    Console.WriteLine($"Theme changed: {changes.Theme.Before} -> {changes.Theme.After}");
}
// changes.RefreshSeconds.IsChanged == false
```
<!-- /sample -->

## Undo with Invert

A change set carries its own before-state, so it inverts without an external
baseline. The inverted transition walks the after-state back to the before-state.

<!-- sample: tutorial-invert -->
```csharp
var undone = changes.Invert();
// undone.ToPatch().Apply(tutorialAfter) replays tutorialBefore
```
<!-- /sample -->

## Run the project

Run the project to observe the output:

```shell
dotnet run --project SparseTutorial
```

```text
Theme changed: light -> dark
```

## Complete Program.cs

Here is the complete `Program.cs` file with top-level executable statements placed before the model declaration:

<!-- illustrative: complete runnable Program.cs assembling the steps above -->
```csharp
using SparseTypes;

// 1. Snapshot the baseline
var baselineSettings = new TutorialSiteSettings { Theme = "light", RefreshSeconds = 30 };
var baseline = TutorialSiteSettings.Fragment.From(baselineSettings);

// 2. Add an environment override
var environmentOverride = new TutorialSiteSettings.Fragment { RefreshSeconds = 60 };

// 3. Merge the layers (override wins, missing members fall through)
var effective = baseline.Merge(environmentOverride);

// 4. Send a patch command
var command = new TutorialSiteSettings.Patch { Theme = "dark" };
var updated = effective.Apply(command);

// 5. Observe the typed transition
var tutorialBefore = Optional<TutorialSiteSettings.Fragment?>.Present(effective);
var tutorialAfter = Optional<TutorialSiteSettings.Fragment?>.Present(updated);
var changes = TutorialSiteSettings.ChangeSet.Between(tutorialBefore, tutorialAfter);

if (changes.Theme.IsChanged)
{
    Console.WriteLine($"Theme changed: {changes.Theme.Before} -> {changes.Theme.After}");
}

// 6. Undo the transition
var undone = changes.Invert();

// Model declaration at the bottom of the file
[SparseModel]
public partial class TutorialSiteSettings
{
    public string? Theme { get; set; }

    public int RefreshSeconds { get; set; }
}
```

## Where to go next

The transition above is also the unit that travels: convert it with
`ToPayload()` and reconcile it with `RebaseOnto` when another writer moves
first. For those steps, read [ChangeSet rebase](../explanation/rebase.md) for the
reconciliation flow and [ChangePayload](../reference/change-payload.md) for the wire
envelope. For task-sized follow-ups, see
[layered settings](../how-to/layered-settings.md) for multi-layer overrides
and [partial updates](../how-to/partial-updates.md) for commands versus
baseline-aware changes. The full operation list stays in
[fragments and patches](../explanation/fragments-and-patches.md).
