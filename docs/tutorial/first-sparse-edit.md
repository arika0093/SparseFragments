# First sparse edit

This tutorial builds one working edit from install to output. It uses a
single settings model and follows one continuous sequence: snapshot a
baseline, add a sparse environment override, merge the layers, send a patch
command, then observe the change as a typed transition. Each step shows the
values it produces, so a mismatch means something went wrong on that step.

Run it with a .NET 8 SDK or later. Create a console project that targets
`net8.0`, add the `SparseFragments` package, and paste each block below into
`Program.cs` in order. Top-level statements work: no `Main` method is needed.

```shell
dotnet new console -o SparseTutorial
dotnet add SparseTutorial package SparseFragments
```

## Define one settings model

The model is an ordinary C# class. The attribute asks the generator for the
sparse companions (`Fragment`, `Patch`, `ChangeSet`) around it. The full
generation rules stay in the [model shapes reference](../model-shapes.md).

<!-- sample: tutorial-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class TutorialSiteSettings
{
    public string? Theme { get; set; }

    public int RefreshSeconds { get; set; }
}
```
<!-- /sample -->

## Snapshot the baseline

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
[merge strategies reference](../merge-strategies.md).

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
// changes.Theme.IsChanged == true
// changes.Theme.Before.Value == "light"
// changes.Theme.After.Value == "dark"
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

## Where to go next

The transition above is also the unit that travels: convert it with
`ToPayload()` and reconcile it with `RebaseOnto` when another writer moves
first. For those steps, read [ChangeSet rebase](../rebase.md) for the
reconciliation flow and [ChangePayload](../change-payload.md) for the wire
envelope. For task-sized follow-ups, see
[layered settings](../how-to/layered-settings.md) for multi-layer overrides
and [partial updates](../how-to/partial-updates.md) for commands versus
baseline-aware changes. The full operation list stays in
[fragments and patches](../fragments-and-patches.md).
