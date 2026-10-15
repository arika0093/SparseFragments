# Blazor edit form

This guide shows how to wire an editable Blazor `EditForm` using an `EditSession`, submit changes, handle concurrency conflicts, and retry failed requests without losing user edits.

## Model and save boundary

Define your model with `[SparseModel]` and DataAnnotations:

<!-- sample: blazor-form-models -->
```csharp
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;
using SparseTypes.Blazor;

[SparseModel]
public partial class BlazorEditFormOrder
{
    [Required]
    public string Number { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Version { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum BlazorFormOutcome
{
    ValidationFailed,
    Saved,
    Conflict,
    TransportError,
}

public abstract record BlazorFormSaveResult;

public sealed record BlazorFormSaved : BlazorFormSaveResult
{
    public BlazorEditFormOrder Persisted { get; init; } = new();
}

public sealed record BlazorFormConflicted : BlazorFormSaveResult
{
    public BlazorEditFormOrder ServerState { get; init; } = new();
}

// The application owns transport. This fake stands in for the real endpoint:
// success returns the persisted row, conflict returns the current server row,
// and transport failure throws before anything is persisted.
public sealed class FakeOrderSaveService
{
    public BlazorFormOutcome Mode { get; set; } = BlazorFormOutcome.Saved;

    public BlazorEditFormOrder ServerState { get; set; } = new() { Number = "SERVER" };

    public BlazorEditFormOrder PersistedState { get; set; } = new() { Number = "ORD-2" };

    public List<string> ReceivedJson { get; } = new();

    public async Task<BlazorFormSaveResult> SaveAsync(
        BlazorEditFormOrder.ChangePayload payload,
        CancellationToken cancellationToken
    )
    {
        ReceivedJson.Add(JsonSerializer.Serialize(payload));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (Mode == BlazorFormOutcome.TransportError)
        {
            throw new HttpRequestException("The server did not respond.");
        }
        if (Mode == BlazorFormOutcome.Conflict)
        {
            return new BlazorFormConflicted { ServerState = ServerState };
        }
        return new BlazorFormSaved { Persisted = PersistedState };
    }
}
```
<!-- /sample -->

## Bind inputs through Observable and notify EditContext

Write input changes through `session.Observable` and notify the `EditContext` via `session.Field()`:

<!-- illustrative: razor markup shape; the compilable component is OrderEditForm.razor in the Blazor consumer fixture -->
```razor
<EditForm @key="formGeneration" EditContext="@editContext" OnValidSubmit="@HandleValidSubmit">
    <DataAnnotationsValidator />
    <InputText Value="@session.Current.Number"
               ValueChanged="@OnNumberChanged"
               ValueExpression="@(() => live.Number)"
               disabled="@isSaving" />
    @if (latestServer is not null)
    {
        <button type="button" @onclick="UseServerValue">Use server value</button>
    }
</EditForm>

@code {
    private int formGeneration;

    private void OnNumberChanged(string value)
    {
        session.Observable.Number = value;
        editContext.NotifyFieldChanged(session.Field(nameof(BlazorEditFormOrder.Number)));
    }
}
```

<!-- sample: blazor-form-input -->
```csharp
var inputOrder = new BlazorEditFormOrder { Number = "ORD-1", Version = 7 };
var inputSession = inputOrder.CreateEditSession();
var inputContext = inputSession.CreateEditContext();
var inputStore = inputSession.CreateValidationStore(inputContext);
inputContext.OnValidationRequested += (_, _) =>
{
    inputStore.Clear();
    if (string.IsNullOrWhiteSpace(inputSession.Current.Number))
    {
        inputStore.Add(
            inputSession.Field(nameof(BlazorEditFormOrder.Number)),
            "Number is required."
        );
    }
};

// The text input writes through the Observable proxy, then tells the
// EditContext which field changed. EditContext.Model stays the
// original model, so never bind an InputText expression to the proxy.
inputSession.Observable.Number = "ORD-2";
inputContext.NotifyFieldChanged(inputSession.Field(nameof(BlazorEditFormOrder.Number)));
// inputContext.IsModified() == true
// inputContext.Validate() == true

inputSession.Observable.Number = string.Empty;
inputContext.NotifyFieldChanged(inputSession.Field(nameof(BlazorEditFormOrder.Number)));
// inputContext.Validate() == false
```
<!-- /sample -->

