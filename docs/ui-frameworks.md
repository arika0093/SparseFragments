# UI Framework Integration

SparseFragments derives semantic changes by comparing a retained baseline with the current model. UI dirty flags and change notifications may drive binding, validation, or UI state, but they do not define the Patch itself.

<!-- sample: ui-session-models -->
```csharp
using SparseFragments;

[SparseFragmentModel]
public partial class UiOrder
{
    public string Number { get; set; } = string.Empty;
}
```
<!-- /sample -->

## Framework-neutral edit sessions

Every generated model has a stable, hashed top-level extension container in its
namespace. Its `CreateChangeSet` extension compares a baseline to an explicit
current model, and reference-type models also have a neutral edit-session
factory:

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
instance. The one-model factory captures that model as the baseline and edits
it; the two-model overload retains the `current` instance and snapshots
`baseline` separately. `HasChanges` and `CreateChangeSet()` always compare that
baseline with the model's current state, so edit-then-restore is clean even if a
UI control reported that a field was touched. `CreatePatch()` projects the same
transition to a baseline-free patch. `AcceptChanges()` captures the current
state as the next baseline. These APIs live in `SparseFragments` and do not
require a UI-framework package.

For bindings that need `INotifyPropertyChanged`, `Optional<T>.ToObservable()`
maps `Optional<Model?>` to the generated, model-specific observable proxy while
preserving missing, present-null, and present-value states:

```csharp
Optional<UiOrder.Observable?> proxy =
    Optional<UiOrder?>.Present(session.Model).ToObservable();
```

The generated extension container is an implementation detail with a
deterministic hash-based name; call the extensions rather than naming the
container directly. Framework-specific packages can adapt the neutral session
without adding framework references to its generated code. The same neutral
`CreateEditSession()` extension is used whether or not a framework package is
referenced.

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

`CreateEditContext()` binds the **original editable model `T`** to the
`EditContext`. Do not use the generated `T.Observable` proxy as
`EditContext.Model`: Blazor field tracking and validation run on
`EditContext`/`FieldIdentifier` and model metadata, so `DataAnnotations` keep
applying to `T`, while the semantic patch still comes from baseline/current `T`.

Blazor extension methods:

| Member | Purpose |
| --- | --- |
| `session.CreateEditContext()` | Creates a Blazor `EditContext` bound to `session.Model` |
| `session.AcceptChanges(editContext)` | Calls the framework-neutral `AcceptChanges()` and clears the supplied context's modified flags |
| `session.CreateValidationStore(editContext)` | Creates a `ValidationMessageStore` bound to the supplied context |
| `session.Field(name)` | Resolves a Blazor `FieldIdentifier` for a model member name |
| `session.AddValidationError(store, field, message)` | Surfaces a message through the `ValidationMessageStore` |

The neutral session members such as `Model`, `HasChanges`,
`CreateChangeSet()`, `CreatePatch()`, and no-argument `AcceptChanges()` remain
available independently of Blazor. Context-taking helpers require an
`EditContext` whose `Model` is the same instance as `session.Model`.

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
the same way via
`uiSession.AddValidationError(store, uiSession.Field(nameof(UiOrder.Number)), message)`.
The model type must be a reference type.

A session ChangeSet is sent through its generated `T.ChangeSetPayload`: call `ToPayload()` before transport, then call `ToChangeSet()` on receipt before reconciling with `RebaseOnto` (see [ChangeSet rebase](rebase.md)). SparseFragments provides no transport abstraction — transport configuration stays with the application.

## WPF / WinForms / .NET MAUI / WinUI / Avalonia

These frameworks bind the generated `T.Observable` wrapper. The wrapper writes through to the same underlying model and raises `INotifyPropertyChanged` notifications for binding.

Nested models surface as child proxies that propagate changes to the root callback, and replacing a nested member or collection rebuilds the corresponding proxy and notification. Create a neutral edit session and bind its observable proxy; the session tracks the same underlying model:

```csharp
var session = model.CreateEditSession(onChanged: () => HasUnsavedChanges = true);
var observable = session.Observable;

// bind the UI to `observable`; edits flow into the same live `model`
observable.Title = "New title";
observable.PropertyChanged += (_, args) => Console.WriteLine(args.PropertyName);

// ...user edits `model` through the UI framework...
var uiChanges = session.CreateChangeSet();
```

### WPF example

```xml
<TextBox Text="{Binding Title, UpdateSourceTrigger=PropertyChanged}" />
<TextBlock Text="{Binding Child.Name}" />
```

```csharp
var session = model.CreateEditSession(
    onChanged: () => SaveCommand.NotifyCanExecuteChanged());
DataContext = session.Observable;
```

WinForms, .NET MAUI, WinUI, and Avalonia use the same `Observable` wrapper over the underlying model; only the framework-specific binding setup differs.
