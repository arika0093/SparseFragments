# UI Framework Integration

SparseFragments derives semantic changes by comparing a retained baseline with the current model. UI dirty flags and change notifications may drive binding, validation, or UI state, but they do not define the Patch itself.

This guide starts with the framework-neutral edit session lifecycle. Framework adapters come after: Blazor helpers bind an `EditContext` to the session model, and desktop frameworks bind the generated observable proxy. The session API is identical in both cases.

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

The single-argument overload `baseline.CreateEditSession()` captures that model as both the baseline source and the live instance to edit. The two-argument overload `baseline.CreateEditSession(current)` snapshots `baseline` separately and edits the `current` instance. Both overloads accept an optional `onChanged` callback raised after observable edits. You can also compare models directly without a session via `baseline.CreateChangeSet(current)`.

The session is synchronous. It provides no async submit, transport, or conflict framework. The application sends the change set through its own transport.

### Model ownership

The caller owns the live model instance. The session keeps a private fragment snapshot as its baseline and a reference to the live model. It never replaces that instance: `RevertChanges`, `Reload`, and in-place apply write the merged result into the existing object, so bindings that hold the reference stay attached. The baseline snapshot is isolated from later mutations of the source model because it is captured through `Fragment.From`.

### Edit through Observable, read through Current

Bind controls to `session.Observable` and read display state from `session.Current`. The observable proxy edits the live model with notifications; the read-only view exposes the same live state without setters, so display code cannot change it by accident. `Current` is a live view over the same instance, not a snapshot: it shows reverted, reloaded, and accepted values immediately.

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

### Change cache and raw access

`HasChanges` and `CreateChangeSet()` always compare the retained baseline with the model's current state, so edit-then-restore is clean even if a UI control reported that a field was touched. While edits stay on the observable proxy, the session caches the `HasChanges` computation and only recomputes when something may have changed.

Reading `session.Model` directly disables that cache permanently, because a retained raw reference can change without observable notifications. Reads that expose raw mutable references through descriptors (arrays, unproxied objects, sets) invalidate the cache through the raw-access callback for the same reason, so a later in-place mutation is still observed. Primitive, string, proxy, and view reads stay cheap and do not invalidate. Framework helpers read the session model through trusted framework access, which keeps the cache intact: creating an `EditContext`, validating it, and resolving field paths do not by themselves disable cached `HasChanges` computation for observable-only edits. Edits made straight to `EditContext.Model` bypass observable notifications. Keep edits on the `Observable` proxy while the cache matters.

When the live model holds duplicate keyed keys it cannot be diffed, so `HasChanges` reports `true` rather than throwing.

### Notifications

The session implements `INotifyPropertyChanged`: `HasChanges` is raised after accept operations and observable-proxy edits. Consumers re-read `HasChanges` instead of the session recomputing it for every model notification. `TransitionObserved` carries the committed transition for each observable edit. The optional `onChanged` callback runs after observable edits for save-button state and similar UI flags.

Notifications are synchronous and do not marshal to a UI thread. Collection views likewise raise `INotifyCollectionChanged` and `INotifyPropertyChanged` (`Count` and `Item[]`) on the editing thread. Callers are responsible for thread affinity.

### BatchEdit groups notifications, never rolls back

