# UI Framework Integration

SparseFragments derives semantic changes by comparing a retained baseline with the current model. UI dirty flags and change notifications may drive binding, validation, or UI state, but they do not define the Patch itself.

<!-- sample: ui-session-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class UiOrder
{
    public string Number { get; set; } = string.Empty;

    public List<UiOrderItem> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class UiOrderItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
```
<!-- /sample -->

## Framework-neutral edit sessions

Reference-type models provide an edit session via `CreateEditSession()` without depending on any UI package. The session retains a baseline snapshot, tracks changes against the live model, and derives `ChangeSet` transitions on demand:

For callers that need an explicit type name, the session type is the per-model `EditSession` in the `SparseFragments.Generated` namespace: `global::SparseFragments.Generated.<Container>.EditSession`, where `<Container>` is the stable per-model container. Prefer `var` and the `CreateEditSession()` extension; name the container only when a declaration requires it (see [Relocated generated types](#relocated-generated-types)). The session composes an implementation emitted into the consumer assembly, so callers do not need to name or depend on a generic runtime session type.

```csharp
var baseline = new UiOrder { Number = "ORD-1" };
var current = new UiOrder { Number = "ORD-2" };
var directChanges = baseline.CreateChangeSet(current);

var session = baseline.CreateEditSession();
var separateBaselineSession = baseline.CreateEditSession(current);

session.Model.Number = "ORD-2";
var changes = session.CreateChangeSet();
// changes.Number.Before == "ORD-1"; changes.Number.After == "ORD-2"

session.AcceptChanges();
// session.HasChanges == false
```

The session retains a private fragment snapshot as its baseline and exposes the
live model as `Model`, alongside a stable typed `Observable` proxy over the same
instance. The single-argument overload `baseline.CreateEditSession()` captures that model as both the baseline and the live instance to edit; the two-argument overload `baseline.CreateEditSession(current)` retains the `current` instance as the live model while snapshotting `baseline` separately. You can also compare models directly without a session via `baseline.CreateChangeSet(current)`.

Use `session.BatchEdit(() => { ... })` when one user action needs several
observable mutations. The outermost batch raises one `TransitionObserved` event
for the net transition; nested batches join it. `PropertyChanged` and the
optional `onChanged` callback are also coalesced. A batch does not roll back
mutations if its action throws.

### Edit through Observable, read through Current

Bind controls to `session.Observable` and read display state from `session.Current`. The observable proxy edits the live model with notifications; the read-only view exposes the same state without setters, so display code cannot change it by accident:

```csharp
var line = new UiOrder { Number = "ORD-1" };
var editSession = line.CreateEditSession();

editSession.Observable.Number = "ORD-2";
string shown = editSession.Current.Number;
// shown == "ORD-2"
// line.Number == "ORD-2"

editSession.BatchEdit(() =>
{
    editSession.Observable.Number = "ORD-3";
});

editSession.RevertChanges();
// line.Number == "ORD-1"
// editSession.HasChanges == false
```

`RevertChanges()` writes the retained baseline back into the live model in place. It discards unsaved edits; it does not contact a server and does not advance the baseline. `Current` always reflects the live model, so it shows the reverted values immediately. For the full transition and payload vocabulary, see [Fragments and patches](fragments-and-patches.md) and [ChangeSet rebase](rebase.md).

### Advanced inspection: Descriptors and flattened changes

Ordinary editing needs only `Observable`, `Current`, and `CreateChangeSet()`. Two further seams exist for generic UI and diagnostics code:

* `session.Descriptors` exposes per-member metadata (path, type, nullability, editability), live get/set accessors, and the property attributes from the source model. It backs generic form builders and validation; handwritten per-member code should use the typed surface instead.
* `ChangeSet.EnumerateChanges()` flattens a transition into `ChangeInfo` rows (path, presence-aware before/after, `ChangeKind`), and `EnumerateChangedPaths()` lists the changed member paths. They feed logs, lists, and tests; ordinary editing reads the typed member transitions instead (see [Observe typed member transitions](fragments-and-patches.md#observe-typed-member-transitions)).

Limitations: descriptors track the live model, so values read through them change as the model changes. Flattened enumeration describes one computed transition; it does not update when the model is edited further.

`HasChanges` and `CreateChangeSet()` always compare that
baseline with the model's current state, so edit-then-restore is clean even if a
UI control reported that a field was touched. `CreatePatch()` projects the same
transition to a baseline-free patch. `AcceptChanges()` captures the current
state as the next baseline. These APIs live in `SparseFragments` and do not
require a UI-framework package.

Writable reference-type models also generate `Fragment.WriteTo(model)` and
`Patch.ApplyInPlace(model)`. They update
the existing model object; supported `List<T>` and `Dictionary<TKey,TValue>`
properties retain their collection instance and have their contents replaced.
Nested model values may be replaced. A model with init-only or constructor-only
members keeps its normal edit-session APIs but cannot use in-place apply
(see [SPF026](analyzer.md#spf026-in-place-submit-is-unavailable)).
`ChangeSet.TryApplyInPlace(model, out conflicts, options)` applies a
baseline-aware transition to a bound model after rebasing onto its current
state; the blind `ToPatch().ApplyInPlace(model)` form skips that check
(see [ChangeSet rebase](rebase.md#in-place-application-for-bound-models)).

The session is synchronous. It provides no async submit, transport, or conflict
framework. The application sends the change set through
its own transport.

### Recommended save workflow

The standard and recommended workflow is:

1. Disable UI editing while the save operation is in flight.
2. Send the change payload (`session.CreateChangeSet().ToPayload()`).
3. Receive the authoritative persisted state returned by the server (including any database-assigned IDs, normalization, or timestamps).
4. Create a fresh edit session from the persisted state, and recreate UI-bound objects such as Blazor's `EditContext`:

```csharp
// 1. Disable editing in UI
isSaving = true;
try
{
    var submitted = session.CreateChangeSet();
    var response = await SendChangesAsync(submitted.ToPayload());
    if (response.IsSuccess)
    {
        // 3. Receive authoritative server state
        var persisted = response.PersistedModel;

        // 4. Create fresh session from persisted state
        session = persisted.CreateEditSession();
        editContext = session.CreateEditContext();
    }
}
finally
{
    isSaving = false;
}
```

This pattern cleanly absorbs server-assigned keys, modified timestamps, and value normalization without requiring partial-state reconciliation.

For forms that keep editing enabled during submission when the server makes no schema changes, key assignments, or normalization, `session.AcceptChanges(submitted)` advances the retained baseline without touching the live model:

```csharp
var session = order.CreateEditSession();
session.Model.Number = "Updated";

var submitted = session.CreateChangeSet();
var response = await SendChangesAsync(submitted.ToPayload());
if (response.IsSuccess)
{
    // Advances the baseline only. The live model is untouched,
    // so edits made after CreateChangeSet stay pending.
    session.AcceptChanges(submitted);
}
```

`AcceptChanges()` captures the current model as the next baseline.
`AcceptChanges(submitted)` advances the retained baseline by that transition
without touching the live model. The transition must match the retained
baseline on every changed path; a stale or foreign change set is rejected with
`InvalidOperationException` and the baseline stays unchanged. Send exceptions
leave the baseline unchanged because acknowledgement never ran.

`AcceptChanges` rejects transitions containing unassigned sentinels (`[SparseKey(Unassigned = ...)]`). Forms with newly added keyed rows must therefore use the fresh session pattern above once the server assigns persistent keys.

### Carrying pending edits across a reload

When a form needs to reload fresh server state (such as after background updates or a manual refresh) while keeping uncommitted user edits, integrate the pending changes before recreating the session.

Until a dedicated session rebase API is introduced, use this pattern:

1. Extract pending changes from the current session: `var pending = session.CreateChangeSet();`.
2. Attempt to apply the pending changes onto the new server state: `pending.TryApplyTo(newServerState, out var merged)`.
3. If conflicts occur (`TryApplyTo` returns `false`), do not recreate the session. Keep the current session and surface the conflicts (for example via `pending.RebaseOnto(newServerState)`).
4. If the merge succeeds, recreate the session using the two-argument overload: `newServerState` serves as the baseline, and `merged` becomes the active model:

```csharp
var pending = session.CreateChangeSet();
if (!pending.TryApplyTo(newServerState, out var merged))
{
    // Conflicting edits: keep current session and display conflicts
    ShowReloadConflicts(pending.RebaseOnto(newServerState).Conflicts);
    return;
}

// Success: new server state becomes the baseline, merged becomes the active model
session = newServerState.CreateEditSession(merged);
editContext = session.CreateEditContext();
```

This ensures the next `session.CreateChangeSet()` accurately reflects the differences between the new server baseline and the user's preserved edits.

The session implements `INotifyPropertyChanged`: `HasChanges` is raised after
accept operations and observable-proxy edits. Consumers re-read `HasChanges`
instead of the session recomputing it for every model notification.

For bindings that need `INotifyPropertyChanged`, `Optional<T>.ToObservable()`
maps `Optional<Model?>` to the generated, model-specific observable proxy while
preserving missing, present-null, and present-value states. The proxy type lives
in the per-model `SparseFragments.Generated` container (see
[Relocated generated types](#relocated-generated-types)), so keep the call
inferred with `var`:

```csharp
var proxy = Optional<UiOrder?>.Present(session.Model).ToObservable();
```

The generated extension container is an implementation detail with a
deterministic hash-based name; call the extensions rather than naming the
container directly. Framework-specific packages can adapt the neutral session
without adding framework references to its generated code. The same neutral
`CreateEditSession()` extension is used whether or not a framework package is
referenced.

## Relocated generated types

`Model.Fragment`, `Model.FragmentBuilder`, `Model.Patch`, `Model.ChangeSet`,
and `Model.ChangePayload` stay nested in the annotated model, and the
`CreateEditSession()`, `ToObservable()`, and `CreateChangeSet()` entry points
are unchanged. Everything else generated per model moved out of the annotated
type into a stable per-model container in `SparseFragments.Generated`:

| Before (nested in the model) | After (per-model container) |
| --- | --- |
| `Model.EditSession` | `global::SparseFragments.Generated.<Container>.EditSession` |
| `Model.Observable` (`Model.SparseObservable` on collision) | `<Container>.Observable` (`<Container>.SparseObservable`) |
| `Model.ReadOnlyView` | `<Container>.ReadOnlyView` |
| `Model.DescriptorFactory` | `<Container>.DescriptorFactory` (internal) |
| Payload DTOs under `Model.ChangePayload` | `global::SparseFragments.Generated.__Internal_<hash>` |
| Fragment JSON converter body | `<Container>FragmentJsonConverter` (`Model.Fragment.FragmentJsonConverter` stays as a private shell) |
| Fragment/Patch/ChangeSet operation bodies | `<Container>FragmentOperations`, `<Container>.PatchOperations`, `<Container>.ChangeSetOperations` (internal) |

`<Container>` is the sanitized fully qualified model identity plus a stable
hash, so same-short-name models in different namespaces get distinct
containers. The playground names one directly:

```csharp
using TaskObservable = global::SparseFragments.Generated.SparseFragments_Playground_Models_PlaygroundTask_E6C8F7DB.Observable;
```

There are no backwards-compatibility aliases: update explicit type references
to the container paths, or drop them in favor of `var` and the extension
entry points. A model member or nested type named `EditSession`,
`Observable`, `ReadOnlyView`, or `DescriptorFactory` no longer collides with
generated code; members named `ChangeSet` or `ChangePayload` are now reported
as SPF009 instead of failing with a raw compiler error (see
[SPF009](analyzer.md#spf009-member-conflicts-with-generated-api)).

## Blazor

The `SparseFragments.Blazor` package (`net8.0` / `net10.0`) adds Blazor helpers
for the framework-neutral edit session. The session retains a baseline and
derives semantic changes by comparing it with the current model:

<!-- sample: ui-session -->
```csharp
var uiOrder = new UiOrder { Number = "ORD-1" };
var uiSession = uiOrder.CreateEditSession();

uiSession.Model.Number = "ORD-2";
// uiSession.HasChanges == true

var uiChanges = uiSession.CreateChangeSet();
// uiChanges.IsEmpty == false

uiSession.AcceptChanges();
// uiSession.HasChanges == false
```
<!-- /sample -->

Create an `EditContext` with the Blazor extensions and pass it to the form:

```csharp
var editContext = uiSession.CreateEditContext();
// After persisting the current model:
uiSession.AcceptChanges(editContext);
```

Bind the created `EditContext` to an ordinary `EditForm`:

```razor
<EditForm EditContext="@editContext">...</EditForm>
```

`CreateEditContext()` binds the original editable model `T` to the
`EditContext`. Do not use the generated observable proxy (the per-model
`Observable` type in `SparseFragments.Generated`) as
`EditContext.Model`. Blazor field tracking and validation run on
`EditContext` and `FieldIdentifier` and model metadata, so `DataAnnotations` keep
applying to `T`, while the semantic patch still comes from baseline and current `T`.

Blazor extension methods:

| Member | Purpose |
| --- | --- |
| `session.CreateEditContext()` | Creates a Blazor `EditContext` bound to `session.Model` |
| `session.AcceptChanges(editContext)` | Calls the framework-neutral `AcceptChanges()` and clears the supplied context's modified flags |
| `session.AcceptChanges(editContext, changes)` | Advances the baseline by the submitted change set; clears the context only when the session is clean, so later edits stay marked modified |
| `session.CreateValidationStore(editContext)` | Creates a `ValidationMessageStore` bound to the supplied context |
| `session.Field(name)` | Resolves a Blazor `FieldIdentifier` for a model member name |
| `session.AddValidationError(store, field, message)` | Surfaces a message through the `ValidationMessageStore` |
| `session.AddValidationError(store, fieldPath, message)` | Resolves `fieldPath` with `session.Field` and surfaces a message |

Dictionary members resolve through bracketed keys such as
`Contacts["billing"].Name`. The canonical spelling quotes the key; an unquoted
key is accepted when it needs no escaping. Non-string keys (numeric, enum,
`Guid`) parse from the same spelling with invariant culture, and keys that do
not parse fail as invalid paths. Resolution works for mutable dictionaries
and for read-only `IReadOnlyDictionary<TKey, TValue>` models alike, including
implementations that do not expose the legacy non-generic `IDictionary`.

List members resolve through numeric indexes such as `Lines[1].Quantity`,
for mutable lists and read-only `IReadOnlyList<T>` models alike, including
implementations without the legacy non-generic `IList`. Indexes resolve
positionally through the indexer; out-of-range and non-numeric indexes fail
as invalid paths.

Keyed collections (members whose element type declares a stable key) also
resolve quoted stable keys such as `Lines["b"].Quantity`. These are the paths
`EnumerateChanges()` emits, so a changed item's path can be passed to
`session.Field` directly and keeps resolving after reorders. Quoted keys never
act as positions, even when numeric: `Items["7"]` looks up key `7` while
`Items[7]` is the eighth position. Index spellings from
`EnumerateChangedPaths()` (such as `Lines[1].Quantity`) resolve positionally.
Removed keys no longer resolve and fail as invalid paths.

The neutral session members such as `Model`, `HasChanges`,
`CreateChangeSet()`, `CreatePatch()`, and no-argument `AcceptChanges()` remain
available independently of Blazor. Context-taking helpers require an
`EditContext` whose `Model` is the same instance as `session.Model`.

Framework helpers read the session model through trusted framework access,
which keeps the session's observable-change cache intact. Creating an
`EditContext`, validating it, and resolving field paths do not by themselves
disable cached `HasChanges` computation for observable-only edits. Reading
`session.Model` directly still disables the cache permanently, because a
retained raw reference can change without observable notifications. Keep edits
on the `Observable` proxy while the cache matters; edits made straight to
`EditContext.Model` bypass observable notifications.

Edit-then-restore yields no semantic change even though fields were touched:

```csharp
uiSession.Model.Number = "changed";
uiSession.Model.Number = "ORD-1";   // restored

// uiSession.HasChanges == false
// uiSession.CreateChangeSet().IsEmpty == true
```

Validation flows through the ordinary `EditContext` pipeline. Continuing with the session above:

```csharp
var store = uiSession.CreateValidationStore(editContext);
editContext.OnValidationRequested += (sender, _) =>
{
    store.Clear();
    if (string.IsNullOrEmpty(uiSession.Model.Number))
    {
        store.Add(uiSession.Field(nameof(UiOrder.Number)), "Number is required.");
    }
};
```

Errors obtained elsewhere (for example structured rebase conflicts) surface
the same way with
`uiSession.AddValidationError(store, uiSession.Field(nameof(UiOrder.Number)), message)`.
Fields resolved from the session keep working when they point at nested
members, list elements, or dictionary values: the error is accepted as long as
the field model is still reachable from the session model. Fields built from
another model graph stay rejected. The `fieldPath` overload covers the same
paths without passing a `FieldIdentifier` between graphs.
The model type must be a reference type.

A session ChangeSet is sent through its generated `T.ChangePayload`: call `ToPayload()` before transport, then call `ToChangeSet()` on receipt before reconciling with `RebaseOnto` (see [ChangeSet rebase](rebase.md)). SparseFragments provides no transport abstraction. Transport configuration stays with the application.

## WPF, WinForms, .NET MAUI, WinUI, and Avalonia

These frameworks bind the generated observable wrapper (the per-model `Observable` type in `SparseFragments.Generated`). The wrapper writes through to the same underlying model and raises `INotifyPropertyChanged` notifications for binding.

Nested models surface as child proxies that propagate changes to the root callback.

Mutable indexable
sequences (`List<T>`, `IList<T>`, `Collection<T>`, and `ObservableCollection<T>`) and mutable
`Dictionary<TKey,TValue>` and `IDictionary<TKey,TValue>` members surface as notifying views over the
original collection instances. List views implement generic and non-generic `IList` for WPF binding;
dictionary collection events contain `KeyValuePair<TKey,TValue>` items. Edits through a view mutate the
model collection in place, raise `INotifyCollectionChanged` and `INotifyPropertyChanged` (`Count` and
`Item[]`), and notify the parent proxy and session callback. An underlying `ObservableCollection<T>`
is forwarded rather than notified twice.

For generated reference-model elements, list and dictionary views expose cached element `Observable`
proxies. Add an element proxy created around a new model (or use `AddModel` / `InsertModel` on a list
view and `AddModel` on a dictionary view). Replacing or removing elements, clearing a view, replacing
the collection through the proxy, or disposing the view detaches stale element callbacks.

`Observable.Items` is the generated view type so bindings can use it directly. Since C# properties
cannot have a view getter and a model-collection setter of different types, replace a collection with
the generated `ReplaceItems(modelCollection)` method; this rebuilds the view and raises the member
notification.

Mutations made directly to a plain model `List<T>` or `Dictionary<TKey,TValue>` update
the model but bypass view notifications; changes to an underlying collection that itself implements
`INotifyCollectionChanged` (such as `ObservableCollection<T>`) are forwarded. Arrays, `IReadOnlyList<T>`, `IEnumerable<T>`, immutable
collections, and sets remain replace-only. Models using collection types the generator cannot deeply
clone must continue to provide the applicable explicit clone policy. Collection notifications are
synchronous and do not marshal to a UI thread; callers are responsible for thread affinity.

Create a neutral edit session and bind its observable proxy; the session tracks the same underlying model:

```csharp
var session = model.CreateEditSession(onChanged: () => HasUnsavedChanges = true);
var observable = session.Observable;

// bind the UI to `observable`; edits flow into the same live `model`
observable.Number = "ORD-2";
observable.Items.AddModel(new UiOrderItem { Id = "line-1", Name = "First item" });
observable.PropertyChanged += (_, args) => Console.WriteLine(args.PropertyName);

// ...user edits `model` through the UI framework...
var uiChanges = session.CreateChangeSet();
```

### WPF example

```xml
<TextBox Text="{Binding Number, UpdateSourceTrigger=PropertyChanged}" />
<ItemsControl ItemsSource="{Binding Items}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <TextBlock Text="{Binding Name}" />
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

```csharp
var session = model.CreateEditSession(
    onChanged: () => SaveCommand.NotifyCanExecuteChanged());
DataContext = session.Observable;
```

WinForms, .NET MAUI, WinUI, and Avalonia use the same `Observable` wrapper over the underlying model; only the framework-specific binding setup differs.