## Submit and adopt the persisted row

On successful save, create a fresh session from the returned authoritative row. Re-keying the `<EditForm>` (`@key="formGeneration"`) ensures input components rebuild cleanly against the new context:

<!-- sample: blazor-form-save -->
```csharp
var saveOrder = new BlazorEditFormOrder
{
    Number = "ORD-1",
    Note = "pick",
    Version = 7,
};
var saveSession = saveOrder.CreateEditSession();
var saveContext = saveSession.CreateEditContext();
var saveStore = saveSession.CreateValidationStore(saveContext);
void ValidateSave(object? sender, ValidationRequestedEventArgs args)
{
    saveStore.Clear();
    if (string.IsNullOrWhiteSpace(saveSession.Current.Number))
    {
        saveStore.Add(
            saveSession.Field(nameof(BlazorEditFormOrder.Number)),
            "Number is required."
        );
    }
}
saveContext.OnValidationRequested += ValidateSave;
var saveService = new FakeOrderSaveService
{
    PersistedState = new BlazorEditFormOrder
    {
        Number = "ORD-2",
        Note = "pick",
        Version = 8,
        UpdatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
    },
};

saveSession.Observable.Number = "ord-2";
saveContext.NotifyFieldChanged(saveSession.Field(nameof(BlazorEditFormOrder.Number)));

var isSaving = true;
try
{
    if (!saveContext.Validate())
    {
        throw new InvalidOperationException("The form has validation errors.");
    }
    var submitted = saveSession.CreateChangeSet();
    var saveResult = await saveService.SaveAsync(
        submitted.ToPayload(),
        CancellationToken.None
    );
    if (saveResult is BlazorFormConflicted)
    {
        throw new InvalidOperationException("Expected the save to succeed.");
    }
    var persisted = ((BlazorFormSaved)saveResult).Persisted;
    // persisted.Number == "ORD-2"
    // persisted.Version == 8

    // On success only, adopt the authoritative state: detach the old
    // handlers, then start a fresh session and EditContext from it.
    saveContext.OnValidationRequested -= ValidateSave;
    saveSession = persisted.CreateEditSession();
    saveContext = saveSession.CreateEditContext();
    saveStore = saveSession.CreateValidationStore(saveContext);
    saveContext.OnValidationRequested += ValidateSave;
    // saveSession.HasChanges == false
    // ReferenceEquals(saveContext.Model, persisted) == true
}
finally
{
    isSaving = false;
}
// isSaving == false
```
<!-- /sample -->

## Surface conflicts on fields

When concurrent modifications clash, project the structured conflicts directly to field validation errors using `AddValidationError`:

