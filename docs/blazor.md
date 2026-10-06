# Blazor Integration

The `SparseFragments.Blazor` package (`net8.0` / `net10.0`) bridges ordinary Blazor forms and SparseFragments semantic patches. The workflow centers on the generated `CreateEditSession()` method and the `SparseEditSession` type.

Related pages: [Patch rebase](rebase.md) (for applications implementing concurrent editing), [Keyed collections](keyed-collections.md), [Model shapes](model-shapes.md).

## Basic Workflow

Reference the package; `CreateEditSession()` is then generated for each `[SparseFragmentModel]` class (projects without the reference generate byte-identical output). Create a session from the model and bind its `EditContext` to an ordinary `EditForm`:

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

A typical edit round-trip, mirroring the tested behavior:

```csharp
var session = order.CreateEditSession();
session.HasChanges.ShouldBeFalse();

session.Model.Number = "ORD-2";
session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });
session.HasChanges.ShouldBeTrue();

var patch = session.CreatePatch();   // semantic patch vs the baseline
session.AcceptChanges();             // commit: new baseline, clean field state
```

## Semantic Model: Baseline vs Field Tracking

The key design point: Blazor's `EditContext` remains responsible for form state and validation, but it is **not** the source of truth for patch generation. Patches are derived from the retained SparseFragments baseline versus the current model, so cases that field-modified tracking alone would misrepresent behave correctly:

* **nested changes** — edits through nested objects produce nested patch operations;
* **collection add/remove/reorder** — including keyed-collection edits by element identity, not position;
* **edit-then-restore** — changing a value and changing it back yields no semantic change (`HasChanges == false`, empty patch), even though fields were touched.

```csharp
session.Model.Number = "changed";
session.Model.Number = "ORD-1";   // restored

session.HasChanges.ShouldBeFalse();
session.CreatePatch().IsEmpty.ShouldBeTrue();
```

## Validation Integration

Validation flows through the ordinary `EditContext` pipeline. Request a store from the session and wire it to validation events:

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

Server-side or conflict errors obtained elsewhere (for example structured rebase conflicts — see [Patch rebase](rebase.md)) can be surfaced the same way via `AddValidationError` without taking a dependency on HTTP transport.

## Boundaries

The package:

* depends only on SparseFragments and Blazor forms abstractions;
* does **not** define HTTP transport — sending `CreatePatch()` results to a server is application code;
* does **not** define ETag or concurrency protocols — combine `CreatePatch()` with [Patch rebase](rebase.md) when concurrent editing needs reconciliation;
* **can** surface externally obtained validation/conflict messages through `ValidationMessageStore`.

The model type must be a reference type (`SparseEditSession<TModel, TFragment, TPatch>` constrains `TModel : class`).
