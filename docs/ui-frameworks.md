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
editing model is centered on `EditContext`. Future framework integrations follow the
`SparseFragments.<Framework>` naming convention.

## Blazor

The `SparseFragments.Blazor` package (`net8.0` / `net10.0`) bridges ordinary Blazor
forms and SparseFragments semantic patches. The workflow centers on the generated
`CreateEditSession()` method and the `SparseEditSession` type.

Reference the package; `CreateEditSession()` is then generated for each
`[SparseFragmentModel]` class (projects without the reference generate byte-identical
output). Create a session from the model and bind its `EditContext` to an ordinary
`EditForm`:

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

Do not make the generated `T.Observable` proxy the `EditContext.Model`:

- Blazor field tracking and validation are based on `EditContext`, `FieldIdentifier`,
  and the actual model/property metadata;
- DataAnnotations and other model metadata keep applying to `T`;
- `INotifyPropertyChanged` is not the primary Blazor form-change mechanism;
- the semantic patch still comes from baseline/current `T`, not from modified fields.

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

Server-side or conflict errors obtained elsewhere (for example structured rebase
conflicts — see [Patch rebase](rebase.md)) can be surfaced the same way via
`AddValidationError` without taking a dependency on HTTP transport.

Boundaries: the package depends only on SparseFragments and Blazor forms
abstractions; it defines no HTTP transport, ETag, or concurrency protocols
(combine `CreatePatch()` with [Patch rebase](rebase.md) for concurrent editing).
The model type must be a reference type.

## WPF / WinForms / .NET MAUI / WinUI / Avalonia

These frameworks bind the generated `T.Observable` proxy. It implements
`INotifyPropertyChanged` over the live model instance with no extra runtime
dependency:

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
[Keyed collections](keyed-collections.md)).

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