Use `session.BatchEdit(() => { ... })` when one user action needs several observable mutations. The outermost batch raises one `TransitionObserved` event for the net transition; nested batches join it. `PropertyChanged` and the optional `onChanged` callback are also coalesced. The batch still publishes when its action throws. A batch does not roll back mutations if its action throws: batching coalesces notifications only. To discard edits, revert explicitly (see [Discard pending edits](#discard-pending-edits)).

### Advanced inspection: Descriptors and flattened changes

Ordinary editing needs only `Observable`, `Current`, and `CreateChangeSet()`. Two further seams exist for generic UI and diagnostics code:

* `session.Descriptors` exposes per-member metadata (path, type, nullability, editability), live get/set accessors, and the property attributes from the source model. It backs generic form builders and validation; handwritten per-member code should use the typed surface instead.
* `ChangeSet.EnumerateChanges()` flattens a transition into `ChangeInfo` rows (path, presence-aware before/after, `ChangeKind`), and `EnumerateChangedPaths()` lists the changed member paths. They feed logs, lists, and tests; ordinary editing reads the typed member transitions instead (see [Observe typed member transitions](fragments-and-patches.md#observe-typed-member-transitions)).

Limitations: descriptors track the live model, so values read through them change as the model changes. Flattened enumeration describes one computed transition; it does not update when the model is edited further. See the [Descriptors reference](descriptors.md) for the full contract.

Writable reference-type models also generate `Fragment.WriteTo(model)` and `Patch.ApplyInPlace(model)`. They update the existing model object; supported `List<T>` and `Dictionary<TKey,TValue>` properties retain their collection instance and have their contents replaced. Nested model values may be replaced. A model with init-only or constructor-only members keeps its normal edit-session APIs but cannot use in-place apply (see [SPF026](analyzer.md#spf026-in-place-submit-is-unavailable)).

For bindings that need `INotifyPropertyChanged`, `Optional<T>.ToObservable()` maps `Optional<Model?>` to the generated, model-specific observable proxy while preserving missing, present-null, and present-value states. The proxy type lives in the per-model `SparseFragments.Generated` container (see [Relocated generated types](#relocated-generated-types)), so keep the call inferred with `var`:

```csharp
var proxy = Optional<UiOrder?>.Present(session.Model).ToObservable();
```

The generated extension container is an implementation detail with a deterministic hash-based name; call the extensions rather than naming the container directly. Framework-specific packages can adapt the neutral session without adding framework references to its generated code. The same neutral `CreateEditSession()` extension is used whether or not a framework package is referenced.

## Choosing how to acknowledge server state

A save ends in exactly one of these. Pick by what the server returned.

| Server result | Session call | Live model | Baseline |
| --- | --- | --- | --- |
| Persisted, state returned (IDs, timestamps, normalization) | New session from the persisted state | Replaced | Persisted state |
| Persisted, submitted transition unchanged | `AcceptChanges(submitted)` | Untouched, later edits stay pending | Advanced by the transition |
| Persisted, current state is authoritative | `AcceptChanges()` | Untouched | Current state |
| Fresh state arrived, keep editing | `Reload(serverState)` | Merged in place | Server state |
| Fresh state arrived, resolve manually first | Manual `TryApplyTo` merge, then a new session | Caller owned | Caller owned |
| Discard edits | `RevertChanges` / `TryRevertChanges` | Baseline restored | Unchanged |
| Remote transition for a bound model | `TryApplyInPlace` / `ApplyInPlace` | Updated in place | Unchanged |

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

<!-- sample: ui-accept-flow -->
```csharp
var orderModel = new UiOrder { Number = "a" };
var orderSession = orderModel.CreateEditSession();

orderSession.Observable.Number = "b";
var submitted = orderSession.CreateChangeSet();

// The server persisted the submitted transition unchanged,
// and the user kept typing while the save was in flight.
orderSession.Observable.Number = "c";
orderSession.AcceptChanges(submitted);
// orderSession.HasChanges == true
// orderSession.CreateChangeSet() carries only Number "b" -> "c"

// When the server returns the authoritative state instead, start over from it.
var persisted = new UiOrder { Number = "b" };
var freshSession = persisted.CreateEditSession();
// freshSession.HasChanges == false
```
<!-- /sample -->

`AcceptChanges()` captures the current model as the next baseline. `AcceptChanges(submitted)` advances the retained baseline by that transition without touching the live model. The transition must match the retained baseline on every changed path; a stale or foreign change set is rejected with `InvalidOperationException` and the baseline stays unchanged. Send exceptions leave the baseline unchanged because acknowledgement never ran. The live model must be diffable when `AcceptChanges()` runs: unassigned keyed sentinels and duplicate stable keys fail fast instead of retaining an unusable baseline.

`AcceptChanges` rejects transitions containing unassigned sentinels (`[SparseKey(Unassigned = ...)]`). Forms with newly added keyed rows must therefore use the fresh session pattern above once the server assigns persistent keys (see [Keyed forms and database-assigned IDs](#keyed-forms-and-database-assigned-ids)).

### Reload with fresh server state

`Reload(serverState)` rebases the pending local edits onto authoritative server state in one call. It returns a `RebaseResult<ChangeSet>`: the rebased transition plus structured conflicts. On success it writes the merged result into the existing live model instance and retains the server state as the new baseline, so pending edits survive the reload as changes against the new baseline.

<!-- sample: ui-reload -->
```csharp
var reloadModel = new UiOrder { Number = "base" };
var reloadSession = reloadModel.CreateEditSession();
reloadSession.Observable.Number = "local";
var serverState = new UiOrder
{
    Number = "base",
    Items = [new UiOrderItem { Id = "line-1", Name = "First item" }],
};

var reload = reloadSession.Reload(serverState);
// reload.HasConflicts == false
// reloadSession.Model.Number == "local"
// reloadSession.Model.Items.Count == 1
// reloadSession.HasChanges == true
```
<!-- /sample -->

Conflicts leave both the live model and the retained baseline untouched. Keep the current session and surface the conflicts instead of recreating anything:

<!-- sample: ui-reload-conflict -->
```csharp
var conflictModel = new UiOrder { Number = "base" };
var conflictSession = conflictModel.CreateEditSession();
conflictSession.Observable.Number = "local";
var conflictingServer = new UiOrder { Number = "server" };

var conflicted = conflictSession.Reload(conflictingServer);
// conflicted.HasConflicts == true
// conflictSession.Model.Number == "local"
// conflictSession.HasChanges == true
```
<!-- /sample -->

A conflict-free reload whose merged result differs from the live model only in members that cannot be written in place (init-only or getter-only) reports those as conflicts rather than silently dropping them. Passing a null server state throws `ArgumentNullException`.

When a form needs explicit control over the merge, for example to show a side-by-side resolution UI before committing, use the manual pattern: extract `session.CreateChangeSet()`, `TryApplyTo` it onto the new server state, and on success recreate the session with the two-argument overload so the new server state becomes the baseline and the merged model becomes the active instance. On conflict, keep the current session and surface `pending.RebaseOnto(newServerState).Conflicts`. `Reload` above performs these steps atomically; prefer it unless the UI must intervene mid-merge.

### Discard pending edits

`RevertChanges()` writes the retained baseline back into the live model in place. It discards unsaved edits; it does not contact a server and does not advance the baseline. `Current` always reflects the live model, so it shows the reverted values immediately.

<!-- sample: ui-revert -->
```csharp
var revertModel = new UiOrder { Number = "a" };
var revertSession = revertModel.CreateEditSession();
revertSession.Observable.Number = "b";

var reverted = revertSession.TryRevertChanges(out var revertConflicts);
// reverted == true
// revertConflicts is null
// revertModel.Number == "a"
// revertSession.HasChanges == false
```
<!-- /sample -->

`bool TryRevertChanges(out conflicts)` reports whether the revert completed: `true` with null conflicts when the model was restored, `false` with structured conflicts when the pending changes cannot be reverted in place. A `false` result leaves both the live model and the retained baseline untouched. Sessions whose baseline differs in members that cannot be written in place (init-only or getter-only) report those operations as conflicts; replace the session instead of expecting an in-place restore. When the live model holds temporarily invalid keyed state such as duplicate keys or unassigned sentinels, the revert restores the retained valid baseline directly instead of deriving a keyed diff from the invalid state. `RevertChanges()` is the throwing form: it throws `InvalidOperationException` where `TryRevertChanges` would return `false`.

### Apply a remote transition to a bound model

When the destination object is already bound to a UI, prefer the conflict-checked `session.TryApplyInPlace(changes, out conflicts)`. It validates the transition before-state against the live model and only then writes the result into the same instance, so an unrelated concurrent edit is preserved while a conflicting edit surfaces as a structured conflict. `ApplyInPlace(changes)` is the throwing form. When the change includes an init-only or constructor-only member, the in-place write cannot complete: `TryApplyInPlace` returns `false` with an in-place write conflict, and `ApplyInPlace` throws `InvalidOperationException`.

The explicit blind form `changes.ToPatch().ApplyInPlace(model)` skips the before-state check. `ToPatch()` discards the before-state, so the result can no longer rebase or report conflicts. Use it only when the caller already owns conflict handling (see [ChangeSet rebase](rebase.md#in-place-application-for-bound-models)).

Neither in-place form is a transaction. Conflicts are detected atomically and leave the model untouched, but once validation passes the write runs ordinary model setters and collection operations, which can throw after partially mutating the model. The session then resynchronizes its snapshots and notifications so later reads observe the partial state rather than stale state.

### Keyed forms and database-assigned IDs

Rows the client adds carry an unassigned sentinel key such as `0` until the database assigns persistent IDs. That sentinel transition cannot simply be accepted as the new baseline: `AcceptChanges` rejects transitions that would retain unassigned sentinels, because future diffs could not tell the new rows apart. Send the change set, let the server insert the rows and assign IDs, then replace the form state with the authoritative persisted state and start a fresh session. See [Database-assigned keys](keyed-collections.md#database-assigned-keys) for the full lifecycle and the reason the unassigned Add must never be acknowledged.

## Relocated generated types

`Model.Fragment`, `Model.FragmentBuilder`, `Model.Patch`, `Model.ChangeSet`, and `Model.ChangePayload` stay nested in the annotated model, and the `CreateEditSession()`, `ToObservable()`, and `CreateChangeSet()` entry points are unchanged. Everything else generated per model moved out of the annotated type into a stable per-model container in `SparseFragments.Generated`:

| Before (nested in the model) | After (per-model container) |
| --- | --- |
| `Model.EditSession` | `global::SparseFragments.Generated.<Container>.EditSession` |
| `Model.Observable` (`Model.SparseObservable` on collision) | `<Container>.Observable` (`<Container>.SparseObservable`) |
| `Model.ReadOnlyView` | `<Container>.ReadOnlyView` |
| `Model.DescriptorFactory` | `<Container>.DescriptorFactory` (internal) |
| Payload DTOs under `Model.ChangePayload` | `global::SparseFragments.Generated.__Internal_<hash>` |
| Fragment JSON converter body | `<Container>FragmentJsonConverter` (`Model.Fragment.FragmentJsonConverter` stays as a private shell) |
| Fragment/Patch/ChangeSet operation bodies | `<Container>FragmentOperations`, `<Container>.PatchOperations`, `<Container>.ChangeSetOperations` (internal) |

`<Container>` is the sanitized fully qualified model identity plus a stable hash, so same-short-name models in different namespaces get distinct containers. The playground names one directly:

```csharp
using TaskObservable = global::SparseFragments.Generated.SparseFragments_Playground_Models_PlaygroundTask_E6C8F7DB.Observable;
```

There are no backwards-compatibility aliases: update explicit type references to the container paths, or drop them in favor of `var` and the extension entry points. A model member or nested type named `EditSession`, `Observable`, `ReadOnlyView`, or `DescriptorFactory` no longer collides with generated code; members named `ChangeSet` or `ChangePayload` are now reported as SPF009 instead of failing with a raw compiler error (see [SPF009](analyzer.md#spf009-member-conflicts-with-generated-api)).

## Blazor

The `SparseFragments.Blazor` package (`net8.0` / `net10.0`) adds Blazor helpers for the framework-neutral edit session. The session retains a baseline and derives semantic changes by comparing it with the current model:

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

`CreateEditContext()` binds the original editable model `T` to the `EditContext`. Do not use the generated observable proxy (the per-model `Observable` type in `SparseFragments.Generated`) as `EditContext.Model`. Blazor field tracking and validation run on `EditContext` and `FieldIdentifier` and model metadata, so `DataAnnotations` keep applying to `T`, while the semantic patch still comes from baseline and current `T`.

### Submit, refresh, and conflict display

One form flow covers binding, validation, submission, authoritative-state refresh, and structured conflict display. Validation runs through the ordinary `EditContext` pipeline; submission sends the session change set through the application transport; conflicts return to the form as validation messages addressed by member path.

<!-- sample: ui-blazor-form -->
```csharp
var formOrder = new BlazorDocsOrder { Number = "ORD-1" };
var formSession = formOrder.CreateEditSession();
var formContext = formSession.CreateEditContext();
var formStore = formSession.CreateValidationStore(formContext);
formContext.OnValidationRequested += (_, _) =>
{
    formStore.Clear();
    if (string.IsNullOrEmpty(formSession.Model.Number))
    {
        formStore.Add(
            formSession.Field(nameof(BlazorDocsOrder.Number)),
            "Number is required."
        );
    }
};

formSession.Model.Number = "ORD-2";
if (!formContext.Validate())
{
    throw new InvalidOperationException("The form has validation errors.");
}

// Send formSession.CreateChangeSet().ToPayload() through the
// application transport. The server rebases it onto the current row:
// unchanged here, the server kept a newer Number instead.
        var submitted = formSession.CreateChangeSet();
        var serverState = new BlazorDocsOrder { Number = "SERVER" };
        var surfacedConflicts = 0;
        var surfacedPath = string.Empty;
        if (!submitted.TryApplyTo(serverState, out _, out var formConflicts))
        {
            foreach (var formConflict in formConflicts)
            {
                formSession.AddValidationError(
                    formStore,
                    formConflict.PathText,
                    "Server kept a newer value."
                );
                surfacedConflicts++;
                surfacedPath = formConflict.PathText;
            }
        }

        // surfacedConflicts == 1
        // surfacedPath == "Number"
        // The next save starts from the authoritative persisted state.
var persisted = new BlazorDocsOrder { Number = "ORD-2" };
formSession = persisted.CreateEditSession();
formContext = formSession.CreateEditContext();
// formSession.HasChanges == false
```
<!-- /sample -->

The conflict loop is the whole display path: each structured conflict already carries its member path, so `AddValidationError` with the `fieldPath` overload addresses the message at the field the server disagrees with. A successful save follows the fresh-session pattern from [Recommended save workflow](#recommended-save-workflow): recreate the session from the persisted model and recreate the `EditContext` with it.

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

Dictionary members resolve through bracketed keys such as `Contacts["billing"].Name`. The canonical spelling quotes the key; an unquoted key is accepted when it needs no escaping. Non-string keys (numeric, enum, `Guid`) parse from the same spelling with invariant culture, and keys that do not parse fail as invalid paths. Resolution works for mutable dictionaries and for read-only `IReadOnlyDictionary<TKey, TValue>` models alike, including implementations that do not expose the legacy non-generic `IDictionary`.

List members resolve through numeric indexes such as `Lines[1].Quantity`, for mutable lists and read-only `IReadOnlyList<T>` models alike, including implementations without the legacy non-generic `IList`. Indexes resolve positionally through the indexer; out-of-range and non-numeric indexes fail as invalid paths.

Keyed collections (members whose element type declares a stable key) also resolve quoted stable keys such as `Lines["b"].Quantity`. These are the paths `EnumerateChanges()` emits, so a changed item's path can be passed to `session.Field` directly and keeps resolving after reorders. Quoted keys never act as positions, even when numeric: `Items["7"]` looks up key `7` while `Items[7]` is the eighth position. Index spellings from `EnumerateChangedPaths()` (such as `Lines[1].Quantity`) resolve positionally. Removed keys no longer resolve and fail as invalid paths.

The neutral session members such as `Model`, `HasChanges`, `CreateChangeSet()`, `CreatePatch()`, and no-argument `AcceptChanges()` remain available independently of Blazor. Context-taking helpers require an `EditContext` whose `Model` is the same instance as `session.Model`.

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

Errors obtained elsewhere (for example structured rebase conflicts) surface the same way with `uiSession.AddValidationError(store, uiSession.Field(nameof(UiOrder.Number)), message)`. Fields resolved from the session keep working when they point at nested members, list elements, or dictionary values: the error is accepted as long as the field model is still reachable from the session model. Fields built from another model graph stay rejected. The `fieldPath` overload covers the same paths without passing a `FieldIdentifier` between graphs. The model type must be a reference type.

A session ChangeSet is sent through its generated `T.ChangePayload`: call `ToPayload()` before transport, then call `ToChangeSet()` on receipt before reconciling with `RebaseOnto` (see [ChangeSet rebase](rebase.md)). SparseFragments provides no transport abstraction. Transport configuration stays with the application.

## WPF, WinForms, .NET MAUI, WinUI, and Avalonia

These frameworks bind the generated observable wrapper (the per-model `Observable` type in `SparseFragments.Generated`). The wrapper writes through to the same underlying model and raises `INotifyPropertyChanged` notifications for binding.

Nested models surface as child proxies that propagate changes to the root callback.

Mutable indexable sequences (`List<T>`, `IList<T>`, `Collection<T>`, and `ObservableCollection<T>`) and mutable `Dictionary<TKey,TValue>` and `IDictionary<TKey,TValue>` members surface as notifying views over the original collection instances. List views implement generic and non-generic `IList` for WPF binding; dictionary collection events contain `KeyValuePair<TKey,TValue>` items. Edits through a view mutate the model collection in place, raise `INotifyCollectionChanged` and `INotifyPropertyChanged` (`Count` and `Item[]`), and notify the parent proxy and session callback. An underlying `ObservableCollection<T>` is forwarded rather than notified twice.

For generated reference-model elements, list and dictionary views expose cached element `Observable` proxies. Add an element proxy created around a new model (or use `AddModel` / `InsertModel` on a list view and `AddModel` on a dictionary view). Replacing or removing elements, clearing a view, replacing the collection through the proxy, or disposing the view detaches stale element callbacks.

`Observable.Items` is the generated view type so bindings can use it directly. Since C# properties cannot have a view getter and a model-collection setter of different types, replace a collection with the generated `ReplaceItems(modelCollection)` method; this rebuilds the view and raises the member notification.

Mutations made directly to a plain model `List<T>` or `Dictionary<TKey,TValue>` update the model but bypass view notifications; changes to an underlying collection that itself implements `INotifyCollectionChanged` (such as `ObservableCollection<T>`) are forwarded. Arrays, `IReadOnlyList<T>`, `IEnumerable<T>`, immutable collections, and sets remain replace-only. Models using collection types the generator cannot deeply clone must continue to provide the applicable explicit clone policy.

Create a neutral edit session and bind its observable proxy; the session tracks the same underlying model. The `onChanged` callback drives save-button state, while `Current` feeds read-only displays:

<!-- sample: ui-wpf-session -->
```csharp
var stockModel = new UiOrder { Number = "ORD-1" };
var saveEnabled = false;
var stockSession = stockModel.CreateEditSession(
    onChanged: () =>
    {
        saveEnabled = true;
    }
);
var stockView = stockSession.Observable;

stockView.Number = "ORD-2";
stockView.Items.AddModel(new UiOrderItem { Id = "line-1", Name = "First item" });
// saveEnabled == true
// stockModel.Number == "ORD-2"
// stockSession.Current.Number == "ORD-2"

var stockChanges = stockSession.CreateChangeSet();
// stockChanges.Number.After.Value == "ORD-2"
```
<!-- /sample -->

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
