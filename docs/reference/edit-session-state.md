# Edit session state and observers

Reference for point-in-time state queries (`FieldState`, `CollectionState`) and live subscriptions (`ObserveField`, `WatchField`) on generated edit sessions.

## Overview

| Capability | Scope | Lifetime & Cost |
| --- | --- | --- |
| `session.FieldState(path)` | Exact, ancestor, and descendant status for one path | Point-in-time snapshot. Live query diffs against the baseline on every call. |
| `changeSet.FieldState(path)` | Same path inspection on an immutable change set | Point-in-time snapshot. O(R) scan over flattened change records without re-diffing. |
| `session.CollectionState(path)` | Item-level state for keyed collections or dictionaries | Point-in-time snapshot. Diffs the live model once on creation. |
| `session.ObserveField(path)` | Live change watcher bound to session `PropertyChanged` | Opt-in subscription. Refreshes state and notifies when values change. Requires `Dispose`. |

## Field state queries

`FieldState` inspects semantic changes at a typed or model-agnostic `SparsePath`.

<!-- sample: edit-session-field-state -->
```csharp
var widget = new UiWidget
{
    Title = "original",
    Child = new UiWidgetChild { Name = "child-original" },
};
var session = widget.CreateEditSession();

// Mutate child name
session.Observable.Child!.Name = "child-updated";

// Query field state at exact path
var childNameState = session.FieldState(session.Paths.Child.Name);
// childNameState.IsChanged == true
// childNameState.Kind == SparseFieldChangeKind.Changed
// childNameState.HasAncestorChange == false
// childNameState.HasDescendantChange == false
// childNameState.HasChanges == true
// childNameState.Before.Value == "child-original"
// childNameState.After.Value == "child-updated"

// Query field state at ancestor path (Child)
var childState = session.FieldState(session.Paths.Child);
// childState.IsChanged == false (Child reference itself was not replaced)
// childState.HasDescendantChange == true
// childState.HasChanges == true

// For repeated queries, snapshot a single ChangeSet to avoid re-diffing:
var changeSet = session.CreateChangeSet();
var fromSnapshot = changeSet.FieldState(session.Paths.Child.Name);
// fromSnapshot.IsChanged == true
```
<!-- /sample -->

### Field state properties

`SparseFieldState<T>` provides:

| Property | Type | Description |
| --- | --- | --- |
| `Path` | `SparsePath` | Queried path. |
| `IsChanged` | `bool` | True only when an exact transition occurred at `Path`. |
| `Kind` | `SparseFieldChangeKind` | Transition classification: `Unchanged`, `Changed`, `Added`, `Removed`, or `Reordered`. |
| `HasAncestorChange` | `bool` | True when a strict ancestor changed (e.g. parent object replaced). |
| `HasDescendantChange` | `bool` | True when any child path below this path changed. |
| `HasChanges` | `bool` | Aggregate flag: `IsChanged \|\| HasAncestorChange \|\| HasDescendantChange`. |
| `Before` | `Optional<T>` | Presence-aware value before transition. `Missing` if unchanged. |
| `After` | `Optional<T>` | Presence-aware value after transition. `Missing` if unchanged. |
| `Current` | `Optional<T>` | Current live value at the queried path. |

`Before` and `After` reflect exact transitions only. When `IsChanged` is false, both endpoints are `Optional<T>.Missing`, which indicates the absence of an exact transition rather than a missing member.

## Collection state queries

`CollectionState` inspects member-level keyed collections and dictionaries. Deeper paths below items throw `ArgumentException`.

<!-- sample: edit-session-collection-state -->
```csharp
var widget = new UiWidget
{
    Items =
    [
        new UiWidgetItem { Id = "a", Name = "item-a" },
        new UiWidgetItem { Id = "b", Name = "item-b" },
    ],
};
var session = widget.CreateEditSession();

// Reorder and edit
session.Observable.Items.Move(1, 0);
session.Observable.Items[0].Name = "item-b-edited";

// Query collection state for Items
var collectionState = session.CollectionState(session.Paths.Items);
var itemBState = collectionState.GetItemState("b");
// itemBState.IsEdited == true
// itemBState.IsReordered == true
// itemBState.IsAdded == false
// itemBState.IsRemoved == false

// Unknown key yields empty state
var unknownState = collectionState.GetItemState("nonexistent");
// unknownState.IsEmpty == true
```
<!-- /sample -->

### Collection item lookup

* **Stable keys**: `collectionState.GetItemState(key)` returns item state for matching keyed rows or dictionary keys. Unknown keys return `SparseCollectionItemState.Empty` (`IsEmpty == true`).
* **Temporary identities**: `collectionState.GetTemporaryState(guid)` returns item state for unassigned rows tracked with `[SparseTemporaryKey]`.
* **Reordering**: `IsReordered` reports whether an item's relative position among surviving keys changed. Dictionary items never report reorders.

## Live field observers

`ObserveField` (and its alias `WatchField`) attaches an active watcher to a specific path.

<!-- sample: edit-session-observe-field -->
```csharp
var widget = new UiWidget { Title = "start" };
var session = widget.CreateEditSession();

var changeCount = 0;
using var watcher = session.ObserveField(session.Paths.Title);
watcher.PropertyChanged += (_, _) => changeCount++;

// Observable change notifies watcher synchronously
session.Observable.Title = "next";
// changeCount > 0
// watcher.Current.Value == "next"
// watcher.IsChanged == true

var lastCount = changeCount;
// Setting same value raises nothing (effective change suppression)
session.Observable.Title = "next";
// changeCount == lastCount

// Disposing detaches notifications
watcher.Dispose();
session.Observable.Title = "after-dispose";
// changeCount == lastCount
```
<!-- /sample -->

### Observer characteristics

* **Notification filtering**: The watcher queries the session on invalidation. It raises `PropertyChanged` only when the refreshed state differs from the retained state. Redundant assignments raise no notifications.
* **Synchronous dispatch**: Events fire synchronously on the mutating thread. No UI dispatching is performed.
* **Raw model writes**: Direct assignments to `session.Model` bypass notification pipelines and will not trigger watcher events. Use `session.Observable` for mutations.
* **Disposal**: Observers subscribe to session events. Always call `Dispose()` to release references when the watcher is no longer needed.
