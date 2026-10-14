# Blazor headless components

`SparseEdit` and `SparseField` render a form without imposing markup,
styling, or a component vendor. `SparseEdit` takes only `Session`;
`TModel` and the concrete generated path builder infer from it. Each
`SparseField` takes only `Path`; `TModel` and `TValue` infer from the
builder. This guide assumes the [first sparse edit](../tutorial/first-sparse-edit.md)
flow and the [Blazor UI contract](../explanation/ui-frameworks.md#blazor) read familiar,
and the [Blazor edit form](blazor-edit-form.md) guide covers the equivalent
save lifecycle with hand-written bindings.

The listings below run in the `package-sparse-blazor` consumer fixture. The
Razor shapes compile as `SparseHeadless.razor` in the same project; that file
is compile-only, while the C# listing carries the runtime assertions.

## Render one form

<!-- illustrative: razor markup shape; the compilable component is SparseHeadless.razor in the Blazor consumer fixture -->
```razor
<SparseEdit Session="session" Context="edit">
    <EditForm EditContext="edit.EditContext">
        <SparseField Path="edit.Path.Number" Context="number">
            <MyInput @bind-Value="number.Value" />
            @if (number.IsChanged) { <span>Edited</span> }
        </SparseField>
        <SparseField Path="edit.Path.Customer" Context="customer">
            <SparseField Path="customer.Select(c => c.Email)" Context="email">
                <MyInput @bind-Value="email.Value" />
            </SparseField>
        </SparseField>
    </EditForm>
    <button disabled="@(!edit.CanUndo)" @onclick="edit.Undo">Undo</button>
</SparseEdit>
```

`MyInput` stands for an ordinary third-party control with
`Value`/`ValueChanged`. The `edit` context exposes the concrete `Path`
builder, the original-model `EditContext`, live `HasChanges`, and the
session-owned history. Each field context exposes the live typed `Value`
with a setter into the binding, `SetAsync`, `IsChanged`, presence
endpoints, `IsModified`, validation messages, and root-preserving
navigation (`Select`, `Key`, `At`, `TemporaryKey`).

## Drive the same contracts in code

<!-- sample: blazor-headless -->
```csharp
var headlessOrder = new HeadlessOrder
{
    Number = "ORD-1",
    Customer = new HeadlessCustomer { Name = "Ada", Email = "ada@example.com" },
    Lines = new List<HeadlessOrderLine>
    {
        new()
        {
            Sku = "a",
            Quantity = 1,
            Price = 10m,
        },
        new()
        {
            Sku = "b",
            Quantity = 2,
            Price = 20m,
        },
    },
};
var headlessSession = headlessOrder.CreateEditSession();
var headlessContext = headlessSession.CreateEditContext();
var headlessHistory = headlessSession.EnableHistory();

// The triples a SparseField hands to inputs: the live value, the change
// delegate, and the original-model expression for member-only paths.
var headlessNumber = headlessSession.Bind(HeadlessOrder.SparsePath.Number, headlessContext);
DocsCheck.Require(headlessNumber.Value == "ORD-1", "binding reads the live value");
DocsCheck.Require(
    headlessNumber.ValueExpression is not null,
    "member path exposes an expression"
);
await headlessNumber.SetAsync("ORD-2");
DocsCheck.Require(headlessOrder.Number == "ORD-2", "binding writes through the session");
DocsCheck.Require(
    headlessSession.FieldState(HeadlessOrder.SparsePath.Number).IsChanged,
    "scalar field state reports the edit"
);
DocsCheck.Require(headlessHistory.CanUndo, "session-owned history retains the edit");

headlessHistory.Undo();
DocsCheck.Require(headlessOrder.Number == "ORD-1", "undo restores");
headlessHistory.Redo();
DocsCheck.Require(headlessOrder.Number == "ORD-2", "redo reapplies");

// Keyed and indexed paths expose no expression, so value/callback
// inputs edit them while validation stays explicit.
var headlessPrice = headlessSession.Bind(
    HeadlessOrder.SparsePath.Lines.Key("b").Price,
    headlessContext
);
DocsCheck.Require(
    headlessPrice.ValueExpression is null,
    "keyed path exposes no expression"
);
await headlessPrice.SetAsync(99m);
DocsCheck.Require(
    headlessOrder.Lines.Single(line => line.Sku == "b").Price == 99m,
    "keyed binding writes through the session"
);
DocsCheck.Require(
    headlessSession
        .CollectionState(HeadlessOrder.SparsePath.Lines)
        .GetItemState("b")
        .IsEdited,
    "collection state reports the edited row"
);

var headlessSku = headlessSession.Bind(
    HeadlessOrder.SparsePath.Lines.At(0).Sku,
    headlessContext
);
DocsCheck.Require(headlessSku.Value == "a", "indexed binding follows the position");

var headlessDrafts = new HeadlessOrder();
var headlessDraftSession = headlessDrafts.CreateEditSession();
var headlessDraftContext = headlessDraftSession.CreateEditContext();
var headlessTemporaryId = Guid.NewGuid();
headlessDraftSession.Observable.Drafts.AddModel(
    new HeadlessDraft
    {
        Id = 0,
        Title = "draft",
        TemporaryId = headlessTemporaryId,
    }
);
var headlessTitle = headlessDraftSession.Bind(
    HeadlessOrder.SparsePath.Drafts.TemporaryKey(headlessTemporaryId).Title,
    headlessDraftContext
);
DocsCheck.Require(headlessTitle.Value == "draft", "temporary-key binding reads the value");
await headlessTitle.SetAsync("draft v2");
DocsCheck.Require(
    headlessDrafts.Drafts.Single(item => item.TemporaryId == headlessTemporaryId).Title
        == "draft v2",
    "temporary-key binding writes through the session"
);
DocsCheck.Require(
    headlessDraftContext.IsModified(),
    "temporary-key write marks the field modified"
);

headlessSession.DisableHistory();
```
<!-- /sample -->

## History belongs to the session

Call `EnableHistory` on the session before rendering when the form needs
undo and redo. The edit context reads the session-owned control live, so
`CanUndo` and `CanRedo` refresh through history stack notifications without
further calls. The components never enable, reconfigure, or dispose the
history: unmounting and remounting with the same session keeps the retained
stacks, and `DisableHistory` stays the single release path.

## Bind third-party and framework inputs

Use `@bind-Value="field.Value"` on controls that only need value and change
notification. For `InputText`, `InputNumber`, and other `InputBase<TValue>`
controls, pass the triple explicitly so validation targets the original
model.

<!-- illustrative: razor markup shape; the compilable component is SparseHeadless.razor in the Blazor consumer fixture -->
```razor
<SparseField Path="edit.Path.Number" Context="number">
    <InputText Value="number.Value"
               ValueChanged="number.ValueChanged"
               ValueExpression="number.ValueExpression" />
</SparseField>
<SparseField Path="@(edit.Path.Lines.Key("b").Price)" Context="price">
    <MyInput @bind-Value="price.Value" />
    @foreach (var message in price.ValidationMessages) { <span>@message</span> }
</SparseField>
```

Do not use `@bind-Value` on `InputBase` controls. Its generated expression
targets the field context instead of the original model, so field-level
validation would misattribute. Keyed, temporary-key, and indexed paths
expose a null `ValueExpression`, and `InputBase` rejects a null expression,
so those paths edit through value and callback inputs with errors surfaced
explicitly.

## Follow nested and collection paths

`Select` navigates a field value to a child member, and `Key`, `At`, and
`TemporaryKey` navigate a collection field to one entry member. Every form
keeps the root model type, so the result feeds a nested `SparseField`
directly. Reads resolve live on every access: row replacement and keyed
reorder are followed, and removed entries report stale instead of writing
to an orphan.

Collection-root fields report the default semantic state because member
transitions cannot be boxed as the collection value. Query
`TryGetCollectionState` on those fields for row states instead.

## Replace sessions without leaks

Assigning a new `Session` unsubscribes the previous session and history,
builds a fresh original-model `EditContext`, and subscribes the next
session. Key the `EditForm` on the session so inputs rebuild against the
new context; reused inputs keep the old cascaded context. Validation
subscriptions rewire to the new context and release on disposal. In-flight
callbacks captured from a previous tree complete against their own binding
and never cross into the replacement model.

One session notification refreshes the subtree once, and one `BatchEdit`
raises one notification however many edits it holds. Each field snapshots
its semantic state once per render from a fresh session diff. For hot
subtrees that need less, keep using the low-level `Bind` and
`SparseSessionScope` APIs around explicit actions instead of rendering on
every keystroke.
