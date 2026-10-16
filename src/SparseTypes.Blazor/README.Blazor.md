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
renders. A changed child whose target is replaced or deleted moves to the
detached holding with its draft intact instead of being disposed; recover it
through `TryGetDetachedChild`, list it with `GetDetachedChildPaths`, and
release it with `DisposeDetachedChildren`. An unchanged child is disposed as
before. Replacing the parent session or disposing the scope still disposes
every owned child, including detached drafts.

The scope's `ScopeContext` property exposes the same context to its owner.
Use `HasChangesFor` when the owner needs to display child status outside the
cascaded subtree; it reads pending drafts in `Draft` mode and parent changes
in `PassThrough` mode.

<!-- illustrative: the parent and child model types are defined by the application -->
```csharp
var customer = scope.OpenChildSession(session, OrderDto.SparsePath.Customer);
customer.Path.Name; // Typed paths rooted at the child model.
customer.TryAcceptChanges(out var conflicts); // Merge only when the section is saved.
customer.DiscardChanges();
customer.Undo();
customer.Redo();
```

For Razor, `SparseChildEditDefault` uses the built-in `SparseConflict` type and
infers the parent, child, path-builder, and child-session types from `Path`.
Nested child paths can use the context's `Path` directly without naming
generated path-builder types. Keep `SparseChildEdit` for applications that use
a custom conflict type.

The child context provides generated paths, a child-bound `EditContext`,
change state, merge conflicts, history actions, and an `Accepted` event.
Calling `child.Session.AcceptChanges()` alone does not merge the draft into
its parent.

`SparseSessionScope.Mode` controls who owns child changes. The default,
`Draft`, keeps edits in the child session until `TryAcceptChanges` or
`AcceptChanges` merges them into the parent. `DiscardChanges` resets that child
baseline while keeping the session connected. Nested scopes inherit the mode
unless they override it.

In `PassThrough`, each child-session change is merged into its parent as soon
as it is reported. The child's `HasChanges` and field or collection state read
the parent change set at the child path, so parent-owned edits remain
highlighted until the parent accepts or reverts them. A failed merge keeps the
child edit available and exposes its conflicts through `Conflicts`; a child
`BatchEdit` becomes one parent transition. `Accepted` fires after either a
successful draft acceptance or pass-through merge.

`SparseSessionScopeContext.HasChanges` reports pending child drafts, not
changes already owned by the parent. Pass-through does not enable history on
the parent session; enable it there when parent-level Undo/Redo is needed.
Field and collection state reads use the active ownership mode, and rendering
`SparseField` does not accept or discard changes. Change-set capture builds a
child-aware preview without accepting; acknowledging a captured submission
merges pending drafts first.

Session scope and refresh

Wrap the form subtree in `SparseSessionScope` to keep it in sync with the
session. The scope cascades the active session to descendants and re-renders
the subtree once per session notification. It subscribes through
`INotifyPropertyChanged` and marshals through the Blazor synchronization
context, so edits from background work still render on the UI thread. The
scope never reads the live model itself, which keeps the observable-only
`HasChanges` cache intact for reads during render.

Refresh is session-level: every notification re-renders the whole subtree.
Group one user action with `BatchEdit` so it notifies once. `PassThrough` uses
those notifications to merge child changes; a failed merge keeps the edit
available for conflict handling. There is no per-path refresh; narrowing
would have to stay correct for ancestor, descendant, collection reorder, and
replacement notifications first.

Field state reads share one snapshot per refresh generation: the first field
captures a single change set and later fields query its indexed view instead
of deriving a root diff each. The generation ends on session notifications
(edits, batches, baseline changes, reload, reconcile, undo/redo), model or
session replacement, or an explicit `InvalidateStateSnapshot` on the edit
context. Raw model edits that raise no session notification need that
explicit invalidation; a plain parameter refresh reuses the generation only
while the session reports no intervening change. Sessions without the
snapshot capability keep the live per-field query path.

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
first choice and the model walk covers the rest. Repeated walks reuse cached
immutable type metadata (member properties, collection interfaces, and key
readers, including misses, keyed weakly so collectible assemblies stay
collectible) while resolving intermediate objects against the live root on
every read; diagnostic path text renders only on failure. Member-only value
expressions are likewise reused while the session root identity holds and
rebuilt when it changes, so nested replacement stays live.
