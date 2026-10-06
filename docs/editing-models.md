# Editing Models

Track edits against a UI model and produce a semantic patch when the user is done. Capture a baseline up front, let the UI mutate the plain model, then diff baseline against current state.

```csharp
var session = order.CreateEditSession(); // generated with the Blazor package

session.Model.Number = "ORD-2";
session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });

if (session.HasChanges)
{
    var patch = session.CreatePatch();
    ...
}

session.AcceptChanges(); // re-baseline, clear Blazor modified flags
```

`HasChanges` comes from the fragment comparison, so collection mutations without field notifications and edit-then-restore (back to the original value, hence no change) behave correctly. Without Blazor, the same flow works manually with `Fragment.From` plus `Patch.Between`. See [UI frameworks](ui-frameworks.md) for `EditContext` wiring, validation stores, and desktop binding via `T.Observable`.
