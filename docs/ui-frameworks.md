# UI Framework Integration

SparseFragments derives a patch from a retained baseline and the current model. UI change tracking is used for binding/validation, not as the source of patch semantics.

```csharp
var session = order.CreateEditSession();

session.Model.Number = "ORD-2";
session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });

if (session.HasChanges)
{
    var patch = session.CreatePatch();
    ...
}

session.AcceptChanges();
```

Without a session, the same flow works manually — snapshot a baseline, let the UI mutate the plain model, and diff baseline against current state:

```csharp
var baseline = WidgetDto.Fragment.From(model); // snapshot, isolated copy
// ...user edits `model` through the UI framework...
var patch = WidgetDto.Patch.Between(baseline, WidgetDto.Fragment.From(model));
```

## Blazor

The `SparseFragments.Blazor` package (`net8.0` / `net10.0`) bridges ordinary Blazor forms and SparseFragments semantic patches through the generated `CreateEditSession()` method and the `SparseEditSession` type. Create a session from the model and bind its `EditContext` to an ordinary `EditForm`:

```csharp
var session = order.CreateEditSession();

<EditForm EditContext="@session.EditContext">...</EditForm>

if (session.HasChanges)
{
    var patch = session.CreatePatch();
    ...
}

session.AcceptChanges(); // re-baseline, clear Blazor modified flags
```

Bind the **original editable model `T`** to the `EditContext`:

```csharp
var editContext = new EditContext(model);
```

Do not use the generated `T.Observable` proxy as `EditContext.Model`: Blazor field tracking and validation run on `EditContext`/`FieldIdentifier` and model metadata, so `DataAnnotations` keep applying to `T`, while the semantic patch still comes from baseline/current `T`.

The session API:

| Member | Purpose |
| --- | --- |
| `Model` | The live editable model; the UI mutates this instance directly |
| `EditContext` | The Blazor edit context for validation, field state, and submit behavior |
| `HasChanges` | Whether the current model differs semantically from the baseline |
| `CreatePatch()` | Derives the semantic patch between the baseline and the current model |
| `AcceptChanges()` | Replaces the baseline with the current state, clears Blazor modified flags, keeps the same model instance and `EditContext` |
| `CreateValidationStore()` | Creates a `ValidationMessageStore` bound to the session's `EditContext` |
| `Field(name)` | Resolves a Blazor `FieldIdentifier` for a model member name |
| `AddValidationError(store, field, message)` | Static helper surfacing a message through the `ValidationMessageStore` |

A typical edit round-trip:

```csharp
var session = order.CreateEditSession();
session.HasChanges.ShouldBeFalse();

session.Model.Number = "ORD-2";
session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });
session.HasChanges.ShouldBeTrue();

var patch = session.CreatePatch();   // semantic patch vs the baseline
session.AcceptChanges();             // commit: new baseline, clean field state
```

Edit-then-restore yields no semantic change even though fields were touched:

```csharp
session.Model.Number = "changed";
session.Model.Number = "ORD-1";   // restored

session.HasChanges.ShouldBeFalse();
session.CreatePatch().IsEmpty.ShouldBeTrue();
```

Validation flows through the ordinary `EditContext` pipeline:

```csharp
var session = order.CreateEditSession();
var store = session.CreateValidationStore();
session.EditContext.OnValidationRequested += (sender, _) =>
{
    store.Clear();
    if (string.IsNullOrEmpty(session.Model.Number))
    {
        store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
    }
};
```

Errors obtained elsewhere (for example structured rebase conflicts) surface the same way via `AddValidationError`. The model type must be a reference type.

## WPF / WinForms / .NET MAUI / WinUI / Avalonia

These frameworks bind the generated `T.Observable` proxy: an `INotifyPropertyChanged` adapter over the live `T` instance with no extra runtime dependency. Scalar members notify only on real change; nested models surface as cached child proxies that propagate to the root callback; replacing a nested member rebuilds its proxy; collections notify on replacement.

```csharp
var baseline = WidgetDto.Fragment.From(model);
var observable = new WidgetDto.Observable(model, onChanged: () => HasUnsavedChanges = true);

// bind the UI to `observable`; edits flow into the same live `model`
observable.Title = "New title";
observable.PropertyChanged += (_, args) => Console.WriteLine(args.PropertyName);

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

The same shape works for WinForms (`INotifyPropertyChanged` binding), .NET MAUI, WinUI, and Avalonia: set the binding context to the observable and keep patching on the underlying model.
