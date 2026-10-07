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

## Blazor

The `SparseFragments.Blazor` package (`net8.0` / `net10.0`) bridges ordinary Blazor forms and SparseFragments semantic patches through the generated `CreateEditSession()` method and the `SparseEditSession` type. The session retains a baseline, exposes the live model for binding, and derives the semantic patch by comparing the baseline with the current model:

<!-- sample: ui-session -->
```csharp
var uiOrder = new UiOrder { Number = "ORD-1" };
var uiSession = uiOrder.CreateEditSession();

uiSession.Model.Number = "ORD-2";
// uiSession.HasChanges == true

var uiPatch = uiSession.CreatePatch();
// uiPatch.IsEmpty == false

uiSession.AcceptChanges();
// uiSession.HasChanges == false
```
<!-- /sample -->

Bind the session's `EditContext` to an ordinary `EditForm`:

```razor
<EditForm EditContext="@uiSession.EditContext">...</EditForm>
```

Bind the **original editable model `T`** to the `EditContext`. Do not use the generated `T.Observable` proxy as `EditContext.Model`: Blazor field tracking and validation run on `EditContext`/`FieldIdentifier` and model metadata, so `DataAnnotations` keep applying to `T`, while the semantic patch still comes from baseline/current `T`.

The session API:

| Member | Purpose |
| --- | --- |
| `Model` | The live editable model; the UI mutates this instance directly |
| `EditContext` | The Blazor edit context for validation, field state, and submit behavior |
| `HasChanges` | Whether the current model differs semantically from the baseline |
| `CreateChangeSet()` | Derives the baseline-aware change set between the baseline and the current model (recommended for changes that leave the local process) |
| `CreatePatch()` | Derives the baseline-free semantic patch (`CreateChangeSet().ToPatch()`) for purely local application |
| `AcceptChanges()` | Replaces the baseline with the current state, clears Blazor modified flags, keeps the same model instance and `EditContext` |
| `CreateValidationStore()` | Creates a `ValidationMessageStore` bound to the session's `EditContext` |
| `session.Field(name)` | Resolves a Blazor `FieldIdentifier` for a model member name |
| `AddValidationError(store, field, message)` | Static helper surfacing a message through the `ValidationMessageStore` |

Edit-then-restore yields no semantic change even though fields were touched:

```csharp
uiSession.Model.Number = "changed";
uiSession.Model.Number = "ORD-1";   // restored

// uiSession.HasChanges == false
// uiSession.CreatePatch().IsEmpty == true
```

Validation flows through the ordinary `EditContext` pipeline. Continuing with the session above:

```csharp
var store = uiSession.CreateValidationStore();
uiSession.EditContext.OnValidationRequested += (sender, _) =>
{
    store.Clear();
    if (string.IsNullOrEmpty(uiSession.Model.Number))
    {
        store.Add(uiSession.Field(nameof(UiOrder.Number)), "Number is required.");
    }
};
```

Errors obtained elsewhere (for example structured rebase conflicts) surface the same way via `AddValidationError`. The model type must be a reference type.

## WPF / WinForms / .NET MAUI / WinUI / Avalonia

These frameworks bind the generated `T.Observable` wrapper. The wrapper writes through to the same underlying model and raises `INotifyPropertyChanged` notifications for binding.

Nested models surface as child proxies that propagate changes to the root callback, and replacing a nested member or collection rebuilds the corresponding proxy and notification. Without a session, snapshot a baseline, let the UI mutate the plain model, and diff the baseline against the current state:

```csharp
var baseline = WidgetDto.Fragment.From(model); // snapshot, isolated copy
var observable = new WidgetDto.Observable(model, onChanged: () => HasUnsavedChanges = true);

// bind the UI to `observable`; edits flow into the same live `model`
observable.Title = "New title";
observable.PropertyChanged += (_, args) => Console.WriteLine(args.PropertyName);

// ...user edits `model` through the UI framework...
var uiPatch = WidgetDto.Patch.Between(baseline, WidgetDto.Fragment.From(model));
```

### WPF example

```xml
<TextBox Text="{Binding Title, UpdateSourceTrigger=PropertyChanged}" />
<TextBlock Text="{Binding Child.Name}" />
```

```csharp
DataContext = new WidgetDto.Observable(model, () => SaveCommand.NotifyCanExecuteChanged());
```

WinForms, .NET MAUI, WinUI, and Avalonia use the same `Observable` wrapper over the underlying model; only the framework-specific binding setup differs.
