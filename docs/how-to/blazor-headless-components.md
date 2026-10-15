# Blazor headless components

`SparseEdit` and `SparseField` render forms without coupling your code to any specific UI component vendor or HTML layout.

## Render a headless form

Wrap your form in `<SparseEdit>` and bind individual fields using `<SparseField>`:

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

## Drive binding contracts in code

You can also bind paths directly in C# without Razor components using `session.Bind()`:

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
// headlessNumber.Value == "ORD-1"
// headlessNumber.ValueExpression is not null

await headlessNumber.SetAsync("ORD-2");
// headlessOrder.Number == "ORD-2"
// headlessSession.FieldState(HeadlessOrder.SparsePath.Number).IsChanged == true
// headlessHistory.CanUndo == true

headlessHistory.Undo();
// headlessOrder.Number == "ORD-1"
headlessHistory.Redo();
// headlessOrder.Number == "ORD-2"

// Keyed and indexed paths expose no expression, so value/callback
// inputs edit them while validation stays explicit.
var headlessPrice = headlessSession.Bind(
    HeadlessOrder.SparsePath.Lines.Key("b").Price,
    headlessContext
);
// headlessPrice.ValueExpression is null

await headlessPrice.SetAsync(99m);
// headlessOrder.Lines.Single(line => line.Sku == "b").Price == 99m
// headlessSession.CollectionState(HeadlessOrder.SparsePath.Lines).GetItemState("b").IsEdited == true

var headlessSku = headlessSession.Bind(
    HeadlessOrder.SparsePath.Lines.At(0).Sku,
    headlessContext
);
// headlessSku.Value == "a"

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
// headlessTitle.Value == "draft"

await headlessTitle.SetAsync("draft v2");
// headlessDrafts.Drafts.Single(item => item.TemporaryId == headlessTemporaryId).Title == "draft v2"
// headlessDraftContext.IsModified() == true

headlessSession.DisableHistory();
```
<!-- /sample -->

## Bind inputs

* **Standard / third-party controls**: Bind directly with `@bind-Value="field.Value"`.
* **Blazor `InputBase` controls (`InputText`, `InputNumber`)**: Pass `Value`, `ValueChanged`, and `ValueExpression` explicitly so validation messages bind to the underlying model:

<!-- illustrative: razor markup shape; the compilable component is SparseHeadless.razor in the Blazor consumer fixture -->
```razor
<SparseField Path="edit.Path.Number" Context="number">
    <InputText Value="@number.Value"
               ValueChanged="number.ValueChanged"
               ValueExpression="number.ValueExpression" />
</SparseField>
<SparseField Path="@(edit.Path.Lines.Key("b").Price)" Context="price">
    <MyInput @bind-Value="price.Value" />
    @foreach (var message in price.ValidationMessages) { <span>@message</span> }
</SparseField>
```

## Navigate nested and collection paths

Use `Select` to navigate into child models, and `Key` or `At` to navigate into collections:

<!-- illustrative: nested path navigation -->
```razor
<SparseField Path="edit.Path.Customer" Context="customer">
    <SparseField Path="customer.Select(c => c.Email)" Context="email">
        <MyInput @bind-Value="email.Value" />
    </SparseField>
</SparseField>
```

When switching the active `Session` instance, key the `<EditForm>` by session or generation (e.g. `@key="session"`) so inputs recreate against the updated context.

For detailed session observer specifications, see [Edit session state and observers reference](../reference/edit-session-state.md).
