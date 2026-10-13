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

Session scope and refresh

Wrap the form subtree in `SparseSessionScope` to keep it in sync with the
session. The scope cascades the active session to descendants and re-renders
the subtree once per session notification. It subscribes through
`INotifyPropertyChanged` and marshals through the Blazor synchronization
context, so edits from background work still render on the UI thread. The
scope never reads the live model itself, which keeps the observable-only
`HasChanges` cache intact for reads during render.

Refresh is session-level: every notification re-renders the whole subtree.
Group one user action with `BatchEdit` so it notifies once. There is no
per-path refresh; narrowing would have to stay correct for ancestor,
descendant, collection reorder, and replacement notifications first.

Replacing the `Session` parameter unsubscribes the old session before
subscribing the new one, and disposing the scope unsubscribes entirely. After
the server returns authoritative state, adopt a fresh session from the
persisted model, recreate the `EditContext` from it, and key the `EditForm`
so Blazor rebuilds the inputs against the new context. Reused inputs keep the
old cascaded context and reject the new one.

Prefer consumer-controlled refresh when a subtree updates at high frequency
or only needs part of the session state, for example by reading field or
collection state on demand around explicit actions rather than re-rendering
on every keystroke. The `SessionScopeRefreshBenchmarks` workload shows the
cost difference between individual edits and one `BatchEdit`; it runs with a
dry job for a quick check.
