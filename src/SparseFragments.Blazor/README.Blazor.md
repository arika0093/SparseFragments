# SparseFragments.Blazor

Blazor edit sessions for SparseFragments with semantic change tracking.

```sh
dotnet add package SparseFragments.Blazor
```

```csharp
var session = order.CreateEditSession();
var editContext = session.CreateEditContext();

<EditForm EditContext="@editContext">...</EditForm>

if (session.HasChanges)
{
    // Baseline-aware output for transport or later reconciliation.
    var changes = session.CreateChangeSet();
    ...
    // Baseline-free projection of the same edits for purely local application.
    var patch = session.CreatePatch();
    ...
}

var submitted = session.CreateChangeSet();
var response = await SendChangesAsync(submitted.ToPayload());
if (response.IsSuccess)
{
    // Advances the baseline only; later edits stay pending.
    // The EditContext is cleared only when the session is clean.
    session.AcceptChanges(editContext, submitted);
}

var store = session.CreateValidationStore(editContext);
session.AddValidationError(
    store,
    session.Field(nameof(Order.Number)),
    "Server rejected the order number.");
```

Blazor helpers are extension methods over the framework-neutral session.
`CreateEditContext()` binds the original `session.Model`, not its observable
proxy. Context-taking helpers require the context's `Model` to be that same
instance. Patches always come from baseline-versus-current model comparison,
never from `EditContext` field tracking. Full integration guidance:
[UI frameworks](https://github.com/arika0093/SparseFragments/blob/main/docs/ui-frameworks.md).
Transport stays with the application: send `CreateChangeSet().ToPayload()`
with an app-owned call, then acknowledge with `AcceptChanges(changes)`.
When the server assigns IDs or normalizes the data, replace the model with the
authoritative state and create a fresh session and `EditContext` instead of
acknowledging the unassigned transition.