<!-- sample: blazor-form-conflict -->
```csharp
var conflictOrder = new BlazorEditFormOrder { Number = "ORD-1", Version = 7 };
var conflictSession = conflictOrder.CreateEditSession();
var conflictContext = conflictSession.CreateEditContext();
var conflictStore = conflictSession.CreateValidationStore(conflictContext);
conflictContext.OnValidationRequested += (_, _) =>
{
    conflictStore.Clear();
    if (string.IsNullOrWhiteSpace(conflictSession.Current.Number))
    {
        conflictStore.Add(
            conflictSession.Field(nameof(BlazorEditFormOrder.Number)),
            "Number is required."
        );
    }
};
var conflictService = new FakeOrderSaveService
{
    Mode = BlazorFormOutcome.Conflict,
    ServerState = new BlazorEditFormOrder { Number = "SERVER", Version = 8 },
};

conflictSession.Observable.Number = "ORD-2";
conflictContext.NotifyFieldChanged(
    conflictSession.Field(nameof(BlazorEditFormOrder.Number))
);
var submitted = conflictSession.CreateChangeSet();
var serverState = conflictService.ServerState;
if (submitted.TryApplyTo(serverState, out _, out var surfacingConflicts))
{
    throw new InvalidOperationException("Expected a semantic conflict.");
}
foreach (var conflict in surfacingConflicts)
{
    conflictSession.AddValidationError(
        conflictStore,
        conflict.PathText,
        "Server kept a newer value."
    );
}
// surfacingConflicts.Single().PathText == "Number"
// conflictSession.HasChanges == true
// conflictSession.Current.Number == "ORD-2"

// The conflict needs an explicit user decision. Reloading the
// conflicting state reports the same conflict and keeps everything.
var reload = conflictSession.Reload(serverState);
// reload.HasConflicts == true
// conflictSession.Current.Number == "ORD-2"

// The user takes the server value, then the pending change is gone.
conflictSession.Observable.Number = "SERVER";
conflictContext.NotifyFieldChanged(
    conflictSession.Field(nameof(BlazorEditFormOrder.Number))
);
var resolved = conflictSession.Reload(serverState);
// resolved.HasConflicts == false
// conflictSession.HasChanges == false
```
<!-- /sample -->

### Managing conflicts with dedicated validation stores

In complex forms with deleted child elements, use `session.AddConflicts()` with a dedicated `ValidationMessageStore` to route errors to parent paths cleanly:

<!-- illustrative: conflict projection helper usage -->
```csharp
var conflictStore = conflictSession.CreateValidationStore(conflictContext);
var options = new SparseConflictValidationOptions
{
    Fallback = SparseConflictFallbackMode.NearestParent,
    ClearStoreFirst = true,
};
conflictSession.AddConflicts(conflictStore, conflictContext, surfacingConflicts, options);
```

## Retry transport failures without losing draft state

Network errors do not advance the session baseline. Re-enabling the form preserves all dirty fields for a retry:

<!-- sample: blazor-form-transport -->
```csharp
var retryOrder = new BlazorEditFormOrder { Number = "ORD-1", Version = 7 };
var retrySession = retryOrder.CreateEditSession();
var retryContext = retrySession.CreateEditContext();
var retryStore = retrySession.CreateValidationStore(retryContext);
retryContext.OnValidationRequested += (_, _) =>
{
    retryStore.Clear();
    if (string.IsNullOrWhiteSpace(retrySession.Current.Number))
    {
        retryStore.Add(
            retrySession.Field(nameof(BlazorEditFormOrder.Number)),
            "Number is required."
        );
    }
};
var retryService = new FakeOrderSaveService
{
    Mode = BlazorFormOutcome.TransportError,
    PersistedState = new BlazorEditFormOrder { Number = "ORD-2", Version = 8 },
};

retrySession.Observable.Number = "ORD-2";
retryContext.NotifyFieldChanged(retrySession.Field(nameof(BlazorEditFormOrder.Number)));

var isSaving = true;
var transportFailed = false;
try
{
    var submitted = retrySession.CreateChangeSet();
    try
    {
        await retryService.SaveAsync(submitted.ToPayload(), CancellationToken.None);
    }
    catch (HttpRequestException)
    {
        transportFailed = true;
    }
    // transportFailed == true
    // retrySession.HasChanges == true
    // retrySession.Current.Number == "ORD-2"
}
finally
{
    isSaving = false;
}
// isSaving == false

// Retry with the same session: the baseline never moved, so the
// pending change is still intact.
retryService.Mode = BlazorFormOutcome.Saved;
var retrySubmitted = retrySession.CreateChangeSet();
var retryResult = await retryService.SaveAsync(
    retrySubmitted.ToPayload(),
    CancellationToken.None
);
var persisted = ((BlazorFormSaved)retryResult).Persisted;
retrySession = persisted.CreateEditSession();
// retrySession.HasChanges == false
```
<!-- /sample -->

For deeper background on edit sessions and UI framework adapters, see [UI frameworks explanation](../explanation/ui-frameworks.md).
