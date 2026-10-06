# UI Framework Integration

SparseFragments stays UI-agnostic: the patch always comes from comparing a retained
baseline against the current model, never from UI change tracking. The UI binding
mechanism is not the source of truth for patch generation, which is what makes
nested edits, collection add/remove/reorder, and edit-then-restore behave correctly.

```csharp
var baseline = WidgetDto.Fragment.From(model); // snapshot, isolated copy
// ...user edits `model` through the UI framework...
var patch = WidgetDto.Patch.Between(baseline, WidgetDto.Fragment.From(model));
```

No framework-specific SparseFragments package is needed except for Blazor, whose
editing model is centered on `EditContext` (see below). Desktop frameworks bind the
generated `T.Observable` proxy directly.

## Blazor

Bind the **original editable model `T`** to a standard `EditContext`:

```csharp
var editContext = new EditContext(model);
```

Do not make the generated `T.Observable` proxy the `EditContext.Model`:

- Blazor field tracking and validation are based on `EditContext`, `FieldIdentifier`,
  and the actual model/property metadata;
- DataAnnotations and other model metadata keep applying to `T`;
- `INotifyPropertyChanged` is not the primary Blazor form-change mechanism;
- the semantic patch still comes from baseline/current `T`, not from modified fields.

The `SparseFragments.Extensions.Blazor` package composes this into
`SparseEditSession<TModel, TFragment, TPatch>`, created per model via the generated
`CreateEditSession()`:

```csharp
var session = order.CreateEditSession();

session.HasChanges.ShouldBeFalse();
session.Model.Number = "ORD-2";
session.HasChanges.ShouldBeTrue();
var orderPatch = session.CreatePatch();
session.AcceptChanges();
```

Server-side errors surface through Blazor validation without an HTTP dependency:

```csharp
var store = session.CreateValidationStore();
SparseEditSession<OrderDto, OrderDto.Fragment, OrderDto.Patch>.AddValidationError(
    store,
    session.Field(nameof(OrderDto.Number)),
    "Server rejected the order number.");
```

See `docs/blazor.md` for the full session contract (`HasChanges`, `CreatePatch`,
`AcceptChanges`, validation-store primitive).

## WPF / WinForms / .NET MAUI / WinUI / Avalonia

These frameworks bind the generated `T.Observable` proxy from the normal
SparseFragments generator. It implements `INotifyPropertyChanged` over the live
model instance with no extra runtime dependency:

```csharp
var baseline = WidgetDto.Fragment.From(model);
var observable = new WidgetDto.Observable(model, onChanged: () => HasUnsavedChanges = true);

// bind the UI to `observable`; edits flow into the same live `model`
observable.Title = "New title";

var uiPatch = WidgetDto.Patch.Between(baseline, WidgetDto.Fragment.From(model));
```

Responsibility split:

- `T.Observable` — UI change notification and binding;
- underlying `T` — the actual editable state;
- baseline versus current `T` — the SparseFragments patch.

Scalar members notify only on real change; nested models surface as cached child
proxies that propagate to the root callback; replacing a nested member rebuilds its
proxy; collections notify on replacement. Keyed collection add/remove/reorder still
flows through `Patch.Between` with `SparseKey` semantics (see
`docs/keyed-collections.md`).

### WPF example

```xml
<TextBox Text="{Binding Title, UpdateSourceTrigger=PropertyChanged}" />
<TextBlock Text="{Binding Child.Name}" />
```

```csharp
DataContext = new WidgetDto.Observable(model, () => SaveCommand.NotifyCanExecuteChanged());
```

The same shape works for WinForms (`INotifyPropertyChanged` binding), .NET MAUI,
WinUI, and Avalonia: set the binding context to the observable and keep patching
on the underlying model.
