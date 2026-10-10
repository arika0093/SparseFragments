# Blazor edit form

This guide wires one order form from first keystroke to saved row. It
assumes the [first sparse edit](../tutorial/first-sparse-edit.md) flow
already reads familiar and the
[UI frameworks](../ui-frameworks.md#blazor) contract makes sense. The
persistence and rebase rules behind the save stay in
[ChangeSet rebase](../rebase.md); this page shows the form around them.

The listings below run in the `package-sparse-blazor` consumer fixture, and
the Razor shape around them compiles as `OrderEditForm.razor` in the same
project. The service is a fake owned by the application; transport stays
with the application in production too.

## Model and save boundary

The order carries a required number, a note, and two server-managed members:
a version and an update timestamp. The fake endpoint answers with one of
three results: the persisted row, the current server row on conflict, or an
`HttpRequestException` when transport fails before anything is persisted.

<!-- sample: blazor-form-models -->
```csharp
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using SparseFragments;
using SparseFragments.Blazor;

[SparseFragmentModel]
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

`CreateEditSession()` captures the model as baseline and live instance, and
`CreateEditContext()` binds that same original model to the `EditContext`.
The observable proxy is never the context model, so validation metadata
such as `[Required]` keeps applying to the model. The listings use a manual
`OnValidationRequested` handler where a real component drops in
`<DataAnnotationsValidator />`; both feed the same `ValidationMessageStore`.

## Edit through Observable, notify the EditContext

The text input writes through `session.Observable` and then tells the
context which field changed. Those are two separate channels with two
separate jobs: the proxy edits the live model with notifications and drives
`HasChanges`, while `NotifyFieldChanged` drives Blazor field tracking
(modified flags) and validation display. Touching a field and changing a
value stay distinct: an edit that is restored to its baseline leaves the
field marked modified but `HasChanges` false.

Never bind an `InputText` expression to the proxy. The expression picks the
validation model, and the proxy is the wrong one. Bind the value expression
to the live model and route the changed value through the proxy, as the
component below does.

<!-- illustrative: razor markup shape; the compilable component is OrderEditForm.razor in the Blazor consumer fixture -->
```razor
<EditForm EditContext="@editContext" OnValidSubmit="@HandleValidSubmit">
    <DataAnnotationsValidator />
    <InputText Value="@session.Current.Number"
               ValueChanged="@OnNumberChanged"
               ValueExpression="@(() => live.Number)"
               disabled="@isSaving" />
</EditForm>

@code {
    private void OnNumberChanged(string value)
    {
        session.Observable.Number = value;
        editContext.NotifyFieldChanged(session.Field(nameof(BlazorEditFormOrder.Number)));
    }
}
```

The fixture drives the same adapter without a renderer: one edit marks the
field modified and passes validation, and clearing the required number
fails validation with the required message. A failed `Validate()` stops the
submit before any change set is captured.

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

## Submit one change set and adopt the persisted row

The submit disables editing while the request runs, validates first, then
captures exactly one change set and sends its payload. The four outcomes
stay apart: validation failure never reaches the service, success adopts
the server row, conflict surfaces field errors, and transport failure keeps
everything for a retry. The listing shows the success path with a normalised
number and server-assigned version and timestamp. The conflict and transport
sections below run the same handler shape against the other two modes.

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

The fresh session is the recommended acknowledgement whenever the server
returns state: it picks up key assignments, timestamps, and normalisation
without reconciliation code. The old validation handler is detached before
the context is replaced, so the dead context keeps no references to the new
session. `HasChanges` is false because the baseline is the persisted row.

Two neighboring calls serve narrower cases. `BatchEdit` groups one user
action that needs several observable mutations into one notification; it
never rolls back, so discarding still goes through `RevertChanges`. For
forms that keep editing enabled while the save runs and the server changes
nothing structural, `AcceptChanges(submitted)` advances only the baseline
and later edits stay pending; the context clears only when the session is
clean. Prefer the fresh session above it whenever IDs, timestamps, or
normalisation are in play.

## Show conflicts and resolve them explicitly

When the server kept a newer value, each structured conflict already
carries its member path, so `AddValidationError` with the path overload
addresses the message at the disputed field. The original session, its
pending changes, and the current `EditContext` all stay: no clean session
is created before an actual save or an explicit user decision. Reloading
the conflicting state reports the same conflict and leaves the live model
and the baseline untouched. Resolution is a user action; here the user
takes the server value, which clears the pending change.

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

A conflict-free `Reload` with disjoint server changes merges instead: it
writes the merged result into the live model instance and keeps the pending
edits as changes against the new baseline, as shown in
[UI frameworks](../ui-frameworks.md#reload-with-fresh-server-state). The
component exposes the same choice as a visible refresh button rather than
an automatic accept.

## Retry transport failure without data loss

A throw before persistence never ran acknowledgement, so the baseline is
unchanged and the pending change is intact. The `finally` unblocks editing,
and the retry sends the original change set through the same session.

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
