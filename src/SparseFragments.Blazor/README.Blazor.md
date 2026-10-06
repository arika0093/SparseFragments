# SparseFragments.Blazor

Blazor edit sessions for SparseFragments with semantic change tracking.

```sh
dotnet add package SparseFragments.Blazor
```

```csharp
var session = order.CreateEditSession();

<EditForm EditContext="@session.EditContext">...</EditForm>

if (session.HasChanges)
{
    var patch = session.CreatePatch();
    ...
}

session.AcceptChanges();
```

Patches always come from baseline-versus-current model comparison, never from
`EditContext` field tracking. Full integration guidance:
[UI frameworks](https://github.com/arika0093/SparseFragments/blob/main/docs/ui-frameworks.md).
