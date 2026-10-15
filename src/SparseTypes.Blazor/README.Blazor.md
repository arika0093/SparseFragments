# SparseTypes.Blazor

Blazor edit sessions for SparseTypes with semantic change tracking.

```sh
dotnet add package SparseTypes.Blazor
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
[UI frameworks](https://github.com/arika0093/SparseTypes/blob/main/docs/ui-frameworks.md).
Transport stays with the application: send `CreateChangeSet().ToPayload()`
with an app-owned call, then acknowledge with `AcceptChanges(changes)`.
When the server assigns IDs or normalizes the data, replace the model with the
authoritative state and create a fresh session and `EditContext` instead of
acknowledging the unassigned transition.

Typed child sessions

`SparseSessionScope` also cascades a `SparseSessionScopeContext<TModel>`.
Open a child with its generated parent path; the scope retains it across
renders and disposes its history and child handle when the parent session is
replaced or the scope is disposed.

<!-- illustrative: the parent and child model types are defined by the application -->
```csharp
var customer = scope.OpenChildSession(session, OrderDto.SparsePath.Customer);
customer.Path.Name; // Typed paths rooted at the child model.
customer.TryAcceptChanges(out var conflicts); // Merge only when the section is saved.
customer.DiscardChanges();
customer.Undo();
customer.Redo();
```

The child context provides generated paths, a child-bound `EditContext`,
change state, acceptance conflicts, child-local history actions, and an
`AutoAccepted` event for successful automatic merges. The scope enables
history; accepting or discarding resets its baseline while the child remains
connected. Calling `child.Session.AcceptChanges()` alone does not merge the
draft into its parent.

Set `AutoAccept="true"` on `SparseSessionScope` to merge each child-session
change into its parent as soon as it is reported. Nested scopes inherit the
setting unless they override it. A failed merge leaves the child draft intact
and exposes its conflicts through `AutoAcceptConflicts`; a child `BatchEdit`
is accepted as one parent transition. `SparseSessionScopeContext.HasChanges`
reports whether any opened child has pending changes, and its `Changed` event
signals changes to those child states. Auto-accept does not enable history on
the parent session; enable it there when parent-level Undo/Redo is needed.

Field and collection state reads inspect the session they are bound to. A
pending child draft stays separate from changes already accepted into the
parent, and rendering `SparseField` does not accept or discard it. Explicit
change-set capture keeps its child-coordination behavior.

Session scope and refresh

Wrap the form subtree in `SparseSessionScope` to keep it in sync with the
session. The scope cascades the active session to descendants and re-renders
the subtree once per session notification. It subscribes through
`INotifyPropertyChanged` and marshals through the Blazor synchronization
context, so edits from background work still render on the UI thread. The
scope never reads the live model itself, which keeps the observable-only
`HasChanges` cache intact for reads during render.

Refresh is session-level: every notification re-renders the whole subtree.
Group one user action with `BatchEdit` so it notifies once. `AutoAccept` also
uses those notifications to attempt child acceptance; a failed acceptance
keeps the draft available for conflict handling. There is no per-path refresh;
narrowing would have to stay correct for ancestor, descendant, collection
reorder, and replacement notifications first.

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

Headless components

`SparseEdit` and `SparseField` render a form without imposing markup,
styling, or a component vendor. `SparseEdit` takes only `Session`, and each
`SparseField` takes only `Path`; the model, value, and concrete path
builder types infer from those two arguments.

```razor
<SparseEdit Session="session" Context="edit">
    <EditForm EditContext="edit.EditContext">
        <SparseField Path="edit.Path.Number" Context="number">
            <MyInput @bind-Value="number.Value" />
        </SparseField>
    </EditForm>
    <button disabled="@(!edit.CanUndo)" @onclick="edit.Undo">Undo</button>
</SparseEdit>
```

The edit context exposes the concrete `Path` builder, the original-model
`EditContext`, live `HasChanges`, and the session-owned history. History
stays owned and configured by the session: enable it with
`EnableHistory` before rendering, and the components only read and observe
it, never dispose it. Each field context exposes the live typed `Value`
with a setter into the binding, `SetAsync`, `IsChanged`, presence
endpoints, `IsModified`, validation messages, and root-preserving
navigation (`Select`, `Key`, `At`, `TemporaryKey`).

Use `@bind-Value="field.Value"` on controls that only need value and
change notification. For `InputText`, `InputNumber`, and other
`InputBase<TValue>` controls, pass `Value`, `ValueChanged`, and
`ValueExpression` explicitly so validation targets the original model; do
not use `@bind-Value` there, since its generated expression would target
the field context. Keyed, temporary-key, and indexed paths expose a null
`ValueExpression`, and `InputBase` rejects a null expression, so those
paths edit through value and callback inputs with errors surfaced
explicitly. Full guidance: [Blazor headless components](https://github.com/arika0093/SparseTypes/blob/main/docs/how-to/blazor-headless-components.md).

Session capabilities and remaining reflection

Generated sessions implement small capability interfaces for history control,
field and collection state, batch edits, and descriptors. Blazor probes these
interfaces directly, so ordinary `SparseEdit`, `SparseField`,
`SparseCollection`, and child and session scope workflows run without
reflective member lookup, and a missing capability reports a typed
unsupported-capability error instead of a `TargetInvocationException`. The
typed `SparsePath<TModel, TValue>` overloads keep compile-time path typing.

Reflection remains for live-model graph walks: member, collection, key, and
temporary-key resolution in `SparseFieldPathResolver`, bind-path validation,
the descriptor-unavailable binding fallback, and value-expression
construction in `SparseFieldBinding`. These paths operate on arbitrary user
model graphs with no generated contract, so generated descriptors stay the
first choice and the model walk covers the rest.
